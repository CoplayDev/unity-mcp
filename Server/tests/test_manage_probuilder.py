from __future__ import annotations

import asyncio
from types import SimpleNamespace
from unittest.mock import AsyncMock

import pytest

from services.tools.manage_probuilder import (
    manage_probuilder,
    ALL_ACTIONS,
    SHAPE_ACTIONS,
    MESH_ACTIONS,
    VERTEX_ACTIONS,
    UV_MATERIAL_ACTIONS,
    QUERY_ACTIONS,
    SMOOTHING_ACTIONS,
    UTILITY_ACTIONS,
)


# ---------------------------------------------------------------------------
# Fixtures
# ---------------------------------------------------------------------------

@pytest.fixture
def mock_unity(monkeypatch):
    """Patch Unity transport layer and return captured call dict."""
    captured: dict[str, object] = {}

    async def fake_send(send_fn, unity_instance, tool_name, params):
        captured["unity_instance"] = unity_instance
        captured["tool_name"] = tool_name
        captured["params"] = params
        return {"success": True, "message": "ok"}

    monkeypatch.setattr(
        "services.tools.manage_probuilder.get_unity_instance_from_context",
        AsyncMock(return_value="unity-instance-1"),
    )
    monkeypatch.setattr(
        "services.tools.manage_probuilder.send_with_unity_instance",
        fake_send,
    )
    return captured


# ---------------------------------------------------------------------------
# Action list completeness
# ---------------------------------------------------------------------------

def test_all_actions_is_union_of_sub_lists():
    expected = set(
        ["ping"] + SHAPE_ACTIONS + MESH_ACTIONS + VERTEX_ACTIONS
        + UV_MATERIAL_ACTIONS + QUERY_ACTIONS + SMOOTHING_ACTIONS + UTILITY_ACTIONS
    )
    assert set(ALL_ACTIONS) == expected


def test_no_duplicate_actions():
    assert len(ALL_ACTIONS) == len(set(ALL_ACTIONS))


# ---------------------------------------------------------------------------
# Invalid / missing action
# ---------------------------------------------------------------------------

def test_unknown_action_returns_error(mock_unity):
    result = asyncio.run(
        manage_probuilder(SimpleNamespace(), action="nonexistent_action")
    )
    assert result["success"] is False
    assert "Unknown action" in result["message"]
    assert "tool_name" not in mock_unity  # Should NOT call Unity


def test_empty_action_returns_error(mock_unity):
    result = asyncio.run(
        manage_probuilder(SimpleNamespace(), action="")
    )
    assert result["success"] is False


# ---------------------------------------------------------------------------
# Shape creation
# ---------------------------------------------------------------------------

def test_create_shape_sends_correct_params(mock_unity):
    result = asyncio.run(
        manage_probuilder(
            SimpleNamespace(),
            action="create_shape",
            properties={"shapeType": "Cube", "size": [2, 2, 2]},
        )
    )
    assert result["success"] is True
    assert mock_unity["tool_name"] == "manage_probuilder"
    assert mock_unity["params"]["action"] == "create_shape"
    assert mock_unity["params"]["properties"]["shapeType"] == "Cube"


def test_create_shape_with_target(mock_unity):
    result = asyncio.run(
        manage_probuilder(
            SimpleNamespace(),
            action="create_shape",
            target="MyParent",
            properties={"shapeType": "Torus"},
        )
    )
    assert result["success"] is True
    assert mock_unity["params"]["target"] == "MyParent"


def test_create_poly_shape_sends_correct_params(mock_unity):
    result = asyncio.run(
        manage_probuilder(
            SimpleNamespace(),
            action="create_poly_shape",
            properties={
                "points": [[0, 0, 0], [5, 0, 0], [5, 0, 5], [0, 0, 5]],
                "extrudeHeight": 3.0,
            },
        )
    )
    assert result["success"] is True
    assert mock_unity["params"]["action"] == "create_poly_shape"
    assert mock_unity["params"]["properties"]["extrudeHeight"] == 3.0


# ---------------------------------------------------------------------------
# Mesh editing
# ---------------------------------------------------------------------------

def test_extrude_faces_sends_correct_params(mock_unity):
    result = asyncio.run(
        manage_probuilder(
            SimpleNamespace(),
            action="extrude_faces",
            target="MyCube",
            properties={"faceIndices": [0, 1], "distance": 1.5},
        )
    )
    assert result["success"] is True
    assert mock_unity["params"]["action"] == "extrude_faces"
    assert mock_unity["params"]["target"] == "MyCube"
    assert mock_unity["params"]["properties"]["faceIndices"] == [0, 1]
    assert mock_unity["params"]["properties"]["distance"] == 1.5


def test_bevel_edges_sends_correct_params(mock_unity):
    result = asyncio.run(
        manage_probuilder(
            SimpleNamespace(),
            action="bevel_edges",
            target="MyCube",
            properties={"edgeIndices": [0, 2], "amount": 0.2},
        )
    )
    assert result["success"] is True
    assert mock_unity["params"]["action"] == "bevel_edges"
    assert mock_unity["params"]["properties"]["amount"] == 0.2


def test_delete_faces_sends_correct_params(mock_unity):
    result = asyncio.run(
        manage_probuilder(
            SimpleNamespace(),
            action="delete_faces",
            target="MyCube",
            properties={"faceIndices": [3]},
        )
    )
    assert result["success"] is True
    assert mock_unity["params"]["action"] == "delete_faces"


def test_subdivide_sends_correct_params(mock_unity):
    result = asyncio.run(
        manage_probuilder(
            SimpleNamespace(),
            action="subdivide",
            target="MyCube",
        )
    )
    assert result["success"] is True
    assert mock_unity["params"]["action"] == "subdivide"


def test_combine_meshes_sends_correct_params(mock_unity):
    result = asyncio.run(
        manage_probuilder(
            SimpleNamespace(),
            action="combine_meshes",
            properties={"targets": ["Cube1", "Cube2"]},
        )
    )
    assert result["success"] is True
    assert mock_unity["params"]["action"] == "combine_meshes"


# ---------------------------------------------------------------------------
# Vertex operations
# ---------------------------------------------------------------------------

def test_move_vertices_sends_correct_params(mock_unity):
    result = asyncio.run(
        manage_probuilder(
            SimpleNamespace(),
            action="move_vertices",
            target="MyCube",
            properties={"vertexIndices": [0, 1, 2], "offset": [0, 1, 0]},
        )
    )
    assert result["success"] is True
    assert mock_unity["params"]["action"] == "move_vertices"
    assert mock_unity["params"]["properties"]["offset"] == [0, 1, 0]


# ---------------------------------------------------------------------------
# UV & materials
# ---------------------------------------------------------------------------

def test_set_face_material_sends_correct_params(mock_unity):
    result = asyncio.run(
        manage_probuilder(
            SimpleNamespace(),
            action="set_face_material",
            target="MyCube",
            properties={"faceIndices": [0], "materialPath": "Assets/Materials/Red.mat"},
        )
    )
    assert result["success"] is True
    assert mock_unity["params"]["action"] == "set_face_material"
    assert mock_unity["params"]["properties"]["materialPath"] == "Assets/Materials/Red.mat"


def test_set_face_uvs_sends_correct_params(mock_unity):
    result = asyncio.run(
        manage_probuilder(
            SimpleNamespace(),
            action="set_face_uvs",
            target="MyCube",
            properties={"faceIndices": [0, 1], "scale": [2, 2], "rotation": 45},
        )
    )
    assert result["success"] is True
    assert mock_unity["params"]["action"] == "set_face_uvs"


# ---------------------------------------------------------------------------
# Query
# ---------------------------------------------------------------------------

def test_get_mesh_info_sends_correct_params(mock_unity):
    result = asyncio.run(
        manage_probuilder(
            SimpleNamespace(),
            action="get_mesh_info",
            target="MyCube",
        )
    )
    assert result["success"] is True
    assert mock_unity["params"]["action"] == "get_mesh_info"
    assert mock_unity["params"]["target"] == "MyCube"


def test_convert_to_probuilder_sends_correct_params(mock_unity):
    result = asyncio.run(
        manage_probuilder(
            SimpleNamespace(),
            action="convert_to_probuilder",
            target="StandardMesh",
        )
    )
    assert result["success"] is True
    assert mock_unity["params"]["action"] == "convert_to_probuilder"


# ---------------------------------------------------------------------------
# Search method passthrough
# ---------------------------------------------------------------------------

def test_search_method_passed_through(mock_unity):
    result = asyncio.run(
        manage_probuilder(
            SimpleNamespace(),
            action="get_mesh_info",
            target="-12345",
            search_method="by_id",
        )
    )
    assert result["success"] is True
    assert mock_unity["params"]["searchMethod"] == "by_id"


# ---------------------------------------------------------------------------
# Ping
# ---------------------------------------------------------------------------

def test_ping_sends_to_unity(mock_unity):
    result = asyncio.run(
        manage_probuilder(SimpleNamespace(), action="ping")
    )
    assert result["success"] is True
    assert mock_unity["params"]["action"] == "ping"


# ---------------------------------------------------------------------------
# All actions are lowercase-normalized
# ---------------------------------------------------------------------------

def test_action_case_insensitive(mock_unity):
    result = asyncio.run(
        manage_probuilder(
            SimpleNamespace(),
            action="Create_Shape",
            properties={"shapeType": "Cube"},
        )
    )
    assert result["success"] is True
    assert mock_unity["params"]["action"] == "create_shape"


# ---------------------------------------------------------------------------
# Non-dict result from Unity
# ---------------------------------------------------------------------------

def test_non_dict_result_wrapped(monkeypatch):
    async def fake_send(send_fn, unity_instance, tool_name, params):
        return "unexpected string result"

    monkeypatch.setattr(
        "services.tools.manage_probuilder.get_unity_instance_from_context",
        AsyncMock(return_value="unity-instance-1"),
    )
    monkeypatch.setattr(
        "services.tools.manage_probuilder.send_with_unity_instance",
        fake_send,
    )

    result = asyncio.run(
        manage_probuilder(SimpleNamespace(), action="ping")
    )
    assert result["success"] is False
    assert "unexpected string result" in result["message"]


# ---------------------------------------------------------------------------
# New action categories
# ---------------------------------------------------------------------------

def test_smoothing_actions_in_all():
    for action in SMOOTHING_ACTIONS:
        assert action in ALL_ACTIONS, f"{action} should be in ALL_ACTIONS"


def test_utility_actions_in_all():
    for action in UTILITY_ACTIONS:
        assert action in ALL_ACTIONS, f"{action} should be in ALL_ACTIONS"


# ---------------------------------------------------------------------------
# Smoothing actions
# ---------------------------------------------------------------------------

def test_auto_smooth_sends_correct_params(mock_unity):
    result = asyncio.run(
        manage_probuilder(
            SimpleNamespace(),
            action="auto_smooth",
            target="MyCube",
            properties={"angleThreshold": 45},
        )
    )
    assert result["success"] is True
    assert mock_unity["params"]["action"] == "auto_smooth"
    assert mock_unity["params"]["target"] == "MyCube"
    assert mock_unity["params"]["properties"]["angleThreshold"] == 45


def test_set_smoothing_sends_correct_params(mock_unity):
    result = asyncio.run(
        manage_probuilder(
            SimpleNamespace(),
            action="set_smoothing",
            target="MyCube",
            properties={"faceIndices": [0, 1, 2], "smoothingGroup": 1},
        )
    )
    assert result["success"] is True
    assert mock_unity["params"]["action"] == "set_smoothing"
    assert mock_unity["params"]["properties"]["faceIndices"] == [0, 1, 2]
    assert mock_unity["params"]["properties"]["smoothingGroup"] == 1


# ---------------------------------------------------------------------------
# Mesh utility actions
# ---------------------------------------------------------------------------

def test_center_pivot_sends_correct_params(mock_unity):
    result = asyncio.run(
        manage_probuilder(
            SimpleNamespace(),
            action="center_pivot",
            target="MyCube",
        )
    )
    assert result["success"] is True
    assert mock_unity["params"]["action"] == "center_pivot"
    assert mock_unity["params"]["target"] == "MyCube"


def test_freeze_transform_sends_correct_params(mock_unity):
    result = asyncio.run(
        manage_probuilder(
            SimpleNamespace(),
            action="freeze_transform",
            target="MyCube",
        )
    )
    assert result["success"] is True
    assert mock_unity["params"]["action"] == "freeze_transform"


def test_validate_mesh_sends_correct_params(mock_unity):
    result = asyncio.run(
        manage_probuilder(
            SimpleNamespace(),
            action="validate_mesh",
            target="MyCube",
        )
    )
    assert result["success"] is True
    assert mock_unity["params"]["action"] == "validate_mesh"


def test_repair_mesh_sends_correct_params(mock_unity):
    result = asyncio.run(
        manage_probuilder(
            SimpleNamespace(),
            action="repair_mesh",
            target="MyCube",
        )
    )
    assert result["success"] is True
    assert mock_unity["params"]["action"] == "repair_mesh"


# ---------------------------------------------------------------------------
# get_mesh_info include parameter passthrough
# ---------------------------------------------------------------------------

def test_get_mesh_info_include_param_passthrough(mock_unity):
    result = asyncio.run(
        manage_probuilder(
            SimpleNamespace(),
            action="get_mesh_info",
            target="MyCube",
            properties={"include": "faces"},
        )
    )
    assert result["success"] is True
    assert mock_unity["params"]["action"] == "get_mesh_info"
    assert mock_unity["params"]["properties"]["include"] == "faces"
