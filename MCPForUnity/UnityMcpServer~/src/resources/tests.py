from typing import Annotated, Literal
from pydantic import BaseModel, Field
from mcp.server.fastmcp import Context

from ..models import MCPResponse
from registry import mcp_for_unity_resource
from unity_connection import async_send_command_with_retry


class TestItem(BaseModel):
    name: Annotated[str, Field(description="The name of the test.")]
    full_name: Annotated[str, Field(description="The full name of the test.")]
    path: Annotated[str, Field(description="The path of the test.")]
    mode: Annotated[str, Field(
        description="The mode the test is for (EditMode or PlayMode).")]


class GetTestsResponse(MCPResponse):
    data: list[TestItem]


@mcp_for_unity_resource(uri="mcpforunity://tests", name="get_tests", description="Provides a list of all tests.")
async def get_tests(ctx: Context) -> GetTestsResponse:
    ctx.info("Getting all tests")
    """Provides a list of all tests."""
    response = await async_send_command_with_retry("get_tests")
    return GetTestsResponse(**response)


@mcp_for_unity_resource(uri="mcpforunity://tests/{mode}", name="get_tests_for_mode", description="Provides a list of tests for a specific mode.")
async def get_tests_for_mode(ctx: Context, mode: Annotated[Literal["edit", "play"], Field(description="The mode to filter tests by.")]) -> GetTestsResponse:
    ctx.info(f"Getting tests for mode: {mode}")
    """Provides a list of tests for a specific mode."""
    response = await async_send_command_with_retry("get_tests_for_mode", {"mode": mode})
    return GetTestsResponse(**response)
