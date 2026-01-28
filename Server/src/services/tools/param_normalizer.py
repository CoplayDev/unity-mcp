"""
Parameter normalization for MCP tools.

Provides automatic camelCase to snake_case parameter conversion,
making the Python MCP layer as forgiving as the C# ToolParams wrapper.
"""
import asyncio
import functools
import re
from typing import Any, Callable


def camel_to_snake(name: str) -> str:
    """Convert camelCase to snake_case, handling edge cases.

    Examples:
        searchMethod -> search_method
        HTMLParser -> html_parser
        filter2D -> filter2_d
        already_snake -> already_snake
    """
    # Handle consecutive capitals (e.g., "HTMLParser" -> "html_parser")
    s1 = re.sub(r'([A-Z]+)([A-Z][a-z])', r'\1_\2', name)
    # Handle standard camelCase (e.g., "searchMethod" -> "search_method")
    s2 = re.sub(r'([a-z\d])([A-Z])', r'\1_\2', s1)
    return s2.lower()


def normalize_params(func: Callable) -> Callable:
    """Decorator that normalizes camelCase params to snake_case.

    Handles both sync and async functions.
    When both camelCase and snake_case versions of a parameter are provided,
    the explicit snake_case value takes precedence.

    This allows MCP clients to use either naming convention:
        - find_gameobjects(searchMethod="by_name", searchTerm="Player")
        - find_gameobjects(search_method="by_name", search_term="Player")
        - find_gameobjects(searchMethod="by_name", search_term="Player")  # mixed
    """
    @functools.wraps(func)
    def wrapper(*args, **kwargs):
        normalized = {}
        seen_snake_keys: set[str] = set()

        # First pass: collect all snake_case keys (explicit or converted)
        for key, value in kwargs.items():
            snake_key = camel_to_snake(key)

            # If we've already seen this snake_case key from an explicit snake_case param,
            # skip the camelCase version (prefer explicit snake_case)
            if snake_key in seen_snake_keys and snake_key != key:
                continue

            # If this is an explicit snake_case key, mark it as seen
            # so we skip any camelCase equivalent that comes later
            if snake_key == key:
                seen_snake_keys.add(snake_key)

            normalized[snake_key] = value

            # Track all snake keys we've set
            if snake_key not in seen_snake_keys:
                seen_snake_keys.add(snake_key)

        # Handle both sync and async functions
        if asyncio.iscoroutinefunction(func):
            async def async_call():
                return await func(*args, **normalized)
            return async_call()
        else:
            return func(*args, **normalized)

    return wrapper
