"""
Local token usage estimation and reporting for MCP for Unity.

This module keeps a lightweight append-only JSONL log of tool/resource usage and
derives simple aggregate reports for the local `/token` dashboard.
"""

from __future__ import annotations

from collections import defaultdict
from dataclasses import dataclass
from datetime import datetime, timedelta
import json
import math
import os
from pathlib import Path
from threading import Lock
from typing import Any


def _now_local() -> datetime:
    return datetime.now().astimezone()


def _default_data_dir() -> Path:
    if os.name == "nt":
        base_dir = Path(os.environ.get(
            "APPDATA", Path.home() / "AppData" / "Roaming"))
    elif os.name == "posix":
        if "darwin" in os.uname().sysname.lower():
            base_dir = Path.home() / "Library" / "Application Support"
        else:
            base_dir = Path(os.environ.get(
                "XDG_DATA_HOME", Path.home() / ".local" / "share"))
    else:
        base_dir = Path.home() / ".unity-mcp"

    data_dir = base_dir / "UnityMCP"
    data_dir.mkdir(parents=True, exist_ok=True)
    return data_dir


def _safe_json(value: Any) -> str:
    try:
        return json.dumps(value, ensure_ascii=False, separators=(",", ":"), default=str)
    except Exception:
        try:
            return repr(value)
        except Exception:
            return "<unserializable>"


def sanitize_payload(value: Any) -> Any:
    if isinstance(value, dict):
        return {str(key): sanitize_payload(val) for key, val in value.items()}
    if isinstance(value, (list, tuple)):
        return [sanitize_payload(item) for item in value]
    if isinstance(value, (str, int, float, bool)) or value is None:
        return value
    return str(value)


def estimate_tokens(value: Any) -> int:
    text = _safe_json(value)
    if not text:
        return 0

    char_estimate = len(text) / 4.0
    word_estimate = len(text.split()) * 0.75
    return max(1, int(math.ceil(max(char_estimate, word_estimate))))


@dataclass
class TokenUsageEntry:
    ts: str
    kind: str
    name: str
    action: str | None
    success: bool
    duration_ms: float
    input_tokens: int
    output_tokens: int
    total_tokens: int


class TokenUsageStore:
    def __init__(self) -> None:
        self._lock = Lock()
        self._path = _default_data_dir() / "token_usage.jsonl"

    @property
    def path(self) -> Path:
        return self._path

    def record(
        self,
        *,
        kind: str,
        name: str,
        action: str | None,
        success: bool,
        duration_ms: float,
        input_payload: Any,
        output_payload: Any,
    ) -> None:
        input_tokens = estimate_tokens(input_payload)
        output_tokens = estimate_tokens(output_payload)
        entry = TokenUsageEntry(
            ts=_now_local().isoformat(),
            kind=kind,
            name=name,
            action=action,
            success=success,
            duration_ms=round(duration_ms, 2),
            input_tokens=input_tokens,
            output_tokens=output_tokens,
            total_tokens=input_tokens + output_tokens,
        )

        line = json.dumps(entry.__dict__, ensure_ascii=False, separators=(",", ":"))
        with self._lock:
            self._path.parent.mkdir(parents=True, exist_ok=True)
            with self._path.open("a", encoding="utf-8") as handle:
                handle.write(line + "\n")

    def load_entries(self) -> list[dict[str, Any]]:
        if not self._path.exists():
            return []

        rows: list[dict[str, Any]] = []
        with self._path.open("r", encoding="utf-8") as handle:
            for line in handle:
                line = line.strip()
                if not line:
                    continue
                try:
                    row = json.loads(line)
                except json.JSONDecodeError:
                    continue
                ts = row.get("ts")
                try:
                    row["_dt"] = datetime.fromisoformat(ts)
                except Exception:
                    continue
                rows.append(row)
        return rows

    def build_report(self) -> dict[str, Any]:
        rows = self.load_entries()
        now = _now_local()
        start_today = now.replace(hour=0, minute=0, second=0, microsecond=0)
        start_week = now - timedelta(days=7)
        start_month = now - timedelta(days=30)

        def summarize(filtered: list[dict[str, Any]]) -> dict[str, Any]:
            total_input = sum(int(row.get("input_tokens", 0)) for row in filtered)
            total_output = sum(int(row.get("output_tokens", 0)) for row in filtered)
            total_tokens = sum(int(row.get("total_tokens", 0)) for row in filtered)
            total_calls = len(filtered)
            success_calls = sum(1 for row in filtered if row.get("success"))
            avg_tokens = round(total_tokens / total_calls, 1) if total_calls else 0

            top_items: dict[tuple[str, str | None], dict[str, Any]] = {}
            for row in filtered:
                key = (str(row.get("name", "unknown")), row.get("action"))
                current = top_items.setdefault(key, {
                    "name": key[0],
                    "action": key[1],
                    "calls": 0,
                    "total_tokens": 0,
                    "input_tokens": 0,
                    "output_tokens": 0,
                })
                current["calls"] += 1
                current["total_tokens"] += int(row.get("total_tokens", 0))
                current["input_tokens"] += int(row.get("input_tokens", 0))
                current["output_tokens"] += int(row.get("output_tokens", 0))

            top_tools = sorted(
                top_items.values(),
                key=lambda item: (item["total_tokens"], item["calls"]),
                reverse=True,
            )[:10]

            return {
                "calls": total_calls,
                "success_calls": success_calls,
                "input_tokens": total_input,
                "output_tokens": total_output,
                "total_tokens": total_tokens,
                "avg_tokens_per_call": avg_tokens,
                "top_tools": top_tools,
            }

        daily_buckets: dict[str, dict[str, int]] = defaultdict(
            lambda: {"input_tokens": 0, "output_tokens": 0, "total_tokens": 0, "calls": 0}
        )
        for row in rows:
            day = row["_dt"].astimezone().strftime("%Y-%m-%d")
            bucket = daily_buckets[day]
            bucket["input_tokens"] += int(row.get("input_tokens", 0))
            bucket["output_tokens"] += int(row.get("output_tokens", 0))
            bucket["total_tokens"] += int(row.get("total_tokens", 0))
            bucket["calls"] += 1

        daily_series = [
            {"day": day, **values}
            for day, values in sorted(daily_buckets.items())
        ]

        since = rows[0]["_dt"].astimezone() if rows else None

        return {
            "generated_at": now.isoformat(),
            "since": since.isoformat() if since else None,
            "periods": {
                "today": summarize([row for row in rows if row["_dt"].astimezone() >= start_today]),
                "week": summarize([row for row in rows if row["_dt"].astimezone() >= start_week]),
                "month": summarize([row for row in rows if row["_dt"].astimezone() >= start_month]),
                "all": summarize(rows),
            },
            "daily_series": daily_series,
            "entries_count": len(rows),
            "log_path": str(self._path),
        }


_store: TokenUsageStore | None = None


def get_token_usage_store() -> TokenUsageStore:
    global _store
    if _store is None:
        _store = TokenUsageStore()
    return _store


def record_token_usage(
    *,
    kind: str,
    name: str,
    action: str | None,
    success: bool,
    duration_ms: float,
    input_payload: Any,
    output_payload: Any,
) -> None:
    get_token_usage_store().record(
        kind=kind,
        name=name,
        action=action,
        success=success,
        duration_ms=duration_ms,
        input_payload=sanitize_payload(input_payload),
        output_payload=sanitize_payload(output_payload),
    )
