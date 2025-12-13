"""Defines the batch_execute tool for orchestrating multiple Unity MCP commands."""
from __future__ import annotations

from typing import Annotated, Any

from fastmcp import Context

from services.registry import mcp_for_unity_tool
from services.tools import get_unity_instance_from_context
from transport.unity_transport import send_with_unity_instance
from transport.legacy.unity_connection import async_send_command_with_retry

MAX_COMMANDS_PER_BATCH = 25


@mcp_for_unity_tool(
    name="batch_execute",
    description=(
        "Runs a list of MCP tool calls as one batch. Use it to send a full sequence of commands, "
        "inspect the results, then submit the next batch for the following step."
    ),
)
async def batch_execute(
    ctx: Context,
    commands: Annotated[list[dict[str, Any]], "List of commands with 'tool' and 'params' keys."],
    parallel: Annotated[bool | None, "Attempt to run read-only commands in parallel"] = None,
    fail_fast: Annotated[bool | None, "Stop processing after the first failure"] = None,
    max_parallelism: Annotated[int | None, "Hint for the maximum number of parallel workers"] = None,
) -> dict[str, Any]:
    """Proxy the batch_execute tool to the Unity Editor transporter."""
    unity_instance = get_unity_instance_from_context(ctx)

    if not isinstance(commands, list) or not commands:
        raise ValueError("'commands' must be a non-empty list of command specifications")

    if len(commands) > MAX_COMMANDS_PER_BATCH:
        raise ValueError(
            f"batch_execute currently supports up to {MAX_COMMANDS_PER_BATCH} commands; received {len(commands)}"
        )

    normalized_commands: list[dict[str, Any]] = []
    for index, command in enumerate(commands):
        if not isinstance(command, dict):
            raise ValueError(f"Command at index {index} must be an object with 'tool' and 'params' keys")

        tool_name = command.get("tool")
        params = command.get("params", {})

        if not tool_name or not isinstance(tool_name, str):
            raise ValueError(f"Command at index {index} is missing a valid 'tool' name")

        if params is None:
            params = {}
        if not isinstance(params, dict):
            raise ValueError(f"Command '{tool_name}' must specify parameters as an object/dict")

        normalized_commands.append({
            "tool": tool_name,
            "params": params,
        })

    payload: dict[str, Any] = {
        "commands": normalized_commands,
    }

    if parallel is not None:
        payload["parallel"] = bool(parallel)
    if fail_fast is not None:
        payload["failFast"] = bool(fail_fast)
    if max_parallelism is not None:
        payload["maxParallelism"] = int(max_parallelism)

    return await send_with_unity_instance(
        async_send_command_with_retry,
        unity_instance,
        "batch_execute",
        payload,
    )
