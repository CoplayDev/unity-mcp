from typing import Any, Literal
from mcp.server.fastmcp import FastMCP, Context

from telemetry_decorator import telemetry_tool
from unity_connection import send_command_with_retry


def register_manage_prefabs_tools(mcp: FastMCP) -> None:
    """Register prefab management tools with the MCP server."""

    @mcp.tool()
    @telemetry_tool("manage_prefabs")
    def manage_prefabs(
        ctx: Context,
        action: Literal["open_stage", "close_stage", "save_open_stage", "apply_instance_overrides", "revert_instance_overrides"],
        path: str | None = None,
        mode: str | None = None,
        save_before_close: bool | None = None,
        instance_id: int | None = None,
        target: str | None = None,
    ) -> dict[str, Any]:
        """Bridge for prefab management commands (stage control, instance overrides).

        Args:
            action: One of the supported prefab actions ("open_stage", "close_stage", "save_open_stage",
                "apply_instance_overrides", "revert_instance_overrides").
            path: Prefab asset path (used by "open_stage").
            mode: Optional prefab stage mode (currently only "InIsolation" is supported by the C# side).
            save_before_close: When true, `close_stage` will save the prefab before exiting the stage.
            instance_id: Prefab instance ID for apply/revert overrides. Accepts int-like values.
            target: Scene GameObject name/path to resolve prefab instance when `instance_id` isn't provided.
        Returns:
            Dictionary mirroring the Unity bridge response.
        """
        try:
            params: dict[str, str] = {"action": action}

            if path:
                params["path"] = path
            if mode:
                params["mode"] = mode
            if save_before_close is not None:
                params["saveBeforeClose"] = bool(save_before_close)

            coerced_instance_id = int(instance_id)
            if coerced_instance_id is not None:
                params["instanceId"] = coerced_instance_id

            if target:
                params["target"] = target

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
