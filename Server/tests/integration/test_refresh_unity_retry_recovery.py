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
    await ctx.set_state("unity_instance", "UnityMCPTests@cc8756d4cce0805a")

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




@pytest.mark.asyncio
async def test_reconnect_rereads_the_payload_it_lost(monkeypatch):
    """The disconnect drops the tool payload, so console_errors must be fetched again."""
    from services.tools.refresh_unity import refresh_unity

    ctx = DummyContext()
    await ctx.set_state("unity_instance", "UnityMCPTests@cc8756d4cce0805a")

    seen: list[dict] = []

    async def fake_send_with_unity_instance(send_fn, unity_instance, command_type, params, **kwargs):
        if command_type == "refresh_unity":
            seen.append(params)
            if params.get("compile") == "request":
                return {"success": False, "error": "connection closed", "hint": "retry"}
            return {"success": True, "data": {
                "refresh_triggered": True,
                "console_errors": ["Assets/Foo.cs(1,1): error CS0246: nope"],
            }}
        if command_type == "get_editor_state":
            return {"success": True, "data": {"advice": {"ready_for_tools": True}}}
        raise ValueError(f"Unexpected command: {command_type}")

    import services.tools.refresh_unity as refresh_mod
    monkeypatch.setattr(refresh_mod.unity_transport,
                        "send_with_unity_instance", fake_send_with_unity_instance)

    resp = await refresh_unity(ctx, compile="request", wait_for_ready=True)
    payload = resp.model_dump() if hasattr(resp, "model_dump") else resp
    data = payload.get("data", {})

    assert payload["success"] is True
    assert data.get("recovered_from_disconnect") is True
    assert data.get("console_errors") == [
        "Assets/Foo.cs(1,1): error CS0246: nope"]
    assert seen[-1]["compile"] == "none", "the re-read must not trigger a second reload"


@pytest.mark.asyncio
async def test_reconnect_still_succeeds_when_the_reread_fails(monkeypatch):
    """A failed re-read must not turn a recovered refresh into an error."""
    from services.tools.refresh_unity import refresh_unity

    ctx = DummyContext()
    await ctx.set_state("unity_instance", "UnityMCPTests@cc8756d4cce0805a")

    async def fake_send_with_unity_instance(send_fn, unity_instance, command_type, params, **kwargs):
        if command_type == "refresh_unity":
            return {"success": False, "error": "connection closed", "hint": "retry"}
        if command_type == "get_editor_state":
            return {"success": True, "data": {"advice": {"ready_for_tools": True}}}
        raise ValueError(f"Unexpected command: {command_type}")

    import services.tools.refresh_unity as refresh_mod
    monkeypatch.setattr(refresh_mod.unity_transport,
                        "send_with_unity_instance", fake_send_with_unity_instance)

    resp = await refresh_unity(ctx, compile="request", wait_for_ready=True)
    payload = resp.model_dump() if hasattr(resp, "model_dump") else resp

    assert payload["success"] is True
    assert payload.get("data", {}).get("recovered_from_disconnect") is True
