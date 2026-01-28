"""
Middleware for normalizing camelCase parameters to snake_case.

This middleware intercepts all tool calls and normalizes parameter names
before FastMCP/pydantic validation, making the API accept both naming conventions.
"""
import logging
import re
from dataclasses import replace

from fastmcp.server.middleware import Middleware, MiddlewareContext

logger = logging.getLogger("mcp-for-unity-server")


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


def normalize_arguments(arguments: dict | None) -> dict | None:
    """Normalize camelCase argument names to snake_case.

    When both camelCase and snake_case versions exist, snake_case takes precedence.
    """
    if arguments is None:
        return None

    normalized = {}
    seen_snake_keys: set[str] = set()

    for key, value in arguments.items():
        snake_key = camel_to_snake(key)

        # If we've already seen this snake_case key from an explicit snake_case param,
        # skip the camelCase version (prefer explicit snake_case)
        if snake_key in seen_snake_keys and snake_key != key:
            logger.debug(
                "Skipping camelCase '%s' as snake_case '%s' already provided",
                key, snake_key
            )
            continue

        # If this is an explicit snake_case key, mark it as seen
        if snake_key == key:
            seen_snake_keys.add(snake_key)

        normalized[snake_key] = value

        if snake_key not in seen_snake_keys:
            seen_snake_keys.add(snake_key)

    return normalized


class ParamNormalizerMiddleware(Middleware):
    """
    Middleware that normalizes camelCase parameters to snake_case before validation.

    This allows MCP clients to use either naming convention:
        - find_gameobjects(searchMethod="by_name", searchTerm="Player")
        - find_gameobjects(search_method="by_name", search_term="Player")
    """

    async def on_call_tool(self, context: MiddlewareContext, call_next):
        """Normalize tool call arguments before passing to the tool."""
        message = context.message

        # Check if message has arguments that need normalizing
        if hasattr(message, 'params') and message.params:
            params = message.params
            if hasattr(params, 'arguments') and params.arguments:
                original_args = params.arguments
                normalized_args = normalize_arguments(original_args)

                if normalized_args != original_args:
                    logger.debug(
                        "Normalized tool arguments: %s -> %s",
                        list(original_args.keys()),
                        list(normalized_args.keys())
                    )

                    # Create a new params object with normalized arguments
                    # We need to handle this carefully based on the actual message structure
                    try:
                        # Try to create a modified params with normalized arguments
                        new_params = replace(params, arguments=normalized_args)
                        new_message = replace(message, params=new_params)
                        context = context.copy(message=new_message)
                    except Exception as e:
                        logger.warning(
                            "Failed to normalize arguments, proceeding with original: %s",
                            e
                        )

        return await call_next(context)
