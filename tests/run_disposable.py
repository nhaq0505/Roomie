#!/usr/bin/env python3
"""Owns only fresh /tmp APIs; never starts against the Development or live DB."""
import argparse
import os
from pathlib import Path
import re
import secrets
import socket
import subprocess
import sys
import tempfile
import time
import urllib.request

ROOT = Path(__file__).resolve().parent.parent

def port():
    with socket.socket() as s:
        s.bind(('127.0.0.1', 0)); return s.getsockname()[1]

def wait(url, process):
    for _ in range(100):
        if process.poll() is not None: raise RuntimeError('Disposable API stopped; inspect private fixture log')
        try:
            with urllib.request.urlopen(url+'/api/health', timeout=1) as r:
                if r.status == 200: return
        except OSError: pass
        time.sleep(.2)
    raise RuntimeError('Disposable API did not become healthy')

def main():
    parser = argparse.ArgumentParser(); parser.add_argument('--app'); parser.add_argument('--image'); parser.add_argument('--provider', choices=['postgres','otp-postgres'], required=True)
    args = parser.parse_args(); app = Path(args.app).resolve() if args.app else ROOT
    with tempfile.TemporaryDirectory(prefix='roomie-ci-', dir='/tmp') as folder:
        temp = Path(folder); api_port = port(); smtp_port = port(); base = f'http://127.0.0.1:{api_port}'
        outbox = temp/'outbox'; children=[]
        connection = os.environ.get('TEST_HTTP_POSTGRES_CONNECTION', '')
        container = os.environ.get('TEST_HTTP_POSTGRES_CONTAINER', '')
        if not container or not re.fullmatch(r'[A-Za-z0-9_-]+', container):
            raise RuntimeError('Invalid disposable container')
        if container == 'rehouse-pg-migration-db':
            raise RuntimeError('Refusing dev container rehouse-pg-migration-db')
        fields = dict(part.split('=', 1) for part in connection.split(';') if '=' in part)
        bootstrap_db = fields.get('Database', '')
        if not bootstrap_db.startswith('roomie_qa_'):
            raise RuntimeError('Database must start with roomie_qa_')
        host = fields.get('Host', '')
        if host not in ('127.0.0.1', 'localhost', '::1'):
            raise RuntimeError('Requires loopback disposable host')
        port_num = fields.get('Port', '')
        if port_num == '15432':
            raise RuntimeError('Refusing dev port 15432')
        user = fields.get('Username', '')
        if not user:
            raise RuntimeError('Missing Username in connection string')
        for _ in range(100):
            if subprocess.run(['docker','exec',container,'pg_isready','-h','127.0.0.1','-U',user],stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL).returncode==0:break
            time.sleep(.2)
        else:raise RuntimeError('Disposable PostgreSQL readiness timeout')
        database = 'roomie_qa_' + secrets.token_hex(8)
        subprocess.run(['docker', 'exec', container, 'createdb', '-U', user, database], check=True)
        try:
            fields['Database'] = database
            pg_conn = ';'.join(k + '=' + v for k, v in fields.items())
            env = {**os.environ, 'ASPNETCORE_ENVIRONMENT':'Development','DatabaseProvider':'Postgres',
                'ConnectionStrings__DefaultConnection':pg_conn, 'DataProtection__KeysPath':str(temp/'keys'),
                'ASPNETCORE_URLS':base,'PublicBaseUrl':base,'WebhookBaseUrl':base,'SeedDemo':'true',
                'Zalo__Enabled':'false','EnableInvoiceSweep':'false','EnablePaymentReconciliation':'false','EmailVerification__Enabled':'false',
                'RequireVerifiedPaymentForTenantLink':'false','Seo__AllowIndexing':'true','AuthRequestsPerMinute':'1000',
                'HTTP_PROXY':'http://127.0.0.1:9','HTTPS_PROXY':'http://127.0.0.1:9','ALL_PROXY':'http://127.0.0.1:9','NO_PROXY':'127.0.0.1,localhost',
                'API_BASE_URL':base,'API_ORIGIN':base,'SEO_BASE_URL':base,
                'TEST_HTTP_POSTGRES_CONNECTION':pg_conn, 'TEST_HTTP_POSTGRES_CONTAINER':container}
            if args.provider == 'otp-postgres':
                outbox.mkdir(mode=0o700); env.update({'EmailVerification__Enabled':'true','SeedDemo':'false',
                    'Email__FromAddress':'otp@example.invalid','Email__Smtp__Host':'127.0.0.1','Email__Smtp__Port':str(smtp_port),'Email__Smtp__Security':'None',
                    'TEST_OTP_OUTBOX':str(outbox)})
                children.append(subprocess.Popen([sys.executable,str(ROOT/'tests/otp_smtp_fixture.py'),'--outbox',str(outbox),'--port',str(smtp_port)], env=env))
            with (temp/'api.log').open('w') as log:
                try:
                    if args.image:
                        command = ['docker','run','--rm','--network','host','--user',f'{os.getuid()}:{os.getgid()}', '--volume',f'{temp}:{temp}']
                        for k,v in env.items():
                            if k not in os.environ or os.environ.get(k) != v:
                                command += ['--env',f'{k}={v}']
                        command += [args.image]
                    else: command = ['dotnet',str(app/'RoomRental.Api.dll')]
                    api = subprocess.Popen(command,cwd=app,env=env,stdout=log,stderr=log); children.append(api)
                    wait(base,api)
                    if args.provider == 'otp-postgres': names=['test_registration_otp','test_password_recovery']
                    else: names=['test_api_integration','test_billing_upgrade','test_reports_archive','test_account_occupancy_dashboard','test_operational_hardening','test_zalo_http','test_payment_modes','test_payos_fixture','test_seo_http']
                    env['PYTHONPATH']=str(ROOT/'tests')
                    env['EXPECTED_SKIPS']='0'
                    code='''import unittest,sys,os
suite=unittest.defaultTestLoader.loadTestsFromNames(sys.argv[1:])
r=unittest.TextTestRunner(verbosity=2).run(suite)
print('FIXTURE SUMMARY: run=%d skipped=%d'%(r.testsRun,len(r.skipped)))
assert r.testsRun >= 3, 'No meaningful fixture tests ran'
assert len(r.skipped)==int(os.environ['EXPECTED_SKIPS']), 'Unexpected skipped fixture tests'
sys.exit(not r.wasSuccessful())
'''
                    result=subprocess.run([sys.executable,'-c',code,*names],cwd=ROOT,env=env)
                    if result.returncode:
                        print(log.name, 'fixture failed; last safe application log lines:')
                        raise RuntimeError('Disposable regression failed')
                finally:
                    for child in reversed(children):
                        child.terminate()
                        try: child.wait(timeout=10)
                        except subprocess.TimeoutExpired: child.kill(); child.wait()
        finally:
            subprocess.run(['docker', 'exec', container, 'dropdb', '--force', '-U', user, database], check=True)

if __name__ == '__main__': main()
