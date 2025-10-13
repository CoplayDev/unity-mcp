"""Tool for executing Unity Test Runner suites."""
from typing import Annotated, Literal, Any

from mcp.server.fastmcp import Context
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
    timeout_seconds: Annotated[int, Field(
        description="Optional timeout in seconds for the Unity test run")] | None = None,
) -> RunTestsResponse:
    await ctx.info(f"Processing run_tests: mode={mode}")

    params: dict[str, Any] = {"mode": mode}
    if timeout_seconds is not None:
        params["timeoutSeconds"] = timeout_seconds

    response = await async_send_command_with_retry("run_tests", params)
    await ctx.info(f'Response {response}')
    return RunTestsResponse(**response) if isinstance(response, dict) else response
