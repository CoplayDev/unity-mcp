from typing import Annotated, Any, Literal

from fastmcp import Context
from fastmcp.server.server import ToolResult
from mcp.types import ToolAnnotations

from services.registry import mcp_for_unity_tool
from services.tools import get_unity_instance_from_context
from services.tools.utils import extract_screenshot_images
from transport.legacy.unity_connection import async_send_command_with_retry
from transport.unity_transport import send_with_unity_instance


@mcp_for_unity_tool(
    description=(
        "List open Unity Editor tabs/windows or capture one as an MCP PNG image. "
        "Use action=list to obtain window IDs; inactive docked tabs are included. "
        "Screenshots read Unity's own window buffer, including covered windows. "
        "Selecting an inactive tab temporarily changes focus; the previous tab is restored by default. "
        "No mouse/keyboard control is used. Captures may contain private Editor data. "
        "One image per capture is intended for the assistant; client display may vary. "
        "No file is saved unless save_file=true. Requires a graphical Editor; "
        "batch mode, native OS dialogs and minimized windows are unsupported."
    ),
    annotations=ToolAnnotations(title="Manage Editor windows", readOnlyHint=False, destructiveHint=False),
)
async def manage_editor_windows(
    ctx: Context,
    action: Annotated[Literal["list", "screenshot"], "List open windows or capture one selected tab."] = "list",
    window_id: Annotated[int | None, "Current window ID from action=list. Use one selector only."] = None,
    window_title: Annotated[str | None, "Exact title, ignoring case. Duplicate titles require window_id."] = None,
    window_type: Annotated[str | None, "Exact C# type name or full name. Duplicate types require window_id."] = None,
    focus: Annotated[bool, "Select the target tab before capture. False requires an already selected tab."] = True,
    restore_focus: Annotated[bool, "Restore the previous tab and keyboard focus after capture."] = True,
    include_image: Annotated[bool, "Return a PNG image block and separate metadata."] = True,
    save_file: Annotated[bool, "Save a unique full-size PNG in Library/McpEditorScreenshots. Default false."] = False,
    max_resolution: Annotated[int, "Maximum edge for the inline image, from 64 to 4096. Does not resize saved files."] = 1600,
) -> dict[str, Any] | ToolResult:
    """Observe open Editor windows through the selected Unity instance."""
    if action not in ("list", "screenshot"):
        return {"success": False, "message": "action must be list or screenshot."}
    params: dict[str, Any] = {"action": action}
    if action == "screenshot":
        if window_id is not None and type(window_id) is not int:
            return {"success": False, "message": "window_id must be an integer."}
        for name, value in {"window_title": window_title, "window_type": window_type}.items():
            if value is not None and (not isinstance(value, str) or not value.strip()):
                return {"success": False, "message": f"{name} must be a non-empty string."}
        selectors = {key: value for key, value in {
            "window_id": window_id, "window_title": window_title, "window_type": window_type,
        }.items() if value is not None and (not isinstance(value, str) or value.strip())}
        if len(selectors) > 1:
            return {"success": False, "message": "Use only one window selector."}
        if type(max_resolution) is not int or not 64 <= max_resolution <= 4096:
            return {"success": False, "message": "max_resolution must be from 64 to 4096."}
        if not include_image and not save_file:
            return {"success": False, "message": "Set include_image or save_file to true."}
        params.update(selectors)
        params.update(focus=focus, restore_focus=restore_focus, include_image=include_image,
                      save_file=save_file, max_resolution=max_resolution)
    instance = await get_unity_instance_from_context(ctx)
    response = await send_with_unity_instance(async_send_command_with_retry, instance, "manage_editor_windows", params)
    if not isinstance(response, dict):
        return {"success": False, "message": str(response)}
    if action == "screenshot":
        images = extract_screenshot_images(response, image_audience=["assistant"])
        if images is not None:
            return images
    return response
