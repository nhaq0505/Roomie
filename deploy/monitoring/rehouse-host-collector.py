#!/usr/bin/env python3
"""Roomie Ubuntu Host Monitoring Collector.

Gathers host resources (CPU, RAM, Disk), container status/restarts for app/db,
loopback /api/health probe, and backup watchdog age.
Maintains persisted incident state, candidate sustained tracking, and notification retry queues.
Outputs bounded atomic JSON to dedicated public directory:
  /var/lib/rehouse/monitoring/public/host-metrics.json (0644)
for read-only bind mounting into the ASP.NET Core container at /app/monitoring-host:ro.
Maintains private collector state at:
  /var/lib/rehouse/monitoring/collector-state.json (0600)
outside the mounted public directory so internal alert queues and credentials stay private.

Dispatches independent SMTP email alerts on sustained failures:
- 3m app/db (>= 1m spacing)
- 10m CPU/RAM >= 85% (real elapsed duration)
- 5m Disk >= 85% (real elapsed duration)
with 1h reminder cooldown and immediate recovery notice.
Uses an asynchronous retry queue with exponential backoff on delivery failures,
marks last_notified_at only after verified successful delivery, and enforces a 20s
overall execution budget within systemd's 45s service limit.
Omits secrets and PII.
"""

from __future__ import annotations

import argparse
import datetime
import email.message
import json
import os
from pathlib import Path
import platform
import shlex
import smtplib
import ssl
import subprocess
import sys
import time
from typing import Any, Callable, Dict, List, Optional, Tuple
import urllib.error
import urllib.request

VERSION = "1.1.0"
SCHEMA_VERSION = "1.0"

# Defaults
DEFAULT_CONFIG_FILES = [
    "/etc/rehouse/monitoring.env",
    "/etc/rehouse/runtime.env",
    "/etc/rehouse/backup.env",
]
DEFAULT_STATE_DIR = "/var/lib/rehouse/monitoring"
DEFAULT_PUBLIC_DIR = "/var/lib/rehouse/monitoring/public"
DEFAULT_OUTPUT_FILE = "/var/lib/rehouse/monitoring/public/host-metrics.json"
DEFAULT_STATE_FILE = "/var/lib/rehouse/monitoring/collector-state.json"
DEFAULT_BACKUP_SUCCESS_FILE = "/var/lib/rehouse/backup-last-success"
DEFAULT_HEALTH_URL = "http://127.0.0.1:8080/api/health"
DEFAULT_APP_CONTAINER = "rehouse-app-1"
DEFAULT_DB_CONTAINER = "rehouse-db-1"

# Alert thresholds & sample requirements
APP_DB_FAIL_THRESHOLD = 3          # 3 consecutive samples
CPU_RAM_SUSTAINED_THRESHOLD = 10   # 10 samples (10 minutes)
DISK_SUSTAINED_THRESHOLD = 5       # 5 samples (5 minutes)

# Minimum required spacing between accepted samples (seconds)
# Allows minor timer jitter around 60s while strictly rejecting rapid duplicate runs
MIN_SAMPLE_SPACING_SECONDS = 55.0

# Maximum allowed gap between samples before candidate sustained period resets (2 minutes)
MAX_SAMPLE_GAP_SECONDS = 120.0

# Real duration thresholds (seconds) for sustained conditions
CPU_RAM_SUSTAINED_DURATION_SECONDS = 9 * 60.0   # 9m between sample 1 and 10 (10 samples total)
DISK_SUSTAINED_DURATION_SECONDS = 4 * 60.0      # 4m between sample 1 and 5 (5 samples total)
APP_DB_SUSTAINED_DURATION_SECONDS = 2 * 60.0    # 2m between sample 1 and 3 (3 samples total)

RESOURCE_HIGH_PERCENT = 85.0       # 85%
ALERT_COOLDOWN_SECONDS = 3600      # 1 hour reminder cooldown
BACKUP_STALE_SECONDS = 27 * 3600   # 27 hours
MAX_RECENT_INCIDENTS = 50
MAX_SAMPLE_HISTORY = 60            # 60 minute samples (1 hour window)

# SMTP Delivery Budget & Queue Constraints (respects systemd 45s service bound)
SMTP_TIME_BUDGET_SECONDS = 20.0    # Max total seconds spent on SMTP attempts per collector run
SMTP_PER_CALL_TIMEOUT = 5.0        # Timeout per individual SMTP attempt
MAX_PENDING_NOTIFICATIONS = 20     # Max bounded pending notification queue
MAX_NOTIFICATION_RETRIES = 5       # Max delivery attempts before dropping


def format_iso_utc(ts: Optional[float] = None) -> str:
    dt = datetime.datetime.fromtimestamp(ts if ts is not None else time.time(), tz=datetime.timezone.utc)
    return dt.strftime("%Y-%m-%dT%H:%M:%SZ")


def parse_iso_utc(iso_str: Optional[str]) -> Optional[float]:
    if not iso_str:
        return None
    try:
        clean_str = iso_str.replace("Z", "+00:00")
        dt = datetime.datetime.fromisoformat(clean_str)
        return dt.timestamp()
    except Exception:
        return None


def read_env_file(path: Path) -> Dict[str, str]:
    result: Dict[str, str] = {}
    if not path.is_file():
        return result
    try:
        content = path.read_text(encoding="utf-8", errors="replace")
    except Exception:
        return result

    for line in content.splitlines():
        line = line.strip()
        if not line or line.startswith("#") or "=" not in line:
            continue
        key, value = line.split("=", 1)
        key = key.strip()
        value = value.strip()
        try:
            parts = shlex.split(value)
            result[key] = parts[0] if len(parts) == 1 else value
        except Exception:
            result[key] = value
    return result


def load_config(config_files: Optional[List[str]] = None) -> Dict[str, Any]:
    files = [Path(f) for f in (config_files or DEFAULT_CONFIG_FILES)]
    raw: Dict[str, str] = {}
    for p in reversed(files):
        raw.update(read_env_file(p))

    cfg: Dict[str, Any] = {
        "SMTP_HOST": raw.get("SMTP_HOST", ""),
        "SMTP_PORT": int(raw.get("SMTP_PORT", "587")),
        "SMTP_SECURITY": raw.get("SMTP_SECURITY", "StartTls"),
        "SMTP_USERNAME": raw.get("SMTP_USERNAME", ""),
        "SMTP_PASSWORD": raw.get("SMTP_PASSWORD", ""),
        "SMTP_FROM_ADDRESS": raw.get("SMTP_FROM_ADDRESS", "noreply@roomie.local"),
        "SMTP_FROM_NAME": raw.get("SMTP_FROM_NAME", "Roomie Monitoring"),
        "MONITORING_ALERT_TO": raw.get("MONITORING_ALERT_TO") or raw.get("BACKUP_ALERT_TO") or raw.get("ALERT_TO", ""),
        "APP_CONTAINER_NAME": raw.get("APP_CONTAINER_NAME", DEFAULT_APP_CONTAINER),
        "DB_CONTAINER_NAME": raw.get("DB_CONTAINER_NAME", DEFAULT_DB_CONTAINER),
        "HEALTH_CHECK_URL": raw.get("HEALTH_CHECK_URL", DEFAULT_HEALTH_URL),
        "BACKUP_SUCCESS_FILE": raw.get("BACKUP_SUCCESS_FILE", DEFAULT_BACKUP_SUCCESS_FILE),
        "METRICS_OUTPUT_FILE": raw.get("METRICS_OUTPUT_FILE", DEFAULT_OUTPUT_FILE),
        "STATE_FILE": raw.get("STATE_FILE", DEFAULT_STATE_FILE),
    }
    return cfg


def load_state(state_file: Path) -> Dict[str, Any]:
    default_state: Dict[str, Any] = {
        "counters": {},
        "tracking": {},
        "active_incidents": {},
        "recent_history": [],
        "samples": [],
        "pending_notifications": [],
        "last_cpu_sample": None,
    }
    if not state_file.is_file():
        return default_state
    try:
        data = json.loads(state_file.read_text(encoding="utf-8"))
        if not isinstance(data, dict):
            raise ValueError("State root is not a dictionary")
        data.setdefault("counters", {})
        data.setdefault("tracking", {})
        data.setdefault("active_incidents", {})
        data.setdefault("recent_history", [])
        data.setdefault("samples", [])
        data.setdefault("pending_notifications", [])
        return data
    except Exception:
        return default_state


def save_atomic_json(target_path: Path, data: Any, mode: int = 0o644) -> None:
    target_path.parent.mkdir(parents=True, exist_ok=True)
    temp_path = target_path.with_name(f"{target_path.name}.tmp.{os.getpid()}")
    try:
        content = json.dumps(data, indent=2) + "\n"
        temp_path.write_text(content, encoding="utf-8")
        os.chmod(temp_path, mode)
        with open(temp_path, "a") as f:
            os.fsync(f.fileno())
        os.replace(temp_path, target_path)
    finally:
        if temp_path.exists():
            try:
                temp_path.unlink()
            except Exception:
                pass


# ---------------------------------------------------------------------------
# Host Resource Collection (CPU, RAM, Disk, System)
# ---------------------------------------------------------------------------

def collect_cpu(state: Dict[str, Any], proc_stat_path: Path = Path("/proc/stat")) -> Dict[str, Any]:
    cores = os.cpu_count() or 1
    load_1m, load_5m, load_15m = (None, None, None)
    try:
        avg = os.getloadavg()
        load_1m, load_5m, load_15m = round(avg[0], 2), round(avg[1], 2), round(avg[2], 2)
    except Exception:
        pass

    percent: Optional[float] = None
    status = "unknown"

    if proc_stat_path.is_file():
        try:
            line = proc_stat_path.read_text(encoding="utf-8").splitlines()[0]
            if line.startswith("cpu "):
                parts = [int(p) for p in line.split()[1:]]
                total = sum(parts)
                idle = parts[3] + (parts[4] if len(parts) > 4 else 0)
                last_sample = state.get("last_cpu_sample")
                now_ts = time.time()
                if last_sample and isinstance(last_sample, dict):
                    prev_total = last_sample.get("total", 0)
                    prev_idle = last_sample.get("idle", 0)
                    delta_total = total - prev_total
                    delta_idle = idle - prev_idle
                    if delta_total > 0:
                        used_ratio = 1.0 - (delta_idle / delta_total)
                        percent = round(max(0.0, min(100.0, used_ratio * 100.0)), 1)
                        status = "warning" if percent >= RESOURCE_HIGH_PERCENT else "healthy"
                state["last_cpu_sample"] = {"total": total, "idle": idle, "timestamp": now_ts}
        except Exception:
            pass

    if percent is None and load_1m is not None:
        percent = round(min(100.0, (load_1m / cores) * 100.0), 1)
        status = "warning" if percent >= RESOURCE_HIGH_PERCENT else "healthy"

    return {
        "percent": percent,
        "cores": cores,
        "load_1m": load_1m,
        "load_5m": load_5m,
        "load_15m": load_15m,
        "status": status,
    }


def collect_memory(proc_meminfo_path: Path = Path("/proc/meminfo")) -> Dict[str, Any]:
    if not proc_meminfo_path.is_file():
        return {
            "total_bytes": None,
            "used_bytes": None,
            "available_bytes": None,
            "percent": None,
            "status": "unknown",
        }

    try:
        meminfo: Dict[str, int] = {}
        for line in proc_meminfo_path.read_text(encoding="utf-8").splitlines():
            if ":" not in line:
                continue
            k, v = line.split(":", 1)
            v_val = v.strip().split()[0]
            if v_val.isdigit():
                meminfo[k.strip()] = int(v_val) * 1024

        total = meminfo.get("MemTotal")
        if total is None or total <= 0:
            return {
                "total_bytes": None,
                "used_bytes": None,
                "available_bytes": None,
                "percent": None,
                "status": "unknown",
            }

        available = meminfo.get("MemAvailable")
        if available is None:
            free = meminfo.get("MemFree", 0)
            buffers = meminfo.get("Buffers", 0)
            cached = meminfo.get("Cached", 0)
            available = free + buffers + cached

        used = max(0, total - available)
        percent = round((used / total) * 100.0, 1)
        status = "warning" if percent >= RESOURCE_HIGH_PERCENT else "healthy"

        return {
            "total_bytes": total,
            "used_bytes": used,
            "available_bytes": available,
            "percent": percent,
            "status": status,
        }
    except Exception:
        return {
            "total_bytes": None,
            "used_bytes": None,
            "available_bytes": None,
            "percent": None,
            "status": "unknown",
        }


def collect_disk(path: str = "/") -> Dict[str, Any]:
    try:
        stat = os.statvfs(path)
        total = stat.f_blocks * stat.f_frsize
        free = stat.f_bavail * stat.f_frsize
        used = total - free
        if total <= 0:
            return {
                "path": path,
                "total_bytes": None,
                "used_bytes": None,
                "free_bytes": None,
                "percent": None,
                "status": "unknown",
            }
        percent = round((used / total) * 100.0, 1)
        status = "warning" if percent >= RESOURCE_HIGH_PERCENT else "healthy"
        return {
            "path": path,
            "total_bytes": total,
            "used_bytes": used,
            "free_bytes": free,
            "percent": percent,
            "status": status,
        }
    except Exception:
        return {
            "path": path,
            "total_bytes": None,
            "used_bytes": None,
            "free_bytes": None,
            "percent": None,
            "status": "unknown",
        }


def collect_host_info(proc_uptime_path: Path = Path("/proc/uptime")) -> Dict[str, Any]:
    uptime_seconds: Optional[int] = None
    if proc_uptime_path.is_file():
        try:
            line = proc_uptime_path.read_text(encoding="utf-8").split()[0]
            uptime_seconds = int(float(line))
        except Exception:
            pass

    return {
        "hostname": platform.node() or "unknown",
        "os": f"{platform.system()} {platform.release()}",
        "uptime_seconds": uptime_seconds,
    }


# ---------------------------------------------------------------------------
# Container & Loopback Health Inspection
# ---------------------------------------------------------------------------

def default_docker_runner(cmd: List[str], timeout: float = 5.0) -> Tuple[int, str, str]:
    proc = subprocess.run(cmd, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True, timeout=timeout)
    return proc.returncode, proc.stdout, proc.stderr


def collect_container_status(
    container_name: str,
    service_name: str,
    runner: Optional[Callable[[List[str], float], Tuple[int, str, str]]] = None,
) -> Dict[str, Any]:
    run = runner or default_docker_runner
    cmd = [
        "docker", "inspect",
        "--format", "{{json .State}}",
        container_name,
    ]
    try:
        code, stdout, stderr = run(cmd, 5.0)
        if code != 0 or not stdout.strip():
            # Missing Docker probe data: report unknown, NOT fabricated healthy nor provably stopped!
            return {
                "name": container_name,
                "service": service_name,
                "status": "unknown",
                "health": "unknown",
                "running": None,
                "restart_count": None,
                "started_at": None,
                "state_description": f"Container inspect unavailable: {stderr.strip() or 'probe failed'}",
                "error": "Docker inspection probe unavailable",
            }

        state_data = json.loads(stdout.strip())
        running = bool(state_data.get("Running", False))
        status_str = state_data.get("Status", "unknown")
        health_info = state_data.get("Health")

        if health_info and isinstance(health_info, dict):
            health_status = health_info.get("Status", "unknown")
        else:
            # Container has no configured Docker HEALTHCHECK (e.g. app container in compose)
            health_status = "none"

        restart_count = 0
        try:
            rc_code, rc_out, _ = run(["docker", "inspect", "--format", "{{.RestartCount}}", container_name], 3.0)
            if rc_code == 0 and rc_out.strip().isdigit():
                restart_count = int(rc_out.strip())
        except Exception:
            pass

        return {
            "name": container_name,
            "service": service_name,
            "status": status_str,
            "health": health_status,
            "running": running,
            "restart_count": restart_count,
            "started_at": state_data.get("StartedAt"),
            "state_description": f"Status: {status_str}, Health: {health_status}",
            "error": None,
        }
    except Exception as exc:
        return {
            "name": container_name,
            "service": service_name,
            "status": "unknown",
            "health": "unknown",
            "running": None,
            "restart_count": None,
            "started_at": None,
            "state_description": f"Inspection error: {type(exc).__name__}",
            "error": f"Inspect exception: {type(exc).__name__}",
        }


def default_http_getter(url: str, timeout: float = 4.0) -> Tuple[int, float]:
    start = time.time()
    req = urllib.request.Request(url, headers={"User-Agent": f"Roomie-Host-Collector/{VERSION}"})
    with urllib.request.urlopen(req, timeout=timeout) as resp:
        latency_ms = round((time.time() - start) * 1000.0, 1)
        return resp.status, latency_ms


def collect_api_health(
    url: str,
    getter: Optional[Callable[[str, float], Tuple[int, float]]] = None,
) -> Dict[str, Any]:
    fetch = getter or default_http_getter
    try:
        status_code, latency_ms = fetch(url, 4.0)
        is_healthy = (200 <= status_code < 300)
        return {
            "url": url,
            "http_status": status_code,
            "latency_ms": latency_ms,
            "status": "healthy" if is_healthy else "unhealthy",
            "error": None if is_healthy else f"HTTP {status_code}",
        }
    except urllib.error.HTTPError as he:
        return {
            "url": url,
            "http_status": he.code,
            "latency_ms": None,
            "status": "unhealthy",
            "error": f"HTTP {he.code}",
        }
    except Exception as exc:
        return {
            "url": url,
            "http_status": None,
            "latency_ms": None,
            "status": "unhealthy",
            "error": f"{type(exc).__name__}",
        }


def collect_backup_watchdog(path_str: str, now_ts: Optional[float] = None) -> Dict[str, Any]:
    now = now_ts if now_ts is not None else time.time()
    p = Path(path_str)
    if not p.exists():
        return {
            "last_success_path": path_str,
            "last_success_at": None,
            "age_seconds": None,
            "threshold_seconds": BACKUP_STALE_SECONDS,
            "status": "unknown",
            "error": "Backup last-success marker file not found",
        }

    try:
        mtime = p.stat().st_mtime
        age = round(max(0.0, now - mtime))
        status = "healthy" if age <= BACKUP_STALE_SECONDS else "stale"
        return {
            "last_success_path": path_str,
            "last_success_at": format_iso_utc(mtime),
            "age_seconds": age,
            "threshold_seconds": BACKUP_STALE_SECONDS,
            "status": status,
            "error": None if status == "healthy" else f"Backup age {age}s exceeds 27h threshold",
        }
    except Exception as exc:
        return {
            "last_success_path": path_str,
            "last_success_at": None,
            "age_seconds": None,
            "threshold_seconds": BACKUP_STALE_SECONDS,
            "status": "unknown",
            "error": f"Error stating backup marker: {type(exc).__name__}",
        }


# ---------------------------------------------------------------------------
# Independent Host SMTP Alerting & Bounded Delivery Queue
# ---------------------------------------------------------------------------

def send_alert_email(
    incident: Dict[str, Any],
    config: Dict[str, Any],
    is_recovery: bool = False,
    is_reminder: bool = False,
    smtp_client_factory: Optional[Callable[..., Any]] = None,
    timeout: float = SMTP_PER_CALL_TIMEOUT,
) -> bool:
    recipient = config.get("MONITORING_ALERT_TO")
    if not recipient:
        print("[collector] Alert delivery skipped: MONITORING_ALERT_TO / BACKUP_ALERT_TO is not configured.", file=sys.stderr)
        return False

    host = config.get("SMTP_HOST")
    if not host:
        print("[collector] Alert delivery skipped: SMTP_HOST is not configured.", file=sys.stderr)
        return False

    port = config.get("SMTP_PORT", 587)
    security = config.get("SMTP_SECURITY", "StartTls")
    username = config.get("SMTP_USERNAME")
    password = config.get("SMTP_PASSWORD")
    from_addr = config.get("SMTP_FROM_ADDRESS", "noreply@roomie.local")
    from_name = config.get("SMTP_FROM_NAME", "Roomie Monitoring")

    target = incident.get("target", "system")
    severity = incident.get("severity", "warning").upper()
    title = incident.get("title", "Operational Incident")

    if is_recovery:
        subject = f"[Roomie Recovery] Resolved: {title}"
    elif is_reminder:
        subject = f"[Roomie Alert] REMINDER ({incident.get('consecutive_samples', 0)}m): {title}"
    else:
        subject = f"[Roomie Alert] {severity}: {title}"

    body_lines = [
        "Roomie Host Monitoring Alert Notification",
        "----------------------------------------",
        f"Event Type:       {'RECOVERY' if is_recovery else ('REMINDER' if is_reminder else 'NEW INCIDENT')}",
        f"Target:           {target}",
        f"Severity:         {severity}",
        f"Title:            {title}",
        f"Timestamp (UTC):  {format_iso_utc()}",
        f"Incident ID:      {incident.get('id') or incident.get('incident_id', 'unknown')}",
        f"Opened At:        {incident.get('opened_at', 'unknown')}",
    ]

    if is_recovery:
        body_lines.append(f"Resolved At:      {incident.get('resolved_at', format_iso_utc())}")
        body_lines.append(f"Duration:         {incident.get('duration_seconds', 0)} seconds")
        body_lines.append(f"Resolved Value:   {incident.get('resolved_value', 'Normal')}")
    else:
        body_lines.append(f"Current Value:    {incident.get('current_value', 'Unknown')}")
        body_lines.append(f"Duration Streak:  {incident.get('consecutive_samples', 0)} samples")

    body_lines.extend([
        "",
        "Operational Notes:",
        "- This alert was generated by the autonomous Ubuntu host systemd monitor.",
        "- Note: SMTP acceptance by server does not guarantee final mailbox delivery (accepted != delivered).",
        "- Security: Automated message generated without secrets, tokens, or PII.",
    ])

    msg = email.message.EmailMessage()
    msg["From"] = f"{from_name} <{from_addr}>" if from_name else from_addr
    msg["To"] = recipient
    msg["Subject"] = subject
    msg.set_content("\n".join(body_lines))

    try:
        if smtp_client_factory:
            with smtp_client_factory(host, port, timeout=timeout) as client:
                if security == "StartTls":
                    client.starttls(context=ssl.create_default_context())
                if username and password:
                    client.login(username, password)
                client.send_message(msg)
        else:
            if security not in {"StartTls", "SslOnConnect"}:
                print("[collector] TLS is required for alert delivery.", file=sys.stderr)
                return False
            cls = smtplib.SMTP_SSL if security == "SslOnConnect" else smtplib.SMTP
            with cls(host, port, timeout=timeout) as client:
                if security == "StartTls":
                    client.starttls(context=ssl.create_default_context())
                if username and password:
                    client.login(username, password)
                client.send_message(msg)
        return True
    except Exception as exc:
        print(f"[collector] SMTP alert delivery failed: {type(exc).__name__}", file=sys.stderr)
        return False


def enqueue_notification(
    state: Dict[str, Any],
    target: str,
    inc: Dict[str, Any],
    notif_type: str,
    now: float,
) -> None:
    """Enqueues a pending notification for delivery with retry tracking."""
    pending: List[Dict[str, Any]] = state.setdefault("pending_notifications", [])

    # Deduplicate pending notifications for the same target
    for p in pending:
        if p.get("target") == target:
            if notif_type in {"new", "reminder"} and p.get("notification_type") in {"new", "reminder"}:
                p["current_value"] = inc.get("current_value")
                p["consecutive_samples"] = inc.get("consecutive_samples", 0)
                return
            if p.get("notification_type") == notif_type:
                p["current_value"] = inc.get("current_value")
                p["consecutive_samples"] = inc.get("consecutive_samples", 0)
                return

    notif = {
        "id": f"notif_{target}_{int(now)}_{notif_type}",
        "target": target,
        "incident_id": inc.get("id"),
        "notification_type": notif_type,
        "severity": inc.get("severity", "warning"),
        "title": inc.get("title", ""),
        "opened_at": inc.get("opened_at"),
        "resolved_at": inc.get("resolved_at"),
        "duration_seconds": inc.get("duration_seconds"),
        "resolved_value": inc.get("resolved_value"),
        "current_value": inc.get("current_value"),
        "consecutive_samples": inc.get("consecutive_samples", 0),
        "enqueued_at": now,
        "attempts": 0,
        "next_retry_at": now,
    }
    pending.append(notif)
    if len(pending) > MAX_PENDING_NOTIFICATIONS:
        pending[:] = pending[-MAX_PENDING_NOTIFICATIONS:]


def process_pending_notifications(
    state: Dict[str, Any],
    config: Dict[str, Any],
    now_ts: float,
    smtp_client_factory: Optional[Callable[..., Any]] = None,
    time_budget_seconds: float = SMTP_TIME_BUDGET_SECONDS,
) -> None:
    """Processes enqueued alert notifications while honoring execution time budget and backoff."""
    pending: List[Dict[str, Any]] = state.setdefault("pending_notifications", [])
    if not pending:
        return

    start_wall = time.time()
    active_incidents = state.setdefault("active_incidents", {})
    remaining_pending: List[Dict[str, Any]] = []

    for notif in list(pending):
        # Honor overall time budget to respect systemd 45s service bound
        if time.time() - start_wall >= time_budget_seconds:
            remaining_pending.append(notif)
            continue

        # Respect retry backoff schedule
        next_retry = notif.get("next_retry_at", 0)
        if now_ts < next_retry:
            remaining_pending.append(notif)
            continue

        success = send_alert_email(
            incident=notif,
            config=config,
            is_recovery=(notif.get("notification_type") == "recovery"),
            is_reminder=(notif.get("notification_type") == "reminder"),
            smtp_client_factory=smtp_client_factory,
        )

        if success:
            target = notif.get("target")
            # Mark last_notified_at only AFTER verified successful delivery
            if notif.get("notification_type") in {"new", "reminder"} and target in active_incidents:
                active_incidents[target]["last_notified_at"] = format_iso_utc(now_ts)
        else:
            attempts = notif.get("attempts", 0) + 1
            notif["attempts"] = attempts
            if attempts < MAX_NOTIFICATION_RETRIES:
                backoff = min(300.0, 30.0 * (2 ** (attempts - 1)))
                notif["next_retry_at"] = now_ts + backoff
                remaining_pending.append(notif)
            else:
                print(f"[collector] Dropping notification {notif.get('id')} after {attempts} failed attempts", file=sys.stderr)

    if len(remaining_pending) > MAX_PENDING_NOTIFICATIONS:
        remaining_pending = remaining_pending[-MAX_PENDING_NOTIFICATIONS:]
    state["pending_notifications"] = remaining_pending


# ---------------------------------------------------------------------------
# Incident Evaluation & State Management
# ---------------------------------------------------------------------------

def evaluate_incidents(
    state: Dict[str, Any],
    config: Dict[str, Any],
    host_metrics: Dict[str, Any],
    containers: Dict[str, Any],
    services: Dict[str, Any],
    now_ts: Optional[float] = None,
    smtp_client_factory: Optional[Callable[..., Any]] = None,
) -> Tuple[List[Dict[str, Any]], List[Dict[str, Any]]]:
    now = now_ts if now_ts is not None else time.time()
    iso_now = format_iso_utc(now)

    counters: Dict[str, int] = state.setdefault("counters", {})
    tracking: Dict[str, Dict[str, Any]] = state.setdefault("tracking", {})
    active_incidents: Dict[str, Dict[str, Any]] = state.setdefault("active_incidents", {})
    recent_history: List[Dict[str, Any]] = state.setdefault("recent_history", [])

    # 1. Monitored Targets Evaluation
    # --------------------------------------------------
    # Target 1: app
    app_cont = containers.get("app", {})
    api_health = services.get("api_health", {})
    app_running = app_cont.get("running")
    app_docker_health = app_cont.get("health")
    api_status = api_health.get("status")

    # Explicit failure: container stopped, docker health explicitly unhealthy, or API probe failed
    app_is_failed = (
        app_running is False
        or app_docker_health == "unhealthy"
        or api_status == "unhealthy"
    )
    # Proven healthy: container running, docker health is healthy or absent ("none"), and API probe 200 OK
    app_is_healthy = (
        app_running is True
        and app_docker_health in {"healthy", "none"}
        and api_status == "healthy"
    )
    app_is_unknown = not app_is_failed and not app_is_healthy
    app_detail = f"Container running={app_running}, health={app_docker_health}, /api/health status={api_status}"

    # Target 2: db (Uses configured actual Docker healthcheck)
    db_cont = containers.get("db", {})
    db_running = db_cont.get("running")
    db_docker_health = db_cont.get("health")

    db_is_failed = (
        db_running is False
        or db_docker_health == "unhealthy"
    )
    db_is_healthy = (
        db_running is True
        and db_docker_health == "healthy"
    )
    db_is_unknown = not db_is_failed and not db_is_healthy
    db_detail = f"Container running={db_running}, health={db_docker_health}"

    # Target 3: cpu (10 minutes sustained, usage >= 85%)
    cpu_percent = host_metrics.get("cpu", {}).get("percent")
    cpu_is_failed = (cpu_percent is not None and cpu_percent >= RESOURCE_HIGH_PERCENT)
    cpu_is_healthy = (cpu_percent is not None and cpu_percent < RESOURCE_HIGH_PERCENT)
    cpu_is_unknown = (cpu_percent is None)
    cpu_detail = f"CPU usage {cpu_percent}% (threshold {RESOURCE_HIGH_PERCENT}%)"

    # Target 4: ram (10 minutes sustained, usage >= 85%)
    ram_percent = host_metrics.get("memory", {}).get("percent")
    ram_is_failed = (ram_percent is not None and ram_percent >= RESOURCE_HIGH_PERCENT)
    ram_is_healthy = (ram_percent is not None and ram_percent < RESOURCE_HIGH_PERCENT)
    ram_is_unknown = (ram_percent is None)
    ram_detail = f"RAM usage {ram_percent}% (threshold {RESOURCE_HIGH_PERCENT}%)"

    # Target 5: disk (5 minutes sustained, usage >= 85%)
    disk_percent = host_metrics.get("disk", {}).get("percent")
    disk_is_failed = (disk_percent is not None and disk_percent >= RESOURCE_HIGH_PERCENT)
    disk_is_healthy = (disk_percent is not None and disk_percent < RESOURCE_HIGH_PERCENT)
    disk_is_unknown = (disk_percent is None)
    disk_detail = f"Disk usage {disk_percent}% (threshold {RESOURCE_HIGH_PERCENT}%)"

    targets_specs = [
        {
            "target": "app",
            "is_failed": app_is_failed,
            "is_healthy": app_is_healthy,
            "is_unknown": app_is_unknown,
            "threshold_count": APP_DB_FAIL_THRESHOLD,
            "threshold_duration": APP_DB_SUSTAINED_DURATION_SECONDS,
            "severity": "critical",
            "title": "Application container or /api/health failed 3 consecutive checks",
            "detail": app_detail,
        },
        {
            "target": "db",
            "is_failed": db_is_failed,
            "is_healthy": db_is_healthy,
            "is_unknown": db_is_unknown,
            "threshold_count": APP_DB_FAIL_THRESHOLD,
            "threshold_duration": APP_DB_SUSTAINED_DURATION_SECONDS,
            "severity": "critical",
            "title": "Database container (rehouse-db-1) unhealthy or stopped",
            "detail": db_detail,
        },
        {
            "target": "cpu",
            "is_failed": cpu_is_failed,
            "is_healthy": cpu_is_healthy,
            "is_unknown": cpu_is_unknown,
            "threshold_count": CPU_RAM_SUSTAINED_THRESHOLD,
            "threshold_duration": CPU_RAM_SUSTAINED_DURATION_SECONDS,
            "severity": "warning",
            "title": "Host CPU utilization >= 85% sustained for 10 minutes",
            "detail": cpu_detail,
        },
        {
            "target": "ram",
            "is_failed": ram_is_failed,
            "is_healthy": ram_is_healthy,
            "is_unknown": ram_is_unknown,
            "threshold_count": CPU_RAM_SUSTAINED_THRESHOLD,
            "threshold_duration": CPU_RAM_SUSTAINED_DURATION_SECONDS,
            "severity": "warning",
            "title": "Host RAM utilization >= 85% sustained for 10 minutes",
            "detail": ram_detail,
        },
        {
            "target": "disk",
            "is_failed": disk_is_failed,
            "is_healthy": disk_is_healthy,
            "is_unknown": disk_is_unknown,
            "threshold_count": DISK_SUSTAINED_THRESHOLD,
            "threshold_duration": DISK_SUSTAINED_DURATION_SECONDS,
            "severity": "warning",
            "title": "Host Disk utilization >= 85% sustained for 5 minutes",
            "detail": disk_detail,
        },
    ]

    for spec in targets_specs:
        target = spec["target"]
        streak_key = f"{target}_streak"
        t_track = tracking.setdefault(target, {})
        last_ts = t_track.get("last_sample_ts")

        # 1. Reject too-fast duplicate samples (< 55s spacing)
        if last_ts is not None:
            elapsed = now - last_ts
            if elapsed < MIN_SAMPLE_SPACING_SECONDS:
                # Rapid duplicate sample: skip advancing counters/durations
                continue

        # 2. Check for timer gaps > 2 minutes (120 seconds)
        is_gap = (last_ts is not None and (now - last_ts > MAX_SAMPLE_GAP_SECONDS))
        if is_gap:
            # Gaps > 2 min reset candidate sustained period (continuous condition broken)
            t_track["candidate_first_seen_ts"] = None
            t_track["candidate_count"] = 0
            counters[streak_key] = 0

        # 3. Handle Sample States: Distinguish Active Incident vs Candidate Condition
        if target in active_incidents:
            # Incident is already OPEN
            if spec["is_healthy"]:
                # Resolves ONLY on proven healthy
                t_track["candidate_first_seen_ts"] = None
                t_track["candidate_count"] = 0
                counters[streak_key] = 0

                inc = active_incidents.pop(target)
                opened_ts = parse_iso_utc(inc.get("opened_at")) or now
                duration_sec = round(max(0.0, now - opened_ts))
                inc["resolved_at"] = iso_now
                inc["duration_seconds"] = duration_sec
                inc["resolved_value"] = spec["detail"]

                enqueue_notification(state, target, inc, "recovery", now)

                recent_history.insert(0, inc)
                if len(recent_history) > MAX_RECENT_INCIDENTS:
                    recent_history[:] = recent_history[:MAX_RECENT_INCIDENTS]

            elif spec["is_failed"]:
                # Still failing: check reminder cooldown
                inc = active_incidents[target]
                inc["current_value"] = spec["detail"]
                if not is_gap:
                    t_track["candidate_count"] = t_track.get("candidate_count", 0) + 1
                else:
                    t_track["candidate_count"] = 1
                    t_track["candidate_first_seen_ts"] = now
                inc["consecutive_samples"] = t_track["candidate_count"]
                counters[streak_key] = t_track["candidate_count"]

                last_notified_ts = parse_iso_utc(inc.get("last_notified_at"))
                if last_notified_ts is not None and (now - last_notified_ts >= ALERT_COOLDOWN_SECONDS):
                    enqueue_notification(state, target, inc, "reminder", now)

            elif spec["is_unknown"]:
                # Unknown: incident STAYS OPEN; candidate streak resets
                t_track["candidate_first_seen_ts"] = None
                t_track["candidate_count"] = 0
                counters[streak_key] = 0

            t_track["last_sample_ts"] = now

        else:
            # Incident is NOT yet active: track candidate sustained condition
            if spec["is_unknown"]:
                # Unknown sample resets candidate sustained period
                t_track["candidate_first_seen_ts"] = None
                t_track["candidate_count"] = 0
                counters[streak_key] = 0

            elif spec["is_healthy"]:
                # Healthy sample resets candidate sustained period
                t_track["candidate_first_seen_ts"] = None
                t_track["candidate_count"] = 0
                counters[streak_key] = 0

            elif spec["is_failed"]:
                # Accumulate candidate sustained window
                if t_track.get("candidate_first_seen_ts") is None:
                    t_track["candidate_first_seen_ts"] = now
                    t_track["candidate_count"] = 1
                else:
                    t_track["candidate_count"] = t_track.get("candidate_count", 0) + 1

                candidate_count = t_track["candidate_count"]
                candidate_duration = now - t_track["candidate_first_seen_ts"]
                counters[streak_key] = candidate_count

                threshold_count = spec["threshold_count"]
                threshold_duration = spec["threshold_duration"]

                # Must satisfy BOTH minimum sample count AND real elapsed duration
                if candidate_count >= threshold_count and candidate_duration >= threshold_duration:
                    inc = {
                        "id": f"inc_{target}_{int(now)}",
                        "target": target,
                        "severity": spec["severity"],
                        "title": spec["title"],
                        "opened_at": iso_now,
                        "last_notified_at": None,  # Recorded only after verified delivery success
                        "consecutive_samples": candidate_count,
                        "current_value": spec["detail"],
                    }
                    active_incidents[target] = inc
                    enqueue_notification(state, target, inc, "new", now)

            t_track["last_sample_ts"] = now

    # Dispatch pending notifications honoring bounded retry & 45s service budget
    process_pending_notifications(
        state=state,
        config=config,
        now_ts=now,
        smtp_client_factory=smtp_client_factory,
    )

    active_list = list(active_incidents.values())
    return active_list, recent_history


# ---------------------------------------------------------------------------
# Main Collection Cycle
# ---------------------------------------------------------------------------

def run_collection_cycle(
    config: Dict[str, Any],
    state: Dict[str, Any],
    now_ts: Optional[float] = None,
    docker_runner: Optional[Callable[..., Any]] = None,
    http_getter: Optional[Callable[..., Any]] = None,
    smtp_client_factory: Optional[Callable[..., Any]] = None,
) -> Dict[str, Any]:
    now = now_ts if now_ts is not None else time.time()
    iso_now = format_iso_utc(now)
    start_wall = time.time()

    # 1. Host Resources
    host_info = collect_host_info()
    cpu_info = collect_cpu(state)
    mem_info = collect_memory()
    disk_info = collect_disk("/")

    host_section = {
        "hostname": host_info.get("hostname"),
        "os": host_info.get("os"),
        "uptime_seconds": host_info.get("uptime_seconds"),
        "cpu": cpu_info,
        "memory": mem_info,
        "disk": disk_info,
    }

    # 2. Container Status
    app_cont = collect_container_status(
        config.get("APP_CONTAINER_NAME", DEFAULT_APP_CONTAINER),
        "app",
        runner=docker_runner,
    )
    db_cont = collect_container_status(
        config.get("DB_CONTAINER_NAME", DEFAULT_DB_CONTAINER),
        "db",
        runner=docker_runner,
    )
    containers_section = {
        "app": app_cont,
        "db": db_cont,
    }

    # 3. Services (API health & Backup watchdog)
    api_health = collect_api_health(
        config.get("HEALTH_CHECK_URL", DEFAULT_HEALTH_URL),
        getter=http_getter,
    )
    backup_watchdog = collect_backup_watchdog(
        config.get("BACKUP_SUCCESS_FILE", DEFAULT_BACKUP_SUCCESS_FILE),
        now_ts=now,
    )
    services_section = {
        "api_health": api_health,
        "backup_watchdog": backup_watchdog,
    }

    # 4. Evaluate Incidents & Alerting
    active_incidents, recent_history = evaluate_incidents(
        state=state,
        config=config,
        host_metrics=host_section,
        containers=containers_section,
        services=services_section,
        now_ts=now,
        smtp_client_factory=smtp_client_factory,
    )

    # 5. Sliding Sample History (Bounded to 60 minute samples)
    samples: List[Dict[str, Any]] = state.setdefault("samples", [])
    app_healthy = bool(
        app_cont.get("running") is True
        and app_cont.get("health") in {"healthy", "none"}
        and api_health.get("status") == "healthy"
    )
    db_healthy = bool(
        db_cont.get("running") is True
        and db_cont.get("health") == "healthy"
    )

    new_sample = {
        "timestamp": iso_now,
        "cpu_percent": cpu_info.get("percent"),
        "ram_percent": mem_info.get("percent"),
        "disk_percent": disk_info.get("percent"),
        "app_healthy": app_healthy,
        "db_healthy": db_healthy,
    }
    samples.append(new_sample)
    if len(samples) > MAX_SAMPLE_HISTORY:
        samples[:] = samples[-MAX_SAMPLE_HISTORY:]

    duration_ms = round((time.time() - start_wall) * 1000.0, 1)

    # Compile Final JSON Payload (Omits private notification queue and credentials)
    payload = {
        "version": SCHEMA_VERSION,
        "generated_at": iso_now,
        "collector": {
            "version": VERSION,
            "status": "healthy",
            "duration_ms": duration_ms,
        },
        "host": host_section,
        "containers": containers_section,
        "services": services_section,
        "incidents": {
            "active": active_incidents,
            "recent_history": recent_history,
        },
        "samples_history": {
            "window_size": MAX_SAMPLE_HISTORY,
            "samples": samples,
        },
    }

    return payload


# ---------------------------------------------------------------------------
# CLI Entry Point
# ---------------------------------------------------------------------------

def main(argv: Optional[List[str]] = None) -> int:
    parser = argparse.ArgumentParser(description="Roomie Ubuntu Host Monitoring Collector")
    parser.add_argument("--config-dir", help="Directory containing environment files (/etc/rehouse)")
    parser.add_argument("--output-file", help="Path to write host-metrics.json")
    parser.add_argument("--state-file", help="Path to collector-state.json")
    parser.add_argument("--dry-run", action="store_true", help="Print metrics JSON to stdout without modifying disk or sending alerts")
    parser.add_argument("--loop", action="store_true", help="Run continuously every 60 seconds")
    parser.add_argument("--interval", type=int, default=60, help="Loop interval in seconds (default 60)")
    args = parser.parse_args(argv)

    config_files = None
    if args.config_dir:
        cd = Path(args.config_dir)
        config_files = [
            str(cd / "monitoring.env"),
            str(cd / "runtime.env"),
            str(cd / "backup.env"),
        ]

    config = load_config(config_files)
    output_file = Path(args.output_file or config["METRICS_OUTPUT_FILE"])
    state_file = Path(args.state_file or config["STATE_FILE"])

    if args.dry_run:
        state = load_state(state_file)
        payload = run_collection_cycle(config, state)
        print(json.dumps(payload, indent=2))
        return 0

    while True:
        state = load_state(state_file)
        payload = run_collection_cycle(config, state)
        save_atomic_json(output_file, payload, mode=0o644)
        save_atomic_json(state_file, state, mode=0o600)

        if not args.loop:
            break
        time.sleep(args.interval)

    return 0


if __name__ == "__main__":
    sys.exit(main())
