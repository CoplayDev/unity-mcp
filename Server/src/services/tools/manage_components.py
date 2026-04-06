"""
Tool for managing components on GameObjects in Unity.
Supports add, remove, set_property, get_referenceable, set_reference, and batch_wire operations.
"""
from typing import Annotated, Any, Literal, Optional

from fastmcp import Context
from services.registry import mcp_for_unity_tool
from services.tools import get_unity_instance_from_context
from transport.unity_transport import send_with_unity_instance
from transport.legacy.unity_connection import async_send_command_with_retry
from services.tools.utils import parse_json_payload, normalize_properties
from services.tools.preflight import preflight


@mcp_for_unity_tool(
    description=(
        "Add, remove, set properties, inspect referenceable targets, or wire object references on "
        "components attached to GameObjects. Actions: add, remove, set_property, get_referenceable, "
        "set_reference, batch_wire. Requires target (instance ID or name) and component_type. "
        "For READING component data, use the mcpforunity://scene/gameobject/{id}/components resource "
        "or mcpforunity://scene/gameobject/{id}/component/{name} for a single component. "
        "For creating/deleting GameObjects themselves, use manage_gameobject instead."
    )
)
async def manage_components(
    ctx: Context,
    action: Annotated[
        Literal["add", "remove", "set_property", "get_referenceable", "set_reference", "batch_wire"],
        "Action to perform: add (add component), remove (remove component), set_property (set component property), "
        "get_referenceable (list valid object reference targets), set_reference (assign or clear an object reference), "
        "batch_wire (assign multiple object references)"
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
        Optional[Literal["by_id", "by_name", "by_path"]],
        "How to find the target GameObject"
    ] = None,
    # For set_property action - single property
    property: Annotated[Optional[str],
                        "Property name to set (for set_property action)"] = None,
    value: Annotated[Optional[str | int | float | bool | dict | list],
                     "Value to set (for set_property action). "
                     "For object references: instance ID (int), asset path (string), "
                     "or {\"guid\": \"...\"} / {\"path\": \"...\"}. "
                     "For Sprite sub-assets: {\"guid\": \"...\", \"spriteName\": \"<name>\"} or "
                     "{\"guid\": \"...\", \"fileID\": <id>}. Single-sprite textures auto-resolve."] = None,
    # For add/set_property - multiple properties
    properties: Annotated[
        Optional[dict[str, Any] | str],
        "Dictionary of property names to values. Example: {\"mass\": 5.0, \"useGravity\": false}"
    ] = None,
    # For targeting a specific component when multiple of the same type exist
    component_index: Annotated[
        Optional[int],
        "Zero-based index to select which component when multiple of the same type exist. "
        "Use the components resource to discover indices. If omitted, targets the first instance."
    ] = None,
    reference_path: Annotated[
        Optional[str],
        "Path to the reference target GameObject (for set_reference or batch_wire)"
    ] = None,
    reference_asset_path: Annotated[
        Optional[str],
        "Asset path to the reference target asset (for set_reference or batch_wire)"
    ] = None,
    reference_instance_id: Annotated[
        Optional[int],
        "Instance ID of the reference target object (for set_reference or batch_wire)"
    ] = None,
    references: Annotated[
        Optional[list[dict]],
        "Batch wiring list for batch_wire, each item containing property_name and one reference selector"
    ] = None,
    include_scene: Annotated[
        Optional[bool],
        "Whether to include matching scene objects for get_referenceable"
    ] = True,
    include_assets: Annotated[
        Optional[bool],
        "Whether to include matching project assets for get_referenceable"
    ] = True,
    limit: Annotated[
        Optional[int],
        "Maximum number of get_referenceable results to return"
    ] = None,
    clear: Annotated[
        Optional[bool],
        "Clear the object reference instead of assigning a new target"
    ] = None,
    atomic: Annotated[
        Optional[bool],
        "Whether batch_wire should validate all references before applying any changes"
    ] = True,
) -> dict[str, Any]:
    """
    Manage components on GameObjects.

    Actions:
    - add: Add a new component to a GameObject
    - remove: Remove a component from a GameObject  
    - set_property: Set one or more properties on a component
    - get_referenceable: List valid reference targets for an object reference property
    - set_reference: Assign or clear an object reference property
    - batch_wire: Assign or clear multiple object reference properties

    Examples:
    - Add Rigidbody: action="add", target="Player", component_type="Rigidbody"
    - Remove BoxCollider: action="remove", target=-12345, component_type="BoxCollider"
    - Set single property: action="set_property", target="Enemy", component_type="Rigidbody", property="mass", value=5.0
    - Set multiple properties: action="set_property", target="Enemy", component_type="Rigidbody", properties={"mass": 5.0, "useGravity": false}
    """
    unity_instance = await get_unity_instance_from_context(ctx)

    gate = await preflight(ctx, wait_for_no_compile=True, refresh_if_dirty=True)
    if gate is not None:
        return gate.model_dump()

    if not action:
        return {
            "success": False,
            "message": "Missing required parameter 'action'. Valid actions: add, remove, set_property, get_referenceable, set_reference, batch_wire"
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

        if component_index is not None:
            params["componentIndex"] = component_index

        if action == "set_property":
            if property and value is not None:
                params["property"] = property
                params["value"] = value
            if properties:
                params["properties"] = properties

        if action == "add" and properties:
            params["properties"] = properties

        if action in ("get_referenceable", "set_reference", "batch_wire") and property:
            params["property"] = property

        if action in ("set_reference", "batch_wire"):
            if reference_path is not None:
                params["reference_path"] = reference_path
            if reference_asset_path is not None:
                params["reference_asset_path"] = reference_asset_path
            if reference_instance_id is not None:
                params["reference_instance_id"] = reference_instance_id
            if clear is not None:
                params["clear"] = clear

        if action == "get_referenceable":
            if include_scene is not None:
                params["include_scene"] = include_scene
            if include_assets is not None:
                params["include_assets"] = include_assets
            if limit is not None:
                params["limit"] = limit

        if action == "batch_wire":
            if references is not None:
                params["references"] = references
            if atomic is not None:
                params["atomic"] = atomic

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
