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


def test_instance_is_selected_before_anything_else(monkeypatch):
    """With several editors on one HTTP server, --instance pins the stress run to one of them."""
    import asyncio
    import json
    from contextlib import asynccontextmanager
    from types import SimpleNamespace

    calls = []

    class FakeClient:
        async def call_tool(self, tool, arguments, **_kwargs):
            calls.append((tool, arguments))
            ok = tool == "set_active_instance"
            return SimpleNamespace(content=[SimpleNamespace(text=json.dumps({"success": ok}))])

    @asynccontextmanager
    async def fake_client(_args):
        yield FakeClient()

    monkeypatch.setattr(stress, "_client", fake_client)
    args = SimpleNamespace(instance="UnityMCPTests@9a77", project_path=".", call_timeout=5)
    assert asyncio.run(stress.main_async(args)) == 2  # stops at the failing activate, after selecting
    assert calls[0] == ("set_active_instance", {"instance": "UnityMCPTests@9a77"})
    assert calls[1][0] == "manage_tools"
