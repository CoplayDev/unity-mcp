"""
Telemetry decorator for Unity MCP tools
"""

import functools
import time
from typing import Callable, Any
from telemetry import record_tool_usage, record_milestone, MilestoneType

def telemetry_tool(tool_name: str):
    """Decorator to add telemetry tracking to MCP tools"""
    def decorator(func: Callable) -> Callable:
        @functools.wraps(func)
        def wrapper(*args, **kwargs) -> Any:
            start_time = time.time()
            success = False
            error = None
            
            try:
                result = func(*args, **kwargs)
                success = True
                
                # Record tool-specific milestones
                if tool_name == "manage_script" and kwargs.get("action") == "create":
                    record_milestone(MilestoneType.FIRST_SCRIPT_CREATION)
                elif tool_name.startswith("manage_scene"):
                    record_milestone(MilestoneType.FIRST_SCENE_MODIFICATION)
                
                # Record general first tool usage
                record_milestone(MilestoneType.FIRST_TOOL_USAGE)
                
                return result
                
            except Exception as e:
                error = str(e)
                raise
            finally:
                duration_ms = (time.time() - start_time) * 1000
                record_tool_usage(tool_name, success, duration_ms, error)
                
        return wrapper
    return decorator