"""
Telemetry decorator for MCP for Unity tools
"""

import functools
import inspect
import logging
import time
from typing import Callable, Any

from core.telemetry import record_resource_usage, record_tool_usage, record_milestone, MilestoneType
from core.token_usage import record_token_usage

_log = logging.getLogger("unity-mcp-telemetry")
_decorator_log_count = 0


def _extract_input_payload(func: Callable, args, kwargs) -> tuple[Any, str | None]:
    try:
        sig = inspect.signature(func)
        bound = sig.bind_partial(*args, **kwargs)
        bound.apply_defaults()
        arguments = dict(bound.arguments)
    except Exception:
        arguments = dict(kwargs)

    action = arguments.get("action")
    filtered = {}
    for key, value in arguments.items():
        if key in {"self", "cls", "ctx", "context", "request"}:
            continue
        filtered[key] = value
    return filtered, action


def _record_token_usage(kind: str, name: str, action: str | None, success: bool, duration_ms: float, input_payload: Any, output_payload: Any) -> None:
    try:
        record_token_usage(
            kind=kind,
            name=name,
            action=action,
            success=success,
            duration_ms=duration_ms,
            input_payload=input_payload,
            output_payload=output_payload,
        )
    except Exception:
        _log.debug("record_token_usage failed", exc_info=True)


def telemetry_tool(tool_name: str):
    """Decorator to add telemetry tracking to MCP tools"""
    def decorator(func: Callable) -> Callable:
        @functools.wraps(func)
        def _sync_wrapper(*args, **kwargs) -> Any:
            start_time = time.time()
            success = False
            error = None
            result = None
            input_payload, sub_action = _extract_input_payload(func, args, kwargs)
            try:
                global _decorator_log_count
                if _decorator_log_count < 10:
                    _log.info(f"telemetry_decorator sync: tool={tool_name}")
                    _decorator_log_count += 1
                result = func(*args, **kwargs)
                success = True
                action_val = sub_action or kwargs.get("action")
                try:
                    if tool_name == "manage_script" and action_val == "create":
                        record_milestone(MilestoneType.FIRST_SCRIPT_CREATION)
                    elif tool_name.startswith("manage_scene"):
                        record_milestone(
                            MilestoneType.FIRST_SCENE_MODIFICATION)
                    record_milestone(MilestoneType.FIRST_TOOL_USAGE)
                except Exception:
                    _log.debug("milestone emit failed", exc_info=True)
                return result
            except Exception as e:
                error = str(e)
                raise
            finally:
                duration_ms = (time.time() - start_time) * 1000
                try:
                    record_tool_usage(tool_name, success,
                                      duration_ms, error, sub_action=sub_action)
                except Exception:
                    _log.debug("record_tool_usage failed", exc_info=True)
                _record_token_usage(
                    "tool",
                    tool_name,
                    sub_action,
                    success,
                    duration_ms,
                    input_payload,
                    result if success else {"error": error},
                )

        @functools.wraps(func)
        async def _async_wrapper(*args, **kwargs) -> Any:
            start_time = time.time()
            success = False
            error = None
            result = None
            input_payload, sub_action = _extract_input_payload(func, args, kwargs)
            try:
                global _decorator_log_count
                if _decorator_log_count < 10:
                    _log.info(f"telemetry_decorator async: tool={tool_name}")
                    _decorator_log_count += 1
                result = await func(*args, **kwargs)
                success = True
                action_val = sub_action or kwargs.get("action")
                try:
                    if tool_name == "manage_script" and action_val == "create":
                        record_milestone(MilestoneType.FIRST_SCRIPT_CREATION)
                    elif tool_name.startswith("manage_scene"):
                        record_milestone(
                            MilestoneType.FIRST_SCENE_MODIFICATION)
                    record_milestone(MilestoneType.FIRST_TOOL_USAGE)
                except Exception:
                    _log.debug("milestone emit failed", exc_info=True)
                return result
            except Exception as e:
                error = str(e)
                raise
            finally:
                duration_ms = (time.time() - start_time) * 1000
                try:
                    record_tool_usage(tool_name, success,
                                      duration_ms, error, sub_action=sub_action)
                except Exception:
                    _log.debug("record_tool_usage failed", exc_info=True)
                _record_token_usage(
                    "tool",
                    tool_name,
                    sub_action,
                    success,
                    duration_ms,
                    input_payload,
                    result if success else {"error": error},
                )

        return _async_wrapper if inspect.iscoroutinefunction(func) else _sync_wrapper
    return decorator


def telemetry_resource(resource_name: str):
    """Decorator to add telemetry tracking to MCP resources"""
    def decorator(func: Callable) -> Callable:
        @functools.wraps(func)
        def _sync_wrapper(*args, **kwargs) -> Any:
            start_time = time.time()
            success = False
            error = None
            result = None
            input_payload, _ = _extract_input_payload(func, args, kwargs)
            try:
                global _decorator_log_count
                if _decorator_log_count < 10:
                    _log.info(
                        f"telemetry_decorator sync: resource={resource_name}")
                    _decorator_log_count += 1
                result = func(*args, **kwargs)
                success = True
                return result
            except Exception as e:
                error = str(e)
                raise
            finally:
                duration_ms = (time.time() - start_time) * 1000
                try:
                    record_resource_usage(resource_name, success,
                                          duration_ms, error)
                except Exception:
                    _log.debug("record_resource_usage failed", exc_info=True)
                _record_token_usage(
                    "resource",
                    resource_name,
                    None,
                    success,
                    duration_ms,
                    input_payload,
                    result if success else {"error": error},
                )

        @functools.wraps(func)
        async def _async_wrapper(*args, **kwargs) -> Any:
            start_time = time.time()
            success = False
            error = None
            result = None
            input_payload, _ = _extract_input_payload(func, args, kwargs)
            try:
                global _decorator_log_count
                if _decorator_log_count < 10:
                    _log.info(
                        f"telemetry_decorator async: resource={resource_name}")
                    _decorator_log_count += 1
                result = await func(*args, **kwargs)
                success = True
                return result
            except Exception as e:
                error = str(e)
                raise
            finally:
                duration_ms = (time.time() - start_time) * 1000
                try:
                    record_resource_usage(resource_name, success,
                                          duration_ms, error)
                except Exception:
                    _log.debug("record_resource_usage failed", exc_info=True)
                _record_token_usage(
                    "resource",
                    resource_name,
                    None,
                    success,
                    duration_ms,
                    input_payload,
                    result if success else {"error": error},
                )

        return _async_wrapper if inspect.iscoroutinefunction(func) else _sync_wrapper
    return decorator
