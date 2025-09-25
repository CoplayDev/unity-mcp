"""
Defines the read_console tool for accessing Unity Editor console messages.
"""
from typing import List, Dict, Any
import time
from mcp.server.fastmcp import FastMCP, Context
from unity_connection import get_unity_connection, send_command_with_retry
from config import config
from telemetry_decorator import telemetry_tool

def register_read_console_tools(mcp: FastMCP):
    """Registers the read_console tool with the MCP server."""

    @mcp.tool()
    @telemetry_tool("read_console")
    def read_console(
        ctx: Context,
        action: str = None,
        types: List[str] = None,
        count: Any = None,
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

        # Set defaults if values are None
        action = action if action is not None else 'get'
        types = types if types is not None else ['error', 'warning', 'log']
        format = format if format is not None else 'detailed'
        include_stacktrace = include_stacktrace if include_stacktrace is not None else True

        # Normalize action if it's a string
        if isinstance(action, str):
            action = action.lower()
        
        # Coerce count defensively (string/float -> int)
        def _coerce_int(value, default=None):
            if value is None:
                return default
            try:
                if isinstance(value, bool):
                    return default
                if isinstance(value, int):
                    return int(value)
                s = str(value).strip()
                if s.lower() in ("", "none", "null"):
                    return default
                return int(float(s))
            except Exception:
                return default

        count = _coerce_int(count)

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

        # Use centralized retry helper
        resp = send_command_with_retry("read_console", params_dict)
        if isinstance(resp, dict) and resp.get("success") and not include_stacktrace:
            # Strip stacktrace fields from returned lines if present
            try:
                lines = resp.get("data", {}).get("lines", [])
                for line in lines:
                    if isinstance(line, dict) and "stacktrace" in line:
                        line.pop("stacktrace", None)
            except Exception:
                pass
        return resp if isinstance(resp, dict) else {"success": False, "message": str(resp)}