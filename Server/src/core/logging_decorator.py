import functools
import inspect
import logging
from typing import Callable, Any

logger = logging.getLogger("mcp-for-unity-server")


def log_execution(name: str, type_label: str):
    """Decorator to log input arguments and return value of a function."""
    def decorator(func: Callable) -> Callable:
        @functools.wraps(func)
        def _sync_wrapper(*args, **kwargs) -> Any:
            logger.info(
                f"{type_label} '{name}' called with args={args} kwargs={kwargs}")
            try:
                result = func(*args, **kwargs)
                logger.info(f"{type_label} '{name}' returned: {result}")
                return result
            except Exception as e:
                logger.info(f"{type_label} '{name}' failed: {e}")
                raise

        @functools.wraps(func)
        async def _async_wrapper(*args, **kwargs) -> Any:
            logger.info(
                f"{type_label} '{name}' called with args={args} kwargs={kwargs}")
            try:
                result = await func(*args, **kwargs)
                logger.info(f"{type_label} '{name}' returned: {result}")
                return result
            except Exception as e:
                logger.info(f"{type_label} '{name}' failed: {e}")
                raise

        return _async_wrapper if inspect.iscoroutinefunction(func) else _sync_wrapper
    return decorator
