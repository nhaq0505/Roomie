#!/usr/bin/env bash
set -euo pipefail

PATH=/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin
export PATH
umask 077

readonly image_pattern='^ghcr\.io/nhaq0505/rehouse@sha256:[a-f0-9]{64}$'
readonly compose_file=/opt/rehouse/compose.yaml
readonly runtime_env=/etc/rehouse/runtime.env
readonly state_dir=/var/lib/rehouse
readonly lock_file=/var/lib/rehouse/ops.lock

fail() {
  printf 'rehouse-restore: %s\n' "$1" >&2
  exit 1
}

[[ "$EUID" -eq 0 ]] || fail 'must run as root'
[[ "$#" -eq 2 && "$2" == --replace-current-data ]] || fail 'usage: rehouse-restore BACKUP_DIR --replace-current-data'
[[ -d "$1" ]] || fail 'backup directory is missing'
backup_dir=$(realpath -e -- "$1")
[[ "$backup_dir" == /var/backups/rehouse/rehouse-* ]] || fail 'backup must be inside /var/backups/rehouse'
[[ -s "$backup_dir/database.dump" && -f "$backup_dir/data-protection-keys.tar.gz" && -f "$backup_dir/manifest.txt" ]] || fail 'backup is incomplete'
[[ -f "$compose_file" && -f "$runtime_env" ]] || fail 'Rehouse host files are missing'
(cd "$backup_dir" && sha256sum --check checksums.sha256) >/dev/null || fail 'backup checksums failed'
exec 9>"$lock_file"
flock --exclusive --wait 600 9 || fail 'another Rehouse backup, restore, or deployment is still running'

readonly db_name=room_rental
restore_db="rehouse_restore_$$"
previous_db="rehouse_previous_$$"
image_ref=$(sed -n 's/^image_ref=//p' "$backup_dir/manifest.txt" | head -n 1)
[[ "$image_ref" =~ $image_pattern ]] || fail 'backup manifest has an invalid image reference'
docker image inspect "$image_ref" >/dev/null 2>&1 || fail 'the backup image is not present locally; restore it through the deployment helper first'

previous_image=''
if [[ -f "$state_dir/current-image" ]]; then
  IFS= read -r previous_image < "$state_dir/current-image" || true
  [[ "$previous_image" =~ $image_pattern ]] || fail 'saved current image reference is invalid'
fi

cd /opt/rehouse
compose() {
  local selected_image=$1
  shift
  IMAGE_REF="$selected_image" docker compose --project-name rehouse --env-file "$runtime_env" --file "$compose_file" "$@"
}

psql_admin() {
  docker exec "$db_id" psql --no-psqlrc -v ON_ERROR_STOP=1 -U roomie_dba -d postgres "$@"
}

psql_database() {
  local selected_db=$1
  shift
  docker exec "$db_id" psql --no-psqlrc -v ON_ERROR_STOP=1 -U roomie_dba -d "$selected_db" "$@"
}

db_id=''
keys_mount=''
keys_snapshot=''
keys_stage=''
keys_replaced=0
restore_db_created=0
database_swapped=0
previous_db_renamed=0
restore_complete=0

wait_for_db() {
  local attempt status
  for ((attempt = 0; attempt < 60; attempt++)); do
    db_id=$(compose "$image_ref" ps -q db 2>/dev/null || true)
    if [[ -n "$db_id" ]]; then
      status=$(docker inspect --format '{{if .State.Health}}{{.State.Health.Status}}{{else}}starting{{end}}' "$db_id" 2>/dev/null || true)
      [[ "$status" == healthy ]] && return 0
      [[ "$status" == unhealthy ]] && return 1
    fi
    sleep 2
  done
  return 1
}

wait_for_app() {
  local attempt query_result admin_count app_id app_uid app_gid keys_owner
  for ((attempt = 0; attempt < 60; attempt++)); do
    if curl --fail --silent --max-time 4 http://127.0.0.1:8080/api/health >/dev/null; then
      query_result=$(psql_database "$db_name" -At -c 'SELECT 1' 2>/dev/null || true)
      admin_count=$(psql_database "$db_name" -At -c "SELECT count(*) FROM \"Users\" WHERE \"Role\"='Admin' AND \"Status\"='Approved'" 2>/dev/null || true)
      app_id=$(compose "$image_ref" ps -q app 2>/dev/null || true)
      if [[ "$query_result" == 1 && "$admin_count" =~ ^[1-9][0-9]*$ && -n "$app_id" ]]; then
        app_uid=$(docker exec "$app_id" id -u 2>/dev/null || true)
        app_gid=$(docker exec "$app_id" id -g 2>/dev/null || true)
        keys_owner=$(docker exec "$app_id" stat -c '%u:%g' /app/data/keys 2>/dev/null || true)
        if [[ "$app_uid" =~ ^[1-9][0-9]*$ && "$app_gid" =~ ^[0-9]+$ && "$keys_owner" == "$app_uid:$app_gid" ]] && \
           docker exec "$app_id" /bin/sh -c 'test -d /app/data/keys && test -w /app/data/keys' >/dev/null 2>&1; then
          return 0
        fi
      fi
    fi
    sleep 2
  done
  return 1
}

rollback_on_failure() {
  local status=$?
  if [[ "$status" -ne 0 && "$restore_complete" -eq 0 ]]; then
    compose "$image_ref" stop app >/dev/null 2>&1 || true
    if [[ "$previous_db_renamed" -eq 1 ]]; then
      if [[ "$database_swapped" -eq 1 ]]; then
        psql_admin -c "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname='${db_name}' AND pid <> pg_backend_pid()" >/dev/null 2>&1 || true
        psql_admin -c "ALTER DATABASE \"${db_name}\" RENAME TO \"${restore_db}\"" >/dev/null 2>&1 || true
      fi
      psql_admin -c "ALTER DATABASE \"${previous_db}\" RENAME TO \"${db_name}\"" >/dev/null 2>&1 || true
      database_swapped=0
      previous_db_renamed=0
    fi
    if [[ "$keys_replaced" -eq 1 && -n "$keys_mount" && -d "$keys_snapshot" ]]; then
      find "$keys_mount" -mindepth 1 -maxdepth 1 -exec rm -rf -- {} + || true
      cp -a "$keys_snapshot/." "$keys_mount/" || true
      keys_replaced=0
    fi
    if [[ -n "$previous_image" ]]; then
      compose "$previous_image" up -d --no-deps --force-recreate app >/dev/null 2>&1 || true
    fi
  fi
  if [[ -n "$db_id" && "$restore_db_created" -eq 1 ]]; then
    psql_admin -c "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname='${restore_db}' AND pid <> pg_backend_pid()" >/dev/null 2>&1 || true
    psql_admin -c "DROP DATABASE IF EXISTS \"${restore_db}\"" >/dev/null 2>&1 || true
  fi
  if [[ -n "$keys_stage" && -d "$keys_stage" ]]; then
    rm -rf -- "$keys_stage"
  fi
  if [[ -n "$keys_snapshot" && -d "$keys_snapshot" ]]; then
    rm -rf -- "$keys_snapshot"
  fi
}
trap rollback_on_failure EXIT

compose "$image_ref" up -d db
wait_for_db || fail 'PostgreSQL did not become healthy'
compose "$image_ref" stop app >/dev/null 2>&1 || fail 'could not stop the app before restore'

psql_admin -c "CREATE DATABASE \"${restore_db}\" OWNER room_rental TEMPLATE template0"
restore_db_created=1
docker exec -i "$db_id" pg_restore -U roomie_dba --role=room_rental -d "$restore_db" --no-owner < "$backup_dir/database.dump"
[[ "$(psql_database "$restore_db" -At -c 'SELECT 1')" == 1 ]] || fail 'restored database failed its SQL check'
table_count=$(psql_database "$restore_db" -At -c "SELECT count(*) FROM information_schema.tables WHERE table_schema='public' AND table_type='BASE TABLE'")
if [[ "$table_count" != 0 ]]; then
  admin_count=$(psql_database "$restore_db" -At -c "SELECT count(*) FROM \"Users\" WHERE \"Role\"='Admin' AND \"Status\"='Approved'")
  [[ "$admin_count" =~ ^[1-9][0-9]*$ ]] || fail 'restored database has no bootstrap administrator'
fi

archive_paths=$(tar -tzf "$backup_dir/data-protection-keys.tar.gz") || fail 'DataProtection archive is invalid'
while IFS= read -r member; do
  member=${member#./}
  case "$member" in
    /*|..|../*|*/../*|*/..) fail 'DataProtection archive contains an unsafe path' ;;
  esac
done <<< "$archive_paths"
keys_stage=$(mktemp -d /var/backups/rehouse/.restore-keys.XXXXXX)
keys_snapshot=$(mktemp -d /var/backups/rehouse/.restore-keys-old.XXXXXX)
chmod 0700 "$keys_stage" "$keys_snapshot"
tar -C "$keys_stage" --same-owner -xzf "$backup_dir/data-protection-keys.tar.gz"
docker volume inspect rehouse_data_protection_keys >/dev/null 2>&1 || docker volume create rehouse_data_protection_keys >/dev/null
keys_mount=$(docker volume inspect --format '{{.Mountpoint}}' rehouse_data_protection_keys)
[[ -d "$keys_mount" ]] || fail 'DataProtection volume mount is missing'
cp -a "$keys_mount/." "$keys_snapshot/"
keys_replaced=1
find "$keys_mount" -mindepth 1 -maxdepth 1 -exec rm -rf -- {} +
cp -a "$keys_stage/." "$keys_mount/"
rm -rf -- "$keys_stage"
keys_stage=''

restore_optional_volume() {
  local archive_name=$1 volume_name=$2
  [[ -f "$backup_dir/$archive_name" ]] || return 0
  local archive_members member vol_stage vol_mount
  archive_members=$(tar -tzf "$backup_dir/$archive_name") || fail "$archive_name archive is invalid"
  while IFS= read -r member; do
    member=${member#./}
    case "$member" in
      /*|..|../*|*/../*|*/..) fail "$archive_name archive contains an unsafe path" ;;
    esac
  done <<< "$archive_members"
  vol_stage=$(mktemp -d /var/backups/rehouse/.restore-vol.XXXXXX)
  chmod 0700 "$vol_stage"
  tar -C "$vol_stage" --same-owner -xzf "$backup_dir/$archive_name"
  docker volume inspect "$volume_name" >/dev/null 2>&1 || docker volume create "$volume_name" >/dev/null
  vol_mount=$(docker volume inspect --format '{{.Mountpoint}}' "$volume_name")
  if [[ -d "$vol_mount" ]]; then
    find "$vol_mount" -mindepth 1 -maxdepth 1 -exec rm -rf -- {} +
    cp -a "$vol_stage/." "$vol_mount/"
  fi
  rm -rf -- "$vol_stage"
}
restore_optional_volume zalo-sessions.tar.gz rehouse_zalo_sessions
restore_optional_volume whatsapp-sessions.tar.gz rehouse_whatsapp_sessions

psql_admin -c "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname='${db_name}' AND pid <> pg_backend_pid()" >/dev/null
psql_admin -c "ALTER DATABASE \"${db_name}\" RENAME TO \"${previous_db}\""
previous_db_renamed=1
if ! psql_admin -c "ALTER DATABASE \"${restore_db}\" RENAME TO \"${db_name}\""; then
  fail 'could not switch the restored database into place'
fi
database_swapped=1

/usr/local/sbin/rehouse-db-roles "$image_ref" || fail 'restored runtime grants failed'
compose "$image_ref" up -d --no-deps --force-recreate app
wait_for_app || fail 'restored database and keys did not pass the app health check'

marker_tmp=$(mktemp "$state_dir/current-image.XXXXXX")
printf '%s\n' "$image_ref" > "$marker_tmp"
chmod 0600 "$marker_tmp"
mv -f -- "$marker_tmp" "$state_dir/current-image"
database_swapped=0
previous_db_renamed=0
keys_replaced=0
restore_complete=1
psql_admin -c "DROP DATABASE IF EXISTS \"${previous_db}\"" >/dev/null || printf 'Warning: previous database is retained as %s\n' "$previous_db" >&2
rm -rf -- "$keys_snapshot"
keys_snapshot=''
trap - EXIT
printf 'Restored backup is healthy at http://127.0.0.1:8080/api/health; the previous database was replaced.\n'
