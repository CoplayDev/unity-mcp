"""ProBuilder CLI commands for managing Unity ProBuilder meshes."""

import click
from typing import Optional, Any

from cli.utils.config import get_config
from cli.utils.output import format_output, print_error, print_success
from cli.utils.connection import run_command, handle_unity_errors
from cli.utils.parsers import parse_json_dict_or_exit, parse_json_list_or_exit
from cli.utils.constants import SEARCH_METHOD_CHOICE_TAGGED


_PB_TOP_LEVEL_KEYS = {"action", "target", "searchMethod", "properties"}


def _normalize_pb_params(params: dict[str, Any]) -> dict[str, Any]:
    params = dict(params)
    properties: dict[str, Any] = {}
    for key in list(params.keys()):
        if key in _PB_TOP_LEVEL_KEYS:
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
def probuilder():
    """ProBuilder operations - 3D modeling, mesh editing, UV management."""
    pass


# =============================================================================
# Shape Creation
# =============================================================================

@probuilder.command("create-shape")
@click.argument("shape_type")
@click.option("--name", "-n", default=None, help="Name for the created GameObject.")
@click.option("--position", nargs=3, type=float, default=None, help="Position X Y Z.")
@click.option("--rotation", nargs=3, type=float, default=None, help="Rotation X Y Z (euler).")
@click.option("--params", "-p", default="{}", help="Shape-specific parameters as JSON.")
@handle_unity_errors
def create_shape(shape_type: str, name: Optional[str], position, rotation, params: str):
    """Create a ProBuilder shape.

    \\b
    Shape types: Cube, Cylinder, Sphere, Plane, Cone, Torus, Pipe, Arch,
                 Stair, CurvedStair, Door, Prism

    \\b
    Examples:
        unity-mcp probuilder create-shape Cube
        unity-mcp probuilder create-shape Torus --name "MyTorus" --params '{"rows": 16, "columns": 16}'
        unity-mcp probuilder create-shape Stair --position 0 0 5 --params '{"steps": 10}'
    """
    config = get_config()
    extra = parse_json_dict_or_exit(params, "params")

    request: dict[str, Any] = {
        "action": "create_shape",
        "shapeType": shape_type,
    }
    if name:
        request["name"] = name
    if position:
        request["position"] = list(position)
    if rotation:
        request["rotation"] = list(rotation)
    request.update(extra)

    result = run_command("manage_probuilder", _normalize_pb_params(request), config)
    click.echo(format_output(result, config.format))
    if result.get("success"):
        print_success(f"Created ProBuilder {shape_type}")


@probuilder.command("create-poly")
@click.option("--points", "-p", required=True, help='Points as JSON: [[x,y,z], ...]')
@click.option("--height", "-h", type=float, default=1.0, help="Extrude height.")
@click.option("--name", "-n", default=None, help="Name for the created GameObject.")
@click.option("--flip-normals", is_flag=True, help="Flip face normals.")
@handle_unity_errors
def create_poly(points: str, height: float, name: Optional[str], flip_normals: bool):
    """Create a ProBuilder mesh from a 2D polygon footprint.

    \\b
    Examples:
        unity-mcp probuilder create-poly --points "[[0,0,0],[5,0,0],[5,0,5],[0,0,5]]" --height 3
    """
    config = get_config()
    points_list = parse_json_list_or_exit(points, "points")

    request: dict[str, Any] = {
        "action": "create_poly_shape",
        "points": points_list,
        "extrudeHeight": height,
    }
    if name:
        request["name"] = name
    if flip_normals:
        request["flipNormals"] = True

    result = run_command("manage_probuilder", _normalize_pb_params(request), config)
    click.echo(format_output(result, config.format))
    if result.get("success"):
        print_success("Created ProBuilder poly shape")


# =============================================================================
# Mesh Info
# =============================================================================

@probuilder.command("info")
@click.argument("target")
@click.option("--include", type=click.Choice(["summary", "faces", "edges", "all"]),
              default="summary", help="Detail level: summary, faces, edges, or all.")
@click.option("--search-method", type=SEARCH_METHOD_CHOICE_TAGGED, default=None)
@handle_unity_errors
def mesh_info(target: str, include: str, search_method: Optional[str]):
    """Get ProBuilder mesh info.

    \\b
    Examples:
        unity-mcp probuilder info "MyCube"
        unity-mcp probuilder info "MyCube" --include faces
        unity-mcp probuilder info "-12345" --search-method by_id --include all
    """
    config = get_config()
    request: dict[str, Any] = {"action": "get_mesh_info", "target": target, "include": include}
    if search_method:
        request["searchMethod"] = search_method

    result = run_command("manage_probuilder", _normalize_pb_params(request), config)
    click.echo(format_output(result, config.format))


# =============================================================================
# Smoothing
# =============================================================================

@probuilder.command("auto-smooth")
@click.argument("target")
@click.option("--angle", type=float, default=30.0, help="Angle threshold in degrees (default: 30).")
@click.option("--search-method", type=SEARCH_METHOD_CHOICE_TAGGED, default=None)
@handle_unity_errors
def auto_smooth(target: str, angle: float, search_method: Optional[str]):
    """Auto-assign smoothing groups by angle threshold.

    \\b
    Examples:
        unity-mcp probuilder auto-smooth "MyCube"
        unity-mcp probuilder auto-smooth "MyCube" --angle 45
    """
    config = get_config()
    request: dict[str, Any] = {
        "action": "auto_smooth",
        "target": target,
        "angleThreshold": angle,
    }
    if search_method:
        request["searchMethod"] = search_method

    result = run_command("manage_probuilder", _normalize_pb_params(request), config)
    click.echo(format_output(result, config.format))
    if result.get("success"):
        print_success(f"Auto-smoothed with angle {angle}°")


@probuilder.command("set-smoothing")
@click.argument("target")
@click.option("--faces", required=True, help="Face indices as JSON array, e.g. '[0,1,2]'.")
@click.option("--group", type=int, required=True, help="Smoothing group (0=hard, 1+=smooth).")
@click.option("--search-method", type=SEARCH_METHOD_CHOICE_TAGGED, default=None)
@handle_unity_errors
def set_smoothing(target: str, faces: str, group: int, search_method: Optional[str]):
    """Set smoothing group on specific faces.

    \\b
    Examples:
        unity-mcp probuilder set-smoothing "MyCube" --faces '[0,1,2]' --group 1
        unity-mcp probuilder set-smoothing "MyCube" --faces '[3,4,5]' --group 0
    """
    config = get_config()
    face_indices = parse_json_list_or_exit(faces, "faces")

    request: dict[str, Any] = {
        "action": "set_smoothing",
        "target": target,
        "faceIndices": face_indices,
        "smoothingGroup": group,
    }
    if search_method:
        request["searchMethod"] = search_method

    result = run_command("manage_probuilder", _normalize_pb_params(request), config)
    click.echo(format_output(result, config.format))
    if result.get("success"):
        print_success(f"Set smoothing group {group}")


# =============================================================================
# Mesh Utilities
# =============================================================================

@probuilder.command("center-pivot")
@click.argument("target")
@click.option("--search-method", type=SEARCH_METHOD_CHOICE_TAGGED, default=None)
@handle_unity_errors
def center_pivot(target: str, search_method: Optional[str]):
    """Move pivot point to mesh bounds center.

    \\b
    Examples:
        unity-mcp probuilder center-pivot "MyCube"
    """
    config = get_config()
    request: dict[str, Any] = {"action": "center_pivot", "target": target}
    if search_method:
        request["searchMethod"] = search_method

    result = run_command("manage_probuilder", _normalize_pb_params(request), config)
    click.echo(format_output(result, config.format))
    if result.get("success"):
        print_success("Pivot centered")


@probuilder.command("freeze-transform")
@click.argument("target")
@click.option("--search-method", type=SEARCH_METHOD_CHOICE_TAGGED, default=None)
@handle_unity_errors
def freeze_transform(target: str, search_method: Optional[str]):
    """Bake position/rotation/scale into vertex data, reset transform.

    \\b
    Examples:
        unity-mcp probuilder freeze-transform "MyCube"
    """
    config = get_config()
    request: dict[str, Any] = {"action": "freeze_transform", "target": target}
    if search_method:
        request["searchMethod"] = search_method

    result = run_command("manage_probuilder", _normalize_pb_params(request), config)
    click.echo(format_output(result, config.format))
    if result.get("success"):
        print_success("Transform frozen")


@probuilder.command("validate")
@click.argument("target")
@click.option("--search-method", type=SEARCH_METHOD_CHOICE_TAGGED, default=None)
@handle_unity_errors
def validate_mesh(target: str, search_method: Optional[str]):
    """Check mesh health (degenerate triangles, unused vertices).

    \\b
    Examples:
        unity-mcp probuilder validate "MyCube"
    """
    config = get_config()
    request: dict[str, Any] = {"action": "validate_mesh", "target": target}
    if search_method:
        request["searchMethod"] = search_method

    result = run_command("manage_probuilder", _normalize_pb_params(request), config)
    click.echo(format_output(result, config.format))


@probuilder.command("repair")
@click.argument("target")
@click.option("--search-method", type=SEARCH_METHOD_CHOICE_TAGGED, default=None)
@handle_unity_errors
def repair_mesh(target: str, search_method: Optional[str]):
    """Auto-fix degenerate triangles and unused vertices.

    \\b
    Examples:
        unity-mcp probuilder repair "MyCube"
    """
    config = get_config()
    request: dict[str, Any] = {"action": "repair_mesh", "target": target}
    if search_method:
        request["searchMethod"] = search_method

    result = run_command("manage_probuilder", _normalize_pb_params(request), config)
    click.echo(format_output(result, config.format))
    if result.get("success"):
        print_success("Mesh repaired")


# =============================================================================
# Raw Command (escape hatch)
# =============================================================================

@probuilder.command("raw")
@click.argument("action")
@click.argument("target", required=False)
@click.option("--params", "-p", default="{}", help="Additional parameters as JSON.")
@click.option("--search-method", type=SEARCH_METHOD_CHOICE_TAGGED, default=None)
@handle_unity_errors
def pb_raw(action: str, target: Optional[str], params: str, search_method: Optional[str]):
    """Execute any ProBuilder action directly.

    \\b
    Actions include:
        create_shape, create_poly_shape,
        extrude_faces, extrude_edges, bevel_edges, subdivide,
        delete_faces, bridge_edges, connect_elements, detach_faces,
        flip_normals, merge_faces, combine_meshes,
        merge_vertices, split_vertices, move_vertices,
        set_face_material, set_face_color, set_face_uvs,
        get_mesh_info, convert_to_probuilder,
        set_smoothing, auto_smooth,
        center_pivot, freeze_transform, validate_mesh, repair_mesh

    \\b
    Examples:
        unity-mcp probuilder raw extrude_faces "MyCube" --params '{"faceIndices": [0], "distance": 1.0}'
        unity-mcp probuilder raw bevel_edges "MyCube" --params '{"edgeIndices": [0,1], "amount": 0.2}'
        unity-mcp probuilder raw set_face_material "MyCube" --params '{"faceIndices": [0], "materialPath": "Assets/Materials/Red.mat"}'
    """
    config = get_config()
    extra = parse_json_dict_or_exit(params, "params")

    request: dict[str, Any] = {"action": action}
    if target:
        request["target"] = target
    if search_method:
        request["searchMethod"] = search_method
    request.update(extra)

    result = run_command("manage_probuilder", _normalize_pb_params(request), config)
    click.echo(format_output(result, config.format))
