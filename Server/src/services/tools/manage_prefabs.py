from typing import Annotated, Any, Literal

from fastmcp import Context
from mcp.types import ToolAnnotations

from services.registry import mcp_for_unity_tool
from services.tools import get_unity_instance_from_context
from services.tools.utils import coerce_bool, coerce_int
from transport.unity_transport import send_with_unity_instance
from transport.legacy.unity_connection import async_send_command_with_retry
from services.tools.preflight import preflight


# Required parameters for each action
REQUIRED_PARAMS = {
    "get_info": ["prefab_path"],
    "get_hierarchy": ["prefab_path"],
    "open_stage": ["prefab_path"],
    "create_from_gameobject": ["target", "prefab_path"],
    "save_open_stage": [],
    "close_stage": [],
}


@mcp_for_unity_tool(
    description=(
        "Manages Unity Prefab assets and stages. "
        "Read operations: get_info (metadata), get_hierarchy (internal structure). "
        "Write operations: open_stage (edit prefab), close_stage (exit editing), save_open_stage (save changes), "
        "create_from_gameobject (convert scene object to prefab). "
        "Note: Use manage_asset with action=search and filterType=Prefab to list prefabs in project."
    ),
    annotations=ToolAnnotations(
        title="Manage Prefabs",
        destructiveHint=True,
    ),
)
async def manage_prefabs(
    ctx: Context,
    action: Annotated[
        Literal[
            "open_stage",
            "close_stage",
            "save_open_stage",
            "create_from_gameobject",
            "get_info",
            "get_hierarchy",
        ],
        "Prefab operation to perform.",
    ],
    prefab_path: Annotated[
        str, "Prefab asset path (e.g., Assets/Prefabs/MyPrefab.prefab). Used by: get_info, get_hierarchy, open_stage."
    ] | None = None,
    mode: Annotated[
        str, "Prefab stage mode for open_stage. Only 'InIsolation' is currently supported."
    ] | None = None,
    save_before_close: Annotated[
        bool, "When true with close_stage, saves the prefab before closing the stage."
    ] | None = None,
    target: Annotated[
        str, "Scene GameObject name for create_from_gameobject. The object to convert to a prefab."
    ] | None = None,
    allow_overwrite: Annotated[
        bool, "When true with create_from_gameobject, allows replacing an existing prefab at the same path."
    ] | None = None,
    search_inactive: Annotated[
        bool, "When true with create_from_gameobject, includes inactive GameObjects in the search."
    ] | None = None,
    page_size: Annotated[
        int | str, "Number of items per page for get_hierarchy (default: 50)."
    ] | None = None,
    cursor: Annotated[
        int | str, "Pagination cursor for get_hierarchy (offset index)."
    ] | None = None,
) -> dict[str, Any]:
    # Validate required parameters
    required = REQUIRED_PARAMS.get(action, [])
    for param_name in required:
        param_value = locals().get(param_name)
        if param_value is None:
            return {
                "success": False,
                "message": f"Action '{action}' requires parameter '{param_name}'."
            }

    unity_instance = get_unity_instance_from_context(ctx)

    # Preflight check for read operations to ensure Unity is ready
    try:
        gate = await preflight(ctx, wait_for_no_compile=True, refresh_if_dirty=True)
        if gate is not None:
            return gate.model_dump()
    except Exception as exc:
        return {
            "success": False,
            "message": f"Unity preflight check failed: {exc}"
        }

    try:
        # Coerce pagination parameters
        coerced_page_size = coerce_int(page_size, default=None)
        coerced_cursor = coerce_int(cursor, default=None)

        # Build parameters dictionary
        params: dict[str, Any] = {"action": action}

        # Handle prefab path parameter
        if prefab_path:
            params["prefabPath"] = prefab_path

        # Handle mode parameter
        if mode:
            params["mode"] = mode

        # Handle boolean parameters
        save_before_close_val = coerce_bool(save_before_close)
        if save_before_close_val is not None:
            params["saveBeforeClose"] = save_before_close_val

        if target:
            params["target"] = target

        allow_overwrite_val = coerce_bool(allow_overwrite)
        if allow_overwrite_val is not None:
            params["allowOverwrite"] = allow_overwrite_val

        search_inactive_val = coerce_bool(search_inactive)
        if search_inactive_val is not None:
            params["searchInactive"] = search_inactive_val

        # Handle pagination parameters
        if coerced_page_size is not None:
            params["pageSize"] = coerced_page_size

        if coerced_cursor is not None:
            params["cursor"] = coerced_cursor

        # Send command to Unity
        response = await send_with_unity_instance(
            async_send_command_with_retry, unity_instance, "manage_prefabs", params
        )

        # Return Unity response directly; ensure success field exists
        if isinstance(response, dict):
            if "success" not in response:
                response["success"] = False
            return response
        return {
            "success": False,
            "message": f"Unexpected response type: {type(response).__name__}"
        }

    except TimeoutError:
        return {
            "success": False,
            "message": "Unity connection timeout. Please check if Unity is running."
        }
    except Exception as exc:
        return {
            "success": False,
            "message": f"Error managing prefabs: {exc}"
        }
