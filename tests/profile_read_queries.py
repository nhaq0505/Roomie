#!/usr/bin/env python3
"""Profile deployed image on VPS using disposable, outbound-blocked API/Postgres.

No production data/config changes. Reuses the synthetic fixture from the read load test.
"""
import argparse
import base64
import hashlib
import inspect
import json
from pathlib import Path
import re
import runpy
import secrets
import struct
import subprocess
import uuid


def parse_queries(logs):
    queries = []
    last = {}
    for match in re.finditer(r'^PROFILE (\w+) \[(\d+)\] (LOG|DETAIL):\s+([\s\S]*?)(?=^PROFILE |\Z)', logs, re.M):
        user, pid, level, message = match.groups()
        if user != 'profile_api':
            continue
        if level == 'LOG':
            found = re.search(r'duration: [\d.]+ ms\s+execute [^:]*: (SELECT[\s\S]*)', message)
            if found:
                parts = re.split(r'\n\s*DETAIL:\s*parameters:', found[1], maxsplit=1, flags=re.I)
                row = {'sql': parts[0].strip(), 'parameters': {}}
                if len(parts) == 2:
                    row['parameters'] = dict(re.findall(r"\$(\d+) = ('(?:[^']|'')*'|NULL)(?=, \$|\s*$)", parts[1].strip()))
                queries.append(row)
                last[pid] = row
            else:
                last.pop(pid, None)
        elif pid in last and message.lower().startswith('parameters:'):
            last[pid]['parameters'] = dict(re.findall(r"\$(\d+) = ('(?:[^']|'')*'|NULL)(?=, \$|\s*$)", message.strip()))
    return queries


def bind_query(row):
    return re.sub(r'\$(\d+)\b', lambda m: row['parameters'][m[1]], row['sql'])


REMOTE = r'''
import http.client, json, pathlib, re, subprocess, time

def run(args, data=None):
 p=subprocess.run(args,input=data,text=True,capture_output=True,timeout=90)
 if p.returncode:
  # Never echo argv/input, which can contain disposable credentials.
  raise RuntimeError('Command failed: '+p.stderr[-1200:])
 return p.stdout

def db(statement):
 return run(['docker','exec','-i',DB,'psql','-X','-qAt','-v','ON_ERROR_STOP=1','-U','postgres','-d','profile'],statement)

def api(path,payload=None,cookie=''):
 c=http.client.HTTPConnection(API_HOST,8080,timeout=4)
 headers={'Accept':'application/json','Origin':'http://127.0.0.1:18081'}
 if cookie: headers['Cookie']=cookie
 if payload is not None: headers['Content-Type']='application/json'
 t=time.monotonic()
 try:
  c.request('POST' if payload is not None else 'GET',path,json.dumps(payload) if payload is not None else None,headers)
  r=c.getresponse(); body=r.read(); status=r.status; cookie=r.getheader('Set-Cookie','').split(';',1)[0]
  assert status==200, f'API {path}: HTTP {status}'
  return json.loads(body),(time.monotonic()-t)*1000,cookie
 finally: c.close()

def live_health():
 c=http.client.HTTPConnection('127.0.0.1',8080,timeout=4)
 t=time.monotonic()
 try:
  c.request('GET','/api/health'); r=c.getresponse(); r.read()
  assert r.status==200 and time.monotonic()-t<1, 'Live health guard'
 finally: c.close()
 mem=dict((x.split(':')[0],int(x.split()[1])) for x in pathlib.Path('/proc/meminfo').read_text().splitlines())
 assert mem['MemAvailable']>300*1024, 'Live memory guard'

def logs():
 p=subprocess.run(['docker','logs',DB],text=True,capture_output=True,check=True)
 return p.stdout+p.stderr

app_image=run(['docker','inspect','--format','{{.Config.Image}}','rehouse-app-1']).strip()
db_image=run(['docker','inspect','--format','{{.Config.Image}}','rehouse-db-1']).strip()
assert ('ghcr.io/nhaq0505/rehouse' in app_image or 'rehouse' in app_image) and 'sha256:' in app_image
result={'image':app_image,'dataset':{'owners':400,'rooms':8000,'invoices':96000,'readings':0},'endpoints':{}}
try:
 live_health()
 run(['docker','network','create','--internal',NET])
 run(['docker','run','-d','--pull=never','--name',DB,'--network',NET,'--memory','384m','--cpus','.65',
      '-e','POSTGRES_DB=profile','-e','POSTGRES_PASSWORD='+PASSWORD,db_image,
      '-c','shared_preload_libraries=pg_stat_statements','-c','shared_buffers=96MB','-c','work_mem=2MB',
      '-c','max_connections=20','-c','track_io_timing=on','-c','pg_stat_statements.track_planning=on',
      '-c','log_line_prefix=PROFILE %u [%p] '])
 for _ in range(40):
  if subprocess.run(['docker','exec',DB,'pg_isready','-U','postgres','-d','profile'],stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL).returncode==0: break
  time.sleep(.5)
 env={'ASPNETCORE_ENVIRONMENT':'Production','DatabaseProvider':'Postgres',
      'ConnectionStrings__DefaultConnection':f'Host={DB};Database=profile;Username=postgres;Password={PASSWORD};Maximum Pool Size=15',
      'Database__MigrateOnly':'true','Zalo__Enabled':'false','EnableInvoiceSweep':'false','EnablePaymentReconciliation':'false',
      'Monitoring__Schedule__IncidentChecksEnabled':'false','Monitoring__Schedule__MaintenanceEnabled':'false','Monitoring__Alerts__Enabled':'false',
      'HTTP_PROXY':'http://127.0.0.1:9','HTTPS_PROXY':'http://127.0.0.1:9','ALL_PROXY':'http://127.0.0.1:9',
      'NO_PROXY':'localhost,127.0.0.1','DataProtection__KeysPath':'/tmp/profile-keys','AuthRequestsPerMinute':'10',
      'PublicBaseUrl':'https://roomie.io.vn'}
 args=['docker','run','--pull=never','--network',NET,'--memory','640m','--cpus','1.25']
 for k,v in env.items(): args+=['-e',k+'='+v]
 run(args+['--rm','--name',API+'-migrate',app_image])
 db(FIXTURE)
 db('INSERT INTO "AuthSessions" ("Id","UserId","CreatedAt","ExpiresAt") SELECT gen_random_uuid()::text,"Id",now(),now()+interval \'12 hours\' FROM "Users" WHERE "Id"<>\''+OWNER+'\'::uuid;')
 db('CREATE EXTENSION pg_stat_statements; CREATE USER profile_api PASSWORD '+"'"+PASSWORD+"'; GRANT CONNECT ON DATABASE profile TO profile_api; GRANT USAGE ON SCHEMA public TO profile_api; GRANT SELECT,INSERT,UPDATE,DELETE ON ALL TABLES IN SCHEMA public TO profile_api; GRANT USAGE,SELECT ON ALL SEQUENCES IN SCHEMA public TO profile_api; ALTER ROLE profile_api SET log_min_duration_statement=0; ALTER ROLE profile_api SET log_parameter_max_length=-1; ANALYZE;")
 env['Database__MigrateOnly']='false'
 env['ConnectionStrings__DefaultConnection']=f'Host={DB};Database=profile;Username=profile_api;Password={PASSWORD};Maximum Pool Size=15'
 args=['docker','run','-d','--pull=never','--name',API,'--network',NET,'--memory','640m','--cpus','1.25']
 for k,v in env.items(): args+=['-e',k+'='+v]
 run(args+[app_image])
 API_HOST=json.loads(run(['docker','inspect',API]))[0]['NetworkSettings']['Networks'][NET]['IPAddress']
 for _ in range(40):
  try: api('/api/health'); break
  except (OSError,AssertionError,http.client.HTTPException): time.sleep(.5)
 else:
  p=subprocess.run(['docker','logs','--tail','30',API],text=True,capture_output=True)
  raise RuntimeError('Disposable API startup failed: '+(p.stdout+p.stderr).replace(PASSWORD,'<redacted>'))
 _,_,cookie=api('/api/auth/login',{'email':OWNER+'@loadtest.invalid','password':PASSWORD})
 assert cookie
 for path in PATHS:
  live_health()
  for _ in range(3): api(path,cookie=cookie)
  db('SELECT pg_stat_statements_reset();')
  old=logs()
  latencies=[]
  for _ in range(20):
   _,ms,_=api(path,cookie=cookie); latencies.append(ms)
  captured=parse_queries(logs()[len(old):])
  stats=json.loads(db("SELECT coalesce(json_agg(t),'[]'::json) FROM (SELECT queryid::text,query,calls,plans,total_plan_time,mean_plan_time,total_exec_time,mean_exec_time,rows,shared_blks_hit,shared_blks_read,temp_blks_written FROM pg_stat_statements WHERE userid=(SELECT oid FROM pg_roles WHERE rolname='profile_api') AND query LIKE 'SELECT%' AND query NOT LIKE '%\"PasswordRecoveries\"%' ORDER BY total_plan_time+total_exec_time DESC) t;"))
  plans=[]; seen=set()
  for q in captured:
   if q['sql'] in seen or '"PasswordRecoveries"' in q['sql']: continue
   if any(n not in q['parameters'] for n in re.findall(r'\$(\d+)\b',q['sql'])): continue
   seen.add(q['sql'])
   bound=bind_query(q)
   plan=json.loads(db("BEGIN READ ONLY; SET LOCAL statement_timeout='3s'; EXPLAIN (ANALYZE,BUFFERS,FORMAT JSON) "+bound+"; ROLLBACK;"))
   # Do not persist even disposable session bearer IDs from an auth plan.
   text=json.dumps(plan)
   for secret in q['parameters'].values():
    if '"AuthSessions"' in q['sql'] and len(secret)>30: text=text.replace(secret[1:-1],'<fixture>')
   plans.append({'sql':q['sql'],'plan':json.loads(text)})
  assert plans, 'No bound SQL captured: '+re.sub(r"'(?:[^']|'')*'", "'<redacted>'",logs()[len(old):][:1400])
  result['endpoints'][path]={'requests':20,'http_mean_ms':sum(latencies)/20,'sql_calls':sum(x['calls'] for x in stats),'sql_total_ms':sum(x['total_exec_time'] for x in stats),'sql_plan_ms':sum(x['total_plan_time'] for x in stats),'stats':stats,'plans':plans}
  print('PROGRESS '+path+' '+json.dumps({k:result['endpoints'][path][k] for k in ['sql_calls','sql_total_ms','sql_plan_ms','http_mean_ms']})+' plans='+str(len(plans)),flush=True)
 live_health()
finally:
 # Only unique resources created for this profiling run; production containers are untouched.
 for name in [API,API+'-migrate',DB]:
  subprocess.run(['docker','rm','-fv',name],stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
 subprocess.run(['docker','network','rm',NET],stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
 remaining=run(['docker','ps','-a','--format','{{.Names}}']).splitlines()
 assert not any(n in remaining for n in [API,API+'-migrate',DB]), 'Temporary container cleanup failed'
 print('CLEANUP verified: isolated API/Postgres removed',flush=True)
print('PROFILE_JSON '+json.dumps(result),flush=True)
'''


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--self-check', action='store_true')
    parser.add_argument('--run-vps', action='store_true')
    parser.add_argument('--output', type=Path, default=Path('artifacts/query-profile-20261022.json'))
    args = parser.parse_args()
    if args.self_check:
        logs = "PROFILE profile_api [1] LOG:  duration: 1.2 ms  execute <unnamed>: SELECT $1::text, $2::int\nPROFILE profile_api [1] DETAIL:  parameters: $1 = 'a''b', $2 = '4'\n"
        rows = parse_queries(logs)
        assert len(rows) == 1 and bind_query(rows[0]) == "SELECT 'a''b'::text, '4'::int"
        assert parse_queries(logs.replace('profile_api','postgres')) == []
        inline = logs.replace('\nPROFILE profile_api [1] DETAIL:', '\n\tDETAIL:')
        assert bind_query(parse_queries(inline)[0]) == "SELECT 'a''b'::text, '4'::int"
        assert bind_query(parse_queries(logs.replace('parameters:','Parameters:'))[0]) == "SELECT 'a''b'::text, '4'::int"
        compile(REMOTE, '<remote>', 'exec')
        print('PASS: SQL/parameter capture, quoting, role filter, remote syntax')
        return
    if not args.run_vps:
        parser.error('Requires --run-vps to create disposable profiling containers on the live VPS')
    load = runpy.run_path(str(Path(__file__).with_name('load_live_readonly.py')))
    owners = [str(uuid.uuid4()) for _ in range(400)]
    password = secrets.token_hex(24)
    salt = secrets.token_bytes(16)
    hashed = base64.b64encode(b'\x01'+struct.pack('>III',2,100000,16)+salt+hashlib.pbkdf2_hmac('sha512',password.encode(),salt,100000,32)).decode()
    name = 'roomie-query-profile-'+secrets.token_hex(4)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.with_suffix('.resources.json').write_text(json.dumps({'api':name+'-api','db':name+'-db','network':name}))
    variables = {'API':name+'-api','DB':name+'-db','NET':name,'PASSWORD':password,'OWNER':owners[0],
                 'FIXTURE':load['fixture_sql'](owners,hashed),'PATHS':load['PATHS']}
    code = '\n'.join(k+'='+repr(v) for k,v in variables.items())+'\n'
    code += 'import re\n'+inspect.getsource(parse_queries)+inspect.getsource(bind_query)+REMOTE
    process = subprocess.Popen(load['SSH']+['sudo -n python3 -'],stdin=subprocess.PIPE,stdout=subprocess.PIPE,text=True)
    process.stdin.write(code); process.stdin.close()
    for line in process.stdout:
        if line.startswith('PROFILE_JSON '):
            value = json.loads(line[len('PROFILE_JSON '):])
            args.output.write_text(json.dumps(value,indent=2))
            print('SAVED '+str(args.output),flush=True)
        else:
            print(line.rstrip(),flush=True)
    assert process.wait()==0, 'Remote profiling failed; check resources manifest for recovery'


if __name__ == '__main__':
    main()
