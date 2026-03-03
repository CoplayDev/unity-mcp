import json
from typing import Annotated, Literal, Any

from fastmcp import Context
from fastmcp.server.server import ToolResult
from mcp.types import ToolAnnotations, TextContent, ImageContent

from services.registry import mcp_for_unity_tool
from services.tools import get_unity_instance_from_context
from services.tools.utils import coerce_int, coerce_bool, normalize_vector3
from transport.unity_transport import send_with_unity_instance
from transport.legacy.unity_connection import async_send_command_with_retry
from services.tools.preflight import preflight


def _extract_images(response: dict[str, Any], action: str) -> ToolResult | None:
    """If the Unity response contains inline base64 images, return a ToolResult
    with TextContent + ImageContent blocks. Returns None for normal text-only responses."""
    if not isinstance(response, dict) or not response.get("success"):
        return None

    data = response.get("data")
    if not isinstance(data, dict):
        return None

    if action == "screenshot":
        # Batch images (surround mode) — multiple screenshots in one response
        screenshots = data.get("screenshots")
        if screenshots and isinstance(screenshots, list):
            blocks: list[TextContent | ImageContent] = []
            summary_screenshots = []
            for s in screenshots:
                summary_screenshots.append({k: v for k, v in s.items() if k != "imageBase64"})
            text_result = {
                "success": True,
                "message": response.get("message", ""),
                "data": {
                    "sceneCenter": data.get("sceneCenter"),
                    "sceneRadius": data.get("sceneRadius"),
                    "screenshots": summary_screenshots,
                },
            }
            blocks.append(TextContent(type="text", text=json.dumps(text_result)))
            for s in screenshots:
                b64 = s.get("imageBase64")
                if b64:
                    blocks.append(TextContent(type="text", text=f"[Angle: {s.get('angle', '?')}]"))
                    blocks.append(ImageContent(type="image", data=b64, mimeType="image/png"))
            return ToolResult(content=blocks)

        # Single image (include_image or positioned capture)
        image_b64 = data.get("imageBase64")
        if not image_b64:
            return None
        text_data = {k: v for k, v in data.items() if k != "imageBase64"}
        text_result = {"success": True, "message": response.get("message", ""), "data": text_data}
        return ToolResult(
            content=[
                TextContent(type="text", text=json.dumps(text_result)),
                ImageContent(type="image", data=image_b64, mimeType="image/png"),
            ],
        )

    return None


@mcp_for_unity_tool(
    description=(
        "Performs CRUD operations on Unity scenes. "
        "Read-only actions: get_hierarchy, get_active, get_build_settings, screenshot, scene_view_frame. "
        "Modifying actions: create, load, save. "
        "screenshot supports include_image=true to return an inline base64 PNG for AI vision. "
        "screenshot with batch='surround' captures 6 angles around the scene (no file saved) for comprehensive scene understanding. "
        "screenshot with batch='orbit' captures configurable azimuth x elevation grid for visual QA (use orbit_angles, orbit_elevations, orbit_distance, orbit_fov). "
        "screenshot with look_at/view_position creates a temp camera at that viewpoint and returns an inline image."
    ),
    annotations=ToolAnnotations(
        title="Manage Scene",
        destructiveHint=True,
    ),
)
async def manage_scene(
    ctx: Context,
    action: Annotated[Literal[
        "create",
        "load",
        "save",
        "get_hierarchy",
        "get_active",
        "get_build_settings",
        "screenshot",
        "scene_view_frame",
    ], "Perform CRUD operations on Unity scenes, capture screenshots, and control the Scene View camera."],
    name: Annotated[str, "Scene name."] | None = None,
    path: Annotated[str, "Scene path."] | None = None,
    build_index: Annotated[int | str,
                           "Unity build index (quote as string, e.g., '0')."] | None = None,
    # --- screenshot params ---
    screenshot_file_name: Annotated[str,
                                    "Screenshot file name (optional). Defaults to timestamp when omitted."] | None = None,
    screenshot_super_size: Annotated[int | str,
                                     "Screenshot supersize multiplier (integer ≥1). Optional."] | None = None,
    camera: Annotated[str,
                      "Camera to capture from (name, path, or instance ID). Defaults to Camera.main."] | None = None,
    include_image: Annotated[bool | str,
                             "If true, return the screenshot as an inline base64 PNG image in the response. "
                             "The AI can see the image. Default false. Recommended max_resolution=512 for context efficiency."] | None = None,
    max_resolution: Annotated[int | str,
                              "Max resolution (longest edge in pixels) for the inline image. Default 640. "
                              "Use 256-512 for quick looks, 640-1024 for detail."] | None = None,
    # --- screenshot extended params (batch, positioned capture) ---
    batch: Annotated[str,
                     "Batch capture mode. 'surround' captures 6 fixed angles (front/back/left/right/top/bird_eye). "
                     "'orbit' captures configurable azimuth x elevation grid for visual QA (use orbit_angles, orbit_elevations, orbit_distance, orbit_fov). "
                     "Both modes center on look_at target or scene bounds. Returns inline images, no file saved."] | None = None,
    look_at: Annotated[str | int | list[float],
                       "Target to aim the camera at before capture. Can be a GameObject name/path/ID or [x,y,z] position. "
                       "For batch='surround', centers the surround on this target. For single shots, creates a temp camera aimed here."] | None = None,
    view_position: Annotated[list[float] | str,
                             "World position [x,y,z] to place the camera for a positioned screenshot."] | None = None,
    view_rotation: Annotated[list[float] | str,
                             "Euler rotation [x,y,z] for the camera. Overrides look_at aiming if both provided."] | None = None,
    # --- orbit batch params ---
    orbit_angles: Annotated[int | str,
                            "Number of azimuth samples for batch='orbit' (default 8, max 36)."] | None = None,
    orbit_elevations: Annotated[list[float] | str,
                                "Elevation angles in degrees for batch='orbit' (default [0, 30, -15]). "
                                "E.g., [0, 30, 60] for ground-level, mid, and high views."] | None = None,
    orbit_distance: Annotated[float | str,
                              "Camera distance from target for batch='orbit' (default auto from bounds)."] | None = None,
    orbit_fov: Annotated[float | str,
                         "Camera field of view in degrees for batch='orbit' (default 60)."] | None = None,
    # --- scene_view_frame params ---
    scene_view_target: Annotated[str | int,
                                 "GameObject reference for scene_view_frame (name, path, or instance ID)."] | None = None,
    # --- get_hierarchy paging/safety ---
    parent: Annotated[str | int,
                      "Optional parent GameObject reference (name/path/instanceID) to list direct children."] | None = None,
    page_size: Annotated[int | str,
                         "Page size for get_hierarchy paging."] | None = None,
    cursor: Annotated[int | str,
                      "Opaque cursor for paging (offset)."] | None = None,
    max_nodes: Annotated[int | str,
                         "Hard cap on returned nodes per request (safety)."] | None = None,
    max_depth: Annotated[int | str,
                         "Accepted for forward-compatibility; current paging returns a single level."] | None = None,
    max_children_per_node: Annotated[int | str,
                                     "Child paging hint (safety)."] | None = None,
    include_transform: Annotated[bool | str,
                                 "If true, include local transform in node summaries."] | None = None,
) -> dict[str, Any] | ToolResult:
    unity_instance = get_unity_instance_from_context(ctx)
    gate = await preflight(ctx, wait_for_no_compile=True, refresh_if_dirty=True)
    if gate is not None:
        return gate.model_dump()
    try:
        coerced_build_index = coerce_int(build_index, default=None)
        coerced_super_size = coerce_int(screenshot_super_size, default=None)
        coerced_page_size = coerce_int(page_size, default=None)
        coerced_cursor = coerce_int(cursor, default=None)
        coerced_max_nodes = coerce_int(max_nodes, default=None)
        coerced_max_depth = coerce_int(max_depth, default=None)
        coerced_max_children_per_node = coerce_int(
            max_children_per_node, default=None)
        coerced_include_transform = coerce_bool(
            include_transform, default=None)
        coerced_include_image = coerce_bool(include_image, default=None)
        coerced_max_resolution = coerce_int(max_resolution, default=None)
        if coerced_max_resolution is not None and coerced_max_resolution <= 0:
            return {"success": False, "message": "max_resolution must be a positive integer greater than zero."}

        params: dict[str, Any] = {"action": action}
        if name:
            params["name"] = name
        if path:
            params["path"] = path
        if coerced_build_index is not None:
            params["buildIndex"] = coerced_build_index
        if screenshot_file_name:
            params["fileName"] = screenshot_file_name
        if coerced_super_size is not None:
            params["superSize"] = coerced_super_size

        # screenshot params
        if camera:
            params["camera"] = camera
        if coerced_include_image is not None:
            params["includeImage"] = coerced_include_image
        if coerced_max_resolution is not None:
            params["maxResolution"] = coerced_max_resolution

        # screenshot extended params (batch, positioned capture)
        if batch:
            params["batch"] = batch
        if look_at is not None:
            params["lookAt"] = look_at

        # orbit batch params
        coerced_orbit_angles = coerce_int(orbit_angles, default=None)
        if coerced_orbit_angles is not None:
            params["orbitAngles"] = coerced_orbit_angles
        if orbit_elevations is not None:
            if isinstance(orbit_elevations, str):
                try:
                    orbit_elevations = json.loads(orbit_elevations)
                except (ValueError, TypeError):
                    return {"success": False, "message": "orbit_elevations must be a JSON array of floats."}
            if not isinstance(orbit_elevations, list) or not all(
                isinstance(v, (int, float)) for v in orbit_elevations
            ):
                return {"success": False, "message": "orbit_elevations must be a list of numbers."}
            params["orbitElevations"] = orbit_elevations
        if orbit_distance is not None:
            try:
                params["orbitDistance"] = float(orbit_distance)
            except (ValueError, TypeError):
                return {"success": False, "message": "orbit_distance must be a number."}
        if orbit_fov is not None:
            try:
                params["orbitFov"] = float(orbit_fov)
            except (ValueError, TypeError):
                return {"success": False, "message": "orbit_fov must be a number."}
        if view_position is not None:
            vec, err = normalize_vector3(view_position, "view_position")
            if err:
                return {"success": False, "message": err}
            params["viewPosition"] = vec
        if view_rotation is not None:
            vec, err = normalize_vector3(view_rotation, "view_rotation")
            if err:
                return {"success": False, "message": err}
            params["viewRotation"] = vec

        # scene_view_frame params
        if scene_view_target is not None:
            params["sceneViewTarget"] = scene_view_target

        # get_hierarchy paging/safety params (optional)
        if parent is not None:
            params["parent"] = parent
        if coerced_page_size is not None:
            params["pageSize"] = coerced_page_size
        if coerced_cursor is not None:
            params["cursor"] = coerced_cursor
        if coerced_max_nodes is not None:
            params["maxNodes"] = coerced_max_nodes
        if coerced_max_depth is not None:
            params["maxDepth"] = coerced_max_depth
        if coerced_max_children_per_node is not None:
            params["maxChildrenPerNode"] = coerced_max_children_per_node
        if coerced_include_transform is not None:
            params["includeTransform"] = coerced_include_transform

        # Use centralized retry helper with instance routing
        response = await send_with_unity_instance(async_send_command_with_retry, unity_instance, "manage_scene", params)

        # Preserve structured failure data; unwrap success into a friendlier shape
        if isinstance(response, dict) and response.get("success"):
            friendly = {"success": True, "message": response.get("message", "Scene operation successful."), "data": response.get("data")}

            # For screenshot actions, check if inline images should be returned as ImageContent
            image_result = _extract_images(response, action)
            if image_result is not None:
                return image_result

            return friendly
        return response if isinstance(response, dict) else {"success": False, "message": str(response)}

    except Exception as e:
        return {"success": False, "message": f"Python error managing scene: {str(e)}"}
