"""
Defines the import_model tool for importing 3D models from the Sketchfab
marketplace into Unity.

Thin pass-through: this tool carries NO API keys and NO file bytes. The C# side
reads the user's Sketchfab token from the OS secure store, performs the search /
download, and imports the model into the Unity project.
"""
from typing import Annotated, Any, Literal

from fastmcp import Context
from mcp.types import ToolAnnotations

from services.registry import mcp_for_unity_tool
from services.tools import get_unity_instance_from_context
from transport.unity_transport import send_with_unity_instance
from transport.legacy.unity_connection import async_send_command_with_retry


@mcp_for_unity_tool(
    group="asset_gen",
    description=(
        "Import 3D models from the Sketchfab marketplace into the Unity project. "
        "Bring-your-own-key: the Sketchfab token lives in the editor's secure store and "
        "never crosses the bridge.\n\n"
        "ACTIONS:\n"
        "- search: Search Sketchfab. Params: query, categories, downloadable, count, "
        "cursor -> results with model uids.\n"
        "- preview: Fetch model metadata (name, thumbnail URLs, license, vertex/face counts) "
        "for a uid before import.\n"
        "- import: Download + import a model by uid. Returns { job_id } immediately; poll "
        "with the status action. Params: uid, target_size, name, output_folder.\n"
        "- status: Poll an async import job by job_id -> { state, progress, assetPath?, error? }.\n"
        "- cancel: Cancel an in-flight import by job_id.\n"
        "- list_providers: List configured marketplace providers (no key values)."
    ),
    annotations=ToolAnnotations(
        title="Import Model",
        destructiveHint=False,
    ),
)
async def import_model(
    ctx: Context,
    action: Annotated[Literal["search", "preview", "import", "status", "cancel", "list_providers"],
                      "Action to perform."],

    query: Annotated[str, "Search query for the search action."] | None = None,
    categories: Annotated[str, "Filter search by category."] | None = None,
    downloadable: Annotated[bool, "Restrict search to downloadable models."] | None = None,
    count: Annotated[int, "Maximum number of search results."] | None = None,
    cursor: Annotated[str, "Pagination cursor for search."] | None = None,
    uid: Annotated[str, "Sketchfab model uid for preview/import."] | None = None,
    target_size: Annotated[float, "Normalize the largest dimension to this size (meters)."] | None = None,
    name: Annotated[str, "Base name for the imported asset."] | None = None,
    output_folder: Annotated[str, "Destination folder under Assets/ for the import."] | None = None,
    job_id: Annotated[str, "Job id for status/cancel."] | None = None,
) -> dict[str, Any]:
    unity_instance = await get_unity_instance_from_context(ctx)

    params_dict = {
        "action": action.lower(),
        "query": query,
        "categories": categories,
        "downloadable": downloadable,
        "count": count,
        "cursor": cursor,
        "uid": uid,
        "targetSize": target_size,
        "name": name,
        "outputFolder": output_folder,
        "jobId": job_id,
    }

    # Remove None values
    params_dict = {k: v for k, v in params_dict.items() if v is not None}

    result = await send_with_unity_instance(
        async_send_command_with_retry,
        unity_instance,
        "import_model",
        params_dict,
    )

    return result if isinstance(result, dict) else {"success": False, "message": str(result)}
