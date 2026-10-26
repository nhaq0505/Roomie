"""Failure isolation: no rotation after upload/check failure; finite subprocess calls."""
import importlib.util
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import patch
spec=importlib.util.spec_from_file_location('drive_sync',Path(__file__).resolve().parents[1]/'deploy/rehouse-drive-sync.py')
sync=importlib.util.module_from_spec(spec);spec.loader.exec_module(sync)

class BackupSafetyTests(unittest.TestCase):
    def test_subprocess_timeout_is_propagated(self):
        with patch.object(sync.subprocess,'run',side_effect=subprocess.TimeoutExpired('rclone',300)) as run:
            with self.assertRaises(subprocess.TimeoutExpired):sync.rclone('check','local','remote','--download')
            self.assertEqual(run.call_args.kwargs['timeout'],300)
    def test_failed_verification_cannot_reach_retention(self):
        class FakePath:
            def __init__(self,value): self.value=str(value);self.name=self.value.rsplit('/',1)[-1];self.parent=Path('/var/backups/rehouse')
            def resolve(self,strict=True):return self
            def __truediv__(self,name):return FakePath(self.value+'/'+name)
            def is_file(self):return True
            def is_symlink(self):return False
            def stat(self):return type('Stat',(),{'st_size':123})()
            def read_text(self):return 'approved-folder'
            def __str__(self):return self.value
        calls=[]
        def fake_rclone(*args,**kwargs):
            calls.append(args[0])
            if args[0]=='check':raise subprocess.CalledProcessError(1,'rclone')
        with patch.object(sync,'Path',side_effect=lambda p: Path(p) if str(p)=='/var/backups/rehouse' else FakePath(p)),patch.object(sync.os,'geteuid',return_value=0,create=True),patch.object(sync.sys,'argv',['sync','/var/backups/rehouse/rehouse-20261016T000000Z-Ab1234']),patch('configparser.ConfigParser.read'),patch('configparser.ConfigParser.get',side_effect=lambda section,key,**kw: {'type':'crypt' if section=='roomie_backup' else 'drive','remote':'roomie_drive:encrypted','root_folder_id':'approved-folder'}[key]),patch.object(sync,'rclone',side_effect=fake_rclone):
            with self.assertRaises(subprocess.CalledProcessError):sync.main()
        self.assertEqual(calls,['copy','check'])
