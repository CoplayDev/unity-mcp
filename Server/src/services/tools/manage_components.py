"""
Tool for managing components on GameObjects in Unity.
Supports add, remove, and set_property operations.
"""
from typing import Annotated, Any, Literal

from fastmcp import Context
from services.registry import mcp_for_unity_tool
from services.tools import get_unity_instance_from_context
from transport.unity_transport import send_with_unity_instance
from transport.legacy.unity_connection import async_send_command_with_retry
from services.tools.utils import parse_json_payload, normalize_properties
from services.tools.preflight import preflight


@mcp_for_unity_tool(
    description="Manages components on GameObjects (add, remove, set_property). For reading component data, use the mcpforunity://scene/gameobject/{id}/components resource."
)
async def manage_components(
    ctx: Context,
    action: Annotated[
        Literal["add", "remove", "set_property"],
        "Action to perform: add (add component), remove (remove component), set_property (set component property)"
    ],
    target: Annotated[
        str | int,
        "Target GameObject - instance ID (preferred) or name/path"
    ],
    component_type: Annotated[
        str,
        "Component type name (e.g., 'Rigidbody', 'BoxCollider', 'MyScript')"
    ],
    search_method: Annotated[
        Literal["by_id", "by_name", "by_path"],
        "How to find the target GameObject"
    ] | None = None,
    # For set_property action - single property
    property: Annotated[str,
                        "Property name to set (for set_property action)"] | None = None,
    value: Annotated[Any,
                     "Value to set (for set_property action)"] | None = None,
    # For add/set_property - multiple properties
    properties: Annotated[
        dict[str, Any],
        "Dictionary of property names to values. Example: {\"mass\": 5.0, \"useGravity\": false}"
    ] | None = None,
) -> dict[str, Any]:
    """
    Manage components on GameObjects.

    Actions:
    - add: Add a new component to a GameObject
    - remove: Remove a component from a GameObject  
    - set_property: Set one or more properties on a component

    Examples:
    - Add Rigidbody: action="add", target="Player", component_type="Rigidbody"
    - Remove BoxCollider: action="remove", target=-12345, component_type="BoxCollider"
    - Set single property: action="set_property", target="Enemy", component_type="Rigidbody", property="mass", value=5.0
    - Set multiple properties: action="set_property", target="Enemy", component_type="Rigidbody", properties={"mass": 5.0, "useGravity": false}
    """
    unity_instance = get_unity_instance_from_context(ctx)

    gate = await preflight(ctx, wait_for_no_compile=True, refresh_if_dirty=True)
    if gate is not None:
        return gate.model_dump()

    if not action:
        return {
            "success": False,
            "message": "Missing required parameter 'action'. Valid actions: add, remove, set_property"
        }

    if not target:
        return {
            "success": False,
            "message": "Missing required parameter 'target'. Specify GameObject instance ID or name."
        }

    if not component_type:
        return {
            "success": False,
            "message": "Missing required parameter 'component_type'. Specify the component type name."
        }

    # --- Normalize properties with detailed error handling ---
    properties, props_error = normalize_properties(properties)
    if props_error:
        return {"success": False, "message": props_error}

    # --- Validate value parameter for serialization issues ---
    if value is not None and isinstance(value, str) and value in ("[object Object]", "undefined"):
        return {"success": False, "message": f"value received invalid input: '{value}'. Expected an actual value."}

    try:
        params = {
            "action": action,
            "target": target,
            "componentType": component_type,
        }

        if search_method:
            params["searchMethod"] = search_method

        if action == "set_property":
            if property and value is not None:
                params["property"] = property
                params["value"] = value
            if properties:
                params["properties"] = properties

        if action == "add" and properties:
            params["properties"] = properties

        response = await send_with_unity_instance(
            async_send_command_with_retry,
            unity_instance,
            "manage_components",
            params,
        )

        if isinstance(response, dict) and response.get("success"):
            return {
                "success": True,
                "message": response.get("message", f"Component {action} successful."),
                "data": response.get("data")
            }
        return response if isinstance(response, dict) else {"success": False, "message": str(response)}

    except Exception as e:
        return {"success": False, "message": f"Error managing component: {e!s}"}
