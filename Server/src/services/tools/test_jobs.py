"""Async Unity Test Runner jobs: start + poll."""
from __future__ import annotations

from typing import Annotated, Any, Literal

from fastmcp import Context

from models import MCPResponse
from services.registry import mcp_for_unity_tool
from services.tools import get_unity_instance_from_context
from services.tools.preflight import preflight
import transport.unity_transport as unity_transport
from transport.legacy.unity_connection import async_send_command_with_retry


@mcp_for_unity_tool(description="Starts a Unity test run asynchronously and returns a job_id immediately. Preferred over run_tests for long-running suites. Poll with get_test_job for progress.")
async def run_tests_async(
    ctx: Context,
    mode: Annotated[Literal["EditMode", "PlayMode"], "Unity test mode to run"] = "EditMode",
    test_names: Annotated[list[str] | str, "Full names of specific tests to run"] | None = None,
    group_names: Annotated[list[str] | str, "Same as test_names, except it allows for Regex"] | None = None,
    category_names: Annotated[list[str] | str, "NUnit category names to filter by"] | None = None,
    assembly_names: Annotated[list[str] | str, "Assembly names to filter tests by"] | None = None,
    include_failed_tests: Annotated[bool, "Include details for failed/skipped tests only (default: false)"] = False,
    include_details: Annotated[bool, "Include details for all tests (default: false)"] = False,
) -> dict[str, Any] | MCPResponse:
    unity_instance = get_unity_instance_from_context(ctx)

    gate = await preflight(ctx, requires_no_tests=True, wait_for_no_compile=True, refresh_if_dirty=True)
    if isinstance(gate, MCPResponse):
        return gate

    def _coerce_string_list(value) -> list[str] | None:
        if value is None:
            return None
        if isinstance(value, str):
            return [value] if value.strip() else None
        if isinstance(value, list):
            result = [str(v).strip() for v in value if v and str(v).strip()]
            return result if result else None
        return None

    params: dict[str, Any] = {"mode": mode}
    if (t := _coerce_string_list(test_names)):
        params["testNames"] = t
    if (g := _coerce_string_list(group_names)):
        params["groupNames"] = g
    if (c := _coerce_string_list(category_names)):
        params["categoryNames"] = c
    if (a := _coerce_string_list(assembly_names)):
        params["assemblyNames"] = a
    if include_failed_tests:
        params["includeFailedTests"] = True
    if include_details:
        params["includeDetails"] = True

    response = await unity_transport.send_with_unity_instance(
        async_send_command_with_retry,
        unity_instance,
        "run_tests_async",
        params,
    )

    if isinstance(response, dict) and not response.get("success", True):
        return MCPResponse(**response)
    return response if isinstance(response, dict) else MCPResponse(success=False, error=str(response)).model_dump()


@mcp_for_unity_tool(description="Polls an async Unity test job by job_id.")
async def get_test_job(
    ctx: Context,
    job_id: Annotated[str, "Job id returned by run_tests_async"],
    include_failed_tests: Annotated[bool, "Include details for failed/skipped tests only (default: false)"] = False,
    include_details: Annotated[bool, "Include details for all tests (default: false)"] = False,
) -> dict[str, Any] | MCPResponse:
    unity_instance = get_unity_instance_from_context(ctx)

    params: dict[str, Any] = {"job_id": job_id}
    if include_failed_tests:
        params["includeFailedTests"] = True
    if include_details:
        params["includeDetails"] = True

    response = await unity_transport.send_with_unity_instance(
        async_send_command_with_retry,
        unity_instance,
        "get_test_job",
        params,
    )
    if isinstance(response, dict) and not response.get("success", True):
        return MCPResponse(**response)
    return response if isinstance(response, dict) else MCPResponse(success=False, error=str(response)).model_dump()


