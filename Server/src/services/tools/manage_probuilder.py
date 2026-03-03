"""
Tool for ProBuilder mesh creation and editing in Unity Editor.
Actions: create_shape, get_info, extrude, set_vertex_colors, subdivide, merge, to_mesh.
Requires com.unity.probuilder package installed in the Unity project (defines PROBUILDER scripting symbol).
"""
from typing import Annotated, Any, Literal

from fastmcp import Context
from mcp.types import ToolAnnotations

from services.registry import mcp_for_unity_tool
from services.tools import get_unity_instance_from_context
from transport.unity_transport import send_with_unity_instance
from transport.legacy.unity_connection import async_send_command_with_retry


@mcp_for_unity_tool(
    description=(
        "Create and edit ProBuilder meshes in the Unity Editor. "
        "Actions: create_shape (spawn cube/cylinder/sphere/plane/stair/arch/prism/torus), "
        "get_info (face/vertex/edge counts and properties), "
        "extrude (push faces outward by distance), "
        "set_vertex_colors (paint faces with RGBA color), "
        "subdivide (tessellate faces for more geometry), "
        "merge (combine multiple ProBuilder meshes into one), "
        "to_mesh (finalize and optimize the mesh). "
        "Requires com.unity.probuilder package. Use get_info to inspect a mesh before editing. "
        "Target GameObjects by name or instanceID. "
        "face_indices is a comma-separated list of integer face indices (0-based)."
    ),
    annotations=ToolAnnotations(
        title="Manage ProBuilder",
    ),
)
async def manage_probuilder(
    ctx: Context,
    action: Annotated[
        Literal[
            "create_shape", "get_info", "extrude",
            "set_vertex_colors", "subdivide", "merge", "to_mesh"
        ],
        "Action to perform on a ProBuilder mesh."
    ],
    # Shape creation params
    shape: Annotated[
        str,
        "Shape type for create_shape: cube, cylinder, sphere, plane, stair, arch, prism, torus"
    ] | None = None,
    size: Annotated[
        str,
        "Size as 'x,y,z' string for create_shape (default '1,1,1')"
    ] | None = None,
    position: Annotated[
        str,
        "World position as 'x,y,z' string for create_shape (default '0,0,0')"
    ] | None = None,
    # Target mesh params
    target: Annotated[
        str,
        "GameObject name or instanceID of the ProBuilder mesh to modify"
    ] | None = None,
    targets: Annotated[
        str,
        "Comma-separated names or instanceIDs of ProBuilder meshes to merge (merge action, min 2)"
    ] | None = None,
    # Face selection
    face_indices: Annotated[
        str,
        "Comma-separated 0-based face indices (e.g. '0,1,4'). Omit to target all faces."
    ] | None = None,
    # Extrude param
    distance: Annotated[
        float,
        "Extrusion distance for extrude action (default 0.5)"
    ] | None = None,
    # Color param
    color: Annotated[
        str,
        "RGBA color as 'r,g,b' or 'r,g,b,a' (values 0-1, e.g. '1,0,0,1' for red)"
    ] | None = None,
) -> dict[str, Any]:
    unity_instance = get_unity_instance_from_context(ctx)

    params = {
        "action":       action,
        "shape":        shape,
        "size":         size,
        "position":     position,
        "target":       target,
        "targets":      targets,
        "face_indices": face_indices,
        "distance":     distance,
        "color":        color,
    }
    params = {k: v for k, v in params.items() if v is not None}

    try:
        response = await send_with_unity_instance(
            async_send_command_with_retry, unity_instance, "manage_probuilder", params
        )

        if isinstance(response, dict) and response.get("success"):
            return {
                "success": True,
                "message": response.get("message", "ProBuilder operation successful."),
                "data":    response.get("data"),
            }
        return response if isinstance(response, dict) else {"success": False, "message": str(response)}

    except Exception as e:
        return {"success": False, "message": f"Python error managing ProBuilder: {str(e)}"}
