import click
from cli.utils.connection import handle_unity_errors, run_command, get_config
from cli.utils.output import format_output


@click.group("graphics")
def graphics():
    """Manage rendering graphics: volumes, effects, and pipeline settings."""
    pass


def _coerce_cli_value(val: str):
    """Convert a CLI string value to bool/float/int/str."""
    if val.lower() in ("true", "false"):
        return val.lower() == "true"
    try:
        return float(val) if "." in val else int(val)
    except ValueError:
        return val


@graphics.command("ping")
@handle_unity_errors
def ping():
    """Check graphics system status."""
    config = get_config()
    params = {"action": "ping"}
    result = run_command("manage_graphics", params, config)
    click.echo(format_output(result, config.format))


@graphics.command("volume-create")
@click.option("--name", "-n", default=None, help="Name for the Volume GameObject.")
@click.option("--global/--local", "is_global", default=True, help="Global or local Volume.")
@click.option("--weight", "-w", type=float, default=None, help="Volume weight (0-1).")
@click.option("--priority", "-p", type=float, default=None, help="Volume priority.")
@click.option("--profile-path", default=None, help="Existing VolumeProfile asset path to assign.")
@handle_unity_errors
def volume_create(name, is_global, weight, priority, profile_path):
    """Create a Volume GameObject with a profile."""
    config = get_config()
    params = {"action": "volume_create", "is_global": is_global}
    if name:
        params["name"] = name
    if weight is not None:
        params["weight"] = weight
    if priority is not None:
        params["priority"] = priority
    if profile_path:
        params["profile_path"] = profile_path
    result = run_command("manage_graphics", params, config)
    click.echo(format_output(result, config.format))


@graphics.command("volume-add-effect")
@click.option("--target", "-t", required=True, help="Volume name or instance ID.")
@click.option("--effect", "-e", required=True, help="Effect type (e.g., Bloom, Vignette).")
@handle_unity_errors
def volume_add_effect(target, effect):
    """Add an effect override to a Volume."""
    config = get_config()
    params = {"action": "volume_add_effect", "target": target, "effect": effect}
    result = run_command("manage_graphics", params, config)
    click.echo(format_output(result, config.format))


@graphics.command("volume-set-effect")
@click.option("--target", "-t", required=True, help="Volume name or instance ID.")
@click.option("--effect", "-e", required=True, help="Effect type (e.g., Bloom).")
@click.option("--param", "-p", multiple=True, type=(str, str), help="Parameter key-value pair.")
@handle_unity_errors
def volume_set_effect(target, effect, param):
    """Set parameters on a Volume effect."""
    config = get_config()
    parameters = {k: v for k, v in param}
    params = {
        "action": "volume_set_effect",
        "target": target,
        "effect": effect,
        "parameters": parameters,
    }
    result = run_command("manage_graphics", params, config)
    click.echo(format_output(result, config.format))


@graphics.command("volume-remove-effect")
@click.option("--target", "-t", required=True, help="Volume name or instance ID.")
@click.option("--effect", "-e", required=True, help="Effect type to remove.")
@handle_unity_errors
def volume_remove_effect(target, effect):
    """Remove an effect from a Volume."""
    config = get_config()
    params = {"action": "volume_remove_effect", "target": target, "effect": effect}
    result = run_command("manage_graphics", params, config)
    click.echo(format_output(result, config.format))


@graphics.command("volume-info")
@click.option("--target", "-t", required=True, help="Volume name or instance ID.")
@handle_unity_errors
def volume_info(target):
    """Get all effects and parameters on a Volume."""
    config = get_config()
    params = {"action": "volume_get_info", "target": target}
    result = run_command("manage_graphics", params, config)
    click.echo(format_output(result, config.format))


@graphics.command("volume-set-properties")
@click.option("--target", "-t", required=True, help="Volume name or instance ID.")
@click.option("--weight", "-w", type=float, default=None, help="Volume weight (0-1).")
@click.option("--priority", "-p", type=float, default=None, help="Volume priority.")
@click.option("--global/--local", "is_global", default=None, help="Global or local Volume.")
@handle_unity_errors
def volume_set_properties(target, weight, priority, is_global):
    """Set Volume properties (weight, priority, is_global)."""
    config = get_config()
    params = {"action": "volume_set_properties", "target": target}
    if weight is not None:
        params["weight"] = weight
    if priority is not None:
        params["priority"] = priority
    if is_global is not None:
        params["is_global"] = is_global
    result = run_command("manage_graphics", params, config)
    click.echo(format_output(result, config.format))


@graphics.command("volume-list-effects")
@handle_unity_errors
def volume_list_effects():
    """List available VolumeComponent effect types."""
    config = get_config()
    params = {"action": "volume_list_effects"}
    result = run_command("manage_graphics", params, config)
    click.echo(format_output(result, config.format))


@graphics.command("volume-create-profile")
@click.option("--path", "-p", required=True, help="Asset path for the VolumeProfile (e.g., Assets/Profiles/MyProfile.asset).")
@click.option("--name", "-n", default=None, help="Display name for the profile.")
@handle_unity_errors
def volume_create_profile(path, name):
    """Create a standalone VolumeProfile asset."""
    config = get_config()
    params = {"action": "volume_create_profile", "path": path}
    if name:
        params["name"] = name
    result = run_command("manage_graphics", params, config)
    click.echo(format_output(result, config.format))


@graphics.command("pipeline-info")
@handle_unity_errors
def pipeline_info():
    """Get active render pipeline, quality level, and settings."""
    config = get_config()
    params = {"action": "pipeline_get_info"}
    result = run_command("manage_graphics", params, config)
    click.echo(format_output(result, config.format))


@graphics.command("pipeline-set-quality")
@click.option("--level", "-l", required=True, help="Quality level name or index.")
@handle_unity_errors
def pipeline_set_quality(level):
    """Switch quality level."""
    config = get_config()
    params = {"action": "pipeline_set_quality", "level": level}
    result = run_command("manage_graphics", params, config)
    click.echo(format_output(result, config.format))


@graphics.command("pipeline-settings")
@handle_unity_errors
def pipeline_settings():
    """Get detailed pipeline settings."""
    config = get_config()
    result = run_command("manage_graphics", {"action": "pipeline_get_settings"}, config)
    click.echo(format_output(result, config.format))


@graphics.command("pipeline-set-settings")
@click.option("--setting", "-s", multiple=True, type=(str, str), required=True,
              help="Setting key-value pair (e.g., -s renderScale 0.5 -s supportsHDR true).")
@handle_unity_errors
def pipeline_set_settings(setting):
    """Set pipeline asset settings."""
    config = get_config()
    settings = {key: _coerce_cli_value(val) for key, val in setting}
    params = {"action": "pipeline_set_settings", "settings": settings}
    result = run_command("manage_graphics", params, config)
    click.echo(format_output(result, config.format))


# --- Bake commands ---

@graphics.command("bake-start")
@click.option("--sync", is_flag=True, help="Synchronous bake (blocks until done).")
@handle_unity_errors
def bake_start(sync):
    """Start lightmap bake."""
    config = get_config()
    params = {"action": "bake_start", "async": not sync}
    result = run_command("manage_graphics", params, config)
    click.echo(format_output(result, config.format))


@graphics.command("bake-cancel")
@handle_unity_errors
def bake_cancel():
    """Cancel running bake."""
    config = get_config()
    result = run_command("manage_graphics", {"action": "bake_cancel"}, config)
    click.echo(format_output(result, config.format))


@graphics.command("bake-status")
@handle_unity_errors
def bake_status():
    """Get bake progress/status."""
    config = get_config()
    result = run_command("manage_graphics", {"action": "bake_status"}, config)
    click.echo(format_output(result, config.format))


@graphics.command("bake-clear")
@handle_unity_errors
def bake_clear():
    """Clear all baked lighting data."""
    config = get_config()
    result = run_command("manage_graphics", {"action": "bake_clear"}, config)
    click.echo(format_output(result, config.format))


@graphics.command("bake-settings")
@handle_unity_errors
def bake_settings():
    """Get current lighting/bake settings."""
    config = get_config()
    result = run_command("manage_graphics", {"action": "bake_get_settings"}, config)
    click.echo(format_output(result, config.format))


@graphics.command("bake-reflection-probe")
@click.option("--target", "-t", required=True, help="Name or instance ID of GameObject with ReflectionProbe.")
@handle_unity_errors
def bake_reflection_probe(target):
    """Bake a specific reflection probe."""
    config = get_config()
    params = {"action": "bake_reflection_probe", "target": target}
    result = run_command("manage_graphics", params, config)
    click.echo(format_output(result, config.format))


@graphics.command("bake-set-settings")
@click.option("--setting", "-s", multiple=True, type=(str, str), required=True,
              help="Lighting setting key-value pair.")
@handle_unity_errors
def bake_set_settings(setting):
    """Set lighting/bake settings."""
    config = get_config()
    settings = {key: _coerce_cli_value(val) for key, val in setting}
    params = {"action": "bake_set_settings", "settings": settings}
    result = run_command("manage_graphics", params, config)
    click.echo(format_output(result, config.format))


@graphics.command("bake-create-probes")
@click.option("--name", "-n", default=None, help="Name for the probe group.")
@click.option("--spacing", "-s", type=float, default=None, help="Grid spacing.")
@handle_unity_errors
def bake_create_probes(name, spacing):
    """Create a light probe group with grid layout."""
    config = get_config()
    params = {"action": "bake_create_light_probe_group"}
    if name:
        params["name"] = name
    if spacing is not None:
        params["spacing"] = spacing
    result = run_command("manage_graphics", params, config)
    click.echo(format_output(result, config.format))


@graphics.command("bake-create-reflection")
@click.option("--name", "-n", default=None, help="Name for the reflection probe.")
@click.option("--resolution", "-r", type=int, default=None, help="Probe resolution.")
@click.option("--mode", "-m", default=None, help="Baked/Realtime/Custom.")
@handle_unity_errors
def bake_create_reflection(name, resolution, mode):
    """Create a reflection probe."""
    config = get_config()
    params = {"action": "bake_create_reflection_probe"}
    if name:
        params["name"] = name
    if resolution is not None:
        params["resolution"] = resolution
    if mode:
        params["mode"] = mode
    result = run_command("manage_graphics", params, config)
    click.echo(format_output(result, config.format))


# --- Stats commands ---

@graphics.command("stats")
@handle_unity_errors
def stats():
    """Get rendering performance stats."""
    config = get_config()
    result = run_command("manage_graphics", {"action": "stats_get"}, config)
    click.echo(format_output(result, config.format))


@graphics.command("stats-memory")
@handle_unity_errors
def stats_memory():
    """Get memory allocation stats."""
    config = get_config()
    result = run_command("manage_graphics", {"action": "stats_get_memory"}, config)
    click.echo(format_output(result, config.format))


@graphics.command("stats-debug-mode")
@click.option("--mode", "-m", required=True, help="Debug mode (Overdraw, Wireframe, Mipmaps, etc.).")
@handle_unity_errors
def stats_debug_mode(mode):
    """Set Scene view debug visualization mode."""
    config = get_config()
    params = {"action": "stats_set_scene_debug", "mode": mode}
    result = run_command("manage_graphics", params, config)
    click.echo(format_output(result, config.format))


# --- Feature commands ---

@graphics.command("feature-list")
@handle_unity_errors
def feature_list():
    """List URP renderer features."""
    config = get_config()
    result = run_command("manage_graphics", {"action": "feature_list"}, config)
    click.echo(format_output(result, config.format))


@graphics.command("feature-add")
@click.option("--type", "-t", "feature_type", required=True, help="Feature type (e.g., FullScreenPassRendererFeature).")
@click.option("--name", "-n", default=None, help="Display name.")
@handle_unity_errors
def feature_add(feature_type, name):
    """Add a renderer feature."""
    config = get_config()
    params = {"action": "feature_add", "type": feature_type}
    if name:
        params["name"] = name
    result = run_command("manage_graphics", params, config)
    click.echo(format_output(result, config.format))


@graphics.command("feature-remove")
@click.option("--index", "-i", type=int, default=None, help="Feature index.")
@click.option("--name", "-n", default=None, help="Feature name.")
@handle_unity_errors
def feature_remove(index, name):
    """Remove a renderer feature."""
    config = get_config()
    params = {"action": "feature_remove"}
    if index is not None:
        params["index"] = index
    if name:
        params["name"] = name
    result = run_command("manage_graphics", params, config)
    click.echo(format_output(result, config.format))


@graphics.command("feature-configure")
@click.option("--index", "-i", type=int, default=None, help="Feature index.")
@click.option("--name", "-n", default=None, help="Feature name.")
@click.option("--prop", "-p", multiple=True, type=(str, str), required=True,
              help="Property key-value pair.")
@handle_unity_errors
def feature_configure(index, name, prop):
    """Configure properties on a renderer feature."""
    config = get_config()
    properties = {key: _coerce_cli_value(val) for key, val in prop}
    params = {"action": "feature_configure", "properties": properties}
    if index is not None:
        params["index"] = index
    if name:
        params["name"] = name
    result = run_command("manage_graphics", params, config)
    click.echo(format_output(result, config.format))


@graphics.command("feature-reorder")
@click.option("--order", "-o", required=True, help="Comma-separated list of indices (e.g., '2,0,1').")
@handle_unity_errors
def feature_reorder(order):
    """Reorder renderer features."""
    config = get_config()
    order_list = [int(x.strip()) for x in order.split(",")]
    params = {"action": "feature_reorder", "order": order_list}
    result = run_command("manage_graphics", params, config)
    click.echo(format_output(result, config.format))


@graphics.command("feature-toggle")
@click.option("--index", "-i", type=int, default=None, help="Feature index.")
@click.option("--name", "-n", default=None, help="Feature name.")
@click.option("--active/--inactive", default=True, help="Enable or disable.")
@handle_unity_errors
def feature_toggle(index, name, active):
    """Enable/disable a renderer feature."""
    config = get_config()
    params = {"action": "feature_toggle", "active": active}
    if index is not None:
        params["index"] = index
    if name:
        params["name"] = name
    result = run_command("manage_graphics", params, config)
    click.echo(format_output(result, config.format))
