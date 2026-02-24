import pytest

from .test_helpers import DummyContext
import services.tools.manage_gameobject as manage_go_mod


@pytest.mark.asyncio
async def test_look_at_vector_target(monkeypatch):
    """look_at action forwards look_at_target as a vector."""
    captured = {}

    async def fake_send(cmd, params, **kwargs):
        captured["params"] = params
        return {"success": True, "data": {"rotation": [0, 90, 0]}}

    monkeypatch.setattr(manage_go_mod, "async_send_command_with_retry", fake_send)

    resp = await manage_go_mod.manage_gameobject(
        ctx=DummyContext(),
        action="look_at",
        target="MainCamera",
        look_at_target=[10.0, 0.0, 5.0],
    )

    assert resp.get("success") is True
    p = captured["params"]
    assert p["action"] == "look_at"
    assert p["target"] == "MainCamera"
    assert p["look_at_target"] == [10.0, 0.0, 5.0]


@pytest.mark.asyncio
async def test_look_at_string_target(monkeypatch):
    """look_at action forwards look_at_target as a GO name string."""
    captured = {}

    async def fake_send(cmd, params, **kwargs):
        captured["params"] = params
        return {"success": True, "data": {"rotation": [0, 45, 0]}}

    monkeypatch.setattr(manage_go_mod, "async_send_command_with_retry", fake_send)

    resp = await manage_go_mod.manage_gameobject(
        ctx=DummyContext(),
        action="look_at",
        target="MainCamera",
        look_at_target="Player",
        look_at_up=[0, 1, 0],
    )

    assert resp.get("success") is True
    p = captured["params"]
    assert p["action"] == "look_at"
    assert p["look_at_target"] == "Player"
    assert p["look_at_up"] == [0, 1, 0]


@pytest.mark.asyncio
async def test_look_at_without_target_still_sends(monkeypatch):
    """look_at without look_at_target should still send the command (C# will error)."""
    captured = {}

    async def fake_send(cmd, params, **kwargs):
        captured["params"] = params
        return {"success": False, "message": "look_at_target is required"}

    monkeypatch.setattr(manage_go_mod, "async_send_command_with_retry", fake_send)

    resp = await manage_go_mod.manage_gameobject(
        ctx=DummyContext(),
        action="look_at",
        target="MainCamera",
    )

    p = captured["params"]
    assert p["action"] == "look_at"
    assert "look_at_target" not in p
