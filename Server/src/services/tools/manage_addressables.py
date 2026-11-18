"""Manage optional Addressables editor configuration."""

from typing import Annotated, Any, Literal

from fastmcp import Context
from mcp.types import ToolAnnotations

from services.registry import mcp_for_unity_tool
from services.tools import get_unity_instance_from_context
from transport.legacy.unity_connection import async_send_command_with_retry
from transport.unity_transport import send_with_unity_instance

AddressablesAction = Literal[
    "ping", "get_settings", "group_list", "entry_list", "entry_find",
    "label_list", "profile_list", "profile_get",
    "group_create", "group_remove", "entry_add", "entry_remove", "entry_move", "entry_set_address",
    "label_add", "label_remove", "label_set", "profile_set", "profile_set_active", "set_settings",
    "build_content", "build_update", "build_clean", "build_status",
]


@mcp_for_unity_tool(
    group="core",
    description=(
        "Query and edit optional Unity Addressables configuration. "
        "ping reports the installed package version, API availability and settings existence. "
        "get_settings reads basic settings; group_list, entry_list, label_list, profile_list and "
        "profile_get return bounded pages (page_size/cursor). entry_find looks up an explicit "
        "entry by exactly one of guid/path, not implicit children of addressable folders. "
        "entry_list optionally filters by group GUID or exact name. profile_get returns raw "
        "variable values for profile_id (defaults to the active profile). "
        "Reuse next_cursor; restart without cursor if configuration changes. "
        "group_create adds bundled/content-update schemas; group_remove needs force for non-empty groups. "
        "entry_add registers an asset in group; entry_move changes group; entry_set_address changes address; "
        "entry_remove cancels registration without deleting the source asset. "
        "label_add/remove manage definitions by name; removal needs force while in use. "
        "label_set replaces an entry's labels with existing definitions ([] clears). "
        "profile_set changes an existing variable by name/value/profile_id; profile_set_active selects a profile. "
        "set_settings accepts a settings object with build_remote_catalog, unique_bundle_ids, contiguous_bundles, "
        "max_concurrent_web_requests (1-1024), catalog_requests_timeout (0-3600), "
        "remote_catalog_build_path and remote_catalog_load_path (existing profile variable names); "
        "initialize=true explicitly creates settings. get_settings returns catalog path variable IDs. "
        "build_content, build_update and build_clean queue a content operation for the active platform/builder. "
        "Poll build_status with job_id; only the latest job is retained. build_update requires content_state_path "
        "from the matching previous build and refuses changed static-content entries until regrouped. "
        "Native builds block Unity's main thread and cannot be cancelled; a client timeout does not cancel a build."
    ),
    annotations=ToolAnnotations(title="Manage Addressables", readOnlyHint=False, destructiveHint=True),
)
async def manage_addressables(
    ctx: Context,
    action: Annotated[AddressablesAction, "Addressables action to perform."],
    group: Annotated[str | None, "Group GUID or exact name for listing, registration, move, or removal."] = None,
    guid: Annotated[str | None, "Asset GUID for an entry action; mutually exclusive with path."] = None,
    path: Annotated[str | None, "Project asset path for an entry action; mutually exclusive with guid."] = None,
    profile_id: Annotated[str | None, "Profile ID; profile_get defaults to active profile, writes require an explicit ID."] = None,
    page_size: Annotated[int | None, "Items per page, 1-200 (default 50)."] = None,
    cursor: Annotated[str | None, "Opaque next_cursor from the preceding page."] = None,
    name: Annotated[str | None, "Group name, label name, or profile variable name, depending on action."] = None,
    address: Annotated[str | None, "New address for entry_set_address."] = None,
    labels: Annotated[list[str] | None, "Replacement labels for label_set; definitions must already exist."] = None,
    value: Annotated[str | None, "Raw string value for profile_set (empty is allowed)."] = None,
    force: Annotated[bool | None, "Allow removal of a non-empty group or an in-use label."] = None,
    settings: Annotated[dict[str, Any] | None, "Whitelisted settings updates for set_settings."] = None,
    initialize: Annotated[bool | None, "set_settings only: explicitly create default settings if absent."] = None,
    content_state_path: Annotated[str | None, "Existing matching previous-build .bin file for build_update."] = None,
    job_id: Annotated[str | None, "Latest content-operation job ID for build_status."] = None,
    static_content: Annotated[bool | None, "group_create: prevent content changes after release; omit to keep Unity's default."] = None,
) -> dict[str, Any]:
    if page_size is not None and (type(page_size) is not int or not 1 <= page_size <= 200):
        return {"success": False, "message": "page_size must be between 1 and 200."}
    if action == "entry_find" and ((guid is None) == (path is None)):
        return {"success": False, "message": "Provide exactly one of guid or path."}
    params: dict[str, Any] = {"action": action}
    for name, value in {
        "group": group, "guid": guid, "path": path, "profile_id": profile_id,
        "page_size": page_size, "cursor": cursor,
        "name": name, "address": address, "labels": labels, "value": value,
        "force": force, "settings": settings, "initialize": initialize,
        "content_state_path": content_state_path, "job_id": job_id,
        "static_content": static_content,
    }.items():
        if value is not None:
            params[name] = value
    unity_instance = await get_unity_instance_from_context(ctx)
    result = await send_with_unity_instance(
        async_send_command_with_retry, unity_instance, "manage_addressables", params
    )
    return result if isinstance(result, dict) else {"success": False, "message": str(result)}
