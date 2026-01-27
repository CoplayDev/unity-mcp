from typing import Annotated, Literal, Optional
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


class PaginatedTestsData(BaseModel):
    """Paginated test results."""
    items: list[TestItem] = Field(description="Tests on current page")
    cursor: int = Field(description="Current page cursor (0-based)")
    nextCursor: Optional[int] = Field(None, description="Next page cursor, null if last page")
    totalCount: int = Field(description="Total number of tests across all pages")
    pageSize: int = Field(description="Number of items per page")
    hasMore: bool = Field(description="Whether there are more items after this page")


class GetTestsResponse(MCPResponse):
    """Response containing paginated test data."""
    data: PaginatedTestsData = Field(description="Paginated test data")


@mcp_for_unity_resource(
    uri="mcpforunity://tests",
    name="get_tests",
    description="Provides a paginated list of Unity tests with optional filtering. "
                "Supports mode, filter, page_size (default 50, max 200), and cursor parameters.\n\nURI: mcpforunity://tests"
)
async def get_tests(ctx: Context) -> GetTestsResponse | MCPResponse:
    """Provides a paginated list of all Unity tests with optional filtering.

    The Unity C# handler accepts these parameters:
    - mode: Optional test mode filter (EditMode or PlayMode)
    - filter: Optional name filter pattern (case-insensitive)
    - page_size: Number of tests per page (default: 50, max: 200)
    - cursor: 0-based cursor position for pagination
    - page_number: 1-based page number (alternative to cursor)

    Parameters are passed through to Unity and handled there.
    """
    unity_instance = get_unity_instance_from_context(ctx)

    # For now, just pass empty params - Unity will handle pagination defaults
    # In the future, we could extract params from ctx if needed
    response = await send_with_unity_instance(
        async_send_command_with_retry,
        unity_instance,
        "get_tests",
        {},
    )
    return GetTestsResponse(**response) if isinstance(response, dict) else response


@mcp_for_unity_resource(
    uri="mcpforunity://tests/{mode}",
    name="get_tests_for_mode",
    description="Provides a paginated list of tests for a specific mode (EditMode or PlayMode).\n\nURI: mcpforunity://tests/{mode}"
)
async def get_tests_for_mode(
    ctx: Context,
    mode: Annotated[Literal["EditMode", "PlayMode"], Field(
        description="The mode to filter tests by (EditMode or PlayMode)."
    )],
) -> GetTestsResponse | MCPResponse:
    """Provides a paginated list of tests for a specific mode.

    Args:
        mode: The test mode to filter by (EditMode or PlayMode)

    The Unity C# handler also accepts optional parameters:
    - filter: Filter test names by pattern (case-insensitive contains)
    - page_size: Number of tests per page (default: 50, max: 200)
    - cursor: 0-based cursor position for pagination
    """
    unity_instance = get_unity_instance_from_context(ctx)

    response = await send_with_unity_instance(
        async_send_command_with_retry,
        unity_instance,
        "get_tests_for_mode",
        {"mode": mode},
    )
    return GetTestsResponse(**response) if isinstance(response, dict) else response
