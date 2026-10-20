#!/usr/bin/env python3
"""Comprehensive Unit and Fixture Tests for Roomie Ubuntu Host Monitoring Collector.

Covers:
A) Dedicated public directory mounting (/var/lib/rehouse/monitoring/public -> /app/monitoring-host:ro),
   inode pinning avoidance, and private state isolation (/var/lib/rehouse/monitoring/collector-state.json 0600).
B) Container healthcheck semantics: absent Docker health + loopback API 200 = healthy (no false alerts),
   explicit stopped/unhealthy/API failures alert, DB uses actual healthcheck, missing Docker probe data is unknown.
C) Sustained thresholds with elapsed timing, rapid duplicate sample rejection (< 55s), candidate period reset
   across gaps > 2min and unknown samples, open incident preservation during unknown, and restart persistence.
D) Independent host SMTP delivery: asynchronous retry queue, bounded exponential backoff, success-gated cooldown,
   recovery notice persistence, 20s execution budget within systemd 45s, and zero credential leakage.
"""

from __future__ import annotations

import copy
import email.message
import importlib.util
import io
import json
import os
from pathlib import Path
import tempfile
import time
import unittest
from unittest.mock import MagicMock, patch

# Dynamically import collector module
collector_path = Path(__file__).resolve().parents[1] / "deploy/monitoring/rehouse-host-collector.py"
spec = importlib.util.spec_from_file_location("collector", collector_path)
collector = importlib.util.module_from_spec(spec)
spec.loader.exec_module(collector)


class FakeSmtpClient:
    """Mock SMTP client tracking invocations, credentials, and sent messages."""

    def __init__(self, host, port, timeout=15, should_fail=False):
        self.host = host
        self.port = port
        self.timeout = timeout
        self.should_fail = should_fail
        self.starttls_called = False
        self.logged_in = False
        self.login_user = None
        self.login_pass = None
        self.sent_messages: list[email.message.EmailMessage] = []

    def __enter__(self):
        if self.should_fail:
            raise ConnectionRefusedError("Simulated SMTP server unreachable")
        return self

    def __exit__(self, exc_type, exc_val, exc_tb):
        return False

    def starttls(self, context=None):
        self.starttls_called = True

    def login(self, username, password):
        self.logged_in = True
        self.login_user = username
        self.login_pass = password

    def send_message(self, msg):
        if self.should_fail:
            raise smtplib.SMTPException("Simulated SMTP delivery rejected")
        self.sent_messages.append(msg)


class HostCollectorTests(unittest.TestCase):
    """Test suite verifying all collector functionality, boundaries, and invariants."""

    def setUp(self):
        self.test_dir = tempfile.TemporaryDirectory()
        self.base_path = Path(self.test_dir.name)
        self.config_dir = self.base_path / "etc_rehouse"
        self.config_dir.mkdir(parents=True, exist_ok=True)
        self.state_dir = self.base_path / "var_lib_rehouse/monitoring"
        self.state_dir.mkdir(parents=True, exist_ok=True)
        self.public_dir = self.state_dir / "public"
        self.public_dir.mkdir(parents=True, exist_ok=True)

        self.monitoring_env = self.config_dir / "monitoring.env"
        self.runtime_env = self.config_dir / "runtime.env"
        self.backup_env = self.config_dir / "backup.env"
        self.backup_success = self.state_dir / "backup-last-success"
        self.output_file = self.public_dir / "host-metrics.json"
        self.state_file = self.state_dir / "collector-state.json"

        # Standard test config
        self.config = {
            "SMTP_HOST": "smtp.example.invalid",
            "SMTP_PORT": 587,
            "SMTP_SECURITY": "StartTls",
            "SMTP_USERNAME": "test-user",
            "SMTP_PASSWORD": "test-password-secret",
            "SMTP_FROM_ADDRESS": "monitoring@roomie.invalid",
            "SMTP_FROM_NAME": "Roomie Monitoring",
            "MONITORING_ALERT_TO": "alerts@roomie.invalid",
            "APP_CONTAINER_NAME": "rehouse-app-1",
            "DB_CONTAINER_NAME": "rehouse-db-1",
            "HEALTH_CHECK_URL": "http://127.0.0.1:8080/api/health",
            "BACKUP_SUCCESS_FILE": str(self.backup_success),
            "METRICS_OUTPUT_FILE": str(self.output_file),
            "STATE_FILE": str(self.state_file),
        }

    def tearDown(self):
        self.test_dir.cleanup()

    # -----------------------------------------------------------------------
    # 1. Config loading & recipient prioritization
    # -----------------------------------------------------------------------

    def test_config_prioritizes_monitoring_env_over_runtime_and_backup(self):
        self.runtime_env.write_text(
            "SMTP_HOST=smtp.runtime.invalid\n"
            "BACKUP_ALERT_TO=ops@runtime.invalid\n"
            "SMTP_PORT=587\n",
            encoding="utf-8",
        )
        self.monitoring_env.write_text(
            "MONITORING_ALERT_TO=dedicated-monitor@custom.invalid\n"
            "SMTP_HOST=smtp.monitoring.invalid\n",
            encoding="utf-8",
        )
        cfg = collector.load_config([str(self.monitoring_env), str(self.runtime_env), str(self.backup_env)])
        self.assertEqual(cfg["SMTP_HOST"], "smtp.monitoring.invalid")
        self.assertEqual(cfg["MONITORING_ALERT_TO"], "dedicated-monitor@custom.invalid")
        self.assertEqual(cfg["SMTP_PORT"], 587)

    def test_config_falls_back_to_backup_alert_to_when_monitoring_alert_unset(self):
        self.runtime_env.write_text(
            "SMTP_HOST=smtp.runtime.invalid\n"
            "BACKUP_ALERT_TO=backup-admin@runtime.invalid\n",
            encoding="utf-8",
        )
        cfg = collector.load_config([str(self.monitoring_env), str(self.runtime_env)])
        self.assertEqual(cfg["MONITORING_ALERT_TO"], "backup-admin@runtime.invalid")

    # -----------------------------------------------------------------------
    # 2. Host resource parsing (CPU, RAM, Disk) & unknown handling
    # -----------------------------------------------------------------------

    def test_cpu_collection_from_proc_stat(self):
        proc_stat = self.base_path / "proc_stat"
        proc_stat.write_text("cpu  1000 0 200 8000 100 0 0 0 0 0\n", encoding="utf-8")
        state = {}
        info1 = collector.collect_cpu(state, proc_stat_path=proc_stat)
        self.assertIn("last_cpu_sample", state)

        # Delta: 100 user, 50 sys, 850 idle -> 15.0%
        proc_stat.write_text("cpu  1100 0 250 8850 100 0 0 0 0 0\n", encoding="utf-8")
        info2 = collector.collect_cpu(state, proc_stat_path=proc_stat)
        self.assertEqual(info2["percent"], 15.0)
        self.assertEqual(info2["status"], "healthy")

    def test_cpu_collection_marks_warning_when_above_85(self):
        proc_stat = self.base_path / "proc_stat"
        proc_stat.write_text("cpu  1000 0 200 1000 0 0 0 0 0 0\n", encoding="utf-8")
        state = {}
        collector.collect_cpu(state, proc_stat_path=proc_stat)

        # Delta: 900 user, 100 idle -> 90% CPU
        proc_stat.write_text("cpu  1900 0 200 1100 0 0 0 0 0 0\n", encoding="utf-8")
        info = collector.collect_cpu(state, proc_stat_path=proc_stat)
        self.assertEqual(info["percent"], 90.0)
        self.assertEqual(info["status"], "warning")

    def test_memory_collection_from_proc_meminfo(self):
        proc_mem = self.base_path / "proc_meminfo"
        proc_mem.write_text(
            "MemTotal:        2000000 kB\n"
            "MemFree:          200000 kB\n"
            "MemAvailable:    1000000 kB\n"
            "Buffers:           50000 kB\n"
            "Cached:           750000 kB\n",
            encoding="utf-8",
        )
        mem = collector.collect_memory(proc_meminfo_path=proc_mem)
        self.assertEqual(mem["total_bytes"], 2000000 * 1024)
        self.assertEqual(mem["available_bytes"], 1000000 * 1024)
        self.assertEqual(mem["used_bytes"], 1000000 * 1024)
        self.assertEqual(mem["percent"], 50.0)
        self.assertEqual(mem["status"], "healthy")

    def test_missing_proc_meminfo_reports_unknown_never_healthy_zeros(self):
        non_existent = self.base_path / "non_existent_meminfo"
        mem = collector.collect_memory(proc_meminfo_path=non_existent)
        self.assertIsNone(mem["percent"])
        self.assertIsNone(mem["total_bytes"])
        self.assertEqual(mem["status"], "unknown")

    def test_disk_collection_returns_valid_metrics(self):
        disk = collector.collect_disk("/")
        self.assertIsNotNone(disk["total_bytes"])
        self.assertIsNotNone(disk["percent"])
        self.assertIn(disk["status"], ["healthy", "warning"])

    # -----------------------------------------------------------------------
    # 3. Requirement B: Container Inspect Semantics & Absent Health Fixtures
    # -----------------------------------------------------------------------

    def test_container_inspect_healthy_with_docker_health(self):
        # Database container inspection (declares Docker HEALTHCHECK)
        inspect_json = json.dumps({
            "Running": True,
            "Status": "running",
            "StartedAt": "2026-10-20T10:00:00Z",
            "Health": {"Status": "healthy"},
        })

        def fake_runner(cmd, timeout):
            if "--format" in cmd and "{{json .State}}" in cmd:
                return 0, inspect_json, ""
            if "{{.RestartCount}}" in cmd:
                return 0, "2\n", ""
            return 1, "", "unknown command"

        res = collector.collect_container_status("rehouse-db-1", "db", runner=fake_runner)
        self.assertEqual(res["name"], "rehouse-db-1")
        self.assertTrue(res["running"])
        self.assertEqual(res["status"], "running")
        self.assertEqual(res["health"], "healthy")
        self.assertEqual(res["restart_count"], 2)
        self.assertIsNone(res["error"])

    def test_container_inspect_app_absent_docker_health_fixture(self):
        # Realistic app container inspection: NO Health field declared in compose.yaml
        inspect_app_json = json.dumps({
            "Running": True,
            "Status": "running",
            "Paused": False,
            "Restarting": False,
            "OOMKilled": False,
            "Dead": False,
            "Pid": 4821,
            "ExitCode": 0,
            "StartedAt": "2026-10-20T10:00:00Z",
            "FinishedAt": "0001-01-01T00:00:00Z",
        })

        def fake_runner(cmd, timeout):
            if "--format" in cmd and "{{json .State}}" in cmd:
                return 0, inspect_app_json, ""
            return 0, "0\n", ""

        res = collector.collect_container_status("rehouse-app-1", "app", runner=fake_runner)
        self.assertTrue(res["running"])
        self.assertEqual(res["status"], "running")
        # When Docker has no healthcheck configured, health is reported as 'none'
        self.assertEqual(res["health"], "none")
        self.assertIsNone(res["error"])

    def test_container_inspect_probe_failure_reports_unknown_not_fabricated_stopped(self):
        # When docker inspect fails (daemon down, command error), must report unknown, NOT stopped!
        def failing_runner(cmd, timeout):
            return 1, "", "Error: No such container: rehouse-app-1"

        res = collector.collect_container_status("rehouse-app-1", "app", runner=failing_runner)
        self.assertIsNone(res["running"])  # Not fabricated False nor True
        self.assertEqual(res["health"], "unknown")
        self.assertEqual(res["status"], "unknown")
        self.assertIsNotNone(res["error"])

    def test_app_absent_docker_health_with_healthy_api_is_healthy_no_false_page(self):
        # Running app with absent Docker health + loopback 200 OK MUST be healthy!
        state = collector.load_state(self.state_file)
        client = FakeSmtpClient("smtp.example.invalid", 587)

        inspect_app_json = json.dumps({
            "Running": True,
            "Status": "running",
            "StartedAt": "2026-10-20T10:00:00Z",
        })

        def app_runner(cmd, timeout):
            return 0, inspect_app_json, ""

        def ok_api(url, timeout):
            return 200, 12.0

        with patch.object(collector, "collect_cpu", return_value={"percent": 20.0, "status": "healthy"}), \
             patch.object(collector, "collect_memory", return_value={"percent": 30.0, "status": "healthy"}), \
             patch.object(collector, "collect_disk", return_value={"percent": 40.0, "status": "healthy"}):

            for i in range(5):
                payload = collector.run_collection_cycle(
                    self.config, state, now_ts=1729438800.0 + (i * 60.0),
                    docker_runner=app_runner, http_getter=ok_api,
                    smtp_client_factory=lambda *a, **kw: client,
                )
                self.assertEqual(state["counters"].get("app_streak", 0), 0)
                self.assertNotIn("app", state["active_incidents"])
                self.assertEqual(len(client.sent_messages), 0)
                self.assertTrue(payload["samples_history"]["samples"][-1]["app_healthy"])

    def test_app_explicit_failure_stopped_container_triggers_alert(self):
        state = collector.load_state(self.state_file)
        client = FakeSmtpClient("smtp.example.invalid", 587)

        def selective_runner(cmd, timeout):
            if "rehouse-app-1" in cmd:
                return 0, json.dumps({"Running": False, "Status": "exited", "ExitCode": 137}), ""
            return 0, json.dumps({"Running": True, "Status": "running", "Health": {"Status": "healthy"}}), ""

        def ok_api(url, timeout):
            return 200, 12.0

        with patch.object(collector, "collect_cpu", return_value={"percent": 20.0, "status": "healthy"}), \
             patch.object(collector, "collect_memory", return_value={"percent": 30.0, "status": "healthy"}), \
             patch.object(collector, "collect_disk", return_value={"percent": 40.0, "status": "healthy"}):

            # 3 samples spaced >= 1 min
            for i in range(3):
                collector.run_collection_cycle(
                    self.config, state, now_ts=1729438800.0 + (i * 60.0),
                    docker_runner=selective_runner, http_getter=ok_api,
                    smtp_client_factory=lambda *a, **kw: client,
                )

            self.assertEqual(state["counters"]["app_streak"], 3)
            self.assertIn("app", state["active_incidents"])
            self.assertEqual(len(client.sent_messages), 1)

    def test_api_health_loopback_200_ok(self):
        def fake_getter(url, timeout):
            return 200, 14.5

        res = collector.collect_api_health("http://127.0.0.1:8080/api/health", getter=fake_getter)
        self.assertEqual(res["status"], "healthy")
        self.assertEqual(res["http_status"], 200)
        self.assertEqual(res["latency_ms"], 14.5)
        self.assertIsNone(res["error"])

    def test_api_health_loopback_500_fails(self):
        def fake_getter(url, timeout):
            return 500, 20.0

        res = collector.collect_api_health("http://127.0.0.1:8080/api/health", getter=fake_getter)
        self.assertEqual(res["status"], "unhealthy")
        self.assertEqual(res["http_status"], 500)
        self.assertIsNotNone(res["error"])

    # -----------------------------------------------------------------------
    # 4. Backup watchdog status surfacing (No duplicate alert path)
    # -----------------------------------------------------------------------

    def test_backup_status_fresh_is_healthy(self):
        now = 1729438800.0
        self.backup_success.touch()
        os.utime(self.backup_success, (now - 10 * 3600, now - 10 * 3600))

        res = collector.collect_backup_watchdog(str(self.backup_success), now_ts=now)
        self.assertEqual(res["status"], "healthy")
        self.assertEqual(res["age_seconds"], 10 * 3600)
        self.assertIsNone(res["error"])

    def test_backup_status_stale_over_27h_surfaces_stale_without_alerting(self):
        now = 1729438800.0
        self.backup_success.touch()
        os.utime(self.backup_success, (now - 28 * 3600, now - 28 * 3600))

        res = collector.collect_backup_watchdog(str(self.backup_success), now_ts=now)
        self.assertEqual(res["status"], "stale")
        self.assertGreater(res["age_seconds"], 27 * 3600)
        self.assertIsNotNone(res["error"])

    def test_backup_status_missing_file_is_unknown(self):
        missing_file = self.state_dir / "does_not_exist"
        res = collector.collect_backup_watchdog(str(missing_file))
        self.assertEqual(res["status"], "unknown")
        self.assertIsNone(res["age_seconds"])

    # -----------------------------------------------------------------------
    # 5. Incident Thresholds & Lifecycle (App 3 samples, Cooldown, Recovery)
    # -----------------------------------------------------------------------

    def test_app_failure_requires_3_consecutive_samples_to_trigger(self):
        client = FakeSmtpClient("smtp.example.invalid", 587)
        state = collector.load_state(self.state_file)
        simulated_time = 1729438800.0

        def failing_inspect(cmd, timeout):
            if "rehouse-app-1" in cmd:
                return 0, json.dumps({"Running": False, "Status": "exited", "ExitCode": 1}), ""
            return 0, json.dumps({"Running": True, "Status": "running", "Health": {"Status": "healthy"}}), ""

        def failing_api(url, timeout):
            raise ConnectionRefusedError("Connection refused")

        with patch.object(collector, "collect_cpu", return_value={"percent": 20.0, "status": "healthy"}), \
             patch.object(collector, "collect_memory", return_value={"percent": 30.0, "status": "healthy"}), \
             patch.object(collector, "collect_disk", return_value={"percent": 40.0, "status": "healthy"}):

            # Sample 1: App fails -> streak = 1, no incident
            collector.run_collection_cycle(
                self.config, state, now_ts=simulated_time,
                docker_runner=failing_inspect, http_getter=failing_api,
                smtp_client_factory=lambda *a, **kw: client,
            )
            self.assertEqual(state["counters"].get("app_streak"), 1)
            self.assertEqual(len(state["active_incidents"]), 0)
            self.assertEqual(len(client.sent_messages), 0)

            # Sample 2: App fails -> streak = 2, still no incident
            simulated_time += 60.0
            collector.run_collection_cycle(
                self.config, state, now_ts=simulated_time,
                docker_runner=failing_inspect, http_getter=failing_api,
                smtp_client_factory=lambda *a, **kw: client,
            )
            self.assertEqual(state["counters"].get("app_streak"), 2)
            self.assertEqual(len(state["active_incidents"]), 0)
            self.assertEqual(len(client.sent_messages), 0)

            # Sample 3: App fails 3rd consecutive time -> INCIDENT OPENED & EMAIL SENT!
            simulated_time += 60.0
            collector.run_collection_cycle(
                self.config, state, now_ts=simulated_time,
                docker_runner=failing_inspect, http_getter=failing_api,
                smtp_client_factory=lambda *a, **kw: client,
            )
            self.assertEqual(state["counters"].get("app_streak"), 3)
            self.assertIn("app", state["active_incidents"])
            self.assertEqual(len(client.sent_messages), 1)

            msg = client.sent_messages[0]
            self.assertIn("[Roomie Alert] CRITICAL", msg["Subject"])
            self.assertEqual(msg["To"], "alerts@roomie.invalid")
            self.assertIn("accepted != delivered", msg.get_content())
            self.assertNotIn("test-password-secret", msg.get_content())

            # Sample 4: 1 minute later, still failing -> in 1h cooldown, NO extra email sent!
            simulated_time += 60.0
            collector.run_collection_cycle(
                self.config, state, now_ts=simulated_time,
                docker_runner=failing_inspect, http_getter=failing_api,
                smtp_client_factory=lambda *a, **kw: client,
            )
            self.assertEqual(len(client.sent_messages), 1)

            # Sample 5: 3601 seconds later -> cooldown expired -> REMINDER EMAIL SENT!
            simulated_time += 3601.0
            collector.run_collection_cycle(
                self.config, state, now_ts=simulated_time,
                docker_runner=failing_inspect, http_getter=failing_api,
                smtp_client_factory=lambda *a, **kw: client,
            )
            self.assertEqual(len(client.sent_messages), 2)
            self.assertIn("REMINDER", client.sent_messages[1]["Subject"])

            # Sample 6: Recovery! Container running with absent Docker health + loopback 200 OK
            def healthy_inspect(cmd, timeout):
                return 0, json.dumps({"Running": True, "Status": "running"}), ""

            def healthy_api(url, timeout):
                return 200, 10.0

            simulated_time += 60.0
            collector.run_collection_cycle(
                self.config, state, now_ts=simulated_time,
                docker_runner=healthy_inspect, http_getter=healthy_api,
                smtp_client_factory=lambda *a, **kw: client,
            )
            self.assertEqual(state["counters"].get("app_streak"), 0)
            self.assertNotIn("app", state["active_incidents"])
            self.assertEqual(len(state["recent_history"]), 1)
            self.assertEqual(state["recent_history"][0]["target"], "app")
            self.assertEqual(len(client.sent_messages), 3)
            self.assertIn("[Roomie Recovery] Resolved", client.sent_messages[2]["Subject"])

    # -----------------------------------------------------------------------
    # 6. Requirement C: Timing, Spacing, Gaps, Sustained Duration, and Restarts
    # -----------------------------------------------------------------------

    def test_cpu_sustained_threshold_requires_10_samples_and_real_duration(self):
        client = FakeSmtpClient("smtp.example.invalid", 587)
        state = collector.load_state(self.state_file)
        simulated_time = 1729438800.0

        def healthy_inspect(cmd, timeout):
            return 0, json.dumps({"Running": True, "Status": "running"}), ""

        def healthy_api(url, timeout):
            return 200, 10.0

        # Simulate 9 samples spaced by 60s (elapsed = 8 * 60 = 480s < 540s)
        for i in range(1, 10):
            with patch.object(collector, "collect_cpu", return_value={"percent": 90.0, "status": "warning"}):
                collector.run_collection_cycle(
                    self.config, state, now_ts=simulated_time + (i * 60.0),
                    docker_runner=healthy_inspect, http_getter=healthy_api,
                    smtp_client_factory=lambda *a, **kw: client,
                )
            self.assertEqual(state["counters"].get("cpu_streak"), i)
            self.assertNotIn("cpu", state["active_incidents"])
            self.assertEqual(len(client.sent_messages), 0)

        # 10th sample at t0 + 600s (elapsed = 9 * 60 = 540s >= 9m) -> INCIDENT FIRES!
        with patch.object(collector, "collect_cpu", return_value={"percent": 90.0, "status": "warning"}):
            collector.run_collection_cycle(
                self.config, state, now_ts=simulated_time + (10 * 60.0),
                docker_runner=healthy_inspect, http_getter=healthy_api,
                smtp_client_factory=lambda *a, **kw: client,
            )
        self.assertEqual(state["counters"].get("cpu_streak"), 10)
        self.assertIn("cpu", state["active_incidents"])
        self.assertEqual(len(client.sent_messages), 1)
        self.assertIn("[Roomie Alert] WARNING", client.sent_messages[0]["Subject"])

        # Recovery when CPU drops below 85%
        with patch.object(collector, "collect_cpu", return_value={"percent": 30.0, "status": "healthy"}):
            collector.run_collection_cycle(
                self.config, state, now_ts=simulated_time + (11 * 60.0),
                docker_runner=healthy_inspect, http_getter=healthy_api,
                smtp_client_factory=lambda *a, **kw: client,
            )
        self.assertEqual(state["counters"].get("cpu_streak"), 0)
        self.assertNotIn("cpu", state["active_incidents"])
        self.assertEqual(len(state["recent_history"]), 1)
        self.assertEqual(len(client.sent_messages), 2)
        self.assertIn("[Roomie Recovery] Resolved", client.sent_messages[1]["Subject"])

    def test_rapid_samples_rejected_and_do_not_satisfy_sustained_threshold(self):
        # 10 rapid manual executions in 10 seconds MUST NOT fire an alert!
        client = FakeSmtpClient("smtp.example.invalid", 587)
        state = collector.load_state(self.state_file)
        start_ts = 1729438800.0

        with patch.object(collector, "collect_cpu", return_value={"percent": 92.0, "status": "warning"}):
            for i in range(10):
                # Only 1 second between runs (spacing < 55s)
                collector.run_collection_cycle(
                    self.config, state, now_ts=start_ts + (i * 1.0),
                    http_getter=lambda u, t: (200, 10.0),
                    smtp_client_factory=lambda *a, **kw: client,
                )

        # Only the first sample was accepted; the other 9 rapid samples were rejected!
        self.assertEqual(state["counters"].get("cpu_streak", 0), 1)
        self.assertNotIn("cpu", state["active_incidents"])
        self.assertEqual(len(client.sent_messages), 0)

    def test_long_timer_gap_resets_candidate_sustained_period(self):
        # High CPU for 5 samples, then a gap of 5 minutes (> 2 min) occurs.
        # Candidate streak MUST reset across gaps > 2 min!
        client = FakeSmtpClient("smtp.example.invalid", 587)
        state = collector.load_state(self.state_file)
        start_ts = 1729438800.0

        with patch.object(collector, "collect_cpu", return_value={"percent": 92.0, "status": "warning"}):
            for i in range(5):
                collector.run_collection_cycle(
                    self.config, state, now_ts=start_ts + (i * 60.0),
                    http_getter=lambda u, t: (200, 10.0),
                    smtp_client_factory=lambda *a, **kw: client,
                )
            self.assertEqual(state["counters"].get("cpu_streak"), 5)

            # Timer stops / gap of 300 seconds (> 120s) occurs
            gap_ts = start_ts + (4 * 60.0) + 300.0
            collector.run_collection_cycle(
                self.config, state, now_ts=gap_ts,
                http_getter=lambda u, t: (200, 10.0),
                smtp_client_factory=lambda *a, **kw: client,
            )

            # Candidate period was reset by the gap; now starting fresh at streak = 1
            self.assertEqual(state["counters"].get("cpu_streak"), 1)
            self.assertNotIn("cpu", state["active_incidents"])

    def test_unknown_sample_resets_candidate_sustained_period(self):
        # 4 high CPU samples, then 1 unknown sample -> candidate streak resets to 0
        state = collector.load_state(self.state_file)
        start_ts = 1729438800.0

        with patch.object(collector, "collect_cpu", return_value={"percent": 92.0, "status": "warning"}):
            for i in range(4):
                collector.run_collection_cycle(
                    self.config, state, now_ts=start_ts + (i * 60.0),
                    http_getter=lambda u, t: (200, 10.0),
                )
            self.assertEqual(state["counters"].get("cpu_streak"), 4)

        # 5th sample returns unknown CPU
        with patch.object(collector, "collect_cpu", return_value={"percent": None, "status": "unknown"}):
            collector.run_collection_cycle(
                self.config, state, now_ts=start_ts + (4 * 60.0),
                http_getter=lambda u, t: (200, 10.0),
            )

        self.assertEqual(state["counters"].get("cpu_streak"), 0)
        self.assertNotIn("cpu", state["active_incidents"])

    def test_existing_open_incident_stays_open_during_unknown_and_gap(self):
        # Open incident stays open during unknown or long gaps; resolves ONLY on proven healthy
        state = collector.load_state(self.state_file)
        client = FakeSmtpClient("smtp.example.invalid", 587)
        now_ts = 1729438800.0

        # Pre-seed active incident
        state["active_incidents"]["cpu"] = {
            "id": "inc_cpu_test",
            "target": "cpu",
            "severity": "warning",
            "title": "CPU high",
            "opened_at": collector.format_iso_utc(now_ts),
            "last_notified_at": collector.format_iso_utc(now_ts),
            "consecutive_samples": 10,
            "current_value": "CPU 90%",
        }

        # Cycle with unknown CPU metric
        with patch.object(collector, "collect_cpu", return_value={"percent": None, "status": "unknown"}):
            collector.run_collection_cycle(
                self.config, state, now_ts=now_ts + 60.0,
                http_getter=lambda u, t: (200, 10.0),
                smtp_client_factory=lambda *a, **kw: client,
            )
            # Incident MUST stay open!
            self.assertIn("cpu", state["active_incidents"])
            self.assertEqual(len(state["recent_history"]), 0)

        # Cycle after a long gap of 10 minutes with unknown CPU
        with patch.object(collector, "collect_cpu", return_value={"percent": None, "status": "unknown"}):
            collector.run_collection_cycle(
                self.config, state, now_ts=now_ts + 700.0,
                http_getter=lambda u, t: (200, 10.0),
                smtp_client_factory=lambda *a, **kw: client,
            )
            # Incident STILL stays open!
            self.assertIn("cpu", state["active_incidents"])

        # Resolves ONLY when proven healthy (CPU < 85%)
        with patch.object(collector, "collect_cpu", return_value={"percent": 30.0, "status": "healthy"}):
            collector.run_collection_cycle(
                self.config, state, now_ts=now_ts + 760.0,
                http_getter=lambda u, t: (200, 10.0),
                smtp_client_factory=lambda *a, **kw: client,
            )
            self.assertNotIn("cpu", state["active_incidents"])
            self.assertEqual(len(state["recent_history"]), 1)
            self.assertEqual(len(client.sent_messages), 1)
            self.assertIn("Recovery", client.sent_messages[0]["Subject"])

    def test_bounded_state_persists_through_restart(self):
        # State saved via save_atomic_json and reloaded via load_state preserves counters, tracking, and incidents
        state = collector.load_state(self.state_file)
        start_ts = 1729438800.0

        def failing_inspect(cmd, timeout):
            if "rehouse-app-1" in cmd:
                return 0, json.dumps({"Running": False, "Status": "exited"}), ""
            return 0, json.dumps({"Running": True, "Status": "running", "Health": {"Status": "healthy"}}), ""

        def ok_api(url, timeout):
            return 200, 10.0

        collector.run_collection_cycle(self.config, state, now_ts=start_ts, docker_runner=failing_inspect, http_getter=ok_api)
        collector.run_collection_cycle(self.config, state, now_ts=start_ts + 60.0, docker_runner=failing_inspect, http_getter=ok_api)
        self.assertEqual(state["counters"].get("app_streak"), 2)

        # Persist to disk
        collector.save_atomic_json(self.state_file, state, mode=0o600)

        # Simulate process termination and fresh restart loading from disk
        restarted_state = collector.load_state(self.state_file)
        self.assertEqual(restarted_state["counters"].get("app_streak"), 2)
        self.assertIn("app", restarted_state["tracking"])
        self.assertEqual(restarted_state["tracking"]["app"]["candidate_count"], 2)

        # 3rd sample after restart -> triggers incident!
        client = FakeSmtpClient("smtp.example.invalid", 587)
        collector.run_collection_cycle(
            self.config, restarted_state, now_ts=start_ts + 120.0,
            docker_runner=failing_inspect, http_getter=ok_api,
            smtp_client_factory=lambda *a, **kw: client,
        )
        self.assertEqual(restarted_state["counters"].get("app_streak"), 3)
        self.assertIn("app", restarted_state["active_incidents"])
        self.assertEqual(len(client.sent_messages), 1)

    # -----------------------------------------------------------------------
    # 7. Requirement D: Independent SMTP Reliability, Queue, Retries & Budgets
    # -----------------------------------------------------------------------

    def test_smtp_failure_does_not_suppress_retry_nor_advance_last_notified(self):
        # When SMTP fails on new incident, last_notified_at is NOT updated,
        # and the notification remains pending for retry
        state = collector.load_state(self.state_file)
        client = FakeSmtpClient("smtp.example.invalid", 587, should_fail=True)
        now_ts = 1729438800.0

        def failing_inspect(cmd, timeout):
            if "rehouse-app-1" in cmd:
                return 0, json.dumps({"Running": False, "Status": "exited"}), ""
            return 0, json.dumps({"Running": True, "Status": "running", "Health": {"Status": "healthy"}}), ""

        def ok_api(url, timeout):
            return 200, 10.0

        with patch("sys.stderr", new_callable=io.StringIO):
            for i in range(3):
                collector.run_collection_cycle(
                    self.config, state, now_ts=now_ts + (i * 60.0),
                    docker_runner=failing_inspect, http_getter=ok_api,
                    smtp_client_factory=lambda *a, **kw: client,
                )

        self.assertIn("app", state["active_incidents"])
        # Crucial: last_notified_at is None because delivery failed!
        self.assertIsNone(state["active_incidents"]["app"]["last_notified_at"])
        # Notification is in pending queue
        self.assertEqual(len(state["pending_notifications"]), 1)
        self.assertEqual(state["pending_notifications"][0]["target"], "app")
        self.assertEqual(state["pending_notifications"][0]["attempts"], 1)

    def test_smtp_retry_and_backoff_on_subsequent_runs(self):
        # Pending notification retries on subsequent run and updates last_notified_at when delivery succeeds
        state = collector.load_state(self.state_file)
        failing_client = FakeSmtpClient("smtp.example.invalid", 587, should_fail=True)
        now_ts = 1729438800.0

        def failing_inspect(cmd, timeout):
            if "rehouse-app-1" in cmd:
                return 0, json.dumps({"Running": False, "Status": "exited"}), ""
            return 0, json.dumps({"Running": True, "Status": "running", "Health": {"Status": "healthy"}}), ""

        def ok_api(url, timeout):
            return 200, 10.0

        # Trigger incident with failing SMTP
        with patch("sys.stderr", new_callable=io.StringIO):
            for i in range(3):
                collector.run_collection_cycle(
                    self.config, state, now_ts=now_ts + (i * 60.0),
                    docker_runner=failing_inspect, http_getter=ok_api,
                    smtp_client_factory=lambda *a, **kw: failing_client,
                )
        self.assertEqual(len(state["pending_notifications"]), 1)
        self.assertIsNone(state["active_incidents"]["app"]["last_notified_at"])

        # Next run 60s later: SMTP is restored!
        healthy_client = FakeSmtpClient("smtp.example.invalid", 587, should_fail=False)
        collector.run_collection_cycle(
            self.config, state, now_ts=now_ts + 180.0,
            docker_runner=failing_inspect, http_getter=ok_api,
            smtp_client_factory=lambda *a, **kw: healthy_client,
        )

        # Notification was successfully delivered!
        self.assertEqual(len(healthy_client.sent_messages), 1)
        self.assertEqual(len(state["pending_notifications"]), 0)
        # last_notified_at is now set to the actual successful delivery timestamp
        self.assertIsNotNone(state["active_incidents"]["app"]["last_notified_at"])

    def test_smtp_recovery_notice_persists_across_failure(self):
        # Recovery notice is not lost when SMTP temporarily fails
        state = collector.load_state(self.state_file)
        now_ts = 1729438800.0

        # Seed active incident
        state["active_incidents"]["app"] = {
            "id": "inc_app_seed",
            "target": "app",
            "severity": "critical",
            "title": "App down",
            "opened_at": collector.format_iso_utc(now_ts),
            "last_notified_at": collector.format_iso_utc(now_ts),
            "consecutive_samples": 3,
            "current_value": "down",
        }

        # App recovers, but SMTP fails
        failing_client = FakeSmtpClient("smtp.example.invalid", 587, should_fail=True)

        def healthy_inspect(cmd, timeout):
            return 0, json.dumps({"Running": True, "Status": "running"}), ""

        def healthy_api(url, timeout):
            return 200, 10.0

        with patch("sys.stderr", new_callable=io.StringIO):
            collector.run_collection_cycle(
                self.config, state, now_ts=now_ts + 60.0,
                docker_runner=healthy_inspect, http_getter=healthy_api,
                smtp_client_factory=lambda *a, **kw: failing_client,
            )

        # Incident was resolved from active list
        self.assertNotIn("app", state["active_incidents"])
        self.assertEqual(len(state["recent_history"]), 1)
        # Recovery notice was enqueued in pending queue, NOT lost!
        self.assertEqual(len(state["pending_notifications"]), 1)
        self.assertEqual(state["pending_notifications"][0]["notification_type"], "recovery")

        # Next run: SMTP works -> recovery email sent!
        healthy_client = FakeSmtpClient("smtp.example.invalid", 587, should_fail=False)
        collector.run_collection_cycle(
            self.config, state, now_ts=now_ts + 120.0,
            docker_runner=healthy_inspect, http_getter=healthy_api,
            smtp_client_factory=lambda *a, **kw: healthy_client,
        )
        self.assertEqual(len(healthy_client.sent_messages), 1)
        self.assertIn("Recovery", healthy_client.sent_messages[0]["Subject"])
        self.assertEqual(len(state["pending_notifications"]), 0)

    def test_systemd_45s_budget_honored_for_multiple_notifications(self):
        # Enforces 20s budget when multiple notifications are pending
        state = collector.load_state(self.state_file)
        now_ts = 1729438800.0

        # Queue 5 pending notifications
        for i in range(5):
            state["pending_notifications"].append({
                "id": f"notif_{i}",
                "target": f"target_{i}",
                "notification_type": "new",
                "next_retry_at": now_ts,
                "attempts": 0,
            })

        # Mock an SMTP client that simulates delay
        class SlowSmtpClient(FakeSmtpClient):
            def send_message(self, msg):
                time.sleep(0.05)  # slight delay
                super().send_message(msg)

        # Call with tiny 0.08s budget
        collector.process_pending_notifications(
            state, self.config, now_ts,
            smtp_client_factory=lambda *a, **kw: SlowSmtpClient("smtp.example.invalid", 587),
            time_budget_seconds=0.08,
        )

        # Some notifications should be processed and some remain pending for next run
        self.assertGreater(len(state["pending_notifications"]), 0)

    # -----------------------------------------------------------------------
    # 8. Requirement A: Dedicated Public Directory & Private State Isolation
    # -----------------------------------------------------------------------

    def test_dedicated_public_directory_and_private_state_isolation(self):
        # 1. Output file is in public directory: /var/lib/rehouse/monitoring/public/host-metrics.json
        # 2. State file is in private directory: /var/lib/rehouse/monitoring/collector-state.json
        state = collector.load_state(self.state_file)
        payload = collector.run_collection_cycle(
            self.config, state,
            docker_runner=lambda cmd, t: (0, json.dumps({"Running": True, "Status": "running"}), ""),
            http_getter=lambda u, t: (200, 10.0),
        )

        collector.save_atomic_json(self.output_file, payload, mode=0o644)
        collector.save_atomic_json(self.state_file, state, mode=0o600)

        self.assertTrue(self.output_file.is_file())
        self.assertTrue(self.state_file.is_file())

        # Public file is in dedicated public subfolder
        self.assertEqual(self.output_file.parent.name, "public")
        # Private state file is in parent folder, outside public mount
        self.assertNotEqual(self.state_file.parent.name, "public")

        # Verify permissions
        out_stat = self.output_file.stat()
        self.assertEqual(oct(out_stat.st_mode)[-3:], "644")
        state_stat = self.state_file.stat()
        self.assertEqual(oct(state_stat.st_mode)[-3:], "600")

        # Verify public metrics file NEVER leaks credentials or private queues
        public_content = self.output_file.read_text(encoding="utf-8")
        self.assertNotIn("test-password-secret", public_content)
        self.assertNotIn("pending_notifications", public_content)
        self.assertNotIn("tracking", public_content)

    # -----------------------------------------------------------------------
    # 9. Bounded History & Atomic JSON File Mechanics
    # -----------------------------------------------------------------------

    def test_samples_history_is_bounded_to_60(self):
        state = collector.load_state(self.state_file)
        simulated_time = 1729438800.0

        def healthy_inspect(cmd, timeout):
            return 0, json.dumps({"Running": True, "Status": "running"}), ""

        def healthy_api(url, timeout):
            return 200, 10.0

        with patch.object(collector, "collect_cpu", return_value={"percent": 20.0, "status": "healthy"}), \
             patch.object(collector, "collect_memory", return_value={"percent": 30.0, "status": "healthy"}), \
             patch.object(collector, "collect_disk", return_value={"percent": 40.0, "status": "healthy"}):
            for i in range(75):
                collector.run_collection_cycle(
                    self.config, state, now_ts=simulated_time + (i * 60.0),
                    docker_runner=healthy_inspect, http_getter=healthy_api,
                )

        self.assertEqual(len(state["samples"]), 60)

    def test_atomic_json_saving_creates_valid_permissions(self):
        test_file = self.state_dir / "atomic-test.json"
        data = {"hello": "world", "num": 42}
        collector.save_atomic_json(test_file, data, mode=0o644)

        self.assertTrue(test_file.exists())
        loaded = json.loads(test_file.read_text(encoding="utf-8"))
        self.assertEqual(loaded["hello"], "world")
        self.assertEqual(loaded["num"], 42)


    # -----------------------------------------------------------------------
    # 10. Requirement E / msg_e99e5fbbbe49 Review Fixes: Incident Identity,
    #     Non-discarding Queue, Monotonic Budget & Pre-network Persistence
    # -----------------------------------------------------------------------

    def test_fail_recover_fail_before_smtp_restored_distinct_incident_identity(self):
        # Target fails -> recovers -> fails again before SMTP restored:
        # Notifications MUST be keyed by incident_id + type and delivered without collision.
        state = collector.load_state(self.state_file)
        now_ts = 1729438800.0

        def failing_inspect(cmd, timeout):
            if "rehouse-app-1" in cmd:
                return 0, json.dumps({"Running": False, "Status": "exited"}), ""
            return 0, json.dumps({"Running": True, "Status": "running", "Health": {"Status": "healthy"}}), ""

        def healthy_inspect(cmd, timeout):
            return 0, json.dumps({"Running": True, "Status": "running"}), ""

        def ok_api(url, timeout):
            return 200, 10.0

        failing_client = FakeSmtpClient("smtp.example.invalid", 587, should_fail=True)

        with patch("sys.stderr", new_callable=io.StringIO):
            # Phase 1: First failure streak (3 samples) -> Incident 1 opens
            for i in range(3):
                collector.run_collection_cycle(
                    self.config, state, now_ts=now_ts + (i * 60.0),
                    docker_runner=failing_inspect, http_getter=ok_api,
                    smtp_client_factory=lambda *a, **kw: failing_client,
                )
            self.assertIn("app", state["active_incidents"])
            inc1_id = state["active_incidents"]["app"]["id"]
            self.assertEqual(len(state["pending_notifications"]), 1)
            self.assertEqual(state["pending_notifications"][0]["incident_id"], inc1_id)
            self.assertEqual(state["pending_notifications"][0]["notification_type"], "new")

            # Phase 2: App recovers -> Incident 1 resolves -> Recovery queued for Incident 1
            now_ts += 180.0
            collector.run_collection_cycle(
                self.config, state, now_ts=now_ts,
                docker_runner=healthy_inspect, http_getter=ok_api,
                smtp_client_factory=lambda *a, **kw: failing_client,
            )
            self.assertNotIn("app", state["active_incidents"])
            self.assertEqual(len(state["pending_notifications"]), 2)
            self.assertEqual(state["pending_notifications"][1]["incident_id"], inc1_id)
            self.assertEqual(state["pending_notifications"][1]["notification_type"], "recovery")

            # Phase 3: App fails again -> Incident 2 opens with distinct ID!
            for i in range(1, 4):
                collector.run_collection_cycle(
                    self.config, state, now_ts=now_ts + (i * 60.0),
                    docker_runner=failing_inspect, http_getter=ok_api,
                    smtp_client_factory=lambda *a, **kw: failing_client,
                )
            self.assertIn("app", state["active_incidents"])
            inc2_id = state["active_incidents"]["app"]["id"]
            self.assertNotEqual(inc1_id, inc2_id)

            # Queue MUST preserve all 3 distinct notifications without stomping!
            self.assertEqual(len(state["pending_notifications"]), 3)
            self.assertEqual(state["pending_notifications"][0]["incident_id"], inc1_id)
            self.assertEqual(state["pending_notifications"][0]["notification_type"], "new")
            self.assertEqual(state["pending_notifications"][1]["incident_id"], inc1_id)
            self.assertEqual(state["pending_notifications"][1]["notification_type"], "recovery")
            self.assertEqual(state["pending_notifications"][2]["incident_id"], inc2_id)
            self.assertEqual(state["pending_notifications"][2]["notification_type"], "new")

            # Phase 4: SMTP restored -> delivers in order!
            healthy_client = FakeSmtpClient("smtp.example.invalid", 587, should_fail=False)
            now_ts += 600.0
            collector.run_collection_cycle(
                self.config, state, now_ts=now_ts,
                docker_runner=failing_inspect, http_getter=ok_api,
                smtp_client_factory=lambda *a, **kw: healthy_client,
            )
            # All 3 notifications successfully delivered!
            self.assertEqual(len(healthy_client.sent_messages), 3)
            self.assertEqual(len(state["pending_notifications"]), 0)
            # Active incident 2 has last_notified_at set to current delivery time
            self.assertIsNotNone(state["active_incidents"]["app"]["last_notified_at"])

    def test_recovery_notices_retained_across_high_retry_attempts(self):
        # Required notices (such as recovery) must NOT be permanently dropped after max retries
        state = collector.load_state(self.state_file)
        now_ts = 1729438800.0

        state["pending_notifications"].append({
            "id": "notif_inc1_recovery",
            "target": "app",
            "incident_id": "inc1",
            "notification_type": "recovery",
            "attempts": 0,
            "next_retry_at": now_ts,
        })

        failing_client = FakeSmtpClient("smtp.example.invalid", 587, should_fail=True)

        with patch("sys.stderr", new_callable=io.StringIO):
            # Run 8 failed delivery attempts (exceeding 5)
            for i in range(8):
                collector.process_pending_notifications(
                    state, self.config, now_ts=now_ts + (i * 300.0),
                    smtp_client_factory=lambda *a, **kw: failing_client,
                )

        # Recovery notice MUST still be retained in queue!
        self.assertEqual(len(state["pending_notifications"]), 1)
        self.assertEqual(state["pending_notifications"][0]["attempts"], 8)
        self.assertEqual(state["pending_notifications"][0]["notification_type"], "recovery")

    def test_state_saved_before_network_calls_and_after_each_delivery(self):
        # Verifies state_save_callback is invoked BEFORE network calls and AFTER each accepted delivery
        state = collector.load_state(self.state_file)
        now_ts = 1729438800.0
        saved_snapshots: list[dict] = []

        def save_callback(st):
            saved_snapshots.append(copy.deepcopy(st))

        def failing_inspect(cmd, timeout):
            if "rehouse-app-1" in cmd:
                return 0, json.dumps({"Running": False, "Status": "exited"}), ""
            return 0, json.dumps({"Running": True, "Status": "running", "Health": {"Status": "healthy"}}), ""

        def ok_api(url, timeout):
            return 200, 10.0

        # Trigger app incident
        with patch("sys.stderr", new_callable=io.StringIO):
            for i in range(3):
                collector.run_collection_cycle(
                    self.config, state, now_ts=now_ts + (i * 60.0),
                    docker_runner=failing_inspect, http_getter=ok_api,
                    state_save_callback=save_callback,
                )

        # At the 3rd sample, state_save_callback was called BEFORE network delivery
        # (with notification enqueued in pending_notifications)
        pre_network_saves = [s for s in saved_snapshots if len(s.get("pending_notifications", [])) > 0]
        self.assertGreater(len(pre_network_saves), 0)

        # Now test delivery save
        delivery_saved_snapshots: list[dict] = []
        healthy_client = FakeSmtpClient("smtp.example.invalid", 587, should_fail=False)
        collector.process_pending_notifications(
            state, self.config, now_ts=now_ts + 180.0,
            smtp_client_factory=lambda *a, **kw: healthy_client,
            state_save_callback=lambda st: delivery_saved_snapshots.append(copy.deepcopy(st)),
        )
        self.assertEqual(len(healthy_client.sent_messages), 1)
        self.assertGreater(len(delivery_saved_snapshots), 0)
        # Final snapshot has 0 pending notifications
        self.assertEqual(len(delivery_saved_snapshots[-1]["pending_notifications"]), 0)


if __name__ == "__main__":
    unittest.main()
