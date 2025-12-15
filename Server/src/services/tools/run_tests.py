"""Tool for executing Unity Test Runner suites."""
from typing import Annotated, Literal, Any

from fastmcp import Context
from pydantic import BaseModel, Field

from models import MCPResponse
from services.registry import mcp_for_unity_tool
from services.tools import get_unity_instance_from_context
from transport.unity_transport import send_with_unity_instance
from transport.legacy.unity_connection import async_send_command_with_retry


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
    results: list[RunTestsTestResult]


class RunTestsResponse(MCPResponse):
    data: RunTestsResult | None = None


@mcp_for_unity_tool(
    description="Runs Unity tests for the specified mode"
)
async def run_tests(
    ctx: Context,
    mode: Annotated[Literal["EditMode", "PlayMode"], "Unity test mode to run"] = "EditMode",
    timeout_seconds: Annotated[int | str, "Optional timeout in seconds for the test run"] | None = None,
    test_names: Annotated[list[str] | str, "Full names of specific tests to run (e.g., 'MyNamespace.MyTests.TestMethod')"] | None = None,
    group_names: Annotated[list[str] | str, "Same as test_names, except it allows for Regex"] | None = None,
    category_names: Annotated[list[str] | str, "NUnit category names to filter by (tests marked with [Category] attribute)"] | None = None,
    assembly_names: Annotated[list[str] | str, "Assembly names to filter tests by"] | None = None,
) -> RunTestsResponse:
    unity_instance = get_unity_instance_from_context(ctx)

    # Coerce timeout defensively (string/float -> int)
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
    ts = _coerce_int(timeout_seconds)
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

    response = await send_with_unity_instance(async_send_command_with_retry, unity_instance, "run_tests", params)
    await ctx.info(f'Response {response}')
    return RunTestsResponse(**response) if isinstance(response, dict) else response
