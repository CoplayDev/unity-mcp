import click

from cli.utils.connection import handle_unity_errors, run_command, get_config
from cli.utils.output import format_output


def _coerce_cli_value(value: str):
    """Coerce a CLI string value to bool, int, float, or leave as str."""
    if value.lower() in ("true", "false"):
        return value.lower() == "true"
    try:
        return float(value) if "." in value else int(value)
    except ValueError:
        return value


@click.group("physics")
def physics():
    """Manage 3D and 2D physics: settings, collision matrix, materials, joints, queries, validation."""
    pass


@physics.command("ping")
@handle_unity_errors
def ping():
    """Check physics system status."""
    config = get_config()
    result = run_command("manage_physics", {"action": "ping"}, config)
    click.echo(format_output(result, config.format))


@physics.command("get-settings")
@click.option("--dimension", "-d", default="3d", help="3d or 2d.")
@handle_unity_errors
def get_settings(dimension):
    """Get physics project settings."""
    config = get_config()
    result = run_command(
        "manage_physics", {"action": "get_settings", "dimension": dimension}, config
    )
    click.echo(format_output(result, config.format))


@physics.command("set-settings")
@click.option("--dimension", "-d", default="3d", help="3d or 2d.")
@click.argument("key")
@click.argument("value")
@handle_unity_errors
def set_settings(dimension, key, value):
    """Set a physics setting (key value)."""
    config = get_config()
    coerced = _coerce_cli_value(value)
    result = run_command(
        "manage_physics",
        {"action": "set_settings", "dimension": dimension, "settings": {key: coerced}},
        config,
    )
    click.echo(format_output(result, config.format))


@physics.command("get-collision-matrix")
@click.option("--dimension", "-d", default="3d", help="3d or 2d.")
@handle_unity_errors
def get_collision_matrix(dimension):
    """Get layer collision matrix."""
    config = get_config()
    result = run_command(
        "manage_physics",
        {"action": "get_collision_matrix", "dimension": dimension},
        config,
    )
    click.echo(format_output(result, config.format))


@physics.command("set-collision-matrix")
@click.argument("layer_a")
@click.argument("layer_b")
@click.option("--collide/--ignore", default=True, help="Enable or disable collision.")
@click.option("--dimension", "-d", default="3d", help="3d or 2d.")
@handle_unity_errors
def set_collision_matrix(layer_a, layer_b, collide, dimension):
    """Set collision between two layers."""
    config = get_config()
    result = run_command(
        "manage_physics",
        {
            "action": "set_collision_matrix",
            "layer_a": layer_a,
            "layer_b": layer_b,
            "collide": collide,
            "dimension": dimension,
        },
        config,
    )
    click.echo(format_output(result, config.format))


@physics.command("create-material")
@click.option("--name", "-n", required=True, help="Material name.")
@click.option("--path", "-p", default=None, help="Folder path.")
@click.option("--dimension", "-d", default="3d", help="3d or 2d.")
@click.option("--bounciness", "-b", type=float, default=None, help="Bounciness (0-1).")
@click.option("--dynamic-friction", type=float, default=None, help="Dynamic friction.")
@click.option("--static-friction", type=float, default=None, help="Static friction.")
@click.option("--friction", type=float, default=None, help="Friction (2D only).")
@handle_unity_errors
def create_material(name, path, dimension, bounciness, dynamic_friction, static_friction, friction):
    """Create a physics material asset."""
    config = get_config()
    params = {
        "action": "create_physics_material",
        "name": name,
        "dimension": dimension,
    }
    if path:
        params["path"] = path
    if bounciness is not None:
        params["bounciness"] = bounciness
    if dynamic_friction is not None:
        params["dynamic_friction"] = dynamic_friction
    if static_friction is not None:
        params["static_friction"] = static_friction
    if friction is not None:
        params["friction"] = friction
    result = run_command("manage_physics", params, config)
    click.echo(format_output(result, config.format))


@physics.command("validate")
@click.option("--target", "-t", default=None, help="Target GameObject (or whole scene).")
@click.option("--dimension", "-d", default="both", help="3d, 2d, or both.")
@handle_unity_errors
def validate(target, dimension):
    """Validate physics setup for common mistakes."""
    config = get_config()
    params = {"action": "validate", "dimension": dimension}
    if target:
        params["target"] = target
    result = run_command("manage_physics", params, config)
    click.echo(format_output(result, config.format))


@physics.command("raycast")
@click.option("--origin", "-o", required=True, help="Origin as 'x,y,z'.")
@click.option("--direction", "-d", required=True, help="Direction as 'x,y,z'.")
@click.option("--max-distance", type=float, default=None, help="Max distance.")
@click.option("--dimension", default="3d", help="3d or 2d.")
@handle_unity_errors
def raycast(origin, direction, max_distance, dimension):
    """Perform a physics raycast."""
    config = get_config()
    params = {
        "action": "raycast",
        "origin": [float(x) for x in origin.split(",")],
        "direction": [float(x) for x in direction.split(",")],
        "dimension": dimension,
    }
    if max_distance is not None:
        params["max_distance"] = max_distance
    result = run_command("manage_physics", params, config)
    click.echo(format_output(result, config.format))


@physics.command("simulate")
@click.option("--steps", "-s", type=int, default=1, help="Number of steps (max 100).")
@click.option("--step-size", type=float, default=None, help="Step size in seconds.")
@click.option("--dimension", "-d", default="3d", help="3d or 2d.")
@handle_unity_errors
def simulate(steps, step_size, dimension):
    """Run physics simulation steps in edit mode."""
    config = get_config()
    params = {"action": "simulate_step", "steps": steps, "dimension": dimension}
    if step_size is not None:
        params["step_size"] = step_size
    result = run_command("manage_physics", params, config)
    click.echo(format_output(result, config.format))


@physics.command("configure-material")
@click.option("--path", "-p", required=True, help="Asset path to physics material.")
@click.option("--dimension", "-d", default="3d", help="3d or 2d.")
@click.argument("properties", nargs=-1)  # key=value pairs like dynamicFriction=0.5
@handle_unity_errors
def configure_material(path, dimension, properties):
    """Configure a physics material asset (key=value ...)."""
    config = get_config()
    props = {k: _coerce_cli_value(v) for kv in properties if "=" in kv for k, v in [kv.split("=", 1)]}
    result = run_command(
        "manage_physics",
        {"action": "configure_physics_material", "path": path, "dimension": dimension, "properties": props},
        config,
    )
    click.echo(format_output(result, config.format))


@physics.command("assign-material")
@click.option("--target", "-t", required=True, help="Target GameObject name or instance ID.")
@click.option("--material-path", "-m", required=True, help="Path to physics material asset.")
@click.option("--collider-type", default=None, help="Specific collider type.")
@click.option(
    "--component-index", "-i",
    type=int,
    default=None,
    help="Zero-based index when multiple colliders of the same type exist."
)
@handle_unity_errors
def assign_material(target, material_path, collider_type, component_index):
    """Assign a physics material to a GameObject's collider."""
    config = get_config()
    params = {
        "action": "assign_physics_material",
        "target": target,
        "material_path": material_path,
    }
    if collider_type:
        params["collider_type"] = collider_type
    if component_index is not None:
        params["componentIndex"] = component_index
    result = run_command("manage_physics", params, config)
    click.echo(format_output(result, config.format))


@physics.command("add-joint")
@click.option("--target", "-t", required=True, help="Target GameObject.")
@click.option("--joint-type", "-j", required=True, help="Joint type (e.g. hinge, fixed, spring).")
@click.option("--connected-body", default=None, help="Connected body GameObject.")
@click.option("--dimension", "-d", default=None, help="3d or 2d (auto-detected if omitted).")
@handle_unity_errors
def add_joint(target, joint_type, connected_body, dimension):
    """Add a physics joint to a GameObject."""
    config = get_config()
    params = {"action": "add_joint", "target": target, "joint_type": joint_type}
    if connected_body:
        params["connected_body"] = connected_body
    if dimension:
        params["dimension"] = dimension
    result = run_command("manage_physics", params, config)
    click.echo(format_output(result, config.format))


@physics.command("configure-joint")
@click.option("--target", "-t", required=True, help="Target GameObject.")
@click.option("--joint-type", "-j", default=None, help="Joint type to target.")
@click.option(
    "--component-index", "-i",
    type=int,
    default=None,
    help="Zero-based index when multiple joints of the same type exist."
)
@click.argument("properties", nargs=-1)  # key=value pairs
@handle_unity_errors
def configure_joint(target, joint_type, component_index, properties):
    """Configure a joint on a GameObject (key=value ...)."""
    config = get_config()
    props = {k: _coerce_cli_value(v) for kv in properties if "=" in kv for k, v in [kv.split("=", 1)]}
    params = {"action": "configure_joint", "target": target, "properties": props}
    if joint_type:
        params["joint_type"] = joint_type
    if component_index is not None:
        params["componentIndex"] = component_index
    result = run_command("manage_physics", params, config)
    click.echo(format_output(result, config.format))


@physics.command("remove-joint")
@click.option("--target", "-t", required=True, help="Target GameObject.")
@click.option("--joint-type", "-j", default=None, help="Joint type to remove (omit to remove all).")
@click.option(
    "--component-index", "-i",
    type=int,
    default=None,
    help="Zero-based index when multiple joints of the same type exist."
)
@handle_unity_errors
def remove_joint(target, joint_type, component_index):
    """Remove joint(s) from a GameObject."""
    config = get_config()
    params = {"action": "remove_joint", "target": target}
    if joint_type:
        params["joint_type"] = joint_type
    if component_index is not None:
        params["componentIndex"] = component_index
    result = run_command("manage_physics", params, config)
    click.echo(format_output(result, config.format))


@physics.command("overlap")
@click.option("--shape", "-s", required=True, help="Shape: sphere, box, capsule (3D); circle, box, capsule (2D).")
@click.option("--position", "-p", required=True, help="Position as 'x,y,z' or 'x,y'.")
@click.option("--size", required=True, help="Size: float for sphere/circle radius, or 'x,y,z' for box.")
@click.option("--dimension", "-d", default="3d", help="3d or 2d.")
@handle_unity_errors
def overlap(shape, position, size, dimension):
    """Perform a physics overlap query."""
    config = get_config()
    pos_parts = [float(x) for x in position.split(",")]
    # Try to parse size as float first, then as array
    try:
        parsed_size = float(size)
    except ValueError:
        parsed_size = [float(x) for x in size.split(",")]
    params = {
        "action": "overlap",
        "shape": shape,
        "position": pos_parts,
        "size": parsed_size,
        "dimension": dimension,
    }
    result = run_command("manage_physics", params, config)
    click.echo(format_output(result, config.format))


@physics.command("raycast-all")
@click.option("--origin", "-o", required=True, help="Origin as 'x,y,z'.")
@click.option("--direction", "-d", required=True, help="Direction as 'x,y,z'.")
@click.option("--max-distance", type=float, default=None, help="Max distance.")
@click.option("--dimension", default="3d", help="3d or 2d.")
@handle_unity_errors
def raycast_all(origin, direction, max_distance, dimension):
    """Perform a multi-hit raycast returning all hits."""
    config = get_config()
    params = {
        "action": "raycast_all",
        "origin": [float(x) for x in origin.split(",")],
        "direction": [float(x) for x in direction.split(",")],
        "dimension": dimension,
    }
    if max_distance is not None:
        params["max_distance"] = max_distance
    result = run_command("manage_physics", params, config)
    click.echo(format_output(result, config.format))


@physics.command("linecast")
@click.option("--start", "-s", required=True, help="Start point as 'x,y,z'.")
@click.option("--end", "-e", required=True, help="End point as 'x,y,z'.")
@click.option("--dimension", "-d", default="3d", help="3d or 2d.")
@handle_unity_errors
def linecast(start, end, dimension):
    """Check if anything intersects the line between two points."""
    config = get_config()
    params = {
        "action": "linecast",
        "start": [float(x) for x in start.split(",")],
        "end": [float(x) for x in end.split(",")],
        "dimension": dimension,
    }
    result = run_command("manage_physics", params, config)
    click.echo(format_output(result, config.format))


@physics.command("shapecast")
@click.option("--shape", "-s", required=True, help="Shape: sphere, box, capsule (3D); circle, box, capsule (2D).")
@click.option("--origin", "-o", required=True, help="Origin as 'x,y,z' or 'x,y'.")
@click.option("--direction", "-d", required=True, help="Direction as 'x,y,z' or 'x,y'.")
@click.option("--size", required=True, help="Size: float for sphere/circle radius, or 'x,y,z' for box.")
@click.option("--max-distance", type=float, default=None, help="Max distance.")
@click.option("--dimension", default="3d", help="3d or 2d.")
@handle_unity_errors
def shapecast(shape, origin, direction, size, max_distance, dimension):
    """Cast a shape (sphere, box, capsule) along a direction."""
    config = get_config()
    try:
        parsed_size = float(size)
    except ValueError:
        parsed_size = [float(x) for x in size.split(",")]
    params = {
        "action": "shapecast",
        "shape": shape,
        "origin": [float(x) for x in origin.split(",")],
        "direction": [float(x) for x in direction.split(",")],
        "size": parsed_size,
        "dimension": dimension,
    }
    if max_distance is not None:
        params["max_distance"] = max_distance
    result = run_command("manage_physics", params, config)
    click.echo(format_output(result, config.format))


@physics.command("apply-force")
@click.option("--target", "-t", required=True, help="Target GameObject.")
@click.option("--force", "-f", required=True, help="Force vector as 'x,y,z' or 'x,y'.")
@click.option("--force-mode", default="Force", help="Force, Impulse, Acceleration, VelocityChange.")
@click.option("--dimension", "-d", default=None, help="3d or 2d.")
@click.option("--torque", default=None, help="Torque as 'x,y,z' (3D) or float (2D).")
@click.option("--position", "-p", default=None, help="Point to apply force at, as 'x,y,z'.")
@handle_unity_errors
def apply_force(target, force, force_mode, dimension, torque, position):
    """Apply force to a Rigidbody."""
    config = get_config()
    params = {
        "action": "apply_force",
        "target": target,
        "force": [float(x) for x in force.split(",")],
        "force_mode": force_mode,
    }
    if dimension:
        params["dimension"] = dimension
    if torque:
        try:
            params["torque"] = [float(torque)]
        except ValueError:
            params["torque"] = [float(x) for x in torque.split(",")]
    if position:
        params["position"] = [float(x) for x in position.split(",")]
    result = run_command("manage_physics", params, config)
    click.echo(format_output(result, config.format))


@physics.command("get-rigidbody")
@click.argument("target")
@click.option("--dimension", "-d", default=None, help="3d or 2d (auto-detected if omitted).")
@click.option("--search-method", default=None, help="Search method for target resolution.")
@handle_unity_errors
def get_rigidbody(target, dimension, search_method):
    """Get Rigidbody state (mass, velocity, position, etc.)."""
    config = get_config()
    params = {"action": "get_rigidbody", "target": target}
    if dimension:
        params["dimension"] = dimension
    if search_method:
        params["search_method"] = search_method
    result = run_command("manage_physics", params, config)
    click.echo(format_output(result, config.format))


@physics.command("configure-rigidbody")
@click.option("--target", "-t", required=True, help="Target GameObject.")
@click.option("--dimension", "-d", default=None, help="3d or 2d.")
@click.argument("properties", nargs=-1)
@handle_unity_errors
def configure_rigidbody(target, dimension, properties):
    """Configure Rigidbody properties (key=value ...)."""
    config = get_config()
    props = {k: _coerce_cli_value(v) for kv in properties if "=" in kv for k, v in [kv.split("=", 1)]}
    params = {"action": "configure_rigidbody", "target": target, "properties": props}
    if dimension:
        params["dimension"] = dimension
    result = run_command("manage_physics", params, config)
    click.echo(format_output(result, config.format))
