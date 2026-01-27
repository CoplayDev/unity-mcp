"""
MCP Resources for reading Prefab data from Unity.

These resources provide read-only access to:
- Prefab info by asset path (mcpforunity://prefab/{path})
- Prefab hierarchy by asset path (mcpforunity://prefab/{path}/hierarchy)
- Currently open prefab stage (mcpforunity://editor/prefab-stage - see prefab_stage.py)
"""
from typing import Any
from urllib.parse import unquote
from pydantic import BaseModel
from fastmcp import Context

from models import MCPResponse
from services.registry import mcp_for_unity_resource
from services.tools import get_unity_instance_from_context
from transport.unity_transport import send_with_unity_instance
from transport.legacy.unity_connection import async_send_command_with_retry


def _normalize_response(response: dict | MCPResponse | Any) -> MCPResponse:
    """Normalize Unity transport response to MCPResponse."""
    if isinstance(response, dict):
        return MCPResponse(**response)
    if isinstance(response, MCPResponse):
        return response
    # Fallback: wrap unexpected types in an error response
    return MCPResponse(success=False, error=f"Unexpected response type: {type(response).__name__}")


def _decode_prefab_path(encoded_path: str) -> str:
    """
    Decode a URL-encoded prefab path.
    Handles paths like 'Assets%2FPrefabs%2FMyPrefab.prefab' -> 'Assets/Prefabs/MyPrefab.prefab'
    """
    return unquote(encoded_path)


# =============================================================================
# Static Helper Resource (shows in UI)
# =============================================================================

@mcp_for_unity_resource(
    uri="mcpforunity://prefab-api",
    name="prefab_api",
    description="Documentation for Prefab resources. Use manage_asset action=search filterType=Prefab to find prefabs, then access resources below.\n\nURI: mcpforunity://prefab-api"
)
async def get_prefab_api_docs(_ctx: Context) -> MCPResponse:
    """
    Returns documentation for the Prefab resource API.

    This is a helper resource that explains how to use the parameterized
    Prefab resources which require an asset path.
    """
    docs = {
        "overview": "Prefab resources provide read-only access to Unity prefab assets.",
        "workflow": [
            "1. Use manage_asset action=search filterType=Prefab to find prefabs",
            "2. Use the asset path to access detailed data via resources below",
            "3. Use manage_prefabs tool for prefab stage operations (open, save, close)"
        ],
        "path_encoding": {
            "note": "Prefab paths must be URL-encoded when used in resource URIs",
            "example": "Assets/Prefabs/MyPrefab.prefab -> Assets%2FPrefabs%2FMyPrefab.prefab"
        },
        "resources": {
            "mcpforunity://prefab/{encoded_path}": {
                "description": "Get prefab asset info (type, root name, components, variant info)",
                "example": "mcpforunity://prefab/Assets%2FPrefabs%2FPlayer.prefab",
                "returns": ["assetPath", "guid", "prefabType", "rootObjectName", "rootComponentTypes", "childCount", "isVariant", "parentPrefab"]
            },
            "mcpforunity://prefab/{encoded_path}/hierarchy": {
                "description": "Get full prefab hierarchy with nested prefab information",
                "example": "mcpforunity://prefab/Assets%2FPrefabs%2FPlayer.prefab/hierarchy",
                "returns": ["prefabPath", "total", "items (with name, instanceId, path, componentTypes, prefab nesting info)"]
            },
            "mcpforunity://editor/prefab-stage": {
                "description": "Get info about the currently open prefab stage (if any)",
                "returns": ["isOpen", "assetPath", "prefabRootName", "mode", "isDirty"]
            }
        },
        "related_tools": {
            "manage_prefabs": "Open/close prefab stages, save changes, create prefabs from GameObjects",
            "manage_asset": "Search for prefab assets, get asset info",
            "manage_gameobject": "Modify GameObjects in open prefab stage",
            "manage_components": "Add/remove/modify components on prefab GameObjects"
        }
    }
    return MCPResponse(success=True, data=docs)


# =============================================================================
# Prefab Info Resource
# =============================================================================

# TODO: Use these typed response classes for better type safety once
# we update the endpoints to validate response structure more strictly.


class PrefabInfoData(BaseModel):
    """Data for a prefab asset."""
    assetPath: str
    guid: str = ""
    prefabType: str = "Regular"
    rootObjectName: str = ""
    rootComponentTypes: list[str] = []
    childCount: int = 0
    isVariant: bool = False
    parentPrefab: str | None = None


class PrefabInfoResponse(MCPResponse):
    """Response containing prefab info data."""
    data: PrefabInfoData | None = None


@mcp_for_unity_resource(
    uri="mcpforunity://prefab/{encoded_path}",
    name="prefab_info",
    description="Get detailed information about a prefab asset by URL-encoded path. Returns prefab type, root object name, component types, child count, and variant info.\n\nURI: mcpforunity://prefab/{encoded_path}"
)
async def get_prefab_info(ctx: Context, encoded_path: str) -> MCPResponse:
    """Get prefab asset info by path."""
    unity_instance = get_unity_instance_from_context(ctx)

    # Decode the URL-encoded path
    decoded_path = _decode_prefab_path(encoded_path)

    response = await send_with_unity_instance(
        async_send_command_with_retry,
        unity_instance,
        "manage_prefabs",
        {
            "action": "get_info",
            "prefabPath": decoded_path
        }
    )

    return _normalize_response(response)


# =============================================================================
# Prefab Hierarchy Resource
# =============================================================================

class PrefabHierarchyItem(BaseModel):
    """Single item in prefab hierarchy."""
    name: str
    instanceId: int
    path: str
    activeSelf: bool = True
    childCount: int = 0
    componentTypes: list[str] = []
    prefab: dict[str, Any] = {}


class PrefabHierarchyData(BaseModel):
    """Data for prefab hierarchy."""
    prefabPath: str
    total: int = 0
    items: list[PrefabHierarchyItem] = []


class PrefabHierarchyResponse(MCPResponse):
    """Response containing prefab hierarchy data."""
    data: PrefabHierarchyData | None = None


@mcp_for_unity_resource(
    uri="mcpforunity://prefab/{encoded_path}/hierarchy",
    name="prefab_hierarchy",
    description="Get the full hierarchy of a prefab with nested prefab information. Returns all GameObjects with their components and nesting depth.\n\nURI: mcpforunity://prefab/{encoded_path}/hierarchy"
)
async def get_prefab_hierarchy(ctx: Context, encoded_path: str) -> MCPResponse:
    """Get prefab hierarchy by path."""
    unity_instance = get_unity_instance_from_context(ctx)

    # Decode the URL-encoded path
    decoded_path = _decode_prefab_path(encoded_path)

    response = await send_with_unity_instance(
        async_send_command_with_retry,
        unity_instance,
        "manage_prefabs",
        {
            "action": "get_hierarchy",
            "prefabPath": decoded_path
        }
    )

    return _normalize_response(response)
