"""
Tests for the manage_components tool.

This tool handles component lifecycle operations (add, remove, set_property)
and object reference wiring (get_referenceable, set_reference, batch_wire).
"""
import pytest

from .test_helpers import DummyContext
import services.tools.manage_components as manage_comp_mod


@pytest.mark.asyncio
async def test_manage_components_add_single(monkeypatch):
    """Test adding a single component."""
    captured = {}

    async def fake_send(cmd, params, **kwargs):
        captured["cmd"] = cmd
        captured["params"] = params
        return {
            "success": True,
            "data": {
                "addedComponents": [{"typeName": "UnityEngine.Rigidbody", "instanceID": 12345}]
            },
        }

    monkeypatch.setattr(
        manage_comp_mod,
        "async_send_command_with_retry",
        fake_send,
    )

    resp = await manage_comp_mod.manage_components(
        ctx=DummyContext(),
        action="add",
        target="Player",
        component_type="Rigidbody",
    )

    assert resp.get("success") is True
    assert captured["cmd"] == "manage_components"
    assert captured["params"]["action"] == "add"
    assert captured["params"]["target"] == "Player"
    assert captured["params"]["componentType"] == "Rigidbody"


@pytest.mark.asyncio
async def test_manage_components_remove(monkeypatch):
    """Test removing a component."""
    captured = {}

    async def fake_send(cmd, params, **kwargs):
        captured["params"] = params
        return {"success": True, "data": {"instanceID": 12345, "name": "Player"}}

    monkeypatch.setattr(
        manage_comp_mod,
        "async_send_command_with_retry",
        fake_send,
    )

    resp = await manage_comp_mod.manage_components(
        ctx=DummyContext(),
        action="remove",
        target="Player",
        component_type="Rigidbody",
    )

    assert resp.get("success") is True
    assert captured["params"]["action"] == "remove"
    assert captured["params"]["componentType"] == "Rigidbody"


@pytest.mark.asyncio
async def test_manage_components_set_property_single(monkeypatch):
    """Test setting a single component property."""
    captured = {}

    async def fake_send(cmd, params, **kwargs):
        captured["params"] = params
        return {"success": True, "data": {"instanceID": 12345}}

    monkeypatch.setattr(
        manage_comp_mod,
        "async_send_command_with_retry",
        fake_send,
    )

    resp = await manage_comp_mod.manage_components(
        ctx=DummyContext(),
        action="set_property",
        target="Player",
        component_type="Rigidbody",
        property="mass",
        value=5.0,
    )

    assert resp.get("success") is True
    assert captured["params"]["action"] == "set_property"
    assert captured["params"]["property"] == "mass"
    assert captured["params"]["value"] == 5.0


@pytest.mark.asyncio
async def test_manage_components_set_property_multiple(monkeypatch):
    """Test setting multiple component properties via properties dict."""
    captured = {}

    async def fake_send(cmd, params, **kwargs):
        captured["params"] = params
        return {"success": True, "data": {"instanceID": 12345}}

    monkeypatch.setattr(
        manage_comp_mod,
        "async_send_command_with_retry",
        fake_send,
    )

    resp = await manage_comp_mod.manage_components(
        ctx=DummyContext(),
        action="set_property",
        target="Player",
        component_type="Rigidbody",
        properties={"mass": 5.0, "drag": 0.5},
    )

    assert resp.get("success") is True
    assert captured["params"]["action"] == "set_property"
    assert captured["params"]["properties"] == {"mass": 5.0, "drag": 0.5}


@pytest.mark.asyncio
async def test_manage_components_set_property_json_string(monkeypatch):
    """Test setting component properties with JSON string input."""
    captured = {}

    async def fake_send(cmd, params, **kwargs):
        captured["params"] = params
        return {"success": True, "data": {"instanceID": 12345}}

    monkeypatch.setattr(
        manage_comp_mod,
        "async_send_command_with_retry",
        fake_send,
    )

    resp = await manage_comp_mod.manage_components(
        ctx=DummyContext(),
        action="set_property",
        target="Player",
        component_type="Rigidbody",
        properties='{"mass": 10.0}',  # JSON string
    )

    assert resp.get("success") is True
    assert captured["params"]["properties"] == {"mass": 10.0}


@pytest.mark.asyncio
async def test_manage_components_add_with_properties(monkeypatch):
    """Test adding a component with initial properties."""
    captured = {}

    async def fake_send(cmd, params, **kwargs):
        captured["params"] = params
        return {
            "success": True,
            "data": {"addedComponents": [{"typeName": "Rigidbody", "instanceID": 123}]},
        }

    monkeypatch.setattr(
        manage_comp_mod,
        "async_send_command_with_retry",
        fake_send,
    )

    resp = await manage_comp_mod.manage_components(
        ctx=DummyContext(),
        action="add",
        target="Player",
        component_type="Rigidbody",
        properties={"mass": 2.0, "useGravity": False},
    )

    assert resp.get("success") is True
    assert captured["params"]["properties"] == {"mass": 2.0, "useGravity": False}


@pytest.mark.asyncio
async def test_manage_components_search_method_passthrough(monkeypatch):
    """Test that search_method is correctly passed through."""
    captured = {}

    async def fake_send(cmd, params, **kwargs):
        captured["params"] = params
        return {"success": True, "data": {}}

    monkeypatch.setattr(
        manage_comp_mod,
        "async_send_command_with_retry",
        fake_send,
    )

    resp = await manage_comp_mod.manage_components(
        ctx=DummyContext(),
        action="add",
        target="Canvas/Panel",
        component_type="Image",
        search_method="by_path",
    )

    assert resp.get("success") is True
    assert captured["params"]["searchMethod"] == "by_path"


@pytest.mark.asyncio
async def test_manage_components_target_by_id(monkeypatch):
    """Test targeting by instance ID."""
    captured = {}

    async def fake_send(cmd, params, **kwargs):
        captured["params"] = params
        return {"success": True, "data": {}}

    monkeypatch.setattr(
        manage_comp_mod,
        "async_send_command_with_retry",
        fake_send,
    )

    resp = await manage_comp_mod.manage_components(
        ctx=DummyContext(),
        action="add",
        target=12345,  # Integer instance ID
        component_type="BoxCollider",
        search_method="by_id",
    )

    assert resp.get("success") is True
    assert captured["params"]["target"] == 12345
    assert captured["params"]["searchMethod"] == "by_id"


@pytest.mark.asyncio
async def test_manage_components_get_referenceable(monkeypatch):
    """get_referenceable forwards property + discovery flags + limit to Unity."""
    captured = {}

    async def fake_send(cmd, params, **kwargs):
        captured["cmd"] = cmd
        captured["params"] = params
        return {
            "success": True,
            "data": {
                "scene_objects": [{"name": "Player", "instanceID": 1001}],
                "assets": [{"name": "BGM", "assetPath": "Assets/Audio/BGM.mp3"}],
            },
        }

    monkeypatch.setattr(
        manage_comp_mod,
        "async_send_command_with_retry",
        fake_send,
    )

    resp = await manage_comp_mod.manage_components(
        ctx=DummyContext(),
        action="get_referenceable",
        target="Player",
        component_type="AudioSource",
        property="clip",
        include_scene=True,
        include_assets=True,
        limit=50,
    )

    assert resp.get("success") is True
    assert captured["cmd"] == "manage_components"
    assert captured["params"]["action"] == "get_referenceable"
    assert captured["params"]["componentType"] == "AudioSource"
    assert captured["params"]["property"] == "clip"
    assert captured["params"]["include_scene"] is True
    assert captured["params"]["include_assets"] is True
    assert captured["params"]["limit"] == 50


@pytest.mark.asyncio
async def test_manage_components_set_reference_asset_path(monkeypatch):
    """set_reference forwards reference_asset_path to Unity."""
    captured = {}

    async def fake_send(cmd, params, **kwargs):
        captured["params"] = params
        return {
            "success": True,
            "data": {
                "previous_value": None,
                "new_value": {"name": "BGM", "assetPath": "Assets/Audio/BGM.mp3"},
            },
        }

    monkeypatch.setattr(
        manage_comp_mod,
        "async_send_command_with_retry",
        fake_send,
    )

    resp = await manage_comp_mod.manage_components(
        ctx=DummyContext(),
        action="set_reference",
        target="Player",
        component_type="AudioSource",
        property="clip",
        reference_asset_path="Assets/Audio/BGM.mp3",
    )

    assert resp.get("success") is True
    assert captured["params"]["action"] == "set_reference"
    assert captured["params"]["property"] == "clip"
    assert captured["params"]["reference_asset_path"] == "Assets/Audio/BGM.mp3"
    assert "reference_path" not in captured["params"]
    assert "reference_instance_id" not in captured["params"]


@pytest.mark.asyncio
async def test_manage_components_set_reference_clear(monkeypatch):
    """set_reference with clear=True forwards clear flag and omits selectors."""
    captured = {}

    async def fake_send(cmd, params, **kwargs):
        captured["params"] = params
        return {
            "success": True,
            "data": {"previous_value": {"name": "BGM"}, "new_value": None},
        }

    monkeypatch.setattr(
        manage_comp_mod,
        "async_send_command_with_retry",
        fake_send,
    )

    resp = await manage_comp_mod.manage_components(
        ctx=DummyContext(),
        action="set_reference",
        target="Player",
        component_type="AudioSource",
        property="clip",
        clear=True,
    )

    assert resp.get("success") is True
    assert captured["params"]["action"] == "set_reference"
    assert captured["params"]["clear"] is True
    assert "reference_path" not in captured["params"]
    assert "reference_asset_path" not in captured["params"]
    assert "reference_instance_id" not in captured["params"]


@pytest.mark.asyncio
async def test_manage_components_batch_wire(monkeypatch):
    """batch_wire forwards references list and atomic flag."""
    captured = {}

    async def fake_send(cmd, params, **kwargs):
        captured["params"] = params
        return {
            "success": True,
            "data": {
                "results": [
                    {"property": "player", "success": True},
                    {"property": "bgMusic", "success": True},
                ]
            },
        }

    monkeypatch.setattr(
        manage_comp_mod,
        "async_send_command_with_retry",
        fake_send,
    )

    references = [
        {"property_name": "player", "reference_path": "Player"},
        {"property_name": "bgMusic", "reference_asset_path": "Assets/Audio/BGM.mp3"},
    ]

    resp = await manage_comp_mod.manage_components(
        ctx=DummyContext(),
        action="batch_wire",
        target="GameManager",
        component_type="GameManager",
        references=references,
        atomic=True,
    )

    assert resp.get("success") is True
    assert captured["params"]["action"] == "batch_wire"
    assert captured["params"]["componentType"] == "GameManager"
    assert captured["params"]["references"] == references
    assert captured["params"]["atomic"] is True
