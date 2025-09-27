from typing import Annotated, Any, Literal

from mcp.server.fastmcp import FastMCP, Context
from telemetry_decorator import telemetry_tool

from unity_connection import send_command_with_retry


def register_manage_prefabs_tools(mcp: FastMCP) -> None:
    """Register prefab management tools with the MCP server."""

    @mcp.tool(name="manage_prefabs", description="Bridge for prefab management commands (stage control and creation).")
    @telemetry_tool("manage_prefabs")
    def manage_prefabs(
        ctx: Context,
        action: Annotated[Literal[
            "open_stage",
            "close_stage",
            "save_open_stage",
            "create_from_gameobject",
        ], "Manage prefabs (stage control and creation)."],
        prefab_path: Annotated[str,
                               "Prefab asset path relative to Assets e.g. Assets/Prefabs/favorite.prefab"] | None = None,
        mode: Annotated[str,
                        "Optional prefab stage mode (only 'InIsolation' is currently supported)"] | None = None,
        save_before_close: Annotated[bool,
                                     "When true, `close_stage` will save the prefab before exiting the stage."] | None = None,
        target: Annotated[str,
                          "Scene GameObject name required for create_from_gameobject"] | None = None,
        allow_overwrite: Annotated[bool,
                                   "Allow replacing an existing prefab at the same path"] | None = None,
        search_inactive: Annotated[bool,
                                   "Include inactive objects when resolving the target name"] | None = None,
    ) -> dict[str, Any]:
        ctx.info(f"Processing manage_prefabs: {action}")
        try:
            params: dict[str, Any] = {"action": action}

            if prefab_path:
                params["prefabPath"] = prefab_path
            if mode:
                params["mode"] = mode
            if save_before_close is not None:
                params["saveBeforeClose"] = bool(save_before_close)
            if target:
                params["target"] = target
            if allow_overwrite is not None:
                params["allowOverwrite"] = bool(allow_overwrite)
            if search_inactive is not None:
                params["searchInactive"] = bool(search_inactive)
            response = send_command_with_retry("manage_prefabs", params)

            if isinstance(response, dict) and response.get("success"):
                return {
                    "success": True,
                    "message": response.get("message", "Prefab operation successful."),
                    "data": response.get("data"),
                }
            return response if isinstance(response, dict) else {"success": False, "message": str(response)}
        except Exception as exc:
            return {"success": False, "message": f"Python error managing prefabs: {exc}"}
