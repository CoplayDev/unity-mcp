"""Camera CLI commands for managing Unity Camera + Cinemachine."""

import click
from typing import Optional, Any

from cli.utils.config import get_config
from cli.utils.output import format_output, print_error, print_success
from cli.utils.connection import run_command, handle_unity_errors
from cli.utils.parsers import parse_json_dict_or_exit
from cli.utils.constants import SEARCH_METHOD_CHOICE_BASIC


_CAM_TOP_LEVEL_KEYS = {"action", "target", "searchMethod", "properties"}


def _normalize_cam_params(params: dict[str, Any]) -> dict[str, Any]:
    params = dict(params)
    properties: dict[str, Any] = {}
    for key in list(params.keys()):
        if key in _CAM_TOP_LEVEL_KEYS:
            continue
        properties[key] = params.pop(key)

    if properties:
        existing = params.get("properties")
        if isinstance(existing, dict):
            params["properties"] = {**properties, **existing}
        else:
            params["properties"] = properties

    return {k: v for k, v in params.items() if v is not None}


@click.group()
def camera():
    """Camera operations - create, configure, and control cameras."""
    pass


# =============================================================================
# Setup
# =============================================================================

@camera.command("ping")
@handle_unity_errors
def ping():
    """Check if Cinemachine is available.

    \b
    Examples:
        unity-mcp camera ping
    """
    config = get_config()
    result = run_command(config, "manage_camera", {"action": "ping"})
    format_output(result, config)


@camera.command("list")
@handle_unity_errors
def list_cameras():
    """List all cameras in the scene.

    \b
    Examples:
        unity-mcp camera list
    """
    config = get_config()
    result = run_command(config, "manage_camera", {"action": "list_cameras"})
    format_output(result, config)


@camera.command("brain-status")
@handle_unity_errors
def brain_status():
    """Get CinemachineBrain status.

    \b
    Examples:
        unity-mcp camera brain-status
    """
    config = get_config()
    result = run_command(config, "manage_camera", {"action": "get_brain_status"})
    format_output(result, config)


# =============================================================================
# Creation
# =============================================================================

@camera.command("create")
@click.option("--name", "-n", default=None, help="Name for the camera GameObject.")
@click.option("--preset", "-p", default=None,
              type=click.Choice(["follow", "third_person", "freelook", "dolly",
                                 "static", "top_down", "side_scroller"]),
              help="Camera preset (Cinemachine only).")
@click.option("--follow", default=None, help="Follow target (name/path/ID).")
@click.option("--look-at", default=None, help="LookAt target (name/path/ID).")
@click.option("--priority", type=int, default=None, help="Camera priority.")
@click.option("--fov", type=float, default=None, help="Field of view.")
@handle_unity_errors
def create(name, preset, follow, look_at, priority, fov):
    """Create a new camera.

    \b
    Examples:
        unity-mcp camera create --name "FollowCam" --preset third_person --follow Player
        unity-mcp camera create --name "MainCam" --fov 50
    """
    config = get_config()
    props: dict[str, Any] = {}
    if name:
        props["name"] = name
    if preset:
        props["preset"] = preset
    if follow:
        props["follow"] = follow
    if look_at:
        props["lookAt"] = look_at
    if priority is not None:
        props["priority"] = priority
    if fov is not None:
        props["fieldOfView"] = fov

    params: dict[str, Any] = {"action": "create_camera"}
    if props:
        params["properties"] = props

    result = run_command(config, "manage_camera", params)
    format_output(result, config)


@camera.command("ensure-brain")
@click.option("--camera-ref", default=None, help="Camera to add Brain to (name/path/ID).")
@click.option("--blend-style", default=None, help="Default blend style.")
@click.option("--blend-duration", type=float, default=None, help="Default blend duration.")
@handle_unity_errors
def ensure_brain(camera_ref, blend_style, blend_duration):
    """Ensure CinemachineBrain exists on main camera.

    \b
    Examples:
        unity-mcp camera ensure-brain
        unity-mcp camera ensure-brain --blend-style EaseInOut --blend-duration 2.0
    """
    config = get_config()
    props: dict[str, Any] = {}
    if camera_ref:
        props["camera"] = camera_ref
    if blend_style:
        props["defaultBlendStyle"] = blend_style
    if blend_duration is not None:
        props["defaultBlendDuration"] = blend_duration

    params: dict[str, Any] = {"action": "ensure_brain"}
    if props:
        params["properties"] = props

    result = run_command(config, "manage_camera", params)
    format_output(result, config)


# =============================================================================
# Configuration
# =============================================================================

@camera.command("set-target")
@click.argument("target")
@click.option("--search-method", "-s", type=SEARCH_METHOD_CHOICE_BASIC, default=None)
@click.option("--follow", default=None, help="Follow target (name/path/ID).")
@click.option("--look-at", default=None, help="LookAt target (name/path/ID).")
@handle_unity_errors
def set_target(target, search_method, follow, look_at):
    """Set camera Follow/LookAt targets.

    \b
    Examples:
        unity-mcp camera set-target "CM Camera" --follow Player --look-at Player
    """
    config = get_config()
    props: dict[str, Any] = {}
    if follow:
        props["follow"] = follow
    if look_at:
        props["lookAt"] = look_at

    params = _normalize_cam_params({
        "action": "set_target",
        "target": target,
        "searchMethod": search_method,
        "properties": props if props else None,
    })
    result = run_command(config, "manage_camera", params)
    format_output(result, config)


@camera.command("set-lens")
@click.argument("target")
@click.option("--search-method", "-s", type=SEARCH_METHOD_CHOICE_BASIC, default=None)
@click.option("--fov", type=float, default=None, help="Field of view.")
@click.option("--near", type=float, default=None, help="Near clip plane.")
@click.option("--far", type=float, default=None, help="Far clip plane.")
@click.option("--ortho-size", type=float, default=None, help="Orthographic size.")
@click.option("--dutch", type=float, default=None, help="Dutch angle (Cinemachine).")
@handle_unity_errors
def set_lens(target, search_method, fov, near, far, ortho_size, dutch):
    """Set camera lens properties.

    \b
    Examples:
        unity-mcp camera set-lens "CM Camera" --fov 40 --near 0.1
    """
    config = get_config()
    props: dict[str, Any] = {}
    if fov is not None:
        props["fieldOfView"] = fov
    if near is not None:
        props["nearClipPlane"] = near
    if far is not None:
        props["farClipPlane"] = far
    if ortho_size is not None:
        props["orthographicSize"] = ortho_size
    if dutch is not None:
        props["dutch"] = dutch

    params = _normalize_cam_params({
        "action": "set_lens",
        "target": target,
        "searchMethod": search_method,
        "properties": props if props else None,
    })
    result = run_command(config, "manage_camera", params)
    format_output(result, config)


@camera.command("set-priority")
@click.argument("target")
@click.option("--search-method", "-s", type=SEARCH_METHOD_CHOICE_BASIC, default=None)
@click.option("--priority", "-p", type=int, required=True, help="Priority value.")
@handle_unity_errors
def set_priority(target, search_method, priority):
    """Set camera priority.

    \b
    Examples:
        unity-mcp camera set-priority "CM Camera" --priority 20
    """
    config = get_config()
    params = _normalize_cam_params({
        "action": "set_priority",
        "target": target,
        "searchMethod": search_method,
        "properties": {"priority": priority},
    })
    result = run_command(config, "manage_camera", params)
    format_output(result, config)


# =============================================================================
# Cinemachine Pipeline
# =============================================================================

@camera.command("set-body")
@click.argument("target")
@click.option("--search-method", "-s", type=SEARCH_METHOD_CHOICE_BASIC, default=None)
@click.option("--body-type", default=None, help="Body component type to swap to.")
@click.option("--props", default=None, help="Body properties as JSON.")
@handle_unity_errors
def set_body(target, search_method, body_type, props):
    """Configure Body component on CinemachineCamera.

    \b
    Examples:
        unity-mcp camera set-body "CM Camera" --body-type CinemachineFollow
        unity-mcp camera set-body "CM Camera" --props '{"cameraDistance": 5.0}'
    """
    config = get_config()
    properties: dict[str, Any] = {}
    if body_type:
        properties["bodyType"] = body_type
    if props:
        properties.update(parse_json_dict_or_exit(props))

    params = _normalize_cam_params({
        "action": "set_body",
        "target": target,
        "searchMethod": search_method,
        "properties": properties if properties else None,
    })
    result = run_command(config, "manage_camera", params)
    format_output(result, config)


@camera.command("set-aim")
@click.argument("target")
@click.option("--search-method", "-s", type=SEARCH_METHOD_CHOICE_BASIC, default=None)
@click.option("--aim-type", default=None, help="Aim component type to swap to.")
@click.option("--props", default=None, help="Aim properties as JSON.")
@handle_unity_errors
def set_aim(target, search_method, aim_type, props):
    """Configure Aim component on CinemachineCamera.

    \b
    Examples:
        unity-mcp camera set-aim "CM Camera" --aim-type CinemachineHardLookAt
    """
    config = get_config()
    properties: dict[str, Any] = {}
    if aim_type:
        properties["aimType"] = aim_type
    if props:
        properties.update(parse_json_dict_or_exit(props))

    params = _normalize_cam_params({
        "action": "set_aim",
        "target": target,
        "searchMethod": search_method,
        "properties": properties if properties else None,
    })
    result = run_command(config, "manage_camera", params)
    format_output(result, config)


@camera.command("set-noise")
@click.argument("target")
@click.option("--search-method", "-s", type=SEARCH_METHOD_CHOICE_BASIC, default=None)
@click.option("--amplitude", type=float, default=None, help="Amplitude gain.")
@click.option("--frequency", type=float, default=None, help="Frequency gain.")
@handle_unity_errors
def set_noise(target, search_method, amplitude, frequency):
    """Configure Noise on CinemachineCamera.

    \b
    Examples:
        unity-mcp camera set-noise "CM Camera" --amplitude 0.5 --frequency 1.0
    """
    config = get_config()
    props: dict[str, Any] = {}
    if amplitude is not None:
        props["amplitudeGain"] = amplitude
    if frequency is not None:
        props["frequencyGain"] = frequency

    params = _normalize_cam_params({
        "action": "set_noise",
        "target": target,
        "searchMethod": search_method,
        "properties": props if props else None,
    })
    result = run_command(config, "manage_camera", params)
    format_output(result, config)


# =============================================================================
# Extensions
# =============================================================================

@camera.command("add-extension")
@click.argument("target")
@click.argument("extension_type")
@click.option("--search-method", "-s", type=SEARCH_METHOD_CHOICE_BASIC, default=None)
@click.option("--props", default=None, help="Extension properties as JSON.")
@handle_unity_errors
def add_extension(target, extension_type, search_method, props):
    """Add extension to CinemachineCamera.

    \b
    Examples:
        unity-mcp camera add-extension "CM Camera" CinemachineDeoccluder
        unity-mcp camera add-extension "CM Camera" CinemachineImpulseListener
    """
    config = get_config()
    properties: dict[str, Any] = {"extensionType": extension_type}
    if props:
        properties.update(parse_json_dict_or_exit(props))

    params = _normalize_cam_params({
        "action": "add_extension",
        "target": target,
        "searchMethod": search_method,
        "properties": properties,
    })
    result = run_command(config, "manage_camera", params)
    format_output(result, config)


@camera.command("remove-extension")
@click.argument("target")
@click.argument("extension_type")
@click.option("--search-method", "-s", type=SEARCH_METHOD_CHOICE_BASIC, default=None)
@handle_unity_errors
def remove_extension(target, extension_type, search_method):
    """Remove extension from CinemachineCamera.

    \b
    Examples:
        unity-mcp camera remove-extension "CM Camera" CinemachineDeoccluder
    """
    config = get_config()
    params = _normalize_cam_params({
        "action": "remove_extension",
        "target": target,
        "searchMethod": search_method,
        "properties": {"extensionType": extension_type},
    })
    result = run_command(config, "manage_camera", params)
    format_output(result, config)


# =============================================================================
# Control
# =============================================================================

@camera.command("set-blend")
@click.option("--style", default=None, help="Blend style (Cut, EaseInOut, Linear, etc.).")
@click.option("--duration", type=float, default=None, help="Blend duration in seconds.")
@handle_unity_errors
def set_blend(style, duration):
    """Configure default blend on CinemachineBrain.

    \b
    Examples:
        unity-mcp camera set-blend --style EaseInOut --duration 2.0
    """
    config = get_config()
    props: dict[str, Any] = {}
    if style:
        props["style"] = style
    if duration is not None:
        props["duration"] = duration

    params: dict[str, Any] = {"action": "set_blend"}
    if props:
        params["properties"] = props

    result = run_command(config, "manage_camera", params)
    format_output(result, config)


@camera.command("force")
@click.argument("target")
@click.option("--search-method", "-s", type=SEARCH_METHOD_CHOICE_BASIC, default=None)
@handle_unity_errors
def force_camera(target, search_method):
    """Force Brain to use a specific camera.

    \b
    Examples:
        unity-mcp camera force "CM Cinematic"
    """
    config = get_config()
    params = _normalize_cam_params({
        "action": "force_camera",
        "target": target,
        "searchMethod": search_method,
    })
    result = run_command(config, "manage_camera", params)
    format_output(result, config)


@camera.command("release")
@handle_unity_errors
def release_override():
    """Release camera override.

    \b
    Examples:
        unity-mcp camera release
    """
    config = get_config()
    result = run_command(config, "manage_camera", {"action": "release_override"})
    format_output(result, config)


# =============================================================================
# Capture
# =============================================================================

@camera.command("screenshot")
@click.option("--camera-ref", default=None, help="Camera to capture from (name/path/ID).")
@click.option("--file-name", default=None, help="Output file name.")
@click.option("--super-size", type=int, default=None, help="Supersize multiplier.")
@click.option("--include-image/--no-include-image", default=None, help="Return inline base64 PNG.")
@click.option("--max-resolution", type=int, default=None, help="Max resolution for inline image.")
@click.option("--capture-source", default=None,
              type=click.Choice(["game_view", "scene_view"], case_sensitive=False),
              help="Capture source: game_view (default) or scene_view.")
@click.option("--batch", default=None, type=click.Choice(["surround", "orbit"]),
              help="Batch capture mode.")
@click.option("--look-at", default=None, help="Target to aim at (name/path/ID or [x,y,z]).")
@click.option("--scene-view-target", default=None,
              help="Target to frame before capture when using --capture-source scene_view.")
@handle_unity_errors
def screenshot(camera_ref, file_name, super_size, include_image, max_resolution, capture_source, batch, look_at, scene_view_target):
    """Capture a screenshot from a camera.

    \b
    Examples:
        unity-mcp camera screenshot
        unity-mcp camera screenshot --camera-ref "CM FollowCam" --include-image --max-resolution 512
        unity-mcp camera screenshot --capture-source scene_view --scene-view-target Canvas --include-image
        unity-mcp camera screenshot --batch surround --look-at Player
    """
    config = get_config()
    params: dict[str, Any] = {"action": "screenshot"}
    if camera_ref:
        params["camera"] = camera_ref
    if file_name:
        params["fileName"] = file_name
    if super_size is not None:
        params["superSize"] = super_size
    if include_image is not None:
        params["includeImage"] = include_image
    if max_resolution is not None:
        params["maxResolution"] = max_resolution
    if capture_source:
        params["captureSource"] = capture_source
    if batch:
        params["batch"] = batch
    if look_at:
        params["lookAt"] = look_at
    if scene_view_target:
        params["sceneViewTarget"] = scene_view_target
    result = run_command(config, "manage_camera", params)
    format_output(result, config)


@camera.command("screenshot-multiview")
@click.option("--max-resolution", type=int, default=None, help="Max resolution per tile.")
@click.option("--look-at", default=None, help="Center target for the multiview capture.")
@handle_unity_errors
def screenshot_multiview(max_resolution, look_at):
    """Capture a 6-angle contact sheet around the scene.

    \b
    Examples:
        unity-mcp camera screenshot-multiview
        unity-mcp camera screenshot-multiview --look-at Player --max-resolution 480
    """
    config = get_config()
    params: dict[str, Any] = {"action": "screenshot_multiview"}
    if max_resolution is not None:
        params["maxResolution"] = max_resolution
    if look_at:
        params["lookAt"] = look_at
    result = run_command(config, "manage_camera", params)
    format_output(result, config)
