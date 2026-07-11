"""Stdio instance-resolution guard tests.

Regression coverage for #1023: the stdio connection pool used to silently
route an unbound session (no unity_instance, no default) to the most-recently
heartbeated editor, letting one project's session retarget another. The pool
now refuses to guess when 2+ instances are connected, mirroring the HTTP
"multiple connected, no active set" guard.
"""

from datetime import datetime, timedelta

import pytest

from models.models import UnityInstanceInfo
from transport.legacy.unity_connection import UnityConnectionPool


def _instance(name: str, port: int, heartbeat: datetime | None) -> UnityInstanceInfo:
    return UnityInstanceInfo(
        id=f"{name}@{name.lower()}hash",
        name=name,
        path=f"/path/{name}",
        hash=f"{name.lower()}hash",
        port=port,
        status="running",
        last_heartbeat=heartbeat,
    )


def test_resolve_none_with_single_instance_selects_it():
    """Sole connected instance is unambiguous and selected without a hint."""
    pool = UnityConnectionPool()
    only = _instance("Solo", 6400, datetime.now())

    resolved = pool._resolve_instance_id(None, [only])

    assert resolved.id == only.id


def test_resolve_none_with_multiple_instances_raises():
    """2+ instances and nothing pinned must error instead of guessing (#1023)."""
    pool = UnityConnectionPool()
    now = datetime.now()
    newer = _instance("Newer", 6400, now)
    older = _instance("Older", 6401, now - timedelta(seconds=30))

    with pytest.raises(ConnectionError, match="Multiple Unity instances"):
        pool._resolve_instance_id(None, [newer, older])


def test_resolve_none_does_not_fall_back_to_most_recent_heartbeat():
    """The guard must not leak the most-recently-heartbeated id into the error.

    Proves we no longer auto-pick sorted_instances[0]; both ids are offered as
    explicit choices rather than one being silently returned.
    """
    pool = UnityConnectionPool()
    now = datetime.now()
    newer = _instance("Newer", 6400, now)
    older = _instance("Older", 6401, now - timedelta(seconds=30))

    with pytest.raises(ConnectionError) as exc_info:
        pool._resolve_instance_id(None, [newer, older])

    message = str(exc_info.value)
    assert newer.id in message
    assert older.id in message


def test_resolve_none_with_default_instance_still_works():
    """An explicit default short-circuits the multi-instance guard."""
    pool = UnityConnectionPool()
    pool._default_instance_id = "Newer@newerhash"
    now = datetime.now()
    newer = _instance("Newer", 6400, now)
    older = _instance("Older", 6401, now - timedelta(seconds=30))

    resolved = pool._resolve_instance_id(None, [newer, older])

    assert resolved.id == "Newer@newerhash"


def test_resolve_explicit_identifier_unaffected_by_guard():
    """Explicit selection still resolves even with multiple instances."""
    pool = UnityConnectionPool()
    now = datetime.now()
    newer = _instance("Newer", 6400, now)
    older = _instance("Older", 6401, now - timedelta(seconds=30))

    resolved = pool._resolve_instance_id("Older@olderhash", [newer, older])

    assert resolved.id == "Older@olderhash"


def test_resolve_no_instances_raises_distinct_error():
    """No instances at all still raises the "none found" error, not the guard."""
    pool = UnityConnectionPool()

    with pytest.raises(ConnectionError, match="No Unity Editor instances found"):
        pool._resolve_instance_id(None, [])
