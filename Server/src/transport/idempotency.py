"""Idempotency keys for Unity command delivery.

Every logical tool call carries one ``request_id``. All transport-level resends of that
call (stdio reconnect loop, reload wait loop, HTTP resend after a plugin reconnect) reuse
the same id, and the Unity-side command ledger executes a given id at most once, replaying
the stored response for duplicates.

When a call ends with an unknown outcome (``hint == "retry"``: the socket dropped or timed
out after the command may already have run), its id is remembered under a fingerprint of
(instance, command, params) for a short window. An identical call made inside that window,
typically the client retrying after the hint, reuses the id, so the retry receives the
original result instead of running the command a second time.
"""
from __future__ import annotations

import hashlib
import json
import threading
import time
import uuid
from collections import OrderedDict
from typing import Any

RETRY_WINDOW_SECONDS = 120.0
MAX_REMEMBERED = 256


def new_request_id() -> str:
    return uuid.uuid4().hex


def fingerprint(unity_instance: str | None, command_type: str, params: Any) -> str:
    body = json.dumps(
        [unity_instance or "", command_type, params],
        sort_keys=True,
        default=str,
        separators=(",", ":"),
    )
    return hashlib.sha256(body.encode("utf-8")).hexdigest()


def _is_retry_hint(response: Any) -> bool:
    if isinstance(response, dict):
        return response.get("success") is False and response.get("hint") == "retry"
    return getattr(response, "success", None) is False and getattr(response, "hint", None) == "retry"


class RetryKeyCache:
    """Maps call fingerprints to the request_id of a recent unknown-outcome attempt."""

    def __init__(self, window_s: float = RETRY_WINDOW_SECONDS, max_entries: int = MAX_REMEMBERED):
        self._window_s = window_s
        self._max_entries = max_entries
        self._entries: OrderedDict[str, tuple[str, float]] = OrderedDict()
        self._lock = threading.Lock()

    def request_id_for(self, fp: str) -> str:
        """Return the remembered id for this fingerprint, or mint a new one."""
        now = time.monotonic()
        with self._lock:
            entry = self._entries.get(fp)
            if entry is not None and now - entry[1] <= self._window_s:
                return entry[0]
            self._entries.pop(fp, None)
        return new_request_id()

    def record_outcome(self, fp: str, request_id: str, response: Any) -> None:
        with self._lock:
            if not _is_retry_hint(response):
                self._entries.pop(fp, None)
                return
            self._entries[fp] = (request_id, time.monotonic())
            self._entries.move_to_end(fp)
            while len(self._entries) > self._max_entries:
                self._entries.popitem(last=False)

    def clear(self) -> None:
        with self._lock:
            self._entries.clear()


retry_keys = RetryKeyCache()
