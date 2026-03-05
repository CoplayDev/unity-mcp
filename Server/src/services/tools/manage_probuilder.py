from typing import Annotated, Any, Literal

from fastmcp import Context
from mcp.types import ToolAnnotations

from services.registry import mcp_for_unity_tool
from services.tools import get_unity_instance_from_context
from transport.unity_transport import send_with_unity_instance
from transport.legacy.unity_connection import async_send_command_with_retry

# All possible actions grouped by category
SHAPE_ACTIONS = [
    "create_shape", "create_poly_shape",
]

MESH_ACTIONS = [
    "extrude_faces", "extrude_edges", "bevel_edges", "subdivide",
    "delete_faces", "bridge_edges", "connect_elements", "detach_faces",
    "flip_normals", "merge_faces", "combine_meshes", "merge_objects",
]

VERTEX_ACTIONS = [
    "merge_vertices", "split_vertices", "move_vertices",
]

UV_MATERIAL_ACTIONS = [
    "set_face_material", "set_face_color", "set_face_uvs",
]

QUERY_ACTIONS = [
    "get_mesh_info", "convert_to_probuilder",
]

SMOOTHING_ACTIONS = ["set_smoothing", "auto_smooth"]

UTILITY_ACTIONS = ["center_pivot", "freeze_transform", "validate_mesh", "repair_mesh"]

ALL_ACTIONS = (
    ["ping"] + SHAPE_ACTIONS + MESH_ACTIONS + VERTEX_ACTIONS
    + UV_MATERIAL_ACTIONS + QUERY_ACTIONS + SMOOTHING_ACTIONS + UTILITY_ACTIONS
)

_PROBUILDER_TOP_LEVEL_KEYS = {"action", "target", "searchMethod", "properties"}


def _normalize_probuilder_params(params: dict[str, Any]) -> dict[str, Any]:
    params = dict(params)
    properties: dict[str, Any] = {}
    for key in list(params.keys()):
        if key in _PROBUILDER_TOP_LEVEL_KEYS:
            continue
        properties[key] = params.pop(key)

    if properties:
        existing = params.get("properties")
        if isinstance(existing, dict):
            params["properties"] = {**properties, **existing}
        else:
            params["properties"] = properties

    return {k: v for k, v in params.items() if v is not None}


@mcp_for_unity_tool(
    group="probuilder",
    description=(
        "Manage ProBuilder meshes for in-editor 3D modeling. Requires com.unity.probuilder package.\n\n"
        "SHAPE CREATION:\n"
        "- create_shape: Create a ProBuilder primitive (shape_type: Cube/Cylinder/Sphere/Plane/Cone/"
        "Torus/Pipe/Arch/Stair/CurvedStair/Door/Prism). Shape-specific params in properties "
        "(size, radius, height, depth, width, segments, rows, columns, innerRadius, outerRadius, etc.).\n"
        "- create_poly_shape: Create mesh from 2D polygon footprint (points: [[x,y,z],...], "
        "extrudeHeight, flipNormals).\n\n"
        "MESH EDITING:\n"
        "- extrude_faces: Extrude faces outward (faceIndices, distance, method: FaceNormal/VertexNormal/IndividualFaces).\n"
        "- extrude_edges: Extrude edges (edgeIndices, distance, asGroup).\n"
        "- bevel_edges: Bevel edges (edgeIndices, amount 0-1).\n"
        "- subdivide: Subdivide faces (faceIndices optional, all if omitted).\n"
        "- delete_faces: Delete faces (faceIndices).\n"
        "- bridge_edges: Bridge two open edges (edgeA, edgeB as {a,b} vertex index pairs).\n"
        "- connect_elements: Connect edges or faces (edgeIndices or faceIndices).\n"
        "- detach_faces: Detach faces to new object (faceIndices, deleteSource).\n"
        "- flip_normals: Flip face normals (faceIndices).\n"
        "- merge_faces: Merge faces into one (faceIndices).\n"
        "- combine_meshes: Combine multiple ProBuilder objects (targets: list of GameObjects).\n"
        "- merge_objects: Merge multiple objects into one ProBuilder mesh (targets list, auto-converts non-ProBuilder objects).\n\n"
        "VERTEX OPERATIONS:\n"
        "- merge_vertices: Merge/weld vertices (vertexIndices).\n"
        "- split_vertices: Split shared vertices (vertexIndices).\n"
        "- move_vertices: Translate vertices (vertexIndices, offset [x,y,z]).\n\n"
        "UV & MATERIALS:\n"
        "- set_face_material: Assign material to faces (faceIndices optional — all faces when omitted, materialPath).\n"
        "- set_face_color: Set vertex color on faces (faceIndices optional — all faces when omitted, color [r,g,b,a]).\n"
        "- set_face_uvs: Set UV auto-unwrap params (faceIndices optional — all faces when omitted, scale, offset, rotation, flipU, flipV).\n\n"
        "QUERY:\n"
        "- get_mesh_info: Get ProBuilder mesh details. Use include parameter to control detail level: "
        "'summary' (default: counts, bounds, materials), 'faces' (+ face normals/centers/directions), "
        "'edges' (+ edge vertex pairs), 'all' (everything). Each face includes direction "
        "('top','bottom','front','back','left','right') for semantic selection.\n"
        "- convert_to_probuilder: Convert a standard Unity mesh into ProBuilder for editing.\n\n"
        "SMOOTHING:\n"
        "- set_smoothing: Set smoothing group on faces (faceIndices, smoothingGroup: 0=hard, 1+=smooth).\n"
        "- auto_smooth: Auto-assign smoothing groups by angle (angleThreshold: default 30).\n\n"
        "MESH UTILITIES:\n"
        "- center_pivot: Move pivot point to mesh bounds center.\n"
        "- freeze_transform: Bake position/rotation/scale into vertex data, reset transform.\n"
        "- validate_mesh: Check mesh health (degenerate triangles, unused vertices). Read-only.\n"
        "- repair_mesh: Auto-fix degenerate triangles and unused vertices.\n\n"
        "WORKFLOW TIP: Call get_mesh_info with include='faces' to see face normals and directions "
        "before editing. Each face shows its direction ('top','bottom','front','back','left','right') "
        "so you can pick the right indices for operations like extrude_faces or delete_faces."
    ),
    annotations=ToolAnnotations(
        title="Manage ProBuilder",
        destructiveHint=True,
    ),
)
async def manage_probuilder(
    ctx: Context,
    action: Annotated[str, "Action to perform."],
    target: Annotated[str | None, "Target GameObject (name/path/id)."] = None,
    search_method: Annotated[
        Literal["by_id", "by_name", "by_path", "by_tag", "by_layer"] | None,
        "How to find the target GameObject.",
    ] = None,
    properties: Annotated[
        dict[str, Any] | str | None,
        "Action-specific parameters (dict or JSON string).",
    ] = None,
) -> dict[str, Any]:
    """Unified ProBuilder mesh management tool."""

    action_normalized = action.lower()

    if action_normalized not in ALL_ACTIONS:
        # Provide helpful category-based suggestions
        categories = {
            "Shape creation": SHAPE_ACTIONS,
            "Mesh editing": MESH_ACTIONS,
            "Vertex operations": VERTEX_ACTIONS,
            "UV & materials": UV_MATERIAL_ACTIONS,
            "Query": QUERY_ACTIONS,
            "Smoothing": SMOOTHING_ACTIONS,
            "Mesh utilities": UTILITY_ACTIONS,
        }
        category_list = "; ".join(
            f"{cat}: {', '.join(actions)}" for cat, actions in categories.items()
        )
        return {
            "success": False,
            "message": (
                f"Unknown action '{action}'. Available actions by category — {category_list}. "
                "Run with action='ping' to test connection."
            ),
        }

    unity_instance = await get_unity_instance_from_context(ctx)

    params_dict: dict[str, Any] = {"action": action_normalized}
    if properties is not None:
        params_dict["properties"] = properties
    if target is not None:
        params_dict["target"] = target
    if search_method is not None:
        params_dict["searchMethod"] = search_method

    params_dict = {k: v for k, v in params_dict.items() if v is not None}

    result = await send_with_unity_instance(
        async_send_command_with_retry,
        unity_instance,
        "manage_probuilder",
        params_dict,
    )

    return result if isinstance(result, dict) else {"success": False, "message": str(result)}
