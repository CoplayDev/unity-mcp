"""
Tool for inspecting and modifying Unity Mesh data (vertex attributes, colors, geometry).
Actions: inspect, get_info, get_attributes, has_attribute, sample_colors,
         sample_vertices, set_colors, force_upload.
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
        "Inspect and modify Unity Mesh data on GameObjects. "
        "Actions: inspect (all-in-one: info + attributes + color samples — use this first), "
        "get_info (vertex/triangle count, bounds, index format, submesh count, isReadable), "
        "get_attributes (list VertexAttributeDescriptor for each attribute: format, dimension, stream), "
        "has_attribute (check if mesh has a specific attribute: Position/Normal/Color/TexCoord0/Tangent/etc), "
        "sample_colors (sample vertex colors evenly spaced across the mesh), "
        "sample_vertices (sample vertex positions evenly spaced across the mesh), "
        "set_colors (set all vertex colors to a solid RGBA color), "
        "force_upload (call mesh.UploadMeshData(false) to upload pending changes). "
        "Target is a GameObject name or instance ID; mesh is read from its MeshFilter.sharedMesh."
    ),
    annotations=ToolAnnotations(
        title="Manage Mesh",
        destructiveHint=True,
    ),
)
async def manage_mesh(
    ctx: Context,
    action: Annotated[
        Literal[
            "inspect", "get_info", "get_attributes", "has_attribute",
            "sample_colors", "sample_vertices", "set_colors", "force_upload"
        ],
        "Action to perform on the mesh."
    ],
    target: Annotated[
        str,
        "GameObject name or instance ID whose MeshFilter.sharedMesh will be used."
    ],
    # has_attribute
    attribute: Annotated[
        str,
        "Vertex attribute name for has_attribute (e.g. Position, Normal, Color, TexCoord0, Tangent)."
    ] | None = None,
    # set_colors
    color: Annotated[
        str,
        "RGBA color as 'r,g,b,a' floats 0-1 for set_colors (e.g. '1,0,0,1' for red)."
    ] | None = None,
    # sampling
    count: Annotated[int, "Number of samples to return for sample_* and inspect actions (default 10)."] | None = None,
    offset: Annotated[int, "Start offset index for sampling (default 0)."] | None = None,
) -> dict[str, Any]:
    unity_instance = get_unity_instance_from_context(ctx)

    params = {
        "action": action,
        "target": target,
        "attribute": attribute,
        "color": color,
        "count": count,
        "offset": offset,
    }
    params = {k: v for k, v in params.items() if v is not None}

    try:
        response = await send_with_unity_instance(
            async_send_command_with_retry, unity_instance, "manage_mesh", params
        )

        if isinstance(response, dict) and response.get("success"):
            return {
                "success": True,
                "message": response.get("message", "Mesh operation successful."),
                "data": response.get("data"),
            }
        return response if isinstance(response, dict) else {"success": False, "message": str(response)}

    except Exception as e:
        return {"success": False, "message": f"Python error managing mesh: {str(e)}"}
