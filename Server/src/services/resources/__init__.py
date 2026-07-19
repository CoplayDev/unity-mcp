"""
MCP Resources package - Auto-discovers and registers all resources in this directory.
"""
import functools
import inspect
import logging
import re
from pathlib import Path
from typing import Any, Callable

from fastmcp import FastMCP
from pydantic import BaseModel
from core.telemetry_decorator import telemetry_resource
from core.logging_decorator import log_execution

from services.registry import get_registered_resources
from utils.module_discovery import discover_modules

logger = logging.getLogger("mcp-for-unity-server")

# Export decorator for easy imports within tools
__all__ = ['register_all_resources']

_QUERY_EXPRESSION_RE = re.compile(r"\{\?([^}]*)\}")
_PATH_PARAMETER_RE = re.compile(r"\{(\w+)(?:\*)?\}")
_UNITY_INSTANCE_PARAMETER = "unity_instance"
_TARGETING_DESCRIPTION = (
    "\n\nAdvanced per-call routing: optionally set the `unity_instance` URI query "
    "parameter to target this read to one Unity Editor instance. Usually omit "
    "it to use the session's active instance. This override does not change "
    "the active instance or affect later calls."
)


def _serialize_pydantic(
    func: Callable[..., Any],
    *,
    consume_unity_instance: bool = False,
) -> Callable[..., Any]:
    """Wrap a resource function and serialize Pydantic results as JSON."""
    @functools.wraps(func)
    async def wrapper(*args: Any, **kwargs: Any) -> Any:
        """Call the resource and serialize its structured result."""
        if consume_unity_instance:
            kwargs.pop(_UNITY_INSTANCE_PARAMETER, None)
        result = await func(*args, **kwargs)
        if isinstance(result, BaseModel):
            return result.model_dump_json()
        if isinstance(result, dict):
            import json
            return json.dumps(result)
        return result

    if consume_unity_instance:
        signature = inspect.signature(func)
        if _UNITY_INSTANCE_PARAMETER not in signature.parameters:
            parameters = list(signature.parameters.values())
            unity_instance_parameter = inspect.Parameter(
                _UNITY_INSTANCE_PARAMETER,
                kind=inspect.Parameter.KEYWORD_ONLY,
                default=None,
                annotation=str | None,
            )
            var_keyword_index = next(
                (
                    index
                    for index, parameter in enumerate(parameters)
                    if parameter.kind is inspect.Parameter.VAR_KEYWORD
                ),
                len(parameters),
            )
            parameters.insert(var_keyword_index, unity_instance_parameter)
            wrapper.__signature__ = signature.replace(  # type: ignore[attr-defined]
                parameters=parameters
            )
            wrapper.__annotations__ = {
                **getattr(func, "__annotations__", {}),
                _UNITY_INSTANCE_PARAMETER: str | None,
            }
    return wrapper


def _split_query_expression(uri: str) -> tuple[str, list[str]]:
    """Return a URI without its RFC 6570 query expression and its parameters."""
    matches = list(_QUERY_EXPRESSION_RE.finditer(uri))
    if len(matches) > 1:
        raise ValueError(f"Resource URI may contain only one query expression: {uri}")
    if not matches:
        return uri, []

    match = matches[0]
    if match.end() != len(uri):
        raise ValueError(f"Resource URI query expression must be last: {uri}")
    query_parameters = [
        name.strip() for name in match.group(1).split(",") if name.strip()
    ]
    return uri[:match.start()], query_parameters


def _targeted_uri(uri: str, func: Callable[..., Any]) -> tuple[str, bool]:
    """Build the FastMCP template URI for a targetable resource.

    Optional business parameters that are not already represented in the path
    remain available as URI query parameters. The routing parameter is appended
    to the same RFC 6570 expression because FastMCP 3.0.2 parses only one such
    expression per template.
    """
    base_uri, existing_query_parameters = _split_query_expression(uri)
    path_parameters = set(_PATH_PARAMETER_RE.findall(base_uri))
    user_signature = inspect.signature(func)

    business_query_parameters: list[str] = []
    for name, parameter in user_signature.parameters.items():
        if name in path_parameters or name in existing_query_parameters:
            continue
        if name == _UNITY_INSTANCE_PARAMETER:
            continue
        if parameter.kind in (
            inspect.Parameter.VAR_POSITIONAL,
            inspect.Parameter.VAR_KEYWORD,
        ):
            continue
        if parameter.default is not inspect.Parameter.empty:
            business_query_parameters.append(name)

    query_parameters: list[str] = list(dict.fromkeys(
        existing_query_parameters
        + business_query_parameters
        + [_UNITY_INSTANCE_PARAMETER]
    ))
    query_expression = "{?" + ",".join(query_parameters) + "}"
    return base_uri + query_expression, bool(path_parameters)


def _register_resource(
    mcp: FastMCP,
    *,
    func: Callable[..., Any],
    uri: str,
    name: str,
    description: str | None,
    kwargs: dict[str, Any],
    consume_unity_instance: bool,
) -> None:
    """Apply the server's resource wrappers and register one URI contract."""
    serialized = _serialize_pydantic(
        func,
        consume_unity_instance=consume_unity_instance,
    )
    wrapped = log_execution(name, "Resource")(serialized)
    wrapped = telemetry_resource(name)(wrapped)
    if consume_unity_instance:
        # Re-attach the synthetic public contract after the logging and telemetry
        # decorators so FastMCP sees the optional query parameter.
        wrapped.__signature__ = inspect.signature(serialized)  # type: ignore[attr-defined]
        wrapped.__annotations__ = dict(serialized.__annotations__)
    mcp.resource(
        uri=uri,
        name=name,
        description=description,
        **kwargs,
    )(wrapped)


def register_all_resources(mcp: FastMCP, *, project_scoped_tools: bool = True) -> None:
    """
    Auto-discover and register all resources in the resources/ directory.

    Targetable resources keep their original concrete URI for compatibility and
    additionally expose a query template that accepts ``unity_instance``.
    Path resources use the template only, with existing optional business query
    parameters preserved alongside the routing parameter.
    """
    logger.info("Auto-discovering MCP for Unity Server resources...")
    resources_dir = Path(__file__).parent
    list(discover_modules(resources_dir, __package__))

    resources = get_registered_resources()

    if not resources:
        logger.warning("No MCP resources registered!")
        return

    registered_count = 0
    for resource_info in resources:
        func = resource_info['func']
        uri = resource_info['uri']
        resource_name = resource_info['name']
        description = resource_info['description']
        unity_targetable = resource_info.get('unity_targetable', True)
        kwargs = resource_info['kwargs']

        if not project_scoped_tools and resource_name == "custom_tools":
            logger.info(
                "Skipping custom_tools resource registration (project-scoped tools disabled)")
            continue

        if not unity_targetable:
            _register_resource(
                mcp,
                func=func,
                uri=uri,
                name=resource_name,
                description=description,
                kwargs=kwargs,
                consume_unity_instance=False,
            )
            registered_count += 1
            logger.debug("Registered server-only resource: %s - %s", resource_name, uri)
            continue

        targeted_uri, has_path_parameters = _targeted_uri(uri, func)
        if not has_path_parameters:
            # Preserve concrete resources in resources/list for existing clients.
            _register_resource(
                mcp,
                func=func,
                uri=uri,
                name=resource_name,
                description=description,
                kwargs=kwargs,
                consume_unity_instance=False,
            )
            registered_count += 1

        _register_resource(
            mcp,
            func=func,
            uri=targeted_uri,
            name=resource_name,
            description=(description or "") + _TARGETING_DESCRIPTION,
            kwargs=kwargs,
            consume_unity_instance=True,
        )
        registered_count += 1
        logger.debug(
            "Registered targetable resource%s: %s - %s",
            " template" if has_path_parameters else " companion template",
            resource_name,
            targeted_uri,
        )

    logger.info(
        f"Registered {registered_count} MCP resources ({len(resources)} unique)")
