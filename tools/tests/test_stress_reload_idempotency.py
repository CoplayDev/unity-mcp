"""The stress harness must not turn a ledger outcome_unknown into a fresh execution."""
from __future__ import annotations

import sys
from pathlib import Path

_TOOLS_DIR = Path(__file__).resolve().parents[1]
if str(_TOOLS_DIR) not in sys.path:
    sys.path.insert(0, str(_TOOLS_DIR))

import stress_reload_idempotency as stress  # noqa: E402


def test_outcome_unknown_is_final_not_transient():
    resp = {"success": False, "code": "outcome_unknown",
            "error": "Command 'execute_code' was still running when the Unity domain reloaded"}
    assert stress._is_transient(resp) is False
    assert stress._is_transient({"success": False, "error": "x", "data": {"code": "outcome_unknown"}}) is False


def test_lost_reply_is_still_transient():
    assert stress._is_transient({"success": False, "hint": "retry", "error": "disconnected"}) is True
    assert stress._is_transient({"success": False, "error": "Unity is reloading"}) is True
    assert stress._is_transient({"success": True}) is False
