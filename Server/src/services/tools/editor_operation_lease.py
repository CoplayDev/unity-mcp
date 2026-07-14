from __future__ import annotations

import json
import logging
import os
import re
import time
import uuid
from dataclasses import dataclass
from pathlib import Path
from typing import Any

from models import MCPResponse

logger = logging.getLogger(__name__)

LEASE_DIR_ENV = "UNITY_MCP_OPERATION_LEASE_DIR"
LEASE_TTL_ENV = "UNITY_MCP_OPERATION_LEASE_TTL_S"
DEFAULT_LEASE_TTL_S = 120.0
_SAFE_KEY_RE = re.compile(r"[^A-Za-z0-9_.@-]+")


@dataclass(frozen=True)
class EditorOperationLeaseInfo:
    instance_id: str
    operation: str
    owner: str
    started_unix_ms: int
    expires_unix_ms: int
    pid: int | None = None
    path: str | None = None


@dataclass
class EditorOperationLease:
    path: Path
    token: str
    info: EditorOperationLeaseInfo
    reentrant: bool = False
    _released: bool = False

    @property
    def instance_id(self) -> str:
        return self.info.instance_id

    @property
    def operation(self) -> str:
        return self.info.operation

    @property
    def owner(self) -> str:
        return self.info.owner

    def release(self) -> None:
        if self._released:
            return
        if self.reentrant:
            self._released = True
            return
        try:
            payload = _read_payload(self.path)
            if payload and payload.get("token") == self.token:
                self.path.unlink(missing_ok=True)
        except Exception as exc:  # pragma: no cover - defensive cleanup path
            logger.debug("Failed to release editor operation lease %s: %r", self.path, exc)
        finally:
            self._released = True


def operation_owner_from_context(ctx: Any) -> str:
    return f"pid:{os.getpid()}:ctx:{id(ctx)}"


def operation_busy_response(
    lease_info: EditorOperationLeaseInfo,
    *,
    retry_after_ms: int = 2000,
) -> MCPResponse:
    return MCPResponse(
        success=False,
        error="operation_busy",
        message=f"Unity editor operation already in progress: {lease_info.operation}",
        hint="retry",
        data={
            "reason": "operation_busy",
            "retry_after_ms": int(retry_after_ms),
            "instance_id": lease_info.instance_id,
            "operation": lease_info.operation,
            "owner": lease_info.owner,
            "pid": lease_info.pid,
            "expires_unix_ms": lease_info.expires_unix_ms,
        },
    )


def try_acquire_editor_operation_lease(
    unity_instance: str | None,
    operation: str,
    *,
    owner: str | None = None,
    ttl_s: float | None = None,
) -> tuple[EditorOperationLease | None, EditorOperationLeaseInfo | None]:
    instance_id = unity_instance or "default"
    lease_dir = _operation_lease_dir()
    lease_dir.mkdir(parents=True, exist_ok=True)
    path = lease_dir / f"{_safe_lease_key(instance_id)}.json"
    owner = owner or f"pid:{os.getpid()}"
    ttl_ms = int(_lease_ttl_s(ttl_s) * 1000)

    while True:
        now_ms = _now_ms()
        token = uuid.uuid4().hex
        payload = {
            "instance_id": instance_id,
            "operation": operation,
            "owner": owner,
            "pid": os.getpid(),
            "token": token,
            "started_unix_ms": now_ms,
            "expires_unix_ms": now_ms + ttl_ms,
        }

        try:
            fd = os.open(str(path), os.O_WRONLY | os.O_CREAT | os.O_EXCL)
        except FileExistsError:
            existing = _read_payload(path)
            if _is_lease_expired(existing, path, now_ms, ttl_ms):
                try:
                    path.unlink()
                except FileNotFoundError:
                    continue
                except OSError:
                    return None, _payload_to_info(existing, path, instance_id)
                continue
            if existing and existing.get("owner") == owner:
                return (
                    EditorOperationLease(
                        path,
                        str(existing.get("token") or ""),
                        _payload_to_info(existing, path, instance_id),
                        reentrant=True,
                    ),
                    None,
                )
            return None, _payload_to_info(existing, path, instance_id)

        with os.fdopen(fd, "w", encoding="utf-8") as lease_file:
            json.dump(payload, lease_file, separators=(",", ":"), sort_keys=True)
        return EditorOperationLease(path, token, _payload_to_info(payload, path, instance_id)), None


def _operation_lease_dir() -> Path:
    configured = os.environ.get(LEASE_DIR_ENV)
    if configured:
        return Path(configured)
    return Path.home() / ".unity-mcp" / "operation-leases"


def _lease_ttl_s(ttl_s: float | None) -> float:
    if ttl_s is None:
        raw = os.environ.get(LEASE_TTL_ENV)
        if raw is None:
            return DEFAULT_LEASE_TTL_S
        try:
            ttl_s = float(raw)
        except ValueError:
            return DEFAULT_LEASE_TTL_S

    try:
        value = float(ttl_s)
    except (TypeError, ValueError):
        return DEFAULT_LEASE_TTL_S
    return max(0.001, min(value, 3600.0))


def _safe_lease_key(instance_id: str) -> str:
    key = _SAFE_KEY_RE.sub("_", instance_id).strip("._-")
    return key or "default"


def _now_ms() -> int:
    return int(time.time() * 1000)


def _read_payload(path: Path) -> dict[str, Any] | None:
    try:
        raw = path.read_text(encoding="utf-8")
        payload = json.loads(raw)
        return payload if isinstance(payload, dict) else None
    except FileNotFoundError:
        return None
    except Exception:
        return None


def _payload_to_info(
    payload: dict[str, Any] | None,
    path: Path,
    fallback_instance_id: str,
) -> EditorOperationLeaseInfo:
    payload = payload or {}
    started = _int_or_default(payload.get("started_unix_ms"), _mtime_ms(path))
    expires = _int_or_default(
        payload.get("expires_unix_ms"),
        started + int(DEFAULT_LEASE_TTL_S * 1000),
    )
    pid = payload.get("pid")
    return EditorOperationLeaseInfo(
        instance_id=str(payload.get("instance_id") or fallback_instance_id),
        operation=str(payload.get("operation") or "unknown"),
        owner=str(payload.get("owner") or "unknown"),
        started_unix_ms=started,
        expires_unix_ms=expires,
        pid=pid if isinstance(pid, int) else None,
        path=str(path),
    )


def _is_lease_expired(
    payload: dict[str, Any] | None,
    path: Path,
    now_ms: int,
    ttl_ms: int,
) -> bool:
    if payload and isinstance(payload.get("expires_unix_ms"), int):
        return payload["expires_unix_ms"] <= now_ms
    try:
        return _mtime_ms(path) + ttl_ms <= now_ms
    except OSError:
        return True


def _mtime_ms(path: Path) -> int:
    try:
        return int(path.stat().st_mtime * 1000)
    except OSError:
        return _now_ms()


def _int_or_default(value: Any, default: int) -> int:
    return value if isinstance(value, int) else default
