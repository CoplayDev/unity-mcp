"""
Tool for Unity UI Toolkit — UIDocument, VisualElement queries, style inspection.
Actions: list_documents, get_document, query_elements, get_element, set_style, list_uxml_assets.
Uses built-in UIElements — no package dependency.
"""
from typing import Annotated, Any, Literal

from fastmcp import Context
from mcp.types import ToolAnnotations

from services.registry import mcp_for_unity_tool
from services.tools import get_unity_instance_from_context
from transport.unity_transport import send_with_unity_instance
from transport.legacy.unity_connection import async_send_command_with_retry


@mcp_for_unity_tool(
    description=(
        "Unity UI Toolkit (UIElements) operations. "
        "Actions: list_documents (all UIDocument components), "
        "get_document (panel settings, source UXML, root element summary), "
        "query_elements (find elements by USS selector), "
        "get_element (element properties — style, class list, text, layout), "
        "set_style (modify inline style property), "
        "list_uxml_assets (find UXML assets in project). "
        "Uses built-in UIElements — no package dependency."
    ),
    annotations=ToolAnnotations(title="Manage UI Toolkit"),
)
async def manage_ui_toolkit(
    ctx: Context,
    action: Annotated[
        Literal[
            "list_documents", "get_document", "query_elements",
            "get_element", "set_style", "list_uxml_assets"
        ],
        "Action to perform on Unity UI Toolkit."
    ],
    target: Annotated[str, "GameObject name or instance ID with UIDocument"] | None = None,
    query: Annotated[str, "USS selector to find elements (e.g. '.my-class', '#my-id', 'Button')"] | None = None,
    property: Annotated[str, "Style property name (for set_style, e.g. 'background-color')"] | None = None,
    value: Annotated[str, "Style property value (for set_style)"] | None = None,
    filter: Annotated[str, "Filter string for asset search"] | None = None,
    page_size: Annotated[int, "Max results to return (default 50)"] | None = None,
    cursor: Annotated[int, "Pagination cursor (0-based offset)"] | None = None,
) -> dict[str, Any]:
    unity_instance = get_unity_instance_from_context(ctx)

    params = {"action": action, "target": target, "query": query,
              "property": property, "value": value, "filter": filter,
              "page_size": page_size, "cursor": cursor}
    params = {k: v for k, v in params.items() if v is not None}

    try:
        response = await send_with_unity_instance(
            async_send_command_with_retry, unity_instance, "manage_ui_toolkit", params)
        if isinstance(response, dict) and response.get("success"):
            return {"success": True, "message": response.get("message", "OK"), "data": response.get("data")}
        return response if isinstance(response, dict) else {"success": False, "message": str(response)}
    except Exception as e:
        return {"success": False, "message": f"Python error managing UI Toolkit: {str(e)}"}
