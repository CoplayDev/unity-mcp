"""
Defines the read_console tool for accessing Unity Editor console messages.
"""
from typing import Annotated, Any, Literal

from fastmcp import Context
from services.registry import mcp_for_unity_tool
from services.tools import get_unity_instance_from_context
from transport.unity_transport import send_with_unity_instance
from transport.legacy.unity_connection import async_send_command_with_retry


@mcp_for_unity_tool(
    description="Gets messages from or clears the Unity Editor console. Note: For maximum client compatibility, pass count as a quoted string (e.g., '5')."
)
async def read_console(
    ctx: Context,
    action: Annotated[Literal['get', 'clear'],
                      "Get or clear the Unity Editor console. Defaults to 'get' if omitted."] | None = None,
    types: Annotated[list[Literal['error', 'warning',
                                  'log', 'all']], "Message types to get"] | None = None,
    count: Annotated[int | str,
                     "Max messages to return (accepts int or string, e.g., 5 or '5')"] | None = None,
    filter_text: Annotated[str, "Text filter for messages"] | None = None,
    since_timestamp: Annotated[str,
                               "Get messages after this timestamp (ISO 8601)"] | None = None,
    format: Annotated[Literal['plain', 'detailed',
                              'json'], "Output format"] | None = None,
    include_stacktrace: Annotated[bool | str,
                                  "Include stack traces in output (accepts true/false or 'true'/'false')"] | None = None,
) -> dict[str, Any]:
    # Get active instance from session state
    # Removed session_state import
    unity_instance = get_unity_instance_from_context(ctx)
    # Set defaults if values are None
    action = action if action is not None else 'get'
    types = types if types is not None else ['error', 'warning', 'log']
    format = format if format is not None else 'detailed'
    # Coerce booleans defensively (strings like 'true'/'false')

    def _coerce_bool(value, default=None):
        if value is None:
            return default
        if isinstance(value, bool):
            return value
        if isinstance(value, str):
            v = value.strip().lower()
            if v in ("true", "1", "yes", "on"):
                return True
            if v in ("false", "0", "no", "off"):
                return False
        return bool(value)

    include_stacktrace = _coerce_bool(include_stacktrace, True)

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
    params_dict = {k: v for k, v in params_dict.items()
                   if v is not None or k == 'count'}

    # Add count back if it was None, explicitly sending null might be important for C# logic
    if 'count' not in params_dict:
        params_dict['count'] = None

    # Use centralized retry helper with instance routing
    resp = await send_with_unity_instance(async_send_command_with_retry, unity_instance, "read_console", params_dict)
    if isinstance(resp, dict) and resp.get("success") and not include_stacktrace:
        # Strip stacktrace fields from returned lines if present
        try:
            data = resp.get("data")
            # Handle standard format: {"data": {"lines": [...]}}
            if isinstance(data, dict) and "lines" in data and isinstance(data["lines"], list):
                for line in data["lines"]:
                    if isinstance(line, dict) and "stacktrace" in line:
                        line.pop("stacktrace", None)
            # Handle legacy/direct list format if any
            elif isinstance(data, list):
                for line in data:
                    if isinstance(line, dict) and "stacktrace" in line:
                        line.pop("stacktrace", None)
        except Exception:
            pass
    return resp if isinstance(resp, dict) else {"success": False, "message": str(resp)}
