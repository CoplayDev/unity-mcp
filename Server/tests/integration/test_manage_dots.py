"""
Tests for the manage_dots tool.

Validates parameter routing, action dispatch, and error handling
for DOTS ECS debugging/monitoring operations.
"""
import pytest

from .test_helpers import DummyContext
import services.tools.manage_dots as manage_dots_mod


def _fake_send_factory(captured: dict, response: dict = None):
    """Create a fake send function that captures params and returns a canned response."""
    if response is None:
        response = {"success": True, "message": "OK", "data": {}}

    async def fake_send(cmd, params, **kwargs):
        captured["cmd"] = cmd
        captured["params"] = params
        return response

    return fake_send


@pytest.mark.asyncio
async def test_list_worlds(monkeypatch):
    """list_worlds routes correctly with no extra params."""
    captured = {}
    monkeypatch.setattr(
        manage_dots_mod, "async_send_command_with_retry",
        _fake_send_factory(captured, {
            "success": True,
            "message": "Found 2 world(s).",
            "data": [{"name": "Default World", "entity_count": 100}],
        }),
    )

    resp = await manage_dots_mod.manage_dots(ctx=DummyContext(), action="list_worlds")

    assert resp["success"] is True
    assert captured["cmd"] == "manage_dots"
    assert captured["params"]["action"] == "list_worlds"


@pytest.mark.asyncio
async def test_query_entities_params(monkeypatch):
    """query_entities sends component_types and page_size correctly."""
    captured = {}
    monkeypatch.setattr(
        manage_dots_mod, "async_send_command_with_retry",
        _fake_send_factory(captured, {
            "success": True,
            "message": "Found 50 entities.",
            "data": {"total_count": 50, "entities": []},
        }),
    )

    resp = await manage_dots_mod.manage_dots(
        ctx=DummyContext(),
        action="query_entities",
        component_types="LocalTransform,Velocity",
        page_size=10,
        world="Server World",
    )

    assert resp["success"] is True
    assert captured["params"]["component_types"] == "LocalTransform,Velocity"
    assert captured["params"]["page_size"] == 10
    assert captured["params"]["world"] == "Server World"


@pytest.mark.asyncio
async def test_get_entity_params(monkeypatch):
    """get_entity sends entity_index and entity_version."""
    captured = {}
    monkeypatch.setattr(
        manage_dots_mod, "async_send_command_with_retry",
        _fake_send_factory(captured, {
            "success": True,
            "message": "Entity details.",
            "data": {"index": 42, "version": 2, "components": []},
        }),
    )

    resp = await manage_dots_mod.manage_dots(
        ctx=DummyContext(),
        action="get_entity",
        entity_index=42,
        entity_version=2,
    )

    assert resp["success"] is True
    assert captured["params"]["entity_index"] == 42
    assert captured["params"]["entity_version"] == 2


@pytest.mark.asyncio
async def test_list_systems_with_group_filter(monkeypatch):
    """list_systems passes group filter."""
    captured = {}
    monkeypatch.setattr(
        manage_dots_mod, "async_send_command_with_retry",
        _fake_send_factory(captured),
    )

    resp = await manage_dots_mod.manage_dots(
        ctx=DummyContext(),
        action="list_systems",
        group="Simulation",
    )

    assert resp["success"] is True
    assert captured["params"]["group"] == "Simulation"


@pytest.mark.asyncio
async def test_get_system_params(monkeypatch):
    """get_system sends system_name."""
    captured = {}
    monkeypatch.setattr(
        manage_dots_mod, "async_send_command_with_retry",
        _fake_send_factory(captured),
    )

    resp = await manage_dots_mod.manage_dots(
        ctx=DummyContext(),
        action="get_system",
        system_name="TransformSystemGroup",
    )

    assert resp["success"] is True
    assert captured["params"]["system_name"] == "TransformSystemGroup"


@pytest.mark.asyncio
async def test_toggle_system_params(monkeypatch):
    """toggle_system sends system_name and enabled."""
    captured = {}
    monkeypatch.setattr(
        manage_dots_mod, "async_send_command_with_retry",
        _fake_send_factory(captured),
    )

    resp = await manage_dots_mod.manage_dots(
        ctx=DummyContext(),
        action="toggle_system",
        system_name="MyMovementSystem",
        enabled=False,
        world="Client World",
    )

    assert resp["success"] is True
    assert captured["params"]["system_name"] == "MyMovementSystem"
    assert captured["params"]["enabled"] is False
    assert captured["params"]["world"] == "Client World"


@pytest.mark.asyncio
async def test_performance_snapshot_params(monkeypatch):
    """performance_snapshot sends limit and world."""
    captured = {}
    monkeypatch.setattr(
        manage_dots_mod, "async_send_command_with_retry",
        _fake_send_factory(captured, {
            "success": True,
            "message": "Snapshot.",
            "data": {"total_entities": 500, "total_chunks": 20},
        }),
    )

    resp = await manage_dots_mod.manage_dots(
        ctx=DummyContext(),
        action="performance_snapshot",
        limit=10,
        world="Default World",
    )

    assert resp["success"] is True
    assert captured["params"]["limit"] == 10
    assert captured["params"]["world"] == "Default World"


@pytest.mark.asyncio
async def test_none_params_stripped(monkeypatch):
    """None-valued optional params are not sent to Unity."""
    captured = {}
    monkeypatch.setattr(
        manage_dots_mod, "async_send_command_with_retry",
        _fake_send_factory(captured),
    )

    await manage_dots_mod.manage_dots(
        ctx=DummyContext(),
        action="list_worlds",
    )

    # Only 'action' should be present; all None optionals stripped
    assert set(captured["params"].keys()) == {"action"}


@pytest.mark.asyncio
async def test_unity_error_returns_failure(monkeypatch):
    """Unity returning a failure dict is passed through."""
    monkeypatch.setattr(
        manage_dots_mod, "async_send_command_with_retry",
        _fake_send_factory({}, {"success": False, "error": "DOTS not available"}),
    )

    resp = await manage_dots_mod.manage_dots(
        ctx=DummyContext(),
        action="list_worlds",
    )

    assert resp["success"] is False


@pytest.mark.asyncio
async def test_python_exception_caught(monkeypatch):
    """Python-side exceptions are caught and returned as failure."""

    async def raising_send(cmd, params, **kwargs):
        raise ConnectionError("Unity not connected")

    monkeypatch.setattr(
        manage_dots_mod, "async_send_command_with_retry",
        raising_send,
    )

    resp = await manage_dots_mod.manage_dots(
        ctx=DummyContext(),
        action="list_worlds",
    )

    assert resp["success"] is False
    assert "Unity not connected" in resp["message"]


@pytest.mark.asyncio
async def test_list_component_types_params(monkeypatch):
    """list_component_types sends filter and category."""
    captured = {}
    monkeypatch.setattr(
        manage_dots_mod, "async_send_command_with_retry",
        _fake_send_factory(captured, {
            "success": True,
            "message": "Found 10 types.",
            "data": {"total_registered": 200, "returned": 10, "types": []},
        }),
    )

    resp = await manage_dots_mod.manage_dots(
        ctx=DummyContext(),
        action="list_component_types",
        filter="Transform",
        category="ComponentData",
        page_size=50,
    )

    assert resp["success"] is True
    assert captured["params"]["filter"] == "Transform"
    assert captured["params"]["category"] == "ComponentData"
    assert captured["params"]["page_size"] == 50


@pytest.mark.asyncio
async def test_create_entity_params(monkeypatch):
    """create_entity sends component_types and world."""
    captured = {}
    monkeypatch.setattr(
        manage_dots_mod, "async_send_command_with_retry",
        _fake_send_factory(captured, {
            "success": True,
            "message": "Created entity.",
            "data": {"index": 99, "version": 1, "components": ["LocalTransform"]},
        }),
    )

    resp = await manage_dots_mod.manage_dots(
        ctx=DummyContext(),
        action="create_entity",
        component_types="LocalTransform,Velocity",
        world="Default World",
    )

    assert resp["success"] is True
    assert captured["params"]["component_types"] == "LocalTransform,Velocity"
    assert captured["params"]["world"] == "Default World"


@pytest.mark.asyncio
async def test_destroy_entity_params(monkeypatch):
    """destroy_entity sends entity_index and entity_version."""
    captured = {}
    monkeypatch.setattr(
        manage_dots_mod, "async_send_command_with_retry",
        _fake_send_factory(captured, {
            "success": True,
            "message": "Destroyed entity.",
        }),
    )

    resp = await manage_dots_mod.manage_dots(
        ctx=DummyContext(),
        action="destroy_entity",
        entity_index=42,
        entity_version=1,
    )

    assert resp["success"] is True
    assert captured["params"]["entity_index"] == 42
    assert captured["params"]["entity_version"] == 1
