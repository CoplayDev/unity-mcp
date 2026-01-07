"""
Tool wrapper for managing ScriptableObject assets via Unity MCP.

Unity-side handler: MCPForUnity.Editor.Tools.ManageScriptableObject
Command name: "manage_scriptable_object"
Actions:
  - create: create an SO asset (optionally with patches)
  - modify: apply serialized property patches to an existing SO asset
"""

from __future__ import annotations

from typing import Annotated, Any, Literal

from fastmcp import Context
from mcp.types import ToolAnnotations

from services.registry import mcp_for_unity_tool
from services.tools import get_unity_instance_from_context
from services.tools.utils import coerce_bool, parse_json_payload
from transport.unity_transport import send_with_unity_instance
from transport.legacy.unity_connection import async_send_command_with_retry


@mcp_for_unity_tool(
    description="Creates and modifies ScriptableObject assets using Unity SerializedObject property paths.",
    annotations=ToolAnnotations(
        title="Manage Scriptable Object",
        destructiveHint=True,
    ),
)
async def manage_scriptable_object(
    ctx: Context,
    action: Annotated[Literal["create", "modify"], "Action to perform: create or modify."],
    # --- create params ---
    type_name: Annotated[str | None,
                         "Namespace-qualified ScriptableObject type name (for create)."] = None,
    folder_path: Annotated[str | None,
                           "Target folder under Assets/... (for create)."] = None,
    asset_name: Annotated[str | None,
                          "Asset file name without extension (for create)."] = None,
    overwrite: Annotated[bool | str | None,
                         "If true, overwrite existing asset at same path (for create)."] = None,
    # --- modify params ---
    target: Annotated[dict[str, Any] | str | None,
                      "Target asset reference {guid|path} (for modify)."] = None,
    # --- shared ---
    patches: Annotated[list[dict[str, Any]] | str | None,
                       "Patch list (or JSON string) to apply."] = None,
    # --- validation ---
    dry_run: Annotated[bool | str | None,
                       "If true, validate patches without applying (modify only)."] = None,
) -> dict[str, Any]:
    unity_instance = get_unity_instance_from_context(ctx)

    # Tolerate JSON-string payloads (LLMs sometimes stringify complex objects)
    parsed_target = parse_json_payload(target)
    parsed_patches = parse_json_payload(patches)

    if parsed_target is not None and not isinstance(parsed_target, dict):
        return {"success": False, "message": "manage_scriptable_object: 'target' must be an object {guid|path} (or JSON string of such)."}

    if parsed_patches is not None and not isinstance(parsed_patches, list):
        return {"success": False, "message": "manage_scriptable_object: 'patches' must be a list (or JSON string of a list)."}

    params: dict[str, Any] = {
        "action": action,
        "typeName": type_name,
        "folderPath": folder_path,
        "assetName": asset_name,
        "overwrite": coerce_bool(overwrite, default=None),
        "target": parsed_target,
        "patches": parsed_patches,
        "dryRun": coerce_bool(dry_run, default=None),
    }

    # Remove None values to keep Unity handler simpler
    params = {k: v for k, v in params.items() if v is not None}

    response = await send_with_unity_instance(
        async_send_command_with_retry,
        unity_instance,
        "manage_scriptable_object",
        params,
    )
    await ctx.info(f"Response {response}")
    return response if isinstance(response, dict) else {"success": False, "message": "Unexpected response from Unity."}
