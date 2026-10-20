#!/usr/bin/env bash
set -euo pipefail
umask 077
[[ "$EUID" -eq 0 ]] || { echo 'Run as root' >&2; exit 1; }
source_dir=$(cd -- "$(dirname -- "$0")" && pwd)

# Create monitoring directories:
# 1. Private monitoring state directory (/var/lib/rehouse/monitoring) - stores collector-state.json (0600)
install -d -m0755 /var/lib/rehouse/monitoring
# 2. Dedicated PUBLIC metrics directory (/var/lib/rehouse/monitoring/public) - mounted into app at /app/monitoring-host:ro
install -d -m0755 /var/lib/rehouse/monitoring/public
install -d -m0700 /etc/rehouse

# Install collector executable
install -m0750 "$source_dir/rehouse-host-collector.py" /usr/local/sbin/rehouse-host-collector

# Install systemd service and timer
install -m0644 "$source_dir/rehouse-host-collector.service" /etc/systemd/system/rehouse-host-collector.service
install -m0644 "$source_dir/rehouse-host-collector.timer" /etc/systemd/system/rehouse-host-collector.timer

# Create default monitoring.env if not present
if [[ ! -e /etc/rehouse/monitoring.env ]]; then
  cat > /etc/rehouse/monitoring.env <<'EOF'
# Optional dedicated monitoring recipient (falls back to BACKUP_ALERT_TO in runtime.env if unset)
# MONITORING_ALERT_TO="monitoring-alerts@example.com"
EOF
  chmod 0600 /etc/rehouse/monitoring.env
fi

# Enable and start timer if systemctl is available
if command -v systemctl >/dev/null 2>&1; then
  systemctl daemon-reload
  systemctl enable --now rehouse-host-collector.timer
  printf 'Host collector installed and timer activated.\n'
else
  printf 'Host collector files installed. (systemctl not detected)\n'
fi
