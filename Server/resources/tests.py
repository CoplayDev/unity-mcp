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


@mcp_for_unity_resource(uri="mcpforunity://tests{?unity_instance}", name="get_tests", description="Provides a list of all tests.")
async def get_tests(unity_instance: str | None = None) -> GetTestsResponse:
    """
    Provides a list of all tests.
    
    Parameters:
        unity_instance (str | None): Target Unity instance identifier (project name, hash, or "Name@hash"). If omitted, the default instance is used.
    
    Returns:
        GetTestsResponse: Response containing the list of tests in its `data` field when the underlying response is a dict; otherwise returns the raw response value.
    """
    response = await async_send_command_with_retry("get_tests", {}, instance_id=unity_instance)
    return GetTestsResponse(**response) if isinstance(response, dict) else response


@mcp_for_unity_resource(uri="mcpforunity://tests/{mode}{?unity_instance}", name="get_tests_for_mode", description="Provides a list of tests for a specific mode.")
async def get_tests_for_mode(
    mode: Annotated[Literal["EditMode", "PlayMode"], Field(description="The mode to filter tests by.")],
    unity_instance: str | None = None
) -> GetTestsResponse:
    """
    Provides a list of tests for a specific mode.
    
    Parameters:
        mode (Literal["EditMode", "PlayMode"]): The test mode to filter by.
        unity_instance (str | None): Target Unity instance (project name, hash, or "Name@hash"). If not specified, the default instance is used.
    
    Returns:
        GetTestsResponse | Any: A GetTestsResponse containing the tests when the backend returns a dict; otherwise returns the raw response value.
    """
    response = await async_send_command_with_retry("get_tests_for_mode", {"mode": mode}, instance_id=unity_instance)
    return GetTestsResponse(**response) if isinstance(response, dict) else response