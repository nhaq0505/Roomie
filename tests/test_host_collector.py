#!/usr/bin/env python3
"""Comprehensive Unit and Fixture Tests for Roomie Ubuntu Host Monitoring Collector."""

import copy
import email.message
import importlib.util
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
    """Mock SMTP client tracking invocations and sent messages."""

    def __init__(self, host, port, timeout=15):
        self.host = host
        self.port = port
        self.timeout = timeout
        self.starttls_called = False
        self.logged_in = False
        self.login_user = None
        self.login_pass = None
        self.sent_messages: list[email.message.EmailMessage] = []

    def __enter__(self):
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
        self.sent_messages.append(msg)


class HostCollectorTests(unittest.TestCase):
    """Test suite verifying all collector functionality, boundaries, and invariants."""

    def setUp(self):
        self.test_dir = tempfile.TemporaryDirectory()
        self.base_path = Path(self.test_dir.name)
        self.config_dir = self.base_path / "etc_rehouse"
        self.config_dir.mkdir(parents=True, exist_ok=True)
        self.state_dir = self.base_path / "var_lib_rehouse"
        self.state_dir.mkdir(parents=True, exist_ok=True)

        self.monitoring_env = self.config_dir / "monitoring.env"
        self.runtime_env = self.config_dir / "runtime.env"
        self.backup_env = self.config_dir / "backup.env"
        self.backup_success = self.state_dir / "backup-last-success"
        self.output_file = self.state_dir / "host-metrics.json"
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
        # Sample 1
        proc_stat.write_text("cpu  1000 0 200 8000 100 0 0 0 0 0\n", encoding="utf-8")
        state = {}
        info1 = collector.collect_cpu(state, proc_stat_path=proc_stat)
        self.assertIn("last_cpu_sample", state)

        # Sample 2 (100 ticks user, 50 ticks sys, 850 ticks idle -> 15% used)
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
    # 3. Container & Loopback Health Inspection
    # -----------------------------------------------------------------------

    def test_container_inspect_healthy(self):
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

        res = collector.collect_container_status("rehouse-app-1", "app", runner=fake_runner)
        self.assertEqual(res["name"], "rehouse-app-1")
        self.assertTrue(res["running"])
        self.assertEqual(res["status"], "running")
        self.assertEqual(res["health"], "healthy")
        self.assertEqual(res["restart_count"], 2)
        self.assertIsNone(res["error"])

    def test_container_inspect_unhealthy_or_missing(self):
        def failing_runner(cmd, timeout):
            return 1, "", "Error: No such container: rehouse-app-1"

        res = collector.collect_container_status("rehouse-app-1", "app", runner=failing_runner)
        self.assertFalse(res["running"])
        self.assertEqual(res["health"], "unhealthy")
        self.assertEqual(res["status"], "missing")
        self.assertIsNotNone(res["error"])

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
        # Created 10 hours ago (< 27h)
        self.backup_success.touch()
        os.utime(self.backup_success, (now - 10 * 3600, now - 10 * 3600))

        res = collector.collect_backup_watchdog(str(self.backup_success), now_ts=now)
        self.assertEqual(res["status"], "healthy")
        self.assertEqual(res["age_seconds"], 10 * 3600)
        self.assertIsNone(res["error"])

    def test_backup_status_stale_over_27h_surfaces_stale_without_alerting(self):
        now = 1729438800.0
        # Created 28 hours ago (> 27h)
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
    # 5. Incident Thresholds & Lifecycle (App 3 samples, CPU 10 samples, Recovery)
    # -----------------------------------------------------------------------

    def test_app_failure_requires_3_consecutive_samples_to_trigger(self):
        client = FakeSmtpClient("smtp.example.invalid", 587)
        state = collector.load_state(self.state_file)
        simulated_time = 1729438800.0

        def selective_inspect(cmd, timeout):
            if "rehouse-app-1" in cmd:
                return 1, "", "No container"
            return 0, json.dumps({"Running": True, "Status": "running", "Health": {"Status": "healthy"}}), ""

        def failing_api(url, timeout):
            raise ConnectionRefusedError("Connection refused")

        # Mock CPU/RAM/Disk to be healthy so only app fails
        with patch.object(collector, "collect_cpu", return_value={"percent": 20.0, "status": "healthy"}), \
             patch.object(collector, "collect_memory", return_value={"percent": 30.0, "status": "healthy"}), \
             patch.object(collector, "collect_disk", return_value={"percent": 40.0, "status": "healthy"}):

            # Sample 1: App fails -> streak = 1, no incident, no email
            collector.run_collection_cycle(
                self.config, state, now_ts=simulated_time,
                docker_runner=selective_inspect, http_getter=failing_api,
                smtp_client_factory=lambda *a, **kw: client,
            )
            self.assertEqual(state["counters"].get("app_streak"), 1)
            self.assertEqual(len(state["active_incidents"]), 0)
            self.assertEqual(len(client.sent_messages), 0)

            # Sample 2: App fails -> streak = 2, still no incident, no email
            simulated_time += 60.0
            collector.run_collection_cycle(
                self.config, state, now_ts=simulated_time,
                docker_runner=selective_inspect, http_getter=failing_api,
                smtp_client_factory=lambda *a, **kw: client,
            )
            self.assertEqual(state["counters"].get("app_streak"), 2)
            self.assertEqual(len(state["active_incidents"]), 0)
            self.assertEqual(len(client.sent_messages), 0)

            # Sample 3: App fails 3rd consecutive time -> INCIDENT OPENED & EMAIL SENT!
            simulated_time += 60.0
            collector.run_collection_cycle(
                self.config, state, now_ts=simulated_time,
                docker_runner=selective_inspect, http_getter=failing_api,
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
                docker_runner=selective_inspect, http_getter=failing_api,
                smtp_client_factory=lambda *a, **kw: client,
            )
            self.assertEqual(state["counters"].get("app_streak"), 4)
            self.assertEqual(len(client.sent_messages), 1)

            # Sample 5: 3601 seconds later -> cooldown expired -> REMINDER EMAIL SENT!
            simulated_time += 3601.0
            collector.run_collection_cycle(
                self.config, state, now_ts=simulated_time,
                docker_runner=selective_inspect, http_getter=failing_api,
                smtp_client_factory=lambda *a, **kw: client,
            )
            self.assertEqual(len(client.sent_messages), 2)
            self.assertIn("REMINDER", client.sent_messages[1]["Subject"])

            # Sample 6: Recovery! Both container and /api/health become healthy!
            def healthy_inspect(cmd, timeout):
                return 0, json.dumps({"Running": True, "Status": "running", "Health": {"Status": "healthy"}}), ""

            def healthy_api(url, timeout):
                return 200, 10.0

            simulated_time += 60.0
            collector.run_collection_cycle(
                self.config, state, now_ts=simulated_time,
                docker_runner=healthy_inspect, http_getter=healthy_api,
                smtp_client_factory=lambda *a, **kw: client,
            )
            # Incident should be resolved, streak reset to 0
            self.assertEqual(state["counters"].get("app_streak"), 0)
            self.assertNotIn("app", state["active_incidents"])
            self.assertEqual(len(state["recent_history"]), 1)
            self.assertEqual(state["recent_history"][0]["target"], "app")
            self.assertIn("duration_seconds", state["recent_history"][0])

            # Immediate RECOVERY email sent!
            self.assertEqual(len(client.sent_messages), 3)
            self.assertIn("[Roomie Recovery] Resolved", client.sent_messages[2]["Subject"])

    def test_cpu_sustained_threshold_requires_10_samples(self):
        client = FakeSmtpClient("smtp.example.invalid", 587)
        state = collector.load_state(self.state_file)
        simulated_time = 1729438800.0

        def healthy_inspect(cmd, timeout):
            return 0, json.dumps({"Running": True, "Status": "running", "Health": {"Status": "healthy"}}), ""

        def healthy_api(url, timeout):
            return 200, 10.0

        # Simulate 9 samples of CPU at 90%
        for i in range(1, 10):
            with patch.object(collector, "collect_cpu", return_value={"percent": 90.0, "status": "warning"}):
                collector.run_collection_cycle(
                    self.config, state, now_ts=simulated_time + (i * 60),
                    docker_runner=healthy_inspect, http_getter=healthy_api,
                    smtp_client_factory=lambda *a, **kw: client,
                )
            self.assertEqual(state["counters"].get("cpu_streak"), i)
            self.assertNotIn("cpu", state["active_incidents"])
            self.assertEqual(len(client.sent_messages), 0)

        # 10th sample of CPU at 90% -> INCIDENT FIRES!
        with patch.object(collector, "collect_cpu", return_value={"percent": 90.0, "status": "warning"}):
            collector.run_collection_cycle(
                self.config, state, now_ts=simulated_time + (10 * 60),
                docker_runner=healthy_inspect, http_getter=healthy_api,
                smtp_client_factory=lambda *a, **kw: client,
            )
        self.assertEqual(state["counters"].get("cpu_streak"), 10)
        self.assertIn("cpu", state["active_incidents"])
        self.assertEqual(len(client.sent_messages), 1)
        self.assertIn("[Roomie Alert] WARNING", client.sent_messages[0]["Subject"])

        # CPU drops back to 30% -> RECOVERY FIRES!
        with patch.object(collector, "collect_cpu", return_value={"percent": 30.0, "status": "healthy"}):
            collector.run_collection_cycle(
                self.config, state, now_ts=simulated_time + (11 * 60),
                docker_runner=healthy_inspect, http_getter=healthy_api,
                smtp_client_factory=lambda *a, **kw: client,
            )
        self.assertEqual(state["counters"].get("cpu_streak"), 0)
        self.assertNotIn("cpu", state["active_incidents"])
        self.assertEqual(len(state["recent_history"]), 1)
        self.assertEqual(len(client.sent_messages), 2)
        self.assertIn("[Roomie Recovery] Resolved", client.sent_messages[1]["Subject"])

    def test_missing_metric_does_not_falsely_clear_incident_or_reset_streak(self):
        client = FakeSmtpClient("smtp.example.invalid", 587)
        state = {
            "counters": {"cpu_streak": 5},
            "active_incidents": {},
            "recent_history": [],
            "samples": [],
        }

        # If CPU reading returns None / unknown
        host_metrics = {"cpu": {"percent": None, "status": "unknown"}, "memory": {}, "disk": {}}
        containers = {"app": {"running": True, "health": "healthy"}, "db": {"running": True, "health": "healthy"}}
        services = {"api_health": {"status": "healthy"}}

        collector.evaluate_incidents(state, self.config, host_metrics, containers, services, now_ts=1000.0)
        # Streak should NOT be cleared to 0 by unknown sample
        self.assertEqual(state["counters"]["cpu_streak"], 5)

    # -----------------------------------------------------------------------
    # 6. Bounded history & Atomic JSON output
    # -----------------------------------------------------------------------

    def test_samples_history_is_bounded_to_60(self):
        state = collector.load_state(self.state_file)
        simulated_time = 1729438800.0

        def healthy_inspect(cmd, timeout):
            return 0, json.dumps({"Running": True, "Status": "running", "Health": {"Status": "healthy"}}), ""

        def healthy_api(url, timeout):
            return 200, 10.0

        # Run 75 collection cycles with healthy resources
        with patch.object(collector, "collect_cpu", return_value={"percent": 20.0, "status": "healthy"}), \
             patch.object(collector, "collect_memory", return_value={"percent": 30.0, "status": "healthy"}), \
             patch.object(collector, "collect_disk", return_value={"percent": 40.0, "status": "healthy"}):
            for i in range(75):
                collector.run_collection_cycle(
                    self.config, state, now_ts=simulated_time + (i * 60),
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


if __name__ == "__main__":
    unittest.main()
