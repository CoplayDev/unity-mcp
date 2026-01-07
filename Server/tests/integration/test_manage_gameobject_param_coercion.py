import pytest

from .test_helpers import DummyContext
import services.tools.manage_gameobject as manage_go_mod


@pytest.mark.asyncio
async def test_manage_gameobject_boolean_coercion(monkeypatch):
    """Test that string boolean values are properly coerced for valid actions."""
    captured = {}

    async def fake_send(cmd, params, **kwargs):
        captured["params"] = params
        return {"success": True, "data": {}}

    monkeypatch.setattr(
        manage_go_mod,
        "async_send_command_with_retry",
        fake_send,
    )

    # Test boolean coercion with "modify" action (valid action)
    resp = await manage_go_mod.manage_gameobject(
        ctx=DummyContext(),
        action="modify",
        target="Player",
        set_active="true",  # String should be coerced to bool
    )
    
    assert resp.get("success") is True
    assert captured["params"]["action"] == "modify"
    assert captured["params"]["target"] == "Player"
    # setActive string "true" is coerced to bool True
    assert captured["params"]["setActive"] is True


@pytest.mark.asyncio
async def test_manage_gameobject_create_with_tag(monkeypatch):
    """Test that create action properly passes tag parameter."""
    captured = {}

    async def fake_send(cmd, params, **kwargs):
        captured["params"] = params
        return {"success": True, "data": {}}

    monkeypatch.setattr(
        manage_go_mod,
        "async_send_command_with_retry",
        fake_send,
    )

    resp = await manage_go_mod.manage_gameobject(
        ctx=DummyContext(),
        action="create",
        name="TestObject",
        tag="Player",
        position=[1.0, 2.0, 3.0],
    )

    assert resp.get("success") is True
    p = captured["params"]
    assert p["action"] == "create"
    assert p["name"] == "TestObject"
    assert p["tag"] == "Player"
    assert p["position"] == [1.0, 2.0, 3.0]
