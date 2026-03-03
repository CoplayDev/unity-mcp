"""
Tool for Unity shader inspection, error checking, and reimport operations.
Actions: reimport, get_errors, get_info, get_passes, find, is_compiling.
Note: Named manage_shader_tool to avoid conflict with upstream manage_shader (CRUD).
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
        "Inspect and manage Unity shaders at runtime. "
        "Actions: reimport (force reimport a shader asset by path), "
        "get_errors (list compiler errors/warnings with file and line info), "
        "get_info (shader name, isSupported, pass count, subshader count, render queue), "
        "get_passes (list all passes with names and enabled state per subshader), "
        "find (locate shader by name, returns asset path and info), "
        "is_compiling (check if any shaders are currently compiling). "
        "Use 'path' param for asset-path-based lookup (e.g. 'Packages/com.foo/Shaders/MyShader.shader'), "
        "or 'name' for shader declaration name (e.g. 'Universal Render Pipeline/Lit')."
    ),
    annotations=ToolAnnotations(
        title="Manage Shader Tool",
    ),
)
async def manage_shader_tool(
    ctx: Context,
    action: Annotated[
        Literal["reimport", "get_errors", "get_info", "get_passes", "find", "is_compiling"],
        "Action to perform on shaders."
    ],
    path: Annotated[
        str,
        "Asset path to the shader (e.g. 'Assets/Shaders/MyShader.shader' or 'Packages/com.foo/MyShader.shader'). "
        "Used by: reimport, get_errors, get_info, get_passes."
    ] | None = None,
    name: Annotated[
        str,
        "Shader declaration name as used in Shader.Find() (e.g. 'Universal Render Pipeline/Lit'). "
        "Used by: find, get_errors, get_info, get_passes."
    ] | None = None,
) -> dict[str, Any]:
    unity_instance = get_unity_instance_from_context(ctx)

    params = {
        "action": action,
        "path": path,
        "name": name,
    }
    params = {k: v for k, v in params.items() if v is not None}

    try:
        response = await send_with_unity_instance(
            async_send_command_with_retry, unity_instance, "manage_shader_tool", params
        )

        if isinstance(response, dict) and response.get("success"):
            return {
                "success": True,
                "message": response.get("message", "Shader operation successful."),
                "data": response.get("data"),
            }
        return response if isinstance(response, dict) else {"success": False, "message": str(response)}

    except Exception as e:
        return {"success": False, "message": f"Python error managing shader: {str(e)}"}
