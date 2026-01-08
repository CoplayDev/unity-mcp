"""
MCP Resources for reading GameObject data from Unity scenes.

These resources provide read-only access to:
- Single GameObject data (mcpforunity://scene/gameobject/{id})
- All components on a GameObject (mcpforunity://scene/gameobject/{id}/components)
- Single component on a GameObject (mcpforunity://scene/gameobject/{id}/component/{name})
"""
from typing import Any
from pydantic import BaseModel
from fastmcp import Context

from models import MCPResponse
from services.registry import mcp_for_unity_resource
from services.tools import get_unity_instance_from_context
from transport.unity_transport import send_with_unity_instance
from transport.legacy.unity_connection import async_send_command_with_retry


def _normalize_response(response: dict | Any) -> MCPResponse:
    """Normalize Unity transport response to MCPResponse."""
    if isinstance(response, dict):
        return MCPResponse(**response)
    return response


def _validate_instance_id(instance_id: str) -> tuple[int | None, MCPResponse | None]:
    """
    Validate and convert instance_id string to int.
    Returns (id_int, None) on success or (None, error_response) on failure.
    """
    try:
        return int(instance_id), None
    except ValueError:
        return None, MCPResponse(success=False, error=f"Invalid instance ID: {instance_id}")


# =============================================================================
# Static Helper Resource (shows in UI)
# =============================================================================

@mcp_for_unity_resource(
    uri="mcpforunity://scene/gameobject-api",
    name="gameobject_api",
    description="Documentation for GameObject resources. Use find_gameobjects tool to get instance IDs, then access resources below."
)
async def get_gameobject_api_docs(_ctx: Context) -> MCPResponse:
    """
    Returns documentation for the GameObject resource API.

    This is a helper resource that explains how to use the parameterized
    GameObject resources which require an instance ID.
    """
    docs = {
        "overview": "GameObject resources provide read-only access to Unity scene objects.",
        "workflow": [
            "1. Use find_gameobjects tool to search for GameObjects and get instance IDs",
            "2. Use the instance ID to access detailed data via resources below"
        ],
        "best_practices": [
            "⚡ Use batch_execute for multiple operations: Combine create/modify/component calls into one batch_execute call for 10-100x better performance",
            "Example: Creating 5 cubes → 1 batch_execute with 5 manage_gameobject commands instead of 5 separate calls",
            "Example: Adding components to 3 objects → 1 batch_execute with 3 manage_components commands"
        ],
        "resources": {
            "mcpforunity://scene/gameobject/{instance_id}": {
                "description": "Get basic GameObject data (name, tag, layer, transform, component type list)",
                "example": "mcpforunity://scene/gameobject/-81840",
                "returns": ["instanceID", "name", "tag", "layer", "transform", "componentTypes", "path", "parent", "children"]
            },
            "mcpforunity://scene/gameobject/{instance_id}/components": {
                "description": "Get all components with full property serialization (paginated)",
                "example": "mcpforunity://scene/gameobject/-81840/components",
                "parameters": {
                    "page_size": "Number of components per page (default: 25)",
                    "cursor": "Pagination offset (default: 0)",
                    "include_properties": "Include full property data (default: true)"
                }
            },
            "mcpforunity://scene/gameobject/{instance_id}/component/{component_name}": {
                "description": "Get a single component by type name with full properties",
                "example": "mcpforunity://scene/gameobject/-81840/component/Camera",
                "note": "Use the component type name (e.g., 'Camera', 'Rigidbody', 'Transform')"
            }
        },
        "related_tools": {
            "find_gameobjects": "Search for GameObjects by name, tag, layer, component, or path",
            "manage_components": "Add, remove, or modify components on GameObjects",
            "manage_gameobject": "Create, modify, or delete GameObjects"
        }
    }
    return MCPResponse(success=True, data=docs)


class TransformData(BaseModel):
    """Transform component data."""
    position: dict[str, float] = {"x": 0.0, "y": 0.0, "z": 0.0}
    localPosition: dict[str, float] = {"x": 0.0, "y": 0.0, "z": 0.0}
    rotation: dict[str, float] = {"x": 0.0, "y": 0.0, "z": 0.0}
    localRotation: dict[str, float] = {"x": 0.0, "y": 0.0, "z": 0.0}
    scale: dict[str, float] = {"x": 1.0, "y": 1.0, "z": 1.0}
    lossyScale: dict[str, float] = {"x": 1.0, "y": 1.0, "z": 1.0}


class GameObjectData(BaseModel):
    """Data for a single GameObject (without full component serialization)."""
    instanceID: int
    name: str
    tag: str = "Untagged"
    layer: int = 0
    layerName: str = "Default"
    active: bool = True
    activeInHierarchy: bool = True
    isStatic: bool = False
    transform: TransformData = TransformData()
    parent: int | None = None
    children: list[int] = []
    componentTypes: list[str] = []
    path: str = ""


# TODO: Use these typed response classes for better type safety once
# we update the endpoints to validate response structure more strictly.
class GameObjectResponse(MCPResponse):
    """Response containing GameObject data."""
    data: GameObjectData | None = None


@mcp_for_unity_resource(
    uri="mcpforunity://scene/gameobject/{instance_id}",
    name="gameobject",
    description="Get detailed information about a single GameObject by instance ID. Returns name, tag, layer, active state, transform data, parent/children IDs, and component type list (no full component properties)."
)
async def get_gameobject(ctx: Context, instance_id: str) -> MCPResponse:
    """Get GameObject data by instance ID."""
    unity_instance = get_unity_instance_from_context(ctx)

    id_int, error = _validate_instance_id(instance_id)
    if error:
        return error

    response = await send_with_unity_instance(
        async_send_command_with_retry,
        unity_instance,
        "get_gameobject",
        {"instanceID": id_int}
    )

    return _normalize_response(response)


class ComponentsData(BaseModel):
    """Data for components on a GameObject."""
    gameObjectID: int
    gameObjectName: str
    components: list[Any] = []
    cursor: int = 0
    pageSize: int = 25
    nextCursor: int | None = None
    totalCount: int = 0
    hasMore: bool = False
    includeProperties: bool = True


class ComponentsResponse(MCPResponse):
    """Response containing components data."""
    data: ComponentsData | None = None


@mcp_for_unity_resource(
    uri="mcpforunity://scene/gameobject/{instance_id}/components",
    name="gameobject_components",
    description="Get all components on a GameObject with full property serialization. Supports pagination with pageSize and cursor parameters."
)
async def get_gameobject_components(
    ctx: Context,
    instance_id: str,
    page_size: int = 25,
    cursor: int = 0,
    include_properties: bool = True
) -> MCPResponse:
    """Get all components on a GameObject."""
    unity_instance = get_unity_instance_from_context(ctx)

    id_int, error = _validate_instance_id(instance_id)
    if error:
        return error

    response = await send_with_unity_instance(
        async_send_command_with_retry,
        unity_instance,
        "get_gameobject_components",
        {
            "instanceID": id_int,
            "pageSize": page_size,
            "cursor": cursor,
            "includeProperties": include_properties
        }
    )

    return _normalize_response(response)


class SingleComponentData(BaseModel):
    """Data for a single component."""
    gameObjectID: int
    gameObjectName: str
    component: Any = None


class SingleComponentResponse(MCPResponse):
    """Response containing single component data."""
    data: SingleComponentData | None = None


@mcp_for_unity_resource(
    uri="mcpforunity://scene/gameobject/{instance_id}/component/{component_name}",
    name="gameobject_component",
    description="Get a specific component on a GameObject by type name. Returns the fully serialized component with all properties."
)
async def get_gameobject_component(
    ctx: Context,
    instance_id: str,
    component_name: str
) -> MCPResponse:
    """Get a specific component on a GameObject."""
    unity_instance = get_unity_instance_from_context(ctx)

    id_int, error = _validate_instance_id(instance_id)
    if error:
        return error

    response = await send_with_unity_instance(
        async_send_command_with_retry,
        unity_instance,
        "get_gameobject_component",
        {
            "instanceID": id_int,
            "componentName": component_name
        }
    )

    return _normalize_response(response)
