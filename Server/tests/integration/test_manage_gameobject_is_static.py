import pytest

from .test_helpers import DummyContext
import services.tools.manage_gameobject as manage_go_mod


@pytest.mark.asyncio
async def test_manage_gameobject_is_static_true(monkeypatch):
    """Test that is_static=True is passed as isStatic in params."""
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
        action="modify",
        target="Ground",
        is_static=True,
    )

    assert resp.get("success") is True
    assert captured["params"]["action"] == "modify"
    assert captured["params"]["target"] == "Ground"
    assert captured["params"]["isStatic"] is True


@pytest.mark.asyncio
async def test_manage_gameobject_is_static_false(monkeypatch):
    """Test that is_static=False is passed as isStatic in params."""
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
        action="modify",
        target="Ground",
        is_static=False,
    )

    assert resp.get("success") is True
    assert captured["params"]["isStatic"] is False


@pytest.mark.asyncio
async def test_manage_gameobject_is_static_string_coercion(monkeypatch):
    """Test that string 'true' is coerced to bool for is_static."""
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
        action="modify",
        target="Ground",
        is_static="true",
    )

    assert resp.get("success") is True
    assert captured["params"]["isStatic"] is True


@pytest.mark.asyncio
async def test_manage_gameobject_is_static_string_false_coercion(monkeypatch):
    """Test that string 'false' is coerced to bool for is_static."""
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
        action="modify",
        target="Ground",
        is_static="false",
    )

    assert resp.get("success") is True
    assert captured["params"]["isStatic"] is False


@pytest.mark.asyncio
async def test_manage_gameobject_is_static_omitted(monkeypatch):
    """Test that omitting is_static does not include isStatic in params."""
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
        action="modify",
        target="Ground",
    )

    assert resp.get("success") is True
    assert "isStatic" not in captured["params"]
