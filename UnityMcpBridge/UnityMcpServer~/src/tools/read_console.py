"""
Defines the read_console tool for accessing Unity Editor console messages.
"""
from typing import List, Dict, Any
import time
from mcp.server.fastmcp import FastMCP, Context
from unity_connection import get_unity_connection, send_command_with_retry
from config import config

def register_read_console_tools(mcp: FastMCP):
    """Registers the read_console tool with the MCP server."""

    @mcp.tool()
    def read_console(
        ctx: Context,
        action: str = None,
        types: List[str] = None,
        count: int = None,
        filter_text: str = None,
        since_timestamp: str = None,
        format: str = None,
        include_stacktrace: bool = None
    ) -> Dict[str, Any]:
        """Gets messages from or clears the Unity Editor console.

        Args:
            ctx: The MCP context.
            action: Operation ('get' or 'clear').
            types: Message types to get ('error', 'warning', 'log', 'all').
            count: Max messages to return.
            filter_text: Text filter for messages.
            since_timestamp: Get messages after this timestamp (ISO 8601).
            format: Output format ('plain', 'detailed', 'json').
            include_stacktrace: Include stack traces in output.

        Returns:
            Dictionary with results. For 'get', includes 'data' (messages).
        """
        
        # Get the connection instance
        bridge = get_unity_connection()

        # Set defaults if values are None (conservative but useful for CI)
        action = action if action is not None else 'get'
        types = types if types is not None else ['error']
        # Normalize types if passed as a single string
        if isinstance(types, str):
            types = [types]
        format = format if format is not None else 'json'
        include_stacktrace = include_stacktrace if include_stacktrace is not None else True
        # Default count to a higher value unless explicitly provided
        count = 50 if count is None else count

        # Normalize action if it's a string
        if isinstance(action, str):
            action = action.lower()
        
        # Prepare parameters for the C# handler
        params_dict = {
            "action": action,
            "types": types,
            "count": count,
            "filterText": filter_text,
            "sinceTimestamp": since_timestamp,
            "format": format.lower() if isinstance(format, str) else format,
            "includeStacktrace": include_stacktrace
        }

        # Remove None values unless it's 'count' (as None might mean 'all')
        params_dict = {k: v for k, v in params_dict.items() if v is not None or k == 'count'} 
        
        # Add count back if it was None, explicitly sending null might be important for C# logic
        if 'count' not in params_dict:
             params_dict['count'] = None 

        # Use centralized retry helper (tolerate legacy list payloads from some agents)
        resp = send_command_with_retry("read_console", params_dict)
        if isinstance(resp, dict) and resp.get("success") and not include_stacktrace:
            data = resp.get("data", {}) or {}
            lines = data.get("lines")
            if lines is None:
                # Some handlers return the raw list under data
                lines = data if isinstance(data, list) else []

            def _entry(x: Any) -> Dict[str, Any]:
                if isinstance(x, dict):
                    return {
                        "level": x.get("level") or x.get("type"),
                        "message": x.get("message") or x.get("text"),
                    }
                if isinstance(x, (list, tuple)) and len(x) >= 2:
                    return {"level": x[0], "message": x[1]}
                return {"level": None, "message": str(x)}

            trimmed = [_entry(l) for l in (lines or [])]
            return {"success": True, "data": {"lines": trimmed}}
        return resp if isinstance(resp, dict) else {"success": False, "message": str(resp)}
