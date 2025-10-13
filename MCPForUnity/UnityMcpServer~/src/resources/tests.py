from typing import Annotated, Literal
from pydantic import BaseModel, Field

from models import MCPResponse
from registry import mcp_for_unity_resource
from unity_connection import async_send_command_with_retry


class TestItem(BaseModel):
    name: Annotated[str, Field(description="The name of the test.")]
    full_name: Annotated[str, Field(description="The full name of the test.")]
    mode: Annotated[Literal["EditMode", "PlayMode"],
                    Field(description="The mode the test is for.")]


class GetTestsResponse(MCPResponse):
    data: list[TestItem] = []


@mcp_for_unity_resource(uri="mcpforunity://tests", name="get_tests", description="Provides a list of all tests.")
async def get_tests() -> GetTestsResponse:
    """Provides a list of all tests."""
    response = await async_send_command_with_retry("get_tests", {})
    return GetTestsResponse(**response) if isinstance(response, dict) else response


@mcp_for_unity_resource(uri="mcpforunity://tests/{mode}", name="get_tests_for_mode", description="Provides a list of tests for a specific mode.")
async def get_tests_for_mode(mode: Annotated[Literal["EditMode", "PlayMode"], Field(description="The mode to filter tests by.")]) -> GetTestsResponse:
    """Provides a list of tests for a specific mode."""
    response = await async_send_command_with_retry("get_tests_for_mode", {"mode": mode})
    return GetTestsResponse(**response) if isinstance(response, dict) else response
