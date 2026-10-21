"""Disposable PostgreSQL fixture client via stdlib and docker exec psql."""
from __future__ import annotations

from datetime import date, datetime
import os
import re
import subprocess
from typing import Any
from urllib.parse import urlsplit
import urllib.request
import uuid


def quote_sql_val(val: Any) -> str:
    # ponytail: ANSI SQL string/literal formatting; upgrade to pg protocol if direct socket driver added.
    if val is None:
        return "NULL"
    if isinstance(val, bool):
        return "TRUE" if val else "FALSE"
    if isinstance(val, (int, float)):
        return str(val)
    if isinstance(val, (uuid.UUID, date, datetime)):
        return f"'{str(val)}'"
    if isinstance(val, str):
        return "'" + val.replace("'", "''") + "'"
    if isinstance(val, bytes):
        return r"'\x" + val.hex() + "'"
    return "'" + str(val).replace("'", "''") + "'"


def format_sql(sql: str, params: tuple | list = ()) -> str:
    if not params:
        return sql
    out = []
    param_idx = 0
    in_quote = False
    quote_char = None
    i = 0
    n = len(sql)
    while i < n:
        c = sql[i]
        if in_quote:
            out.append(c)
            if c == quote_char:
                if i + 1 < n and sql[i + 1] == quote_char:
                    out.append(sql[i + 1])
                    i += 1
                else:
                    in_quote = False
        else:
            if c in ("'", '"'):
                in_quote = True
                quote_char = c
                out.append(c)
            elif c == "?":
                if param_idx >= len(params):
                    raise ValueError(f"Too few parameters for placeholders in: {sql}")
                out.append(quote_sql_val(params[param_idx]))
                param_idx += 1
            else:
                out.append(c)
        i += 1
    if param_idx != len(params):
        raise ValueError(f"Expected {param_idx} parameters, got {len(params)}")
    return "".join(out)


class PgFixture:
    def __init__(self, connection: str | None = None, container: str | None = None):
        conn_str = connection or os.environ.get("TEST_HTTP_POSTGRES_CONNECTION", "")
        self.container = container or os.environ.get("TEST_HTTP_POSTGRES_CONTAINER", "")
        if not self.container or not re.fullmatch(r"[A-Za-z0-9_-]+", self.container):
            raise RuntimeError("Invalid or missing TEST_HTTP_POSTGRES_CONTAINER")
        if self.container == "rehouse-pg-migration-db":
            raise RuntimeError("Refusing dev container rehouse-pg-migration-db")
        fields = dict(part.split("=", 1) for part in conn_str.split(";") if "=" in part)
        self.database = fields.get("Database", "")
        self.user = fields.get("Username", "")
        host = fields.get("Host", "")
        port = fields.get("Port", "")
        if not self.database.startswith("roomie_qa_"):
            raise RuntimeError(f"Refusing non-disposable database: {self.database}")
        if host not in ("127.0.0.1", "localhost", "::1"):
            raise RuntimeError(f"Refusing non-loopback database host: {host}")
        if port == "15432":
            raise RuntimeError("Refusing dev port 15432")
        if not self.user:
            raise RuntimeError("Missing Username in database connection")
        self._last_result: list[list[str]] = []

    def execute(self, sql: str, params: tuple | list = ()) -> PgFixture:
        query = format_sql(sql, params)
        trimmed = query.strip().upper()
        if trimmed.startswith("SELECT") or trimmed.startswith("WITH"):
            self._last_result = self.query(sql, params)
        else:
            self._last_result = []
            cmd = ["docker", "exec", "-i", self.container, "psql", "-X", "-q", "-v", "ON_ERROR_STOP=1",
                   "-U", self.user, "-d", self.database]
            res = subprocess.run(cmd, input=query, text=True, capture_output=True)
            if res.returncode != 0:
                raise RuntimeError(f"psql execution failed: {res.stderr}\nQuery: {query}")
        return self

    def fetchone(self) -> tuple[str, ...] | None:
        return tuple(self._last_result[0]) if self._last_result else None

    def fetchall(self) -> list[tuple[str, ...]]:
        return [tuple(r) for r in self._last_result]

    def commit(self) -> None:
        pass

    def close(self) -> None:
        pass

    def query(self, sql: str, params: tuple | list = ()) -> list[list[str]]:
        query = format_sql(sql, params)
        cmd = ["docker", "exec", "-i", self.container, "psql", "-X", "-A", "-F", "\t", "-t", "-q",
               "-v", "ON_ERROR_STOP=1", "-U", self.user, "-d", self.database]
        res = subprocess.run(cmd, input=query, text=True, capture_output=True)
        if res.returncode != 0:
            raise RuntimeError(f"psql query failed: {res.stderr}\nQuery: {query}")
        lines = [line for line in res.stdout.splitlines() if line]
        return [line.split("\t") for line in lines]

    def query_row(self, sql: str, params: tuple | list = ()) -> tuple[str, ...] | None:
        rows = self.query(sql, params)
        return tuple(rows[0]) if rows else None

    def query_val(self, sql: str, params: tuple | list = ()) -> str | None:
        row = self.query_row(sql, params)
        return row[0] if row else None


def get_pg_fixture(api_url: str | None = None) -> PgFixture:
    fixture = PgFixture()
    if api_url:
        parsed = urlsplit(api_url)
        if parsed.hostname not in ("127.0.0.1", "localhost", "::1"):
            raise RuntimeError(f"API must be on loopback, got {api_url}")
        # Verify API identity on loopback
        try:
            req = urllib.request.Request(f"{api_url.rstrip('/')}/api/health", headers={"User-Agent": "pg_fixture"})
            with urllib.request.urlopen(req, timeout=2) as r:
                if r.status != 200:
                    raise RuntimeError(f"API health check failed with status {r.status}")
        except Exception as e:
            raise RuntimeError(f"Could not reach API at {api_url}: {e}")
        # Verify database connection identity
        current_db = fixture.query_val("SELECT current_database();")
        if current_db != fixture.database:
            raise RuntimeError(f"Fixture connected to {current_db}, expected {fixture.database}")
    return fixture
