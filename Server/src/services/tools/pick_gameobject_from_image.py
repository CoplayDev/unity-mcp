from __future__ import annotations

import math
from typing import Annotated, Any, Literal

from fastmcp import Context
from mcp.types import ToolAnnotations

from services.registry import mcp_for_unity_tool
from services.tools import get_unity_instance_from_context
from services.tools.utils import coerce_float, coerce_int, parse_json_payload
from transport.legacy.unity_connection import async_send_command_with_retry
from transport.unity_transport import send_with_unity_instance


DESCRIPTION = (
    "Pick the Unity GameObject visible at an image coordinate from a Unity screenshot. "
    "Read-only. Preconditions: this only works for Unity screenshots, not arbitrary external images. "
    "The caller must pass the screenshot-time pick_view returned by supported screenshot captures, or pass "
    "a camera reference only when that Unity Camera still has the same transform/projection it had at capture time. "
    "Supported in this first version: screenshots captured with an explicit camera, screenshots captured with "
    "view_position/view_target, and screenshots captured with capture_source='scene_view'. "
    "Not supported in this first version: default Game View screenshots with no camera, screenshot_multiview, "
    "batch='surround', batch='orbit', and whole-contact-sheet coordinates. "
    "For capture_source='scene_view', Unity Editor picking is tried first, then an Editor mesh-intersection "
    "fallback can return visible MeshRenderer/SkinnedMeshRenderer objects without Colliders; if both miss, "
    "the tool falls back to physics picking. "
    "For camera, game_view, and view_position/view_target captures, picking uses 3D Collider or 2D Collider2D "
    "ray queries. If layer_mask is omitted, picking defaults to the screenshot camera culling mask when available. "
    "UI GraphicRaycaster, non-selectable Scene View visuals, Renderer-only objects in non-Scene View captures, "
    "and non-Unity images are not supported."
)


def _finite_float(value: Any, name: str) -> tuple[float | None, str | None]:
    parsed = coerce_float(value)
    if parsed is None or not math.isfinite(parsed):
        return None, f"{name} must be a finite number."
    return parsed, None


def _positive_int(value: Any, name: str) -> tuple[int | None, str | None]:
    parsed = coerce_int(value)
    if parsed is None or parsed <= 0:
        return None, f"{name} must be a positive integer."
    return parsed, None


def _positive_float(value: Any, name: str) -> tuple[float | None, str | None]:
    parsed = coerce_float(value)
    if parsed is None or not math.isfinite(parsed) or parsed <= 0:
        return None, f"{name} must be a positive finite number."
    return parsed, None


@mcp_for_unity_tool(
    group="core",
    description=DESCRIPTION,
    annotations=ToolAnnotations(title="Pick GameObject From Image", destructiveHint=False),
)
async def pick_gameobject_from_image(
    ctx: Context,
    image_x: Annotated[float | int | str, "X coordinate in the screenshot image, origin at top-left, increasing right."],
    image_y: Annotated[float | int | str, "Y coordinate in the screenshot image, origin at top-left, increasing down."],
    image_width: Annotated[int | str, "Width of the image the AI is inspecting, in pixels."],
    image_height: Annotated[int | str, "Height of the image the AI is inspecting, in pixels."],
    scale_x: Annotated[float | int | str | None, "Scale from inspected image X pixels back to the captured Unity viewport pixels. Default 1."] = None,
    scale_y: Annotated[float | int | str | None, "Scale from inspected image Y pixels back to the captured Unity viewport pixels. Defaults to scale_x."] = None,
    viewport_width: Annotated[int | str | None, "Optional original captured Unity viewport width. Use pick_view.viewportWidth when present."] = None,
    viewport_height: Annotated[int | str | None, "Optional original captured Unity viewport height. Use pick_view.viewportHeight when present."] = None,
    dimension: Annotated[Literal["3d", "2d"] | str | None, "Physics dimension to query: '3d' for Collider, '2d' for Collider2D."] = "3d",
    camera: Annotated[str | None, "Camera name, path, or instance ID. Use only if the camera has not changed since capture."] = None,
    pick_view: Annotated[dict[str, Any] | str | None, "Screenshot-time pickView object returned by supported Unity screenshots. Preferred over camera."] = None,
    layer_mask: Annotated[str | int | None, "Layer mask as integer mask or layer name(s). Optional."] = None,
    max_distance: Annotated[float | int | str | None, "Maximum ray distance. Default infinity."] = None,
    query_trigger_interaction: Annotated[str | None, "3D trigger policy: UseGlobal, Ignore, or Collide."] = None,
) -> dict[str, Any]:
    """Pick a GameObject from a Unity screenshot coordinate."""

    x, error = _finite_float(image_x, "image_x")
    if error:
        return {"success": False, "message": error}
    y, error = _finite_float(image_y, "image_y")
    if error:
        return {"success": False, "message": error}
    width, error = _positive_int(image_width, "image_width")
    if error:
        return {"success": False, "message": error}
    height, error = _positive_int(image_height, "image_height")
    if error:
        return {"success": False, "message": error}

    if x < 0 or x >= width:
        return {"success": False, "message": "image_x must be within [0, image_width)."}
    if y < 0 or y >= height:
        return {"success": False, "message": "image_y must be within [0, image_height)."}

    sx = 1.0
    if scale_x is not None:
        sx, error = _positive_float(scale_x, "scale_x")
        if error:
            return {"success": False, "message": error}

    sy = sx
    if scale_y is not None:
        sy, error = _positive_float(scale_y, "scale_y")
        if error:
            return {"success": False, "message": error}

    vw = None
    if viewport_width is not None:
        vw, error = _positive_int(viewport_width, "viewport_width")
        if error:
            return {"success": False, "message": error}

    vh = None
    if viewport_height is not None:
        vh, error = _positive_int(viewport_height, "viewport_height")
        if error:
            return {"success": False, "message": error}

    dim = (dimension or "3d").lower()
    if dim not in {"3d", "2d"}:
        return {"success": False, "message": "dimension must be '3d' or '2d'."}

    parsed_pick_view = parse_json_payload(pick_view)
    if isinstance(parsed_pick_view, str):
        return {"success": False, "message": "pick_view must be a JSON object when provided."}
    if parsed_pick_view is not None and not isinstance(parsed_pick_view, dict):
        return {"success": False, "message": "pick_view must be a JSON object when provided."}

    camera_ref = camera.strip() if isinstance(camera, str) else camera
    if parsed_pick_view is None and not camera_ref:
        return {
            "success": False,
            "message": "Provide either pick_view from the screenshot response or a still-unchanged camera reference.",
        }

    max_dist = None
    if max_distance is not None:
        max_dist, error = _positive_float(max_distance, "max_distance")
        if error:
            return {"success": False, "message": error}

    qti = None
    if query_trigger_interaction is not None:
        qti_key = str(query_trigger_interaction).strip().lower()
        qti_values = {
            "useglobal": "UseGlobal",
            "ignore": "Ignore",
            "collide": "Collide",
        }
        qti = qti_values.get(qti_key)
        if qti is None:
            return {
                "success": False,
                "message": "query_trigger_interaction must be UseGlobal, Ignore, or Collide.",
            }

    params: dict[str, Any] = {
        "imageX": x,
        "imageY": y,
        "imageWidth": width,
        "imageHeight": height,
        "scaleX": sx,
        "scaleY": sy,
        "dimension": dim,
    }
    if vw is not None:
        params["viewportWidth"] = vw
    if vh is not None:
        params["viewportHeight"] = vh
    if parsed_pick_view is not None:
        params["pickView"] = parsed_pick_view
    if camera_ref:
        params["camera"] = camera_ref
    if layer_mask is not None:
        params["layerMask"] = layer_mask
    if max_dist is not None:
        params["maxDistance"] = max_dist
    if qti is not None:
        params["queryTriggerInteraction"] = qti

    unity_instance = await get_unity_instance_from_context(ctx)
    result = await send_with_unity_instance(
        async_send_command_with_retry,
        unity_instance,
        "pick_gameobject_from_image",
        params,
    )
    return result if isinstance(result, dict) else {"success": False, "message": str(result)}
