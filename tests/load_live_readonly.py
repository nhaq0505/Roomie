#!/usr/bin/env python3
"""Opt-in live GET workload. Creates scoped synthetic fixtures; cleans them in finally.

No provider calls, real tenant tokens, existing-user credentials or product mutations.
Run --self-check first. One virtual user sends three API reads every ten seconds.
"""
import argparse
import base64
from collections import Counter
import concurrent.futures
import hashlib
import http.client
import json
import math
from pathlib import Path
import random
import secrets
import shlex
import ssl
import struct
import subprocess
import threading
import time
import uuid

HOST = 'roomie.io.vn'
PATHS = ('/api/dashboard', '/api/rooms', '/api/invoices?page=1&pageSize=10',
         '/api/buildings', '/api/readings')
SSH = ['ssh', '-i', '/Users/nguyenhoanhquan/.ssh/crypto_signal_hub_vps',
       '-o', 'IdentitiesOnly=yes', '-o', 'BatchMode=yes', '-o', 'ConnectTimeout=10',
       '-o', 'StrictHostKeyChecking=yes', 'roomie-ops@116.118.3.40']


def remote(argv, data=None):
    result = subprocess.run(SSH + [shlex.join(argv)], input=data, text=True,
                            capture_output=True, timeout=30)
    if result.returncode:
        # Commands may contain a fixture password hash: don't echo argv/input.
        raise RuntimeError('SSH command failed: ' + result.stderr[-1200:])
    return result.stdout


def sql(statement):
    return remote(['sudo', '-n', 'docker', 'exec', '-i', 'rehouse-db-1',
                   'psql', '-X', '-qAt', '-v', 'ON_ERROR_STOP=1',
                   '-U', 'room_rental', '-d', 'room_rental'], statement)


def percentile(values, p):
    return sorted(values)[max(0, math.ceil(len(values) * p) - 1)] if values else None


def temporary_auth_limit(enable):
    # Host timer restores the limit even if the load generator loses its connection.
    if not enable:
        remote(['sudo', '-n', 'python3', '/run/roomie-stress-auth-restore.py'])
        return
    code = '''import json, os, pathlib, subprocess
def run(args):
 return subprocess.check_output(args,text=True,env=env)
env=os.environ.copy()
current=json.loads(subprocess.check_output(['docker','inspect','rehouse-app-1'],text=True))[0]
env['IMAGE_REF']=current['Config']['Image']
base=['docker','compose','--project-directory','/opt/rehouse','--env-file','/etc/rehouse/runtime.env','-f','/opt/rehouse/compose.yaml']
app=json.loads(run(base+['config','--format','json']))['services']['app']
existing=dict(x.split('=',1) for x in current['Config']['Env'])
assert app['image']==current['Config']['Image']
assert all(str(v)==existing.get(k) for k,v in app['environment'].items()), 'Compose environment differs from live; refusing recreation'
assert existing.get('AuthRequestsPerMinute')=='10'
restore=pathlib.Path('/run/roomie-stress-auth-restore.py')
assert not pathlib.Path('/run/roomie-stress-auth.yaml').exists(), 'Previous temporary override requires recovery'
restore.write_text('import os, subprocess\\nenv=os.environ.copy()\\nenv["IMAGE_REF"]='+repr(env['IMAGE_REF'])+'\\nsubprocess.run('+repr(base+['up','-d','--no-deps','--pull','never','app'])+',env=env,check=True)\\nsubprocess.run(["systemctl","stop","roomie-stress-auth-restore.timer"],check=False)\\nfrom pathlib import Path\\nPath("/run/roomie-stress-auth.yaml").unlink(missing_ok=True)\\n')
restore.chmod(0o700)
subprocess.run(['systemd-run','--unit=roomie-stress-auth-restore','--on-active=15m','/usr/bin/python3',str(restore)],check=True)
pathlib.Path('/run/roomie-stress-auth.yaml').write_text('services:\\n  app:\\n    environment:\\n      AuthRequestsPerMinute: "1000"\\n')
print(run(base+['-f','/run/roomie-stress-auth.yaml','up','-d','--no-deps','--pull','never','app']))
'''
    remote(['sudo', '-n', 'python3', '-'], code)


def await_health():
    for _ in range(30):
        client = connection()
        status, _, _, _ = request(client, '/api/health')
        client.close()
        if status == 200:
            return
        time.sleep(1)
    raise RuntimeError('API did not recover after configuration change')


def request(connection, path, cookie='', payload=None):
    headers = {'Accept': 'application/json', 'User-Agent': 'Roomie-authorized-load-test/1'}
    if cookie:
        headers['Cookie'] = cookie
    if payload is not None:
        headers.update({'Content-Type': 'application/json', 'Origin': 'https://' + HOST})
    start = time.monotonic()
    try:
        connection.request('POST' if payload is not None else 'GET', path,
                           body=json.dumps(payload) if payload is not None else None, headers=headers)
        response = connection.getresponse()
        body = response.read()
        return response.status, (time.monotonic() - start) * 1000, body, response.getheader('Set-Cookie', '')
    except (OSError, http.client.HTTPException):
        connection.close()
        return 0, (time.monotonic() - start) * 1000, b'', ''


def connection():
    return http.client.HTTPSConnection(HOST, timeout=4, context=ssl.create_default_context())


def fixture_sql(owners, password_hash):
    # Only generated UUIDs and password hashes are interpolated. No user input SQL.
    rows = ','.join(f"('{owner}'::uuid,{i})" for i, owner in enumerate(owners))
    return f'''BEGIN;
SET LOCAL lock_timeout = '2s'; SET LOCAL statement_timeout = '20s';
CREATE TEMP TABLE stress_owners AS SELECT * FROM (VALUES {rows}) AS t(id,n);
INSERT INTO "Users" ("Id","Name","Email","PasswordHash","Role","Status","SubscriptionTier","CreatedAt")
SELECT id,'STRESS TEST disposable',id::text || '@loadtest.invalid','{password_hash}',
'Owner','Approved','Pro',now() FROM stress_owners;
CREATE TEMP TABLE stress_rooms AS
SELECT o.id owner, gen_random_uuid() building FROM stress_owners o;
INSERT INTO "Buildings" ("Id","OwnerId","Name","Address","CreatedAt")
SELECT building,owner,'STRESS TEST disposable','Synthetic fixture',now() FROM stress_rooms;
CREATE TEMP TABLE stress_units AS
SELECT owner,building,gen_random_uuid() room,gen_random_uuid() occupancy,n
FROM stress_rooms CROSS JOIN generate_series(1,20) n;
INSERT INTO "Rooms" ("Id","BuildingId","Name","Rent","ElectricityRate","WaterFee","WaterPerPerson","ServiceFee","InitialReading","CreatedAt")
SELECT room,building,'Test ' || n,3000000,4000,80000,true,100000,100,now() FROM stress_units;
INSERT INTO "Occupancies" ("Id","RoomId","TenantName","People","StartDate","IsActive","CreatedAt")
SELECT occupancy,room,'Synthetic tenant',2,'2025-01-01',true,now() FROM stress_units;
INSERT INTO "Invoices" ("Id","Kind","IsFinalMeterReading","LifecycleState","Version","OwnerId","RoomId","BuildingId","OccupancyId",
"RoomName","BuildingName","Period","TenantName","PreviousReading","CurrentReading","Consumption","Rent","ElectricityAmount","WaterAmount","ServiceAmount","Total","Paid","Status","DueDate","IssuedAt")
SELECT gen_random_uuid(),'Monthly',false,'Issued',1,owner,room,building,occupancy,
'Test ' || n,'STRESS TEST disposable',to_char(month,'YYYY-MM'),'Synthetic tenant',100,150,50,3000000,200000,160000,100000,
3460000,0,'Unpaid',(month + interval '27 days')::date,month
FROM stress_units CROSS JOIN generate_series(date_trunc('month',now() AT TIME ZONE 'Asia/Ho_Chi_Minh') - interval '11 months',
date_trunc('month',now() AT TIME ZONE 'Asia/Ho_Chi_Minh'),interval '1 month') month;
COMMIT;
'''


def cleanup(owners):
    ids = ','.join(f"'{owner}'::uuid" for owner in owners)
    # Scope every delete to exact fixture UUIDs. Never delete by a name/email prefix.
    statements = ['BEGIN;', "SET LOCAL lock_timeout = '2s'; SET LOCAL statement_timeout = '20s';"]
    for table, column in [('AuthSessions', 'UserId'), ('MilestoneOutboxEvents', 'OwnerId'),
                          ('Invoices', 'OwnerId'), ('Occupancies', None), ('Rooms', None),
                          ('Buildings', 'OwnerId'), ('Users', 'Id')]:
        where = f'"{column}" IN ({ids})' if column else (
            f'"RoomId" IN (SELECT r."Id" FROM "Rooms" r JOIN "Buildings" b ON r."BuildingId"=b."Id" WHERE b."OwnerId" IN ({ids}))'
            if table == 'Occupancies' else f'"BuildingId" IN (SELECT "Id" FROM "Buildings" WHERE "OwnerId" IN ({ids}))')
        statements.append(f'DELETE FROM "{table}" WHERE {where};')
    keys = ','.join("'" + hashlib.sha256((o + '@loadtest.invalid').encode()).hexdigest().upper() + "'" for o in owners)
    statements += [f'DELETE FROM "LoginThrottles" WHERE "Key" IN ({keys});', 'COMMIT;',
                   f'SELECT count(*) FROM "Users" WHERE "Id" IN ({ids});']
    assert sql('\n'.join(statements)).strip() == '0', 'Fixture cleanup verification failed'


def resources():
    # Fixed read-only probe; no container environment or user data in output.
    code = """import json, pathlib, time
def cpu():
 v=list(map(int,pathlib.Path('/proc/stat').read_text().splitlines()[0].split()[1:])); return sum(v[:8]),v[3]+v[4]
a=cpu(); time.sleep(.3); b=cpu()
m=dict((x.split(':')[0],int(x.split()[1])) for x in pathlib.Path('/proc/meminfo').read_text().splitlines())
print(json.dumps({'cpu_pct':round(100*(1-(b[1]-a[1])/(b[0]-a[0])),1),'available_mb':m['MemAvailable']//1024}))
"""
    value = json.loads(remote(['python3', '-c', code]))
    value['containers'] = remote(['sudo', '-n', 'docker', 'stats', '--no-stream', '--format',
                                 '{{.Name}} CPU={{.CPUPerc}} MEM={{.MemUsage}}']).strip().splitlines()
    return value


def stage(users, seconds, cookies, out, label):
    stop = threading.Event()
    start = time.monotonic()
    samples, probes = [], []
    lock = threading.Lock()
    reason = None

    def worker(index):
        rng = random.Random(index)
        client = connection()
        cookie = cookies[index % len(cookies)]
        try:
            if stop.wait(rng.uniform(0,10)):
                return
            while not stop.is_set() and time.monotonic() - start < seconds:
                cycle = time.monotonic()
                for _ in range(3):
                    if stop.is_set():
                        return
                    path = rng.choices(PATHS, weights=(25,30,30,10,5))[0]
                    status, elapsed, body, _ = request(client, path, cookie)
                    # A 200 error page isn't a valid API read.
                    if status == 200:
                        try:
                            assert isinstance(json.loads(body), (dict, list))
                        except (ValueError, AssertionError):
                            status = -1
                    with lock:
                        samples.append((time.monotonic()-start,path,status,elapsed))
                stop.wait(max(0,10-(time.monotonic()-cycle)))
        finally:
            client.close()

    with concurrent.futures.ThreadPoolExecutor(max_workers=users) as pool:
        futures = []
        try:
            futures = [pool.submit(worker, i) for i in range(users)]
            while time.monotonic()-start < seconds:
                if stop.wait(5):
                    break
                client = connection()
                health, health_ms, _, _ = request(client, '/api/health')
                client.close()
                resource = resources()
                probe = {'at_s':round(time.monotonic()-start,1), 'health':health,
                         'health_ms':round(health_ms,1), **resource}
                probes.append(probe)
                with lock:
                    recent = [s for s in samples if s[0] > time.monotonic()-start-10]
                errors = sum(s[2] != 200 for s in recent)
                p95 = percentile([s[3] for s in recent], .95)
                print(json.dumps({'stage':label,'users':users, 'recent_requests':len(recent),
                                  'p95_ms':p95, 'errors':errors, **probe}), flush=True)
                if health != 200 or health_ms > 1000 or resource['available_mb'] < 250:
                    reason = 'health_or_memory_guard'
                elif len(probes) >= 2 and all(p['cpu_pct'] >= 90 for p in probes[-2:]):
                    reason = 'host_cpu_guard'
                elif len(recent) >= 50 and (errors/len(recent) >= .01 or p95 > 1000):
                    reason = 'latency_or_error_guard'
                if reason:
                    break
        finally:
            stop.set()
        for future in futures:
            future.result()
    elapsed = time.monotonic()-start
    # Exclude ramp-in and the observer's final SSH probe/drain from the rate window.
    window_end = min(seconds, elapsed)
    steady = [s for s in samples if 10 <= s[0] <= window_end]
    statuses = Counter(str(s[2]) for s in steady)
    result = {'label':label,'users':users,'target_rps':users*.3,'duration_s':round(elapsed,2),
              'steady_requests':len(steady),'steady_window_s':round(max(0,window_end-10),2),
              'steady_rps':round(len(steady)/max(.001,window_end-10),2),
              'p50_ms':percentile([s[3] for s in steady],.5),
              'p95_ms':percentile([s[3] for s in steady],.95),
              'p99_ms':percentile([s[3] for s in steady],.99), 'statuses':dict(statuses),
              'stop_reason':reason, 'probes':probes, 'endpoints':{}}
    for path in PATHS:
        subset = [s for s in steady if s[1] == path]
        result['endpoints'][path] = {'requests':len(subset),
                                     'p95_ms':percentile([s[3] for s in subset],.95)}
    result['passed'] = bool(steady) and reason is None and statuses.get('200',0)/len(steady) > .99 and result['p95_ms'] <= 1000
    # Raw timings contain only fixed paths/statuses; no cookies, tokens or response bodies.
    out.write(json.dumps({'result':result,'samples':samples})+'\n'); out.flush()
    print('RESULT ' + json.dumps(result), flush=True)
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--self-check', action='store_true')
    parser.add_argument('--allow-live-fixture', action='store_true')
    parser.add_argument('--distinct-owners', action='store_true',
                        help='200 separate owners/sessions; stage logins at <10/min; ramp to 200')
    parser.add_argument('--temporary-auth-limit', action='store_true',
                        help='Explicitly authorized only: temporarily raise live auth limit for setup')
    parser.add_argument('--max-owners', type=int, choices=(400,800),
                        help='Find read capacity with distinct owners, refining the failure boundary in steps of 25')
    parser.add_argument('--output', type=Path, default=Path('artifacts/stress-live.jsonl'))
    args = parser.parse_args()
    if args.self_check:
        assert percentile([4,1,3,2],.95) == 4
        assert percentile([], .95) is None
        assert all(p.startswith('/api/') and '://' not in p and '..' not in p for p in PATHS)
        assert "DELETE FROM" not in fixture_sql([str(uuid.uuid4())], 'hash')
        class FakeConnection:
            def request(self, method, path, **kwargs):
                assert method == 'GET' and path in PATHS
            def getresponse(self):
                return self
            def read(self):
                return b'{}'
            def getheader(self, name, default):
                return default
            status = 200
        assert request(FakeConnection(), PATHS[0])[0] == 200
        print('PASS: percentile, fixed read targets, GET-only workload, additive fixture SQL')
        return
    if not args.allow_live_fixture:
        parser.error('Requires --allow-live-fixture: this targets the real Roomie service.')
    owner_count = args.max_owners or (200 if args.distinct_owners else 8)
    distinct_owners = bool(args.distinct_owners or args.max_owners)
    owners = [str(uuid.uuid4()) for _ in range(owner_count)]
    password = secrets.token_urlsafe(24)
    salt = secrets.token_bytes(16)
    password_hash = base64.b64encode(b'\x01'+struct.pack('>III',2,100000,16)+salt+
                                    hashlib.pbkdf2_hmac('sha512',password.encode(),salt,100000,32)).decode()
    args.output.parent.mkdir(parents=True, exist_ok=True)
    # A private recovery manifest permits exact cleanup if interrupted or SSH fails.
    manifest = args.output.with_suffix('.fixture.json')
    manifest.write_text(json.dumps({'owner_ids':owners,'created_at':time.time()}))
    manifest.chmod(0o600)
    auth_changed = False
    try:
        if args.temporary_auth_limit:
            auth_changed = True
            temporary_auth_limit(True)
            await_health()
            print('TEMPORARY AUTH LIMIT: 1000/min; host rollback timer armed for 15 minutes', flush=True)
        sql(fixture_sql(owners,password_hash))
        cookies = []
        previous_login = None
        for owner in owners:
            if distinct_owners and not args.temporary_auth_limit and previous_login is not None:
                time.sleep(max(0,6.5-(time.monotonic()-previous_login)))
            previous_login = time.monotonic()
            client = connection()
            status, _, body, header = request(client,'/api/auth/login',payload={
                'email':owner+'@loadtest.invalid','password':password})
            client.close()
            if status != 200 or not header:
                raise RuntimeError(f'Fixture login failed: HTTP {status}')
            assert json.loads(body)['id'] == owner, 'Login owner mismatch'
            cookies.append(header.split(';',1)[0])
            if len(cookies) % 8 == 0 or len(cookies) == owner_count:
                client = connection()
                health, health_ms, _, _ = request(client,'/api/health'); client.close()
                assert health == 200 and health_ms <= 1000, 'Health guard during login setup'
                print(f'LOGIN SETUP: {len(cookies)}/{owner_count} unique owners; health={health}; ms={health_ms:.1f}',flush=True)
        assert len(set(cookies)) == owner_count, 'Duplicate login sessions'
        if auth_changed:
            temporary_auth_limit(False)
            auth_changed = False
            await_health()
            print('AUTH LIMIT RESTORED: 10/min before GET workload', flush=True)
        for path in PATHS:
            client = connection()
            status, _, body, _ = request(client,path,cookies[0]); client.close()
            assert status == 200 and isinstance(json.loads(body),(dict,list)), 'Preflight failed'
            value = json.loads(body)
            if path == '/api/rooms':
                assert len(value) == 20, 'Fixture room count mismatch'
            if path.startswith('/api/invoices?'):
                assert value['total'] == 240, 'Fixture invoice count mismatch'
        with args.output.open('w') as out:
            out.write(json.dumps({'metadata':{'host':HOST,'owners':owner_count,'distinct_owners':distinct_owners,'rooms_per_owner':20,
                       'invoices_per_owner':240,'profile':'3 reads / 10 seconds / virtual user',
                       'started_at':time.time(),'baseline':resources()}})+'\n'); out.flush()
            best = None
            levels = ([n for n in (100,200,250,300,350,400,500,600,800) if n <= owner_count]
                      if args.max_owners else (25,50,100,200) if distinct_owners else (10,25,50,100,200,400,800))
            failed = None
            for users in levels:
                result = stage(users,40,cookies,out,'ramp')
                if not result['passed']:
                    failed = users
                    break
                best = users
            if args.max_owners and best and failed:
                time.sleep(10)
                for users in range(best+25,failed,25):
                    result = stage(users,40,cookies,out,'refine')
                    if not result['passed']:
                        break
                    best = users
            if best:
                # Recover before verifying sustained traffic, even if the last stage stopped.
                time.sleep(10)
                result = stage(best,120,cookies,out,'sustain')
                while args.max_owners and not result['passed'] and best > 25:
                    best -= 25
                    time.sleep(10)
                    result = stage(best,120,cookies,out,'sustain')
    finally:
        try:
            if auth_changed:
                temporary_auth_limit(False)
                await_health()
        finally:
            # Also verify cleanup if SSH timed out: the remote transaction may have committed.
            cleanup(owners)
            manifest.write_text(json.dumps({'owner_ids':owners,'cleanup_verified':True,'finished_at':time.time()}))
            print(f'CLEANUP verified: all {owner_count} synthetic owners and scoped fixtures removed.', flush=True)


if __name__ == '__main__':
    main()
