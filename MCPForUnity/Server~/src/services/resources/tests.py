from typing import Annotated, Literal
from pydantic import BaseModel, Field

from fastmcp import Context

from models import MCPResponse
from services.registry import mcp_for_unity_resource
from services.tools import get_unity_instance_from_context
from transport.unity_transport import send_with_unity_instance
from transport.legacy.unity_connection import async_send_command_with_retry


class TestItem(BaseModel):
    name: Annotated[str, Field(description="The name of the test.")]
    full_name: Annotated[str, Field(description="The full name of the test.")]
    mode: Annotated[Literal["EditMode", "PlayMode"],
                    Field(description="The mode the test is for.")]


class GetTestsResponse(MCPResponse):
    data: list[TestItem] = []


@mcp_for_unity_resource(uri="mcpforunity://tests", name="get_tests", description="Provides a list of all tests.")
async def get_tests(ctx: Context) -> GetTestsResponse | MCPResponse:
    """Provides a list of all tests.
    """
    unity_instance = get_unity_instance_from_context(ctx)
    response = await send_with_unity_instance(
        async_send_command_with_retry,
        unity_instance,
        "get_tests",
        {},
    )
    return GetTestsResponse(**response) if isinstance(response, dict) else response


@mcp_for_unity_resource(uri="mcpforunity://tests/{mode}", name="get_tests_for_mode", description="Provides a list of tests for a specific mode.")
async def get_tests_for_mode(
    ctx: Context,
    mode: Annotated[Literal["EditMode", "PlayMode"], Field(description="The mode to filter tests by.")],
) -> GetTestsResponse | MCPResponse:
    """Provides a list of tests for a specific mode.

    Args:
        mode: The test mode to filter by (EditMode or PlayMode).
    """
    unity_instance = get_unity_instance_from_context(ctx)
    response = await send_with_unity_instance(
        async_send_command_with_retry,
        unity_instance,
        "get_tests_for_mode",
        {"mode": mode},
    )
    return GetTestsResponse(**response) if isinstance(response, dict) else response
