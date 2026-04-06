"""
Execute arbitrary C# code inside the Unity Editor.

Supports execute, history, replay, and clear actions with basic blocked-pattern
checks. Code is compiled in-memory via CSharpCodeProvider — no script files created.

WARNING: This tool runs arbitrary code in the Unity Editor process.
Safety checks block known dangerous patterns but are NOT a security sandbox.
"""
from typing import Annotated, Any, Literal

from fastmcp import Context
from mcp.types import ToolAnnotations

from services.registry import mcp_for_unity_tool
from services.tools import get_unity_instance_from_context
from transport.unity_transport import send_with_unity_instance
from transport.legacy.unity_connection import async_send_command_with_retry


@mcp_for_unity_tool(
    description=(
        "Execute arbitrary C# code inside the Unity Editor. "
        "The code runs as a method body with access to UnityEngine and UnityEditor namespaces. "
        "Use 'return' to send data back. Compiled in-memory — no script files created. "
        "Actions: execute (run code), get_history (list past executions), "
        "replay (re-run a history entry), clear_history. "
        "NOTE: safety_checks blocks known dangerous patterns but is not a full sandbox. "
        "Compiler options: 'auto' (Roslyn if available, else CodeDom), 'roslyn' (C# 12+, requires Microsoft.CodeAnalysis), 'codedom' (C# 6 only)."
    ),
    group="scripting_ext",
    annotations=ToolAnnotations(
        title="Execute Code",
        destructiveHint=True,
    ),
)
async def execute_code(
    ctx: Context,
    action: Annotated[
        Literal["execute", "get_history", "replay", "clear_history"],
        "Action to perform.",
    ],
    code: Annotated[
        str,
        "C# code to execute (for 'execute' action). Must be a valid method body. "
        "Access UnityEngine and UnityEditor namespaces. Use 'return' to send data back.",
    ] | None = None,
    safety_checks: Annotated[
        bool,
        "Enable basic blocked-pattern checks (File.Delete, Process.Start, infinite loops, etc). "
        "Not a full sandbox — advanced bypass is possible. Default: true.",
    ] = True,
    index: Annotated[
        int,
        "History entry index to replay (for 'replay' action).",
    ] | None = None,
    limit: Annotated[
        int,
        "Number of history entries to return (for 'get_history' action, 1-50). Default: 10.",
    ] = 10,
    compiler: Annotated[
        Literal["auto", "roslyn", "codedom"],
        "Compiler backend for 'execute' action. "
        "'auto' uses Roslyn if Microsoft.CodeAnalysis is installed, else falls back to CodeDom. "
        "'roslyn' forces Roslyn (C# 12+). 'codedom' forces legacy CSharpCodeProvider (C# 6). Default: auto.",
    ] = "auto",
) -> dict[str, Any]:
    unity_instance = await get_unity_instance_from_context(ctx)

    params_dict: dict[str, Any] = {"action": action}

    if action == "execute":
        if code is None:
            return {"success": False, "message": "Parameter 'code' is required for 'execute' action."}
        params_dict["code"] = code
        params_dict["safety_checks"] = safety_checks
        params_dict["compiler"] = compiler
    elif action == "replay":
        if index is None:
            return {"success": False, "message": "Parameter 'index' is required for 'replay' action."}
        params_dict["index"] = index
    elif action == "get_history":
        params_dict["limit"] = max(1, min(limit, 50))

    params_dict = {k: v for k, v in params_dict.items() if v is not None}

    response = await send_with_unity_instance(
        async_send_command_with_retry,
        unity_instance,
        "execute_code",
        params_dict,
    )

    if not isinstance(response, dict):
        return {"success": False, "message": str(response)}

    return {
        "success": response.get("success", False),
        "message": response.get("message", response.get("error", "")),
        "data": response.get("data"),
    }
