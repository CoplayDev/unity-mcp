"""
Defines the manage_asset tool for interacting with Unity assets.
"""
import asyncio  # Added: Import asyncio for running sync code in async
from typing import Dict, Any
from mcp.server.fastmcp import FastMCP, Context
# from ..unity_connection import get_unity_connection  # Original line that caused error
from unity_connection import get_unity_connection  # Use absolute import relative to Python dir

def register_manage_asset_tools(mcp: FastMCP):
    """Registers the manage_asset tool with the MCP server."""

    @mcp.tool()
    async def manage_asset(
        ctx: Context,
        action: str,
        path: str,
        asset_type: str = None,
        properties: Dict[str, Any] = None,
        destination: str = None,
        generate_preview: bool = False,
        search_pattern: str = None,
        filter_type: str = None,
        filter_date_after: str = None,
        page_size: int = None,
        page_number: int = None
    ) -> Dict[str, Any]:
        """Performs asset operations (import, create, modify, delete, etc.) in Unity.

        Args:
            ctx: The MCP context.
            action: Operation to perform (e.g., 'import', 'create', 'modify', 'delete', 'duplicate', 'move', 'rename', 'search', 'get_info', 'create_folder', 'get_components').
            path: Asset path (e.g., "Materials/MyMaterial.mat") or search scope.
            asset_type: Asset type (e.g., 'Material', 'Folder') - required for 'create'.
            properties: Dictionary of properties for 'create'/'modify'.
            destination: Target path for 'duplicate'/'move'.
            search_pattern: Search pattern (e.g., '*.prefab').
            filter_*: Filters for search (type, date).
            page_*: Pagination for search.

        Returns:
            A dictionary with operation results ('success', 'data', 'error').
        """
        # Ensure properties is a dict if None
        if properties is None:
            properties = {}
            
        # Prepare parameters for the C# handler
        params_dict = {
            "action": action.lower(),
            "path": path,
            "assetType": asset_type,
            "properties": properties,
            "destination": destination,
            "generatePreview": generate_preview,
            "searchPattern": search_pattern,
            "filterType": filter_type,
            "filterDateAfter": filter_date_after,
            "pageSize": page_size,
            "pageNumber": page_number
        }
        
        # Remove None values to avoid sending unnecessary nulls
        params_dict = {k: v for k, v in params_dict.items() if v is not None}

        # Get the current asyncio event loop
        loop = asyncio.get_running_loop()
        # Get the Unity connection instance
        connection = get_unity_connection()
        
        # Run the synchronous send_command in the default executor (thread pool)
        # This prevents blocking the main async event loop.
        result = await loop.run_in_executor(
            None,  # Use default executor
            connection.send_command, # The function to call
            "manage_asset", # First argument for send_command
            params_dict # Second argument for send_command
        )
        # Return the result obtained from Unity
        return result 