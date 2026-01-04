"""Tool for executing Unity Test Runner suites."""
from typing import Annotated, Literal, Any

from fastmcp import Context
from pydantic import BaseModel, Field

from models import MCPResponse
from services.registry import mcp_for_unity_tool
from services.tools import get_unity_instance_from_context
from services.tools.utils import coerce_int
from transport.unity_transport import send_with_unity_instance
from transport.legacy.unity_connection import async_send_command_with_retry
from services.tools.preflight import preflight


class RunTestsSummary(BaseModel):
    total: int
    passed: int
    failed: int
    skipped: int
    durationSeconds: float
    resultState: str


class RunTestsTestResult(BaseModel):
    name: str
    fullName: str
    state: str
    durationSeconds: float
    message: str | None = None
    stackTrace: str | None = None
    output: str | None = None


class RunTestsResult(BaseModel):
    mode: str
    summary: RunTestsSummary
    results: list[RunTestsTestResult] | None = None


class RunTestsResponse(MCPResponse):
    data: RunTestsResult | None = None


@mcp_for_unity_tool(
    description="Runs Unity tests synchronously (blocks until complete). Prefer run_tests_async for non-blocking execution with progress polling."
)
async def run_tests(
    ctx: Context,
    mode: Annotated[Literal["EditMode", "PlayMode"], "Unity test mode to run"] = "EditMode",
    timeout_seconds: Annotated[int | str, "Optional timeout in seconds for the test run"] | None = None,
    test_names: Annotated[list[str] | str, "Full names of specific tests to run (e.g., 'MyNamespace.MyTests.TestMethod')"] | None = None,
    group_names: Annotated[list[str] | str, "Same as test_names, except it allows for Regex"] | None = None,
    category_names: Annotated[list[str] | str, "NUnit category names to filter by (tests marked with [Category] attribute)"] | None = None,
    assembly_names: Annotated[list[str] | str, "Assembly names to filter tests by"] | None = None,
    include_failed_tests: Annotated[bool, "Include details for failed/skipped tests only (default: false)"] = False,
    include_details: Annotated[bool, "Include details for all tests (default: false)"] = False,
) -> RunTestsResponse | MCPResponse:
    unity_instance = get_unity_instance_from_context(ctx)

    gate = await preflight(ctx, requires_no_tests=True, wait_for_no_compile=True, refresh_if_dirty=True)
    if isinstance(gate, MCPResponse):
        return gate

    # Coerce string or list to list of strings
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
    ts = coerce_int(timeout_seconds)
    if ts is not None:
        params["timeoutSeconds"] = ts

    # Add filter parameters if provided
    test_names_list = _coerce_string_list(test_names)
    if test_names_list:
        params["testNames"] = test_names_list

    group_names_list = _coerce_string_list(group_names)
    if group_names_list:
        params["groupNames"] = group_names_list

    category_names_list = _coerce_string_list(category_names)
    if category_names_list:
        params["categoryNames"] = category_names_list

    assembly_names_list = _coerce_string_list(assembly_names)
    if assembly_names_list:
        params["assemblyNames"] = assembly_names_list

    # Add verbosity parameters
    if include_failed_tests:
        params["includeFailedTests"] = True
    if include_details:
        params["includeDetails"] = True

    response = await send_with_unity_instance(async_send_command_with_retry, unity_instance, "run_tests", params)

    # If Unity indicates a run is already active, return a structured "busy" response rather than
    # letting clients interpret this as a generic failure (avoids #503 retry loops).
    if isinstance(response, dict) and not response.get("success", True):
        err = (response.get("error") or response.get("message") or "").strip()
        if "test run is already in progress" in err.lower():
            return MCPResponse(
                success=False,
                error="tests_running",
                message=err,
                hint="retry",
                data={"reason": "tests_running", "retry_after_ms": 5000},
            )
        return MCPResponse(**response)

    return RunTestsResponse(**response) if isinstance(response, dict) else response
