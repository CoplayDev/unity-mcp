"""Animation CLI commands - control Animator and manage AnimationClips."""

import json
import click
from typing import Optional, Any

from cli.utils.config import get_config
from cli.utils.output import format_output, print_error, print_success
from cli.utils.connection import run_command, handle_unity_errors
from cli.utils.parsers import parse_json_list_or_exit, parse_json_dict_or_exit, parse_value_safe
from cli.utils.constants import SEARCH_METHOD_CHOICE_BASIC


_TOP_LEVEL_KEYS = {"action", "target", "searchMethod", "clipPath", "controllerPath", "properties"}


def _normalize_params(params: dict[str, Any]) -> dict[str, Any]:
    params = dict(params)
    properties: dict[str, Any] = {}
    for key in list(params.keys()):
        if key in _TOP_LEVEL_KEYS:
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
def animation():
    """Animation operations - control Animator, manage AnimationClips."""
    pass


# =============================================================================
# Animator Commands
# =============================================================================

@animation.group()
def animator():
    """Animator component operations."""
    pass


@animator.command("info")
@click.argument("target")
@click.option("--search-method", type=SEARCH_METHOD_CHOICE_BASIC, default=None)
@handle_unity_errors
def animator_info(target: str, search_method: Optional[str]):
    """Get Animator state, parameters, clips, and layers.

    \b
    Examples:
        unity-mcp animation animator info "Player"
        unity-mcp animation animator info "-12345" --search-method by_id
    """
    config = get_config()
    params: dict[str, Any] = {"action": "animator_get_info", "target": target}
    if search_method:
        params["searchMethod"] = search_method

    result = run_command("manage_animation", _normalize_params(params), config)
    click.echo(format_output(result, config.format))


@animator.command("play")
@click.argument("target")
@click.argument("state_name")
@click.option("--layer", "-l", default=-1, type=int, help="Animator layer index (-1 for default).")
@click.option("--search-method", type=SEARCH_METHOD_CHOICE_BASIC, default=None)
@handle_unity_errors
def animator_play(target: str, state_name: str, layer: int, search_method: Optional[str]):
    """Play an animation state on a target's Animator.

    \b
    Examples:
        unity-mcp animation animator play "Player" "Walk"
        unity-mcp animation animator play "Enemy" "Attack" --layer 1
    """
    config = get_config()
    params: dict[str, Any] = {
        "action": "animator_play",
        "target": target,
        "stateName": state_name,
        "layer": layer,
    }
    if search_method:
        params["searchMethod"] = search_method

    result = run_command("manage_animation", _normalize_params(params), config)
    click.echo(format_output(result, config.format))
    if result.get("success"):
        print_success(f"Playing state '{state_name}' on {target}")


@animator.command("crossfade")
@click.argument("target")
@click.argument("state_name")
@click.option("--duration", "-d", default=0.25, type=float, help="Crossfade duration in seconds.")
@click.option("--layer", "-l", default=-1, type=int, help="Animator layer index (-1 for default).")
@click.option("--search-method", type=SEARCH_METHOD_CHOICE_BASIC, default=None)
@handle_unity_errors
def animator_crossfade(target: str, state_name: str, duration: float, layer: int, search_method: Optional[str]):
    """Crossfade to an animation state.

    \b
    Examples:
        unity-mcp animation animator crossfade "Player" "Run" --duration 0.5
    """
    config = get_config()
    params: dict[str, Any] = {
        "action": "animator_crossfade",
        "target": target,
        "stateName": state_name,
        "duration": duration,
        "layer": layer,
    }
    if search_method:
        params["searchMethod"] = search_method

    result = run_command("manage_animation", _normalize_params(params), config)
    click.echo(format_output(result, config.format))


@animator.command("set-parameter")
@click.argument("target")
@click.argument("param_name")
@click.argument("value")
@click.option(
    "--type", "-t", "param_type",
    type=click.Choice(["float", "int", "bool", "trigger"]),
    default=None,
    help="Parameter type (auto-detected if omitted)."
)
@click.option("--search-method", type=SEARCH_METHOD_CHOICE_BASIC, default=None)
@handle_unity_errors
def animator_set_parameter(target: str, param_name: str, value: str, param_type: Optional[str], search_method: Optional[str]):
    """Set an Animator parameter.

    \b
    Examples:
        unity-mcp animation animator set-parameter "Player" "Speed" 5.0
        unity-mcp animation animator set-parameter "Player" "IsRunning" true --type bool
        unity-mcp animation animator set-parameter "Player" "Jump" "" --type trigger
    """
    config = get_config()
    params: dict[str, Any] = {
        "action": "animator_set_parameter",
        "target": target,
        "parameterName": param_name,
        "value": parse_value_safe(value),
    }
    if param_type:
        params["parameterType"] = param_type
    if search_method:
        params["searchMethod"] = search_method

    result = run_command("manage_animation", _normalize_params(params), config)
    click.echo(format_output(result, config.format))


@animator.command("get-parameter")
@click.argument("target")
@click.argument("param_name")
@click.option("--search-method", type=SEARCH_METHOD_CHOICE_BASIC, default=None)
@handle_unity_errors
def animator_get_parameter(target: str, param_name: str, search_method: Optional[str]):
    """Get the current value of an Animator parameter.

    \b
    Examples:
        unity-mcp animation animator get-parameter "Player" "Speed"
    """
    config = get_config()
    params: dict[str, Any] = {
        "action": "animator_get_parameter",
        "target": target,
        "parameterName": param_name,
    }
    if search_method:
        params["searchMethod"] = search_method

    result = run_command("manage_animation", _normalize_params(params), config)
    click.echo(format_output(result, config.format))


@animator.command("set-speed")
@click.argument("target")
@click.argument("speed", type=float)
@click.option("--search-method", type=SEARCH_METHOD_CHOICE_BASIC, default=None)
@handle_unity_errors
def animator_set_speed(target: str, speed: float, search_method: Optional[str]):
    """Set Animator playback speed.

    \b
    Examples:
        unity-mcp animation animator set-speed "Player" 2.0
        unity-mcp animation animator set-speed "Player" 0  # pause
    """
    config = get_config()
    params: dict[str, Any] = {
        "action": "animator_set_speed",
        "target": target,
        "speed": speed,
    }
    if search_method:
        params["searchMethod"] = search_method

    result = run_command("manage_animation", _normalize_params(params), config)
    click.echo(format_output(result, config.format))


@animator.command("set-enabled")
@click.argument("target")
@click.argument("enabled", type=bool)
@click.option("--search-method", type=SEARCH_METHOD_CHOICE_BASIC, default=None)
@handle_unity_errors
def animator_set_enabled(target: str, enabled: bool, search_method: Optional[str]):
    """Enable or disable an Animator component.

    \b
    Examples:
        unity-mcp animation animator set-enabled "Player" true
        unity-mcp animation animator set-enabled "Player" false
    """
    config = get_config()
    params: dict[str, Any] = {
        "action": "animator_set_enabled",
        "target": target,
        "enabled": enabled,
    }
    if search_method:
        params["searchMethod"] = search_method

    result = run_command("manage_animation", _normalize_params(params), config)
    click.echo(format_output(result, config.format))


# =============================================================================
# AnimationClip Commands
# =============================================================================

@animation.group()
def clip():
    """AnimationClip operations."""
    pass


@clip.command("create")
@click.argument("clip_path")
@click.option("--name", default=None, help="Clip name (defaults to filename).")
@click.option("--length", "-l", default=1.0, type=float, help="Clip length in seconds.")
@click.option("--loop/--no-loop", default=False, help="Whether clip loops.")
@click.option("--frame-rate", default=60.0, type=float, help="Frame rate.")
@handle_unity_errors
def clip_create(clip_path: str, name: Optional[str], length: float, loop: bool, frame_rate: float):
    """Create a new AnimationClip asset.

    \b
    Examples:
        unity-mcp animation clip create "Assets/Animations/Bounce.anim" --length 2.0 --loop
        unity-mcp animation clip create "Assets/Anim/Walk.anim" --frame-rate 30
    """
    config = get_config()
    params: dict[str, Any] = {
        "action": "clip_create",
        "clipPath": clip_path,
        "length": length,
        "loop": loop,
        "frameRate": frame_rate,
    }
    if name:
        params["name"] = name

    result = run_command("manage_animation", _normalize_params(params), config)
    click.echo(format_output(result, config.format))
    if result.get("success"):
        print_success(f"Created clip at {clip_path}")


@clip.command("info")
@click.argument("clip_path")
@handle_unity_errors
def clip_info(clip_path: str):
    """Get AnimationClip info (curves, length, events).

    \b
    Examples:
        unity-mcp animation clip info "Assets/Animations/Walk.anim"
    """
    config = get_config()
    params: dict[str, Any] = {
        "action": "clip_get_info",
        "clipPath": clip_path,
    }

    result = run_command("manage_animation", _normalize_params(params), config)
    click.echo(format_output(result, config.format))


@clip.command("add-curve")
@click.argument("clip_path")
@click.option("--property", "-p", "property_path", required=True, help="Property path (e.g. 'localPosition.x').")
@click.option("--type", "-t", "component_type", default="Transform", help="Component type name.")
@click.option("--keys", "-k", required=True, help='Keyframes as JSON: [[0,0],[0.5,1],[1,0]] or [{"time":0,"value":0},...]')
@handle_unity_errors
def clip_add_curve(clip_path: str, property_path: str, component_type: str, keys: str):
    """Add a keyframe curve to an AnimationClip.

    \b
    Examples:
        unity-mcp animation clip add-curve "Assets/Anim/Bounce.anim" \\
            --property "localPosition.y" --type Transform \\
            --keys "[[0,0],[0.5,2],[1,0]]"
    """
    config = get_config()
    keys_parsed = parse_json_list_or_exit(keys, "keys")

    params: dict[str, Any] = {
        "action": "clip_add_curve",
        "clipPath": clip_path,
        "propertyPath": property_path,
        "type": component_type,
        "keys": keys_parsed,
    }

    result = run_command("manage_animation", _normalize_params(params), config)
    click.echo(format_output(result, config.format))


@clip.command("set-curve")
@click.argument("clip_path")
@click.option("--property", "-p", "property_path", required=True, help="Property path (e.g. 'localPosition.x').")
@click.option("--type", "-t", "component_type", default="Transform", help="Component type name.")
@click.option("--keys", "-k", required=True, help='Keyframes as JSON: [[0,0],[0.5,1],[1,0]]')
@handle_unity_errors
def clip_set_curve(clip_path: str, property_path: str, component_type: str, keys: str):
    """Replace all keyframes on a curve in an AnimationClip.

    \b
    Examples:
        unity-mcp animation clip set-curve "Assets/Anim/Bounce.anim" \\
            --property "localPosition.y" --type Transform \\
            --keys "[[0,0],[1,3]]"
    """
    config = get_config()
    keys_parsed = parse_json_list_or_exit(keys, "keys")

    params: dict[str, Any] = {
        "action": "clip_set_curve",
        "clipPath": clip_path,
        "propertyPath": property_path,
        "type": component_type,
        "keys": keys_parsed,
    }

    result = run_command("manage_animation", _normalize_params(params), config)
    click.echo(format_output(result, config.format))


@clip.command("set-vector-curve")
@click.argument("clip_path")
@click.option("--property", "-p", "vector_property", required=True, help="Property group (e.g. 'localPosition', 'localEulerAngles', 'localScale').")
@click.option("--type", "-t", "component_type", default="Transform", help="Component type name.")
@click.option("--keys", "-k", required=True, help='Vector3 keyframes as JSON: [{"time":0,"value":[0,1,0]},...]')
@handle_unity_errors
def clip_set_vector_curve(clip_path: str, vector_property: str, component_type: str, keys: str):
    """Set 3 curves (x/y/z) from Vector3 keyframes in one call.

    \b
    Examples:
        unity-mcp animation clip set-vector-curve "Assets/Anim/Move.anim" \\
            --property "localPosition" \\
            --keys '[{"time":0,"value":[0,1,-10]},{"time":1,"value":[2,1,-10]}]'
    """
    config = get_config()
    keys_parsed = parse_json_list_or_exit(keys, "keys")

    params: dict[str, Any] = {
        "action": "clip_set_vector_curve",
        "clipPath": clip_path,
        "property": vector_property,
        "type": component_type,
        "keys": keys_parsed,
    }

    result = run_command("manage_animation", _normalize_params(params), config)
    click.echo(format_output(result, config.format))


@clip.command("create-preset")
@click.argument("clip_path")
@click.argument("preset", type=click.Choice(["bounce", "rotate", "pulse", "fade", "shake", "hover", "spin", "sway", "bob", "wiggle", "blink", "slide_in", "elastic"]))
@click.option("--duration", "-d", default=1.0, type=float, help="Duration in seconds.")
@click.option("--amplitude", "-a", default=1.0, type=float, help="Amplitude/intensity multiplier.")
@click.option("--loop/--no-loop", default=True, help="Whether clip loops.")
@handle_unity_errors
def clip_create_preset(clip_path: str, preset: str, duration: float, amplitude: float, loop: bool):
    """Create an AnimationClip from a named preset.

    \b
    Presets: bounce, rotate, pulse, fade, shake, hover, spin, sway, bob, wiggle, blink, slide_in, elastic

    \b
    Examples:
        unity-mcp animation clip create-preset "Assets/Anim/Bounce.anim" bounce --duration 2.0
        unity-mcp animation clip create-preset "Assets/Anim/Spin.anim" spin --amplitude 2 --no-loop
    """
    config = get_config()
    params: dict[str, Any] = {
        "action": "clip_create_preset",
        "clipPath": clip_path,
        "preset": preset,
        "duration": duration,
        "amplitude": amplitude,
        "loop": loop,
    }

    result = run_command("manage_animation", _normalize_params(params), config)
    click.echo(format_output(result, config.format))
    if result.get("success"):
        print_success(f"Created '{preset}' preset at {clip_path}")


@clip.command("assign")
@click.argument("target")
@click.argument("clip_path")
@click.option("--search-method", type=SEARCH_METHOD_CHOICE_BASIC, default=None)
@handle_unity_errors
def clip_assign(target: str, clip_path: str, search_method: Optional[str]):
    """Assign an AnimationClip to a GameObject.

    Adds an Animation component if the GameObject has no Animator or Animation.

    \b
    Examples:
        unity-mcp animation clip assign "Cube" "Assets/Animations/Bounce.anim"
    """
    config = get_config()
    params: dict[str, Any] = {
        "action": "clip_assign",
        "target": target,
        "clipPath": clip_path,
    }
    if search_method:
        params["searchMethod"] = search_method

    result = run_command("manage_animation", _normalize_params(params), config)
    click.echo(format_output(result, config.format))


@clip.command("add-event")
@click.argument("clip_path")
@click.option("--function", "function_name", required=True, help="Function name to call.")
@click.option("--time", type=float, required=True, help="Time in seconds.")
@click.option("--string-param", default="", help="String parameter to pass.")
@click.option("--float-param", type=float, default=0.0, help="Float parameter to pass.")
@click.option("--int-param", type=int, default=0, help="Int parameter to pass.")
@handle_unity_errors
def clip_add_event(clip_path: str, function_name: str, time: float, string_param: str, float_param: float, int_param: int):
    """Add an animation event to a clip.

    \b
    Examples:
        unity-mcp animation clip add-event "Assets/Anim/Attack.anim" \\
            --function "OnAttackHit" --time 0.5
        unity-mcp animation clip add-event "Assets/Anim/Footstep.anim" \\
            --function "PlaySound" --time 0.3 --string-param "footstep"
    """
    config = get_config()
    params: dict[str, Any] = {
        "action": "clip_add_event",
        "clipPath": clip_path,
        "functionName": function_name,
        "time": time,
        "stringParameter": string_param,
        "floatParameter": float_param,
        "intParameter": int_param,
    }

    result = run_command("manage_animation", _normalize_params(params), config)
    click.echo(format_output(result, config.format))
    if result.get("success"):
        print_success(f"Added event '{function_name}' at time {time}")


@clip.command("remove-event")
@click.argument("clip_path")
@click.option("--event-index", type=int, default=None, help="Index of event to remove.")
@click.option("--function", "function_name", default=None, help="Remove events by function name.")
@click.option("--time", type=float, default=None, help="Filter by time when removing by function name.")
@handle_unity_errors
def clip_remove_event(clip_path: str, event_index: Optional[int], function_name: Optional[str], time: Optional[float]):
    """Remove animation event(s) from a clip.

    \b
    Examples:
        unity-mcp animation clip remove-event "Assets/Anim/Attack.anim" --event-index 0
        unity-mcp animation clip remove-event "Assets/Anim/Attack.anim" --function "OnAttackHit"
        unity-mcp animation clip remove-event "Assets/Anim/Attack.anim" --function "OnAttackHit" --time 0.5
    """
    config = get_config()
    params: dict[str, Any] = {
        "action": "clip_remove_event",
        "clipPath": clip_path,
    }
    if event_index is not None:
        params["eventIndex"] = event_index
    if function_name:
        params["functionName"] = function_name
    if time is not None:
        params["time"] = time

    result = run_command("manage_animation", _normalize_params(params), config)
    click.echo(format_output(result, config.format))
    if result.get("success"):
        print_success("Event(s) removed")


# =============================================================================
# AnimatorController Commands
# =============================================================================

@animation.group()
def controller():
    """AnimatorController operations."""
    pass


@controller.command("create")
@click.argument("controller_path")
@handle_unity_errors
def controller_create(controller_path: str):
    """Create a new AnimatorController asset.

    \b
    Examples:
        unity-mcp animation controller create "Assets/Animations/Player.controller"
    """
    config = get_config()
    params: dict[str, Any] = {
        "action": "controller_create",
        "controllerPath": controller_path,
    }

    result = run_command("manage_animation", _normalize_params(params), config)
    click.echo(format_output(result, config.format))
    if result.get("success"):
        print_success(f"Created controller at {controller_path}")


@controller.command("add-state")
@click.argument("controller_path")
@click.argument("state_name")
@click.option("--clip-path", default=None, help="AnimationClip to assign as motion.")
@click.option("--speed", default=1.0, type=float, help="State playback speed.")
@click.option("--is-default/--no-default", default=False, help="Set as default state.")
@click.option("--layer-index", default=0, type=int, help="Layer index.")
@handle_unity_errors
def controller_add_state(controller_path: str, state_name: str, clip_path: Optional[str], speed: float, is_default: bool, layer_index: int):
    """Add a state to an AnimatorController.

    \b
    Examples:
        unity-mcp animation controller add-state "Assets/Anim/Player.controller" "Walk" \\
            --clip-path "Assets/Anim/Walk.anim"
        unity-mcp animation controller add-state "Assets/Anim/Player.controller" "Idle" --is-default
    """
    config = get_config()
    params: dict[str, Any] = {
        "action": "controller_add_state",
        "controllerPath": controller_path,
        "stateName": state_name,
        "speed": speed,
        "isDefault": is_default,
        "layerIndex": layer_index,
    }
    if clip_path:
        params["clipPath"] = clip_path

    result = run_command("manage_animation", _normalize_params(params), config)
    click.echo(format_output(result, config.format))


@controller.command("add-transition")
@click.argument("controller_path")
@click.argument("from_state")
@click.argument("to_state")
@click.option("--has-exit-time/--no-exit-time", default=True, help="Whether transition uses exit time.")
@click.option("--duration", "-d", default=0.25, type=float, help="Transition duration.")
@click.option("--conditions", "-c", default=None, help='Conditions as JSON: [{"parameter":"Speed","mode":"greater","threshold":0.1}]')
@click.option("--layer-index", default=0, type=int, help="Layer index.")
@handle_unity_errors
def controller_add_transition(controller_path: str, from_state: str, to_state: str, has_exit_time: bool, duration: float, conditions: Optional[str], layer_index: int):
    """Add a transition between states in an AnimatorController.

    \b
    Examples:
        unity-mcp animation controller add-transition "Assets/Anim/Player.controller" "Idle" "Walk" \\
            --no-exit-time --duration 0.25 \\
            --conditions '[{"parameter":"Speed","mode":"greater","threshold":0.1}]'
    """
    config = get_config()
    params: dict[str, Any] = {
        "action": "controller_add_transition",
        "controllerPath": controller_path,
        "fromState": from_state,
        "toState": to_state,
        "hasExitTime": has_exit_time,
        "duration": duration,
        "layerIndex": layer_index,
    }
    if conditions:
        params["conditions"] = parse_json_list_or_exit(conditions, "conditions")

    result = run_command("manage_animation", _normalize_params(params), config)
    click.echo(format_output(result, config.format))


@controller.command("add-parameter")
@click.argument("controller_path")
@click.argument("param_name")
@click.option(
    "--type", "-t", "param_type",
    type=click.Choice(["float", "int", "bool", "trigger"]),
    default="float",
    help="Parameter type.",
)
@click.option("--default-value", default=None, help="Default value for the parameter.")
@handle_unity_errors
def controller_add_parameter(controller_path: str, param_name: str, param_type: str, default_value: Optional[str]):
    """Add a parameter to an AnimatorController.

    \b
    Examples:
        unity-mcp animation controller add-parameter "Assets/Anim/Player.controller" "Speed" --type float --default-value 0.0
        unity-mcp animation controller add-parameter "Assets/Anim/Player.controller" "Jump" --type trigger
    """
    config = get_config()
    params: dict[str, Any] = {
        "action": "controller_add_parameter",
        "controllerPath": controller_path,
        "parameterName": param_name,
        "parameterType": param_type,
    }
    if default_value is not None:
        params["defaultValue"] = parse_value_safe(default_value)

    result = run_command("manage_animation", _normalize_params(params), config)
    click.echo(format_output(result, config.format))


@controller.command("info")
@click.argument("controller_path")
@handle_unity_errors
def controller_info(controller_path: str):
    """Get AnimatorController info (states, transitions, parameters).

    \b
    Examples:
        unity-mcp animation controller info "Assets/Animations/Player.controller"
    """
    config = get_config()
    params: dict[str, Any] = {
        "action": "controller_get_info",
        "controllerPath": controller_path,
    }

    result = run_command("manage_animation", _normalize_params(params), config)
    click.echo(format_output(result, config.format))


@controller.command("assign")
@click.argument("controller_path")
@click.argument("target")
@click.option("--search-method", type=SEARCH_METHOD_CHOICE_BASIC, default=None)
@handle_unity_errors
def controller_assign(controller_path: str, target: str, search_method: Optional[str]):
    """Assign an AnimatorController to a GameObject.

    Adds an Animator component if needed.

    \b
    Examples:
        unity-mcp animation controller assign "Assets/Animations/Player.controller" "Player"
    """
    config = get_config()
    params: dict[str, Any] = {
        "action": "controller_assign",
        "controllerPath": controller_path,
        "target": target,
    }
    if search_method:
        params["searchMethod"] = search_method

    result = run_command("manage_animation", _normalize_params(params), config)
    click.echo(format_output(result, config.format))
    if result.get("success"):
        print_success(f"Assigned controller to {target}")


@controller.command("add-layer")
@click.argument("controller_path")
@click.argument("layer_name")
@click.option("--weight", type=float, default=1.0, help="Layer weight (default: 1.0).")
@click.option("--blending-mode", type=click.Choice(["override", "additive"]), default="override", help="Blending mode.")
@handle_unity_errors
def controller_add_layer(controller_path: str, layer_name: str, weight: float, blending_mode: str):
    """Add a layer to an AnimatorController.

    \b
    Examples:
        unity-mcp animation controller add-layer "Assets/Anim/Player.controller" "UpperBody" --weight 0.8
        unity-mcp animation controller add-layer "Assets/Anim/Player.controller" "Effects" --blending-mode additive
    """
    config = get_config()
    params: dict[str, Any] = {
        "action": "controller_add_layer",
        "controllerPath": controller_path,
        "layerName": layer_name,
        "weight": weight,
        "blendingMode": blending_mode,
    }

    result = run_command("manage_animation", _normalize_params(params), config)
    click.echo(format_output(result, config.format))
    if result.get("success"):
        print_success(f"Added layer '{layer_name}'")


@controller.command("remove-layer")
@click.argument("controller_path")
@click.option("--layer-index", type=int, default=None, help="Layer index to remove.")
@click.option("--layer-name", default=None, help="Layer name to remove.")
@handle_unity_errors
def controller_remove_layer(controller_path: str, layer_index: Optional[int], layer_name: Optional[str]):
    """Remove a layer from an AnimatorController.

    \b
    Examples:
        unity-mcp animation controller remove-layer "Assets/Anim/Player.controller" --layer-index 1
        unity-mcp animation controller remove-layer "Assets/Anim/Player.controller" --layer-name "UpperBody"
    """
    config = get_config()
    params: dict[str, Any] = {
        "action": "controller_remove_layer",
        "controllerPath": controller_path,
    }
    if layer_index is not None:
        params["layerIndex"] = layer_index
    if layer_name:
        params["layerName"] = layer_name

    result = run_command("manage_animation", _normalize_params(params), config)
    click.echo(format_output(result, config.format))
    if result.get("success"):
        print_success("Layer removed")


@controller.command("set-layer-weight")
@click.argument("controller_path")
@click.argument("weight", type=float)
@click.option("--layer-index", type=int, default=None, help="Layer index.")
@click.option("--layer-name", default=None, help="Layer name.")
@handle_unity_errors
def controller_set_layer_weight(controller_path: str, weight: float, layer_index: Optional[int], layer_name: Optional[str]):
    """Set the weight of a layer in an AnimatorController.

    \b
    Examples:
        unity-mcp animation controller set-layer-weight "Assets/Anim/Player.controller" 0.5 --layer-index 1
        unity-mcp animation controller set-layer-weight "Assets/Anim/Player.controller" 0.8 --layer-name "UpperBody"
    """
    config = get_config()
    params: dict[str, Any] = {
        "action": "controller_set_layer_weight",
        "controllerPath": controller_path,
        "weight": weight,
    }
    if layer_index is not None:
        params["layerIndex"] = layer_index
    if layer_name:
        params["layerName"] = layer_name

    result = run_command("manage_animation", _normalize_params(params), config)
    click.echo(format_output(result, config.format))
    if result.get("success"):
        print_success(f"Set layer weight to {weight}")


@controller.command("create-blend-tree-1d")
@click.argument("controller_path")
@click.argument("state_name")
@click.option("--blend-param", required=True, help="Blend parameter name.")
@click.option("--layer-index", type=int, default=0, help="Layer index.")
@handle_unity_errors
def controller_create_blend_tree_1d(controller_path: str, state_name: str, blend_param: str, layer_index: int):
    """Create a 1D blend tree state in an AnimatorController.

    \b
    Examples:
        unity-mcp animation controller create-blend-tree-1d "Assets/Anim/Player.controller" "Locomotion" --blend-param "Speed"
    """
    config = get_config()
    params: dict[str, Any] = {
        "action": "controller_create_blend_tree_1d",
        "controllerPath": controller_path,
        "stateName": state_name,
        "blendParameter": blend_param,
        "layerIndex": layer_index,
    }

    result = run_command("manage_animation", _normalize_params(params), config)
    click.echo(format_output(result, config.format))
    if result.get("success"):
        print_success(f"Created 1D blend tree state '{state_name}'")


@controller.command("create-blend-tree-2d")
@click.argument("controller_path")
@click.argument("state_name")
@click.option("--blend-param-x", required=True, help="X-axis blend parameter name.")
@click.option("--blend-param-y", required=True, help="Y-axis blend parameter name.")
@click.option("--blend-type", type=click.Choice(["simpledirectional2d", "freeformdirectional2d", "freeformcartesian2d"]), default="simpledirectional2d", help="Blend tree type.")
@click.option("--layer-index", type=int, default=0, help="Layer index.")
@handle_unity_errors
def controller_create_blend_tree_2d(controller_path: str, state_name: str, blend_param_x: str, blend_param_y: str, blend_type: str, layer_index: int):
    """Create a 2D blend tree state in an AnimatorController.

    \b
    Examples:
        unity-mcp animation controller create-blend-tree-2d "Assets/Anim/Player.controller" "Movement" \\
            --blend-param-x "VelocityX" --blend-param-y "VelocityZ"
    """
    config = get_config()
    params: dict[str, Any] = {
        "action": "controller_create_blend_tree_2d",
        "controllerPath": controller_path,
        "stateName": state_name,
        "blendParameterX": blend_param_x,
        "blendParameterY": blend_param_y,
        "blendType": blend_type,
        "layerIndex": layer_index,
    }

    result = run_command("manage_animation", _normalize_params(params), config)
    click.echo(format_output(result, config.format))
    if result.get("success"):
        print_success(f"Created 2D blend tree state '{state_name}'")


@controller.command("add-blend-tree-child")
@click.argument("controller_path")
@click.argument("state_name")
@click.option("--clip-path", required=True, help="AnimationClip path.")
@click.option("--threshold", type=float, default=None, help="Threshold for 1D blend tree.")
@click.option("--position", type=(float, float), default=None, help="Position (x, y) for 2D blend tree.")
@click.option("--layer-index", type=int, default=0, help="Layer index.")
@handle_unity_errors
def controller_add_blend_tree_child(controller_path: str, state_name: str, clip_path: str, threshold: Optional[float], position: Optional[tuple], layer_index: int):
    """Add a child motion to a blend tree.

    \b
    Examples:
        unity-mcp animation controller add-blend-tree-child "Assets/Anim/Player.controller" "Locomotion" \\
            --clip-path "Assets/Anim/Walk.anim" --threshold 1.0
        unity-mcp animation controller add-blend-tree-child "Assets/Anim/Player.controller" "Movement" \\
            --clip-path "Assets/Anim/WalkForward.anim" --position 0 1
    """
    config = get_config()
    params: dict[str, Any] = {
        "action": "controller_add_blend_tree_child",
        "controllerPath": controller_path,
        "stateName": state_name,
        "clipPath": clip_path,
        "layerIndex": layer_index,
    }
    if threshold is not None:
        params["threshold"] = threshold
    if position is not None:
        params["position"] = list(position)

    result = run_command("manage_animation", _normalize_params(params), config)
    click.echo(format_output(result, config.format))
    if result.get("success"):
        print_success("Added blend tree child")


# =============================================================================
# Raw Command (escape hatch for all animation actions)
# =============================================================================

@animation.command("raw")
@click.argument("action")
@click.argument("target", required=False)
@click.option("--clip-path", default=None, help="AnimationClip asset path.")
@click.option("--params", "-p", "extra_params", default="{}", help="Additional parameters as JSON.")
@click.option("--search-method", type=SEARCH_METHOD_CHOICE_BASIC, default=None)
@handle_unity_errors
def animation_raw(action: str, target: Optional[str], clip_path: Optional[str], extra_params: str, search_method: Optional[str]):
    """Execute any animation action directly.

    \b
    Actions include:
        animator_*: animator_get_info, animator_play, animator_crossfade, ...
        controller_*: controller_create, controller_add_state, controller_add_transition, ...
        clip_*: clip_create, clip_get_info, clip_add_curve, clip_set_curve, clip_set_vector_curve, clip_create_preset, clip_assign

    \b
    Examples:
        unity-mcp animation raw animator_play "Player" --params '{"stateName": "Walk"}'
        unity-mcp animation raw clip_create --clip-path "Assets/Anim/Test.anim" --params '{"length": 2.0, "loop": true}'
    """
    config = get_config()
    parsed = parse_json_dict_or_exit(extra_params, "params")

    request_params: dict[str, Any] = {"action": action}
    if target:
        request_params["target"] = target
    if clip_path:
        request_params["clipPath"] = clip_path
    if search_method:
        request_params["searchMethod"] = search_method

    request_params.update(parsed)
    result = run_command("manage_animation", _normalize_params(request_params), config)
    click.echo(format_output(result, config.format))
