"""VFX CLI commands for managing Unity visual effects."""

import sys
import json
import click
from typing import Optional, Tuple, Any

from cli.utils.config import get_config
from cli.utils.output import format_output, print_error, print_success
from cli.utils.connection import run_command, handle_unity_errors
from cli.utils.parsers import parse_json_list_or_exit, parse_json_dict_or_exit
from cli.utils.constants import SEARCH_METHOD_CHOICE_TAGGED


_VFX_TOP_LEVEL_KEYS = {"action", "target", "searchMethod", "properties"}


def _normalize_vfx_params(params: dict[str, Any]) -> dict[str, Any]:
    params = dict(params)
    properties: dict[str, Any] = {}
    for key in list(params.keys()):
        if key in _VFX_TOP_LEVEL_KEYS:
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
def vfx():
    """VFX operations - particle systems, line renderers, trails."""
    pass


# =============================================================================
# Particle System Commands
# =============================================================================

@vfx.group()
def particle():
    """Particle system operations."""
    pass


@particle.command("info")
@click.argument("target")
@click.option("--search-method", type=SEARCH_METHOD_CHOICE_TAGGED, default=None)
@handle_unity_errors
def particle_info(target: str, search_method: Optional[str]):
    """Get particle system info.

    \\b
    Examples:
        unity-mcp vfx particle info "Fire"
        unity-mcp vfx particle info "-12345" --search-method by_id
    """
    config = get_config()
    params: dict[str, Any] = {"action": "particle_get_info", "target": target}
    if search_method:
        params["searchMethod"] = search_method

    result = run_command(
        "manage_vfx", _normalize_vfx_params(params), config)
    click.echo(format_output(result, config.format))


@particle.command("play")
@click.argument("target")
@click.option("--with-children", is_flag=True, help="Also play child particle systems.")
@click.option("--search-method", type=SEARCH_METHOD_CHOICE_TAGGED, default=None)
@handle_unity_errors
def particle_play(target: str, with_children: bool, search_method: Optional[str]):
    """Play a particle system.

    \\b
    Examples:
        unity-mcp vfx particle play "Fire"
        unity-mcp vfx particle play "Effects" --with-children
    """
    config = get_config()
    params: dict[str, Any] = {"action": "particle_play", "target": target}
    if with_children:
        params["withChildren"] = True
    if search_method:
        params["searchMethod"] = search_method

    result = run_command(
        "manage_vfx", _normalize_vfx_params(params), config)
    click.echo(format_output(result, config.format))
    if result.get("success"):
        print_success(f"Playing particle system: {target}")


@particle.command("stop")
@click.argument("target")
@click.option("--with-children", is_flag=True, help="Also stop child particle systems.")
@click.option("--search-method", type=SEARCH_METHOD_CHOICE_TAGGED, default=None)
@handle_unity_errors
def particle_stop(target: str, with_children: bool, search_method: Optional[str]):
    """Stop a particle system."""
    config = get_config()
    params: dict[str, Any] = {"action": "particle_stop", "target": target}
    if with_children:
        params["withChildren"] = True
    if search_method:
        params["searchMethod"] = search_method

    result = run_command(
        "manage_vfx", _normalize_vfx_params(params), config)
    click.echo(format_output(result, config.format))
    if result.get("success"):
        print_success(f"Stopped particle system: {target}")


@particle.command("pause")
@click.argument("target")
@click.option("--search-method", type=SEARCH_METHOD_CHOICE_TAGGED, default=None)
@handle_unity_errors
def particle_pause(target: str, search_method: Optional[str]):
    """Pause a particle system."""
    config = get_config()
    params: dict[str, Any] = {"action": "particle_pause", "target": target}
    if search_method:
        params["searchMethod"] = search_method

    result = run_command(
        "manage_vfx", _normalize_vfx_params(params), config)
    click.echo(format_output(result, config.format))


@particle.command("restart")
@click.argument("target")
@click.option("--with-children", is_flag=True)
@click.option("--search-method", type=SEARCH_METHOD_CHOICE_TAGGED, default=None)
@handle_unity_errors
def particle_restart(target: str, with_children: bool, search_method: Optional[str]):
    """Restart a particle system."""
    config = get_config()
    params: dict[str, Any] = {"action": "particle_restart", "target": target}
    if with_children:
        params["withChildren"] = True
    if search_method:
        params["searchMethod"] = search_method

    result = run_command(
        "manage_vfx", _normalize_vfx_params(params), config)
    click.echo(format_output(result, config.format))


@particle.command("clear")
@click.argument("target")
@click.option("--with-children", is_flag=True)
@click.option("--search-method", type=SEARCH_METHOD_CHOICE_TAGGED, default=None)
@handle_unity_errors
def particle_clear(target: str, with_children: bool, search_method: Optional[str]):
    """Clear all particles from a particle system."""
    config = get_config()
    params: dict[str, Any] = {"action": "particle_clear", "target": target}
    if with_children:
        params["withChildren"] = True
    if search_method:
        params["searchMethod"] = search_method

    result = run_command(
        "manage_vfx", _normalize_vfx_params(params), config)
    click.echo(format_output(result, config.format))


# =============================================================================
# Line Renderer Commands
# =============================================================================

@vfx.group()
def line():
    """Line renderer operations."""
    pass


@line.command("info")
@click.argument("target")
@click.option("--search-method", type=SEARCH_METHOD_CHOICE_TAGGED, default=None)
@handle_unity_errors
def line_info(target: str, search_method: Optional[str]):
    """Get line renderer info.

    \\b
    Examples:
        unity-mcp vfx line info "LaserBeam"
    """
    config = get_config()
    params: dict[str, Any] = {"action": "line_get_info", "target": target}
    if search_method:
        params["searchMethod"] = search_method

    result = run_command(
        "manage_vfx", _normalize_vfx_params(params), config)
    click.echo(format_output(result, config.format))


@line.command("set-positions")
@click.argument("target")
@click.option("--positions", "-p", required=True, help='Positions as JSON array: [[0,0,0], [1,1,1], [2,0,0]]')
@click.option("--search-method", type=SEARCH_METHOD_CHOICE_TAGGED, default=None)
@handle_unity_errors
def line_set_positions(target: str, positions: str, search_method: Optional[str]):
    """Set all positions on a line renderer.

    \\b
    Examples:
        unity-mcp vfx line set-positions "Line" --positions "[[0,0,0], [5,2,0], [10,0,0]]"
    """
    config = get_config()

    positions_list = parse_json_list_or_exit(positions, "positions")

    params: dict[str, Any] = {
        "action": "line_set_positions",
        "target": target,
        "positions": positions_list,
    }
    if search_method:
        params["searchMethod"] = search_method

    result = run_command(
        "manage_vfx", _normalize_vfx_params(params), config)
    click.echo(format_output(result, config.format))


@line.command("create-line")
@click.argument("target")
@click.option("--start", nargs=3, type=float, required=True, help="Start point X Y Z")
@click.option("--end", nargs=3, type=float, required=True, help="End point X Y Z")
@click.option("--search-method", type=SEARCH_METHOD_CHOICE_TAGGED, default=None)
@handle_unity_errors
def line_create_line(target: str, start: Tuple[float, float, float], end: Tuple[float, float, float], search_method: Optional[str]):
    """Create a simple line between two points.

    \\b
    Examples:
        unity-mcp vfx line create-line "MyLine" --start 0 0 0 --end 10 5 0
    """
    config = get_config()
    params: dict[str, Any] = {
        "action": "line_create_line",
        "target": target,
        "start": list(start),
        "end": list(end),
    }
    if search_method:
        params["searchMethod"] = search_method

    result = run_command(
        "manage_vfx", _normalize_vfx_params(params), config)
    click.echo(format_output(result, config.format))


@line.command("create-circle")
@click.argument("target")
@click.option("--center", nargs=3, type=float, default=(0, 0, 0), help="Center point X Y Z")
@click.option("--radius", type=float, required=True, help="Circle radius")
@click.option("--segments", type=int, default=32, help="Number of segments")
@click.option("--search-method", type=SEARCH_METHOD_CHOICE_TAGGED, default=None)
@handle_unity_errors
def line_create_circle(target: str, center: Tuple[float, float, float], radius: float, segments: int, search_method: Optional[str]):
    """Create a circle shape.

    \\b
    Examples:
        unity-mcp vfx line create-circle "Circle" --radius 5 --segments 64
        unity-mcp vfx line create-circle "Ring" --center 0 2 0 --radius 3
    """
    config = get_config()
    params: dict[str, Any] = {
        "action": "line_create_circle",
        "target": target,
        "center": list(center),
        "radius": radius,
        "segments": segments,
    }
    if search_method:
        params["searchMethod"] = search_method

    result = run_command(
        "manage_vfx", _normalize_vfx_params(params), config)
    click.echo(format_output(result, config.format))


@line.command("clear")
@click.argument("target")
@click.option("--search-method", type=SEARCH_METHOD_CHOICE_TAGGED, default=None)
@handle_unity_errors
def line_clear(target: str, search_method: Optional[str]):
    """Clear all positions from a line renderer."""
    config = get_config()
    params: dict[str, Any] = {"action": "line_clear", "target": target}
    if search_method:
        params["searchMethod"] = search_method

    result = run_command(
        "manage_vfx", _normalize_vfx_params(params), config)
    click.echo(format_output(result, config.format))


# =============================================================================
# Trail Renderer Commands
# =============================================================================

@vfx.group()
def trail():
    """Trail renderer operations."""
    pass


@trail.command("info")
@click.argument("target")
@click.option("--search-method", type=SEARCH_METHOD_CHOICE_TAGGED, default=None)
@handle_unity_errors
def trail_info(target: str, search_method: Optional[str]):
    """Get trail renderer info."""
    config = get_config()
    params: dict[str, Any] = {"action": "trail_get_info", "target": target}
    if search_method:
        params["searchMethod"] = search_method

    result = run_command(
        "manage_vfx", _normalize_vfx_params(params), config)
    click.echo(format_output(result, config.format))


@trail.command("set-time")
@click.argument("target")
@click.argument("duration", type=float)
@click.option("--search-method", type=SEARCH_METHOD_CHOICE_TAGGED, default=None)
@handle_unity_errors
def trail_set_time(target: str, duration: float, search_method: Optional[str]):
    """Set trail duration.

    \\b
    Examples:
        unity-mcp vfx trail set-time "PlayerTrail" 2.0
    """
    config = get_config()
    params: dict[str, Any] = {
        "action": "trail_set_time",
        "target": target,
        "time": duration,
    }
    if search_method:
        params["searchMethod"] = search_method

    result = run_command(
        "manage_vfx", _normalize_vfx_params(params), config)
    click.echo(format_output(result, config.format))


@trail.command("clear")
@click.argument("target")
@click.option("--search-method", type=SEARCH_METHOD_CHOICE_TAGGED, default=None)
@handle_unity_errors
def trail_clear(target: str, search_method: Optional[str]):
    """Clear a trail renderer."""
    config = get_config()
    params: dict[str, Any] = {"action": "trail_clear", "target": target}
    if search_method:
        params["searchMethod"] = search_method

    result = run_command(
        "manage_vfx", _normalize_vfx_params(params), config)
    click.echo(format_output(result, config.format))


# =============================================================================
# Raw Command (escape hatch for all VFX actions)
# =============================================================================

@vfx.command("raw")
@click.argument("action")
@click.argument("target", required=False)
@click.option("--params", "-p", default="{}", help="Additional parameters as JSON.")
@click.option("--search-method", type=SEARCH_METHOD_CHOICE_TAGGED, default=None)
@handle_unity_errors
def vfx_raw(action: str, target: Optional[str], params: str, search_method: Optional[str]):
    """Execute any VFX action directly.

    For advanced users who need access to all 60+ VFX actions.

    \\b
    Actions include:
        particle_*: particle_set_main, particle_set_emission, particle_set_shape, ...
        vfx_*: vfx_set_float, vfx_send_event, vfx_play, ...
        line_*: line_create_arc, line_create_bezier, ...
        trail_*: trail_set_width, trail_set_color, ...

    \\b
    Examples:
        unity-mcp vfx raw particle_set_main "Fire" --params '{"duration": 5, "looping": true}'
        unity-mcp vfx raw line_create_arc "Arc" --params '{"radius": 3, "startAngle": 0, "endAngle": 180}'
        unity-mcp vfx raw vfx_send_event "Explosion" --params '{"eventName": "OnSpawn"}'
    """
    config = get_config()

    extra_params = parse_json_dict_or_exit(params, "params")

    request_params: dict[str, Any] = {"action": action}
    if target:
        request_params["target"] = target
    if search_method:
        request_params["searchMethod"] = search_method

    # Merge extra params
    request_params.update(extra_params)
    result = run_command(
        "manage_vfx", _normalize_vfx_params(request_params), config)
    click.echo(format_output(result, config.format))
