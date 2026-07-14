"""Env-override clamping for session resolve/ready waits (#1207).

The ceiling used to equal the default (20s), which silently neutered
UNITY_MCP_SESSION_RESOLVE_MAX_WAIT_S for projects whose domain reloads or
test boundaries legitimately exceed 20s.
"""

from transport.plugin_hub import _read_bounded_wait_env

ENV = "UNITY_MCP_SESSION_RESOLVE_MAX_WAIT_S"


def test_default_when_unset(monkeypatch):
    monkeypatch.delenv(ENV, raising=False)
    assert _read_bounded_wait_env(ENV, default_s=20.0, max_s=120.0) == 20.0


def test_override_above_old_ceiling_is_honored(monkeypatch):
    monkeypatch.setenv(ENV, "45")
    assert _read_bounded_wait_env(ENV, default_s=20.0, max_s=120.0) == 45.0


def test_override_clamped_to_ceiling(monkeypatch):
    monkeypatch.setenv(ENV, "600")
    assert _read_bounded_wait_env(ENV, default_s=20.0, max_s=120.0) == 120.0


def test_negative_clamped_to_zero(monkeypatch):
    monkeypatch.setenv(ENV, "-5")
    assert _read_bounded_wait_env(ENV, default_s=20.0, max_s=120.0) == 0.0


def test_invalid_falls_back_to_default(monkeypatch):
    monkeypatch.setenv(ENV, "not-a-number")
    assert _read_bounded_wait_env(ENV, default_s=20.0, max_s=120.0) == 20.0
