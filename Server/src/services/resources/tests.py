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
    data: PaginatedTestsData | list[TestItem] = Field(
        description="Paginated test data (new format) or list of tests (legacy format)"
    )


@mcp_for_unity_resource(
    uri="mcpforunity://tests",
    name="get_tests",
    description="Provides a paginated list of Unity tests with optional filtering. "
                "Use page_size (default 50, max 200), cursor, and filter parameters."
)
async def get_tests(
    ctx: Context,
    mode: Annotated[Optional[Literal["EditMode", "PlayMode"]], Field(
        None,
        description="Optional: Filter by test mode (EditMode or PlayMode)"
    )] = None,
    filter: Annotated[Optional[str], Field(
        None,
        description="Optional: Filter test names by pattern (case-insensitive contains)"
    )] = None,
    page_size: Annotated[Optional[int], Field(
        None,
        ge=1,
        le=200,
        description="Optional: Number of tests per page (default: 50, max: 200)"
    )] = None,
    cursor: Annotated[Optional[int], Field(
        None,
        ge=0,
        description="Optional: 0-based cursor for pagination"
    )] = None,
    page_number: Annotated[Optional[int], Field(
        None,
        ge=1,
        description="Optional: 1-based page number (converted to cursor)"
    )] = None,
) -> GetTestsResponse | MCPResponse:
    """Provides a paginated list of all Unity tests with optional filtering.

    Args:
        mode: Optional test mode filter (EditMode or PlayMode)
        filter: Optional name filter pattern (case-insensitive)
        page_size: Number of tests per page (default: 50, max: 200)
        cursor: 0-based cursor position for pagination
        page_number: 1-based page number (alternative to cursor)
    """
    unity_instance = get_unity_instance_from_context(ctx)

    # Build params dict with only non-None values
    params = {}
    if mode is not None:
        params["mode"] = mode
    if filter is not None:
        params["filter"] = filter
    if page_size is not None:
        params["page_size"] = page_size
    if cursor is not None:
        params["cursor"] = cursor
    if page_number is not None:
        params["page_number"] = page_number

    response = await send_with_unity_instance(
        async_send_command_with_retry,
        unity_instance,
        "get_tests",
        params,
    )
    return GetTestsResponse(**response) if isinstance(response, dict) else response


@mcp_for_unity_resource(
    uri="mcpforunity://tests/{mode}",
    name="get_tests_for_mode",
    description="DEPRECATED: Use mcpforunity://tests with mode parameter instead. "
                "Provides a paginated list of tests for a specific mode with optional filtering."
)
async def get_tests_for_mode(
    ctx: Context,
    mode: Annotated[Literal["EditMode", "PlayMode"], Field(
        description="The mode to filter tests by (EditMode or PlayMode)."
    )],
    filter: Annotated[Optional[str], Field(
        None,
        description="Optional: Filter test names by pattern (case-insensitive contains)"
    )] = None,
    page_size: Annotated[Optional[int], Field(
        None,
        ge=1,
        le=200,
        description="Optional: Number of tests per page (default: 50, max: 200)"
    )] = None,
    cursor: Annotated[Optional[int], Field(
        None,
        ge=0,
        description="Optional: 0-based cursor for pagination"
    )] = None,
) -> GetTestsResponse | MCPResponse:
    """DEPRECATED: Use get_tests resource with mode parameter instead.

    Provides a paginated list of tests for a specific mode with optional filtering.

    Args:
        mode: The test mode to filter by (EditMode or PlayMode)
        filter: Optional name filter pattern (case-insensitive)
        page_size: Number of tests per page (default: 50, max: 200)
        cursor: 0-based cursor position for pagination
    """
    unity_instance = get_unity_instance_from_context(ctx)

    # Build params dict with only non-None values
    params = {"mode": mode}
    if filter is not None:
        params["filter"] = filter
    if page_size is not None:
        params["page_size"] = page_size
    if cursor is not None:
        params["cursor"] = cursor

    response = await send_with_unity_instance(
        async_send_command_with_retry,
        unity_instance,
        "get_tests_for_mode",
        params,
    )
    return GetTestsResponse(**response) if isinstance(response, dict) else response
