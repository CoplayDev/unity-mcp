import asyncio
from types import SimpleNamespace
from unittest.mock import AsyncMock

import pytest

from services.tools.manage_physics import manage_physics, ALL_ACTIONS


@pytest.fixture
def mock_unity(monkeypatch):
    captured = {}

    async def fake_send(send_fn, unity_instance, tool_name, params):
        captured["unity_instance"] = unity_instance
        captured["tool_name"] = tool_name
        captured["params"] = params
        return {"success": True, "message": "ok", "data": {}}

    monkeypatch.setattr(
        "services.tools.manage_physics.get_unity_instance_from_context",
        AsyncMock(return_value="test-instance"),
    )
    monkeypatch.setattr(
        "services.tools.manage_physics.send_with_unity_instance",
        fake_send,
    )
    return captured


def test_unknown_action(mock_unity):
    result = asyncio.run(
        manage_physics(SimpleNamespace(), action="nonexistent")
    )
    assert result["success"] is False
    assert "Unknown" in result["message"]


def test_ping_forwards(mock_unity):
    result = asyncio.run(manage_physics(SimpleNamespace(), action="ping"))
    assert result["success"] is True
    assert mock_unity["tool_name"] == "manage_physics"
    assert mock_unity["params"]["action"] == "ping"


def test_get_settings_forwards_dimension(mock_unity):
    result = asyncio.run(
        manage_physics(SimpleNamespace(), action="get_settings", dimension="2d")
    )
    assert mock_unity["params"]["dimension"] == "2d"


def test_set_settings_forwards(mock_unity):
    result = asyncio.run(
        manage_physics(
            SimpleNamespace(),
            action="set_settings",
            dimension="3d",
            settings={"gravity": [0, -20, 0]},
        )
    )
    assert mock_unity["params"]["settings"] == {"gravity": [0, -20, 0]}


def test_set_collision_matrix_forwards(mock_unity):
    result = asyncio.run(
        manage_physics(
            SimpleNamespace(),
            action="set_collision_matrix",
            layer_a="Default",
            layer_b="Player",
            collide=False,
        )
    )
    assert mock_unity["params"]["layer_a"] == "Default"
    assert mock_unity["params"]["collide"] is False


def test_create_material_forwards(mock_unity):
    result = asyncio.run(
        manage_physics(
            SimpleNamespace(),
            action="create_physics_material",
            name="Bouncy",
            bounciness=0.9,
            dimension="3d",
        )
    )
    assert mock_unity["params"]["name"] == "Bouncy"
    assert mock_unity["params"]["bounciness"] == 0.9


def test_raycast_forwards(mock_unity):
    result = asyncio.run(
        manage_physics(
            SimpleNamespace(),
            action="raycast",
            origin=[0, 0, 0],
            direction=[0, 0, 1],
            max_distance=100.0,
        )
    )
    assert mock_unity["params"]["origin"] == [0, 0, 0]
    assert mock_unity["params"]["direction"] == [0, 0, 1]


def test_overlap_forwards(mock_unity):
    result = asyncio.run(
        manage_physics(
            SimpleNamespace(),
            action="overlap",
            shape="sphere",
            position=[0, 0, 0],
            size=10.0,
        )
    )
    assert mock_unity["params"]["shape"] == "sphere"
    assert mock_unity["params"]["size"] == 10.0


def test_validate_forwards(mock_unity):
    result = asyncio.run(
        manage_physics(
            SimpleNamespace(),
            action="validate",
            target="Player",
            dimension="both",
        )
    )
    assert mock_unity["params"]["target"] == "Player"
    assert mock_unity["params"]["dimension"] == "both"


def test_joint_forwards(mock_unity):
    result = asyncio.run(
        manage_physics(
            SimpleNamespace(),
            action="add_joint",
            target="Player",
            joint_type="hinge",
            motor={"targetVelocity": 100, "force": 50},
        )
    )
    assert mock_unity["params"]["joint_type"] == "hinge"
    assert mock_unity["params"]["motor"]["targetVelocity"] == 100


def test_simulate_forwards(mock_unity):
    result = asyncio.run(
        manage_physics(
            SimpleNamespace(),
            action="simulate_step",
            steps=5,
            step_size=0.02,
        )
    )
    assert mock_unity["params"]["steps"] == 5
    assert mock_unity["params"]["step_size"] == 0.02


def test_all_actions_list():
    assert len(ALL_ACTIONS) == 21
    assert "ping" in ALL_ACTIONS
    assert "validate" in ALL_ACTIONS
    assert "simulate_step" in ALL_ACTIONS
    assert "raycast" in ALL_ACTIONS
    assert "raycast_all" in ALL_ACTIONS
    assert "linecast" in ALL_ACTIONS
    assert "shapecast" in ALL_ACTIONS
    assert "overlap" in ALL_ACTIONS
    assert "add_joint" in ALL_ACTIONS
    assert "create_physics_material" in ALL_ACTIONS
    assert "apply_force" in ALL_ACTIONS
    assert "get_rigidbody" in ALL_ACTIONS
    assert "configure_rigidbody" in ALL_ACTIONS


def test_shapecast_forwards(mock_unity):
    result = asyncio.run(
        manage_physics(
            SimpleNamespace(),
            action="shapecast",
            shape="sphere",
            origin=[0, 5, 0],
            direction=[0, -1, 0],
            size=1.0,
        )
    )
    assert result["success"] is True
    assert mock_unity["params"]["shape"] == "sphere"
    assert mock_unity["params"]["origin"] == [0, 5, 0]


def test_raycast_all_forwards(mock_unity):
    result = asyncio.run(
        manage_physics(
            SimpleNamespace(),
            action="raycast_all",
            origin=[0, 0, 0],
            direction=[0, 0, 1],
        )
    )
    assert result["success"] is True
    assert mock_unity["params"]["action"] == "raycast_all"


def test_linecast_forwards(mock_unity):
    result = asyncio.run(
        manage_physics(
            SimpleNamespace(),
            action="linecast",
            start=[0, 0, 0],
            end=[10, 0, 0],
        )
    )
    assert result["success"] is True
    assert mock_unity["params"]["start"] == [0, 0, 0]
    assert mock_unity["params"]["end"] == [10, 0, 0]


def test_apply_force_forwards(mock_unity):
    result = asyncio.run(
        manage_physics(
            SimpleNamespace(),
            action="apply_force",
            target="Player",
            force=[0, 100, 0],
            force_mode="Impulse",
        )
    )
    assert result["success"] is True
    assert mock_unity["params"]["force"] == [0, 100, 0]
    assert mock_unity["params"]["force_mode"] == "Impulse"


def test_configure_rigidbody_forwards(mock_unity):
    result = asyncio.run(
        manage_physics(
            SimpleNamespace(),
            action="configure_rigidbody",
            target="Player",
            properties={"mass": 5.0, "useGravity": True},
        )
    )
    assert result["success"] is True
    assert mock_unity["params"]["properties"] == {"mass": 5.0, "useGravity": True}


def test_get_rigidbody_forwards(mock_unity):
    result = asyncio.run(
        manage_physics(
            SimpleNamespace(),
            action="get_rigidbody",
            target="Player",
            dimension="3d",
        )
    )
    assert result["success"] is True
    assert mock_unity["params"]["action"] == "get_rigidbody"
    assert mock_unity["params"]["target"] == "Player"


def test_none_params_not_forwarded(mock_unity):
    result = asyncio.run(
        manage_physics(SimpleNamespace(), action="ping")
    )
    # Only 'action' should be in params, no None values
    assert "dimension" not in mock_unity["params"]
    assert "target" not in mock_unity["params"]
