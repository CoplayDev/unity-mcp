"""
Tests for the GameObject resources.

Resources:
- mcpforunity://scene/gameobject/{instance_id}
- mcpforunity://scene/gameobject/{instance_id}/components
- mcpforunity://scene/gameobject/{instance_id}/component/{component_name}
"""
import pytest

from .test_helpers import DummyContext
import services.resources.gameobject as gameobject_res_mod


@pytest.mark.asyncio
async def test_get_gameobject_data(monkeypatch):
    """Test reading a single GameObject resource."""
    captured = {}

    async def fake_send(cmd, params, **kwargs):
        captured["cmd"] = cmd
        captured["params"] = params
        return {
            "success": True,
            "data": {
                "instanceID": 12345,
                "name": "Player",
                "tag": "Player",
                "layer": 0,
                "activeSelf": True,
                "activeInHierarchy": True,
                "isStatic": False,
                "path": "/Player",
                "componentTypes": ["Transform", "PlayerController", "Rigidbody"],
            },
        }

    monkeypatch.setattr(
        gameobject_res_mod,
        "async_send_command_with_retry",
        fake_send,
    )

    resp = await gameobject_res_mod.get_gameobject(
        ctx=DummyContext(),
        instance_id="12345",
    )

    assert resp.success is True
    assert captured["params"]["instanceID"] == 12345


@pytest.mark.asyncio
async def test_get_gameobject_components(monkeypatch):
    """Test reading all components for a GameObject."""
    captured = {}

    async def fake_send(cmd, params, **kwargs):
        captured["cmd"] = cmd
        captured["params"] = params
        return {
            "success": True,
            "data": {
                "cursor": 0,
                "pageSize": 25,
                "next_cursor": None,
                "truncated": False,
                "total": 3,
                "items": [
                    {"typeName": "UnityEngine.Transform", "instanceID": 1, "enabled": True},
                    {"typeName": "UnityEngine.MeshRenderer", "instanceID": 2, "enabled": True},
                    {"typeName": "UnityEngine.BoxCollider", "instanceID": 3, "enabled": True},
                ],
            },
        }

    monkeypatch.setattr(
        gameobject_res_mod,
        "async_send_command_with_retry",
        fake_send,
    )

    resp = await gameobject_res_mod.get_gameobject_components(
        ctx=DummyContext(),
        instance_id="12345",
    )

    assert resp.success is True
    assert captured["params"]["instanceID"] == 12345


@pytest.mark.asyncio
async def test_get_gameobject_components_pagination(monkeypatch):
    """Test pagination parameters for components resource."""
    captured = {}

    async def fake_send(cmd, params, **kwargs):
        captured["params"] = params
        return {
            "success": True,
            "data": {
                "cursor": 10,
                "pageSize": 5,
                "next_cursor": "15",
                "truncated": True,
                "total": 20,
                "items": [],
            },
        }

    monkeypatch.setattr(
        gameobject_res_mod,
        "async_send_command_with_retry",
        fake_send,
    )

    resp = await gameobject_res_mod.get_gameobject_components(
        ctx=DummyContext(),
        instance_id="12345",
        page_size=5,
        cursor=10,
    )

    assert resp.success is True
    p = captured["params"]
    assert p["pageSize"] == 5
    assert p["cursor"] == 10


@pytest.mark.asyncio
async def test_get_gameobject_components_include_properties(monkeypatch):
    """Test include_properties flag for components resource."""
    captured = {}

    async def fake_send(cmd, params, **kwargs):
        captured["params"] = params
        return {
            "success": True,
            "data": {
                "items": [
                    {
                        "typeName": "UnityEngine.Rigidbody",
                        "instanceID": 123,
                        "mass": 1.0,
                        "drag": 0.0,
                        "useGravity": True,
                    }
                ]
            },
        }

    monkeypatch.setattr(
        gameobject_res_mod,
        "async_send_command_with_retry",
        fake_send,
    )

    resp = await gameobject_res_mod.get_gameobject_components(
        ctx=DummyContext(),
        instance_id="12345",
        include_properties=True,
    )

    assert resp.success is True
    assert captured["params"]["includeProperties"] is True


@pytest.mark.asyncio
async def test_get_gameobject_component_single(monkeypatch):
    """Test reading a single component by name."""
    captured = {}

    async def fake_send(cmd, params, **kwargs):
        captured["cmd"] = cmd
        captured["params"] = params
        return {
            "success": True,
            "data": {
                "typeName": "UnityEngine.Rigidbody",
                "instanceID": 67890,
                "mass": 5.0,
                "drag": 0.1,
                "angularDrag": 0.05,
                "useGravity": True,
                "isKinematic": False,
            },
        }

    monkeypatch.setattr(
        gameobject_res_mod,
        "async_send_command_with_retry",
        fake_send,
    )

    resp = await gameobject_res_mod.get_gameobject_component(
        ctx=DummyContext(),
        instance_id="12345",
        component_name="Rigidbody",
    )

    assert resp.success is True
    p = captured["params"]
    assert p["instanceID"] == 12345
    assert p["componentName"] == "Rigidbody"


@pytest.mark.asyncio
async def test_get_gameobject_component_not_found(monkeypatch):
    """Test error when component is not found."""
    async def fake_send(cmd, params, **kwargs):
        return {
            "success": False,
            "message": "GameObject '12345' does not have a 'NonExistent' component.",
        }

    monkeypatch.setattr(
        gameobject_res_mod,
        "async_send_command_with_retry",
        fake_send,
    )

    resp = await gameobject_res_mod.get_gameobject_component(
        ctx=DummyContext(),
        instance_id="12345",
        component_name="NonExistent",
    )

    assert resp.success is False
    assert "NonExistent" in (resp.message or "")


@pytest.mark.asyncio
async def test_get_gameobject_not_found(monkeypatch):
    """Test error when GameObject is not found."""
    async def fake_send(cmd, params, **kwargs):
        return {
            "success": False,
            "message": "GameObject with instanceID '99999' not found.",
        }

    monkeypatch.setattr(
        gameobject_res_mod,
        "async_send_command_with_retry",
        fake_send,
    )

    resp = await gameobject_res_mod.get_gameobject(
        ctx=DummyContext(),
        instance_id="99999",
    )

    assert resp.success is False
    assert "99999" in (resp.message or "")

