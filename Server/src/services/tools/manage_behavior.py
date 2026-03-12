"""
Tool for Unity Behavior (AI) — behavior graph agents, blackboard variables, debug state.
Actions: list_agents, get_agent, list_variables, get_variable, set_variable.
Requires com.unity.behavior package.
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
        "Unity Behavior (AI) operations. "
        "Actions: list_agents (all BehaviorGraphAgent components), "
        "get_agent (graph name, running state, current node), "
        "list_variables (blackboard variables on agent), "
        "get_variable (variable name, type, value), "
        "set_variable (set blackboard variable value). "
        "Requires com.unity.behavior package."
    ),
    annotations=ToolAnnotations(title="Manage Behavior"),
)
async def manage_behavior(
    ctx: Context,
    action: Annotated[
        Literal[
            "list_agents", "get_agent", "list_variables",
            "get_variable", "set_variable"
        ],
        "Action to perform on Unity Behavior."
    ],
    target: Annotated[str, "GameObject name or instance ID with BehaviorGraphAgent"] | None = None,
    variable_name: Annotated[str, "Blackboard variable name"] | None = None,
    value: Annotated[str, "Variable value to set (as string)"] | None = None,
    page_size: Annotated[int, "Max results to return (default 50)"] | None = None,
    cursor: Annotated[int, "Pagination cursor (0-based offset)"] | None = None,
) -> dict[str, Any]:
    unity_instance = get_unity_instance_from_context(ctx)

    params = {"action": action, "target": target, "variable_name": variable_name,
              "value": value, "page_size": page_size, "cursor": cursor}
    params = {k: v for k, v in params.items() if v is not None}

    try:
        response = await send_with_unity_instance(
            async_send_command_with_retry, unity_instance, "manage_behavior", params)
        if isinstance(response, dict) and response.get("success"):
            return {"success": True, "message": response.get("message", "OK"), "data": response.get("data")}
        return response if isinstance(response, dict) else {"success": False, "message": str(response)}
    except Exception as e:
        return {"success": False, "message": f"Python error managing behavior: {str(e)}"}
