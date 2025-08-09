"""
Defines the execute_menu_item tool for running Unity Editor menu commands.
"""
from typing import Dict, Any
from mcp.server.fastmcp import FastMCP, Context
from unity_connection import get_unity_connection  # Import unity_connection module

def register_execute_menu_item_tools(mcp: FastMCP):
    """Registers the execute_menu_item tool with the MCP server."""

    @mcp.tool()
    async def execute_menu_item(
        ctx: Context,
        menu_path: str,
        action: str = 'execute',
        parameters: Dict[str, Any] = None,
    ) -> Dict[str, Any]:
        """Executes a Unity Editor menu item via its path (e.g., "File/Save Project").

        Args:
            ctx: The MCP context.
            menu_path: The full path of the menu item to execute.
            action: The operation to perform (default: 'execute').
            parameters: Optional parameters for the menu item (rarely used).

        Returns:
            A dictionary indicating success or failure, with optional message/error.
        """
        
        action = action.lower() if action else 'execute'
        
        # Prepare parameters for the C# handler
        params_dict = {
            "action": action,
            "menuPath": menu_path,
            "parameters": parameters if parameters else {},
        }

        # Remove None values
        params_dict = {k: v for k, v in params_dict.items() if v is not None}

        if "parameters" not in params_dict:
            params_dict["parameters"] = {} # Ensure parameters dict exists

        # Get Unity connection and send the command
        # We use the unity_connection module to communicate with Unity
        unity_conn = get_unity_connection()
        
        # Send command to the ExecuteMenuItem C# handler
        # The command type should match what the Unity side expects
        return unity_conn.send_command("execute_menu_item", params_dict) 