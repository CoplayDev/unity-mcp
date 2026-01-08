import pytest

from models import MCPResponse
from services.state.external_changes_scanner import external_changes_scanner
from services.state.external_changes_scanner import ExternalChangesState

from .test_helpers import DummyContext


@pytest.mark.asyncio
async def test_refresh_unity_recovers_from_retry_disconnect(monkeypatch):
    """
    Option A: if Unity disconnects and the transport returns hint=retry, refresh_unity(wait_for_ready=true)
    should poll readiness and then return success + clear external dirty.
    """
    from services.tools.refresh_unity import refresh_unity

    ctx = DummyContext()
    ctx.set_state("unity_instance", "UnityMCPTests@cc8756d4cce0805a")

    # Seed dirty state
    inst = "UnityMCPTests@cc8756d4cce0805a"
    external_changes_scanner._states[inst] = ExternalChangesState(dirty=True, dirty_since_unix_ms=1)

    async def fake_send_with_unity_instance(send_fn, unity_instance, command_type, params, **kwargs):
        if command_type == "refresh_unity":
            return {"success": False, "error": "disconnected", "hint": "retry"}
        elif command_type == "get_editor_state":
            return {"success": True, "data": {"advice": {"ready_for_tools": True}}}
        raise ValueError(f"Unexpected command: {command_type}")

    import services.tools.refresh_unity as refresh_mod
    monkeypatch.setattr(refresh_mod.unity_transport, "send_with_unity_instance", fake_send_with_unity_instance)

    resp = await refresh_unity(ctx, wait_for_ready=True)
    payload = resp.model_dump() if hasattr(resp, "model_dump") else resp
    assert payload["success"] is True
    assert payload.get("data", {}).get("recovered_from_disconnect") is True

    # Dirty should be cleared
    assert external_changes_scanner._states[inst].dirty is False


