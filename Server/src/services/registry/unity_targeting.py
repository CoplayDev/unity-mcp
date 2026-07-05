from __future__ import annotations

import functools
import inspect
from typing import Annotated, Any, Callable

UnityInstanceParameter = Annotated[
    str | None,
    "Target Unity instance (Name@hash, hash prefix, or port number in stdio mode).",
]


def add_optional_unity_instance_parameter(
    func: Callable[..., Any],
) -> Callable[..., Any]:
    """Expose an optional unity_instance kwarg without forwarding it downstream."""
    signature = inspect.signature(func)
    if "unity_instance" in signature.parameters:
        return func

    parameters = list(signature.parameters.values())
    unity_parameter = inspect.Parameter(
        "unity_instance",
        inspect.Parameter.KEYWORD_ONLY,
        default=None,
        annotation=UnityInstanceParameter,
    )

    insert_at = len(parameters)
    for index, parameter in enumerate(parameters):
        if parameter.kind == inspect.Parameter.VAR_KEYWORD:
            insert_at = index
            break
    parameters.insert(insert_at, unity_parameter)

    wrapped_signature = signature.replace(parameters=parameters)

    if inspect.iscoroutinefunction(func):
        @functools.wraps(func)
        async def wrapper(*args, **kwargs):
            kwargs.pop("unity_instance", None)
            return await func(*args, **kwargs)
    else:
        @functools.wraps(func)
        def wrapper(*args, **kwargs):
            kwargs.pop("unity_instance", None)
            return func(*args, **kwargs)

    wrapper.__signature__ = wrapped_signature
    wrapper.__annotations__ = {
        **getattr(func, "__annotations__", {}),
        "unity_instance": UnityInstanceParameter,
    }
    return wrapper


def append_unity_instance_query_template(uri: str) -> str:
    """Append unity_instance to a resource URI template if it is not present."""
    if "unity_instance" in uri:
        return uri

    if "{?" not in uri:
        return f"{uri}{{?unity_instance}}"

    prefix, _, suffix = uri.partition("{?")
    query_suffix = suffix[:-1] if suffix.endswith("}") else suffix
    query_names = [name for name in query_suffix.split(",") if name]
    if "unity_instance" not in query_names:
        query_names.append("unity_instance")
    return f"{prefix}{{?{','.join(query_names)}}}"
