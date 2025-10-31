"""Tool for executing Unity Test Runner suites."""
from typing import Annotated, Literal, Any

from fastmcp import Context
from pydantic import BaseModel, Field

from models import MCPResponse
from registry import mcp_for_unity_tool
from unity_connection import async_send_command_with_retry


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


@mcp_for_unity_tool(description="Runs Unity tests for the specified mode")
async def run_tests(
    ctx: Context,
    mode: Annotated[Literal["edit", "play"], Field(
        description="Unity test mode to run")] = "edit",
    timeout_seconds: Annotated[str, Field(
        description="Optional timeout in seconds for the Unity test run (string, e.g. '30')")] | None = None,
    unity_instance: Annotated[str,
                             "Target Unity instance (project name, hash, or 'Name@hash'). If not specified, uses default instance."] | None = None,
) -> RunTestsResponse:
    """
    Run Unity Test Runner suites for the specified mode and return the parsed test run results.
    
    @param mode: Test mode to run, either "edit" or "play".
    @param timeout_seconds: Optional timeout in seconds for the test run. Accepts numeric values or string representations (e.g., "30"); values that cannot be interpreted as an integer are ignored.
    @param unity_instance: Optional identifier of the target Unity instance (project name, hash, or "Name@hash"). If omitted, the default instance is used.
    @returns: `RunTestsResponse` containing the test run summary and individual results, or the original response value if it was not a dictionary.
    """
    await ctx.info(f"Processing run_tests: mode={mode}")

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

    params: dict[str, Any] = {"mode": mode}
    ts = _coerce_int(timeout_seconds)
    if ts is not None:
        params["timeoutSeconds"] = ts

    response = await async_send_command_with_retry("run_tests", params, instance_id=unity_instance)
    await ctx.info(f'Response {response}')
    return RunTestsResponse(**response) if isinstance(response, dict) else response