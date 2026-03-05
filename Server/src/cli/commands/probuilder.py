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
    """Create a ProBuilder shape with real dimensions.

    \\b
    Shape types: Cube, Cylinder, Sphere, Plane, Cone, Torus, Pipe, Arch,
                 Stair, CurvedStair, Door, Prism

    Each shape accepts type-specific dimension parameters:
      Cube/Prism:      width, height, depth (or size for uniform)
      Cylinder:        radius, height, segments/axisDivisions, heightCuts
      Cone:            radius, height, segments/subdivAxis
      Sphere:          radius, subdivisions
      Torus:           innerRadius, outerRadius, rows, columns
      Pipe:            radius, height, thickness, segments
      Plane:           width, height, widthCuts, heightCuts
      Stair:           width, height, depth, steps, buildSides
      CurvedStair:     width, height, innerRadius, circumference, steps
      Arch:            radius, width, depth, angle, radialCuts
      Door:            width, height, depth, ledgeHeight, legWidth

    \\b
    Examples:
        unity-mcp probuilder create-shape Cube
        unity-mcp probuilder create-shape Cube --params '{"width": 2, "height": 3, "depth": 1}'
        unity-mcp probuilder create-shape Cylinder --params '{"radius": 0.5, "height": 3, "segments": 16}'
        unity-mcp probuilder create-shape Torus --name "MyTorus" --params '{"innerRadius": 0.2, "outerRadius": 1}'
        unity-mcp probuilder create-shape Stair --position 0 0 5 --params '{"steps": 10, "width": 2}'
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
# Mesh Editing
# =============================================================================

@probuilder.command("extrude-faces")
@click.argument("target")
@click.option("--faces", required=True, help="Face indices as JSON array, e.g. '[0,1,2]'.")
@click.option("--distance", "-d", type=float, default=0.5, help="Extrusion distance.")
@click.option("--method", type=click.Choice(["FaceNormal", "VertexNormal", "IndividualFaces"]),
              default="FaceNormal", help="Extrusion method.")
@click.option("--search-method", type=SEARCH_METHOD_CHOICE_TAGGED, default=None)
@handle_unity_errors
def extrude_faces(target: str, faces: str, distance: float, method: str,
                  search_method: Optional[str]):
    """Extrude faces of a ProBuilder mesh.

    \\b
    Examples:
        unity-mcp probuilder extrude-faces "MyCube" --faces '[0]' --distance 1.0
        unity-mcp probuilder extrude-faces "MyCube" --faces '[0,1,2]' --method IndividualFaces
    """
    config = get_config()
    face_indices = parse_json_list_or_exit(faces, "faces")

    request: dict[str, Any] = {
        "action": "extrude_faces",
        "target": target,
        "faceIndices": face_indices,
        "distance": distance,
        "method": method,
    }
    if search_method:
        request["searchMethod"] = search_method

    result = run_command("manage_probuilder", _normalize_pb_params(request), config)
    click.echo(format_output(result, config.format))
    if result.get("success"):
        print_success(f"Extruded faces by {distance}")


@probuilder.command("extrude-edges")
@click.argument("target")
@click.option("--edges", required=True,
              help='Edge indices as JSON array [0,1] or vertex pairs [{"a":0,"b":1}].')
@click.option("--distance", "-d", type=float, default=0.5, help="Extrusion distance.")
@click.option("--as-group/--no-group", default=True, help="Extrude as group.")
@click.option("--search-method", type=SEARCH_METHOD_CHOICE_TAGGED, default=None)
@handle_unity_errors
def extrude_edges(target: str, edges: str, distance: float, as_group: bool,
                  search_method: Optional[str]):
    """Extrude edges of a ProBuilder mesh.

    \\b
    Edges can be specified as flat indices into the unique edge list,
    or as vertex pairs [{a: 0, b: 1}, ...].

    \\b
    Examples:
        unity-mcp probuilder extrude-edges "MyCube" --edges '[0,1]' --distance 0.5
        unity-mcp probuilder extrude-edges "MyCube" --edges '[{"a":0,"b":1}]' --distance 1
    """
    config = get_config()
    import json
    try:
        parsed = json.loads(edges)
    except json.JSONDecodeError:
        print_error("Invalid JSON for edges parameter")
        raise SystemExit(1)

    request: dict[str, Any] = {
        "action": "extrude_edges",
        "target": target,
        "distance": distance,
        "asGroup": as_group,
    }

    if parsed and isinstance(parsed[0], dict):
        request["edges"] = parsed
    else:
        request["edgeIndices"] = parsed

    if search_method:
        request["searchMethod"] = search_method

    result = run_command("manage_probuilder", _normalize_pb_params(request), config)
    click.echo(format_output(result, config.format))
    if result.get("success"):
        print_success(f"Extruded edges by {distance}")


@probuilder.command("bevel-edges")
@click.argument("target")
@click.option("--edges", required=True,
              help='Edge indices as JSON array [0,1] or vertex pairs [{"a":0,"b":1}].')
@click.option("--amount", "-a", type=float, default=0.1, help="Bevel amount (0-1).")
@click.option("--search-method", type=SEARCH_METHOD_CHOICE_TAGGED, default=None)
@handle_unity_errors
def bevel_edges(target: str, edges: str, amount: float, search_method: Optional[str]):
    """Bevel edges of a ProBuilder mesh.

    \\b
    Examples:
        unity-mcp probuilder bevel-edges "MyCube" --edges '[0,1,2]' --amount 0.2
        unity-mcp probuilder bevel-edges "MyCube" --edges '[{"a":0,"b":1}]' --amount 0.15
    """
    config = get_config()
    import json
    try:
        parsed = json.loads(edges)
    except json.JSONDecodeError:
        print_error("Invalid JSON for edges parameter")
        raise SystemExit(1)

    request: dict[str, Any] = {
        "action": "bevel_edges",
        "target": target,
        "amount": amount,
    }

    if parsed and isinstance(parsed[0], dict):
        request["edges"] = parsed
    else:
        request["edgeIndices"] = parsed

    if search_method:
        request["searchMethod"] = search_method

    result = run_command("manage_probuilder", _normalize_pb_params(request), config)
    click.echo(format_output(result, config.format))
    if result.get("success"):
        print_success(f"Beveled edges with amount {amount}")


@probuilder.command("delete-faces")
@click.argument("target")
@click.option("--faces", required=True, help="Face indices as JSON array, e.g. '[0,1,2]'.")
@click.option("--search-method", type=SEARCH_METHOD_CHOICE_TAGGED, default=None)
@handle_unity_errors
def delete_faces(target: str, faces: str, search_method: Optional[str]):
    """Delete faces from a ProBuilder mesh.

    \\b
    Examples:
        unity-mcp probuilder delete-faces "MyCube" --faces '[0,1]'
    """
    config = get_config()
    face_indices = parse_json_list_or_exit(faces, "faces")

    request: dict[str, Any] = {
        "action": "delete_faces",
        "target": target,
        "faceIndices": face_indices,
    }
    if search_method:
        request["searchMethod"] = search_method

    result = run_command("manage_probuilder", _normalize_pb_params(request), config)
    click.echo(format_output(result, config.format))
    if result.get("success"):
        print_success("Deleted faces")


@probuilder.command("subdivide")
@click.argument("target")
@click.option("--faces", default=None, help="Face indices as JSON array (optional, subdivides all if omitted).")
@click.option("--search-method", type=SEARCH_METHOD_CHOICE_TAGGED, default=None)
@handle_unity_errors
def subdivide(target: str, faces: Optional[str], search_method: Optional[str]):
    """Subdivide faces of a ProBuilder mesh.

    \\b
    Examples:
        unity-mcp probuilder subdivide "MyCube"
        unity-mcp probuilder subdivide "MyCube" --faces '[0,1]'
    """
    config = get_config()

    request: dict[str, Any] = {
        "action": "subdivide",
        "target": target,
    }
    if faces:
        request["faceIndices"] = parse_json_list_or_exit(faces, "faces")
    if search_method:
        request["searchMethod"] = search_method

    result = run_command("manage_probuilder", _normalize_pb_params(request), config)
    click.echo(format_output(result, config.format))
    if result.get("success"):
        print_success("Subdivided mesh")


@probuilder.command("select-faces")
@click.argument("target")
@click.option("--direction", type=click.Choice(["up", "down", "forward", "back", "left", "right"]),
              default=None, help="Select faces by normal direction.")
@click.option("--tolerance", type=float, default=0.7, help="Dot product tolerance for direction (0-1).")
@click.option("--grow-from", default=None, help="Face indices to grow selection from (JSON array).")
@click.option("--grow-angle", type=float, default=-1, help="Max angle for grow selection (-1=any).")
@click.option("--search-method", type=SEARCH_METHOD_CHOICE_TAGGED, default=None)
@handle_unity_errors
def select_faces(target: str, direction: Optional[str], tolerance: float,
                 grow_from: Optional[str], grow_angle: float,
                 search_method: Optional[str]):
    """Select faces by criteria (direction, grow, flood, loop).

    \\b
    Examples:
        unity-mcp probuilder select-faces "MyCube" --direction up
        unity-mcp probuilder select-faces "MyCube" --direction forward --tolerance 0.9
        unity-mcp probuilder select-faces "MyCube" --grow-from '[0]' --grow-angle 45
    """
    config = get_config()

    request: dict[str, Any] = {
        "action": "select_faces",
        "target": target,
    }
    if direction:
        request["direction"] = direction
    if tolerance != 0.7:
        request["tolerance"] = tolerance
    if grow_from:
        request["growFrom"] = parse_json_list_or_exit(grow_from, "grow-from")
    if grow_angle != -1:
        request["growAngle"] = grow_angle
    if search_method:
        request["searchMethod"] = search_method

    result = run_command("manage_probuilder", _normalize_pb_params(request), config)
    click.echo(format_output(result, config.format))


@probuilder.command("move-vertices")
@click.argument("target")
@click.option("--vertices", required=True, help="Vertex indices as JSON array, e.g. '[0,1,2]'.")
@click.option("--offset", nargs=3, type=float, required=True, help="Offset X Y Z.")
@click.option("--search-method", type=SEARCH_METHOD_CHOICE_TAGGED, default=None)
@handle_unity_errors
def move_vertices(target: str, vertices: str, offset, search_method: Optional[str]):
    """Move vertices by an offset.

    \\b
    Examples:
        unity-mcp probuilder move-vertices "MyCube" --vertices '[0,1,2,3]' --offset 0 1 0
    """
    config = get_config()
    vertex_indices = parse_json_list_or_exit(vertices, "vertices")

    request: dict[str, Any] = {
        "action": "move_vertices",
        "target": target,
        "vertexIndices": vertex_indices,
        "offset": list(offset),
    }
    if search_method:
        request["searchMethod"] = search_method

    result = run_command("manage_probuilder", _normalize_pb_params(request), config)
    click.echo(format_output(result, config.format))
    if result.get("success"):
        print_success("Moved vertices")


@probuilder.command("weld-vertices")
@click.argument("target")
@click.option("--vertices", required=True, help="Vertex indices as JSON array.")
@click.option("--radius", "-r", type=float, default=0.01, help="Neighbor radius for welding.")
@click.option("--search-method", type=SEARCH_METHOD_CHOICE_TAGGED, default=None)
@handle_unity_errors
def weld_vertices(target: str, vertices: str, radius: float,
                  search_method: Optional[str]):
    """Weld vertices within a proximity radius.

    \\b
    Examples:
        unity-mcp probuilder weld-vertices "MyCube" --vertices '[0,1,2,3]' --radius 0.1
    """
    config = get_config()
    vertex_indices = parse_json_list_or_exit(vertices, "vertices")

    request: dict[str, Any] = {
        "action": "weld_vertices",
        "target": target,
        "vertexIndices": vertex_indices,
        "radius": radius,
    }
    if search_method:
        request["searchMethod"] = search_method

    result = run_command("manage_probuilder", _normalize_pb_params(request), config)
    click.echo(format_output(result, config.format))
    if result.get("success"):
        print_success("Welded vertices")


@probuilder.command("set-material")
@click.argument("target")
@click.option("--faces", required=True, help="Face indices as JSON array.")
@click.option("--material", "-m", required=True, help="Material asset path.")
@click.option("--search-method", type=SEARCH_METHOD_CHOICE_TAGGED, default=None)
@handle_unity_errors
def set_material(target: str, faces: str, material: str,
                 search_method: Optional[str]):
    """Assign a material to specific faces.

    \\b
    Examples:
        unity-mcp probuilder set-material "MyCube" --faces '[0,1]' --material "Assets/Materials/Red.mat"
    """
    config = get_config()
    face_indices = parse_json_list_or_exit(faces, "faces")

    request: dict[str, Any] = {
        "action": "set_face_material",
        "target": target,
        "faceIndices": face_indices,
        "materialPath": material,
    }
    if search_method:
        request["searchMethod"] = search_method

    result = run_command("manage_probuilder", _normalize_pb_params(request), config)
    click.echo(format_output(result, config.format))
    if result.get("success"):
        print_success("Set material on faces")


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
    Edge data now includes world-space vertex positions and uses deduplicated edges.

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


@probuilder.command("set-pivot")
@click.argument("target")
@click.option("--position", nargs=3, type=float, required=True, help="World position X Y Z.")
@click.option("--search-method", type=SEARCH_METHOD_CHOICE_TAGGED, default=None)
@handle_unity_errors
def set_pivot(target: str, position, search_method: Optional[str]):
    """Set pivot to an arbitrary world position.

    \\b
    Examples:
        unity-mcp probuilder set-pivot "MyCube" --position 0 0 0
        unity-mcp probuilder set-pivot "MyCube" --position 1.5 0 2.3
    """
    config = get_config()
    request: dict[str, Any] = {
        "action": "set_pivot",
        "target": target,
        "position": list(position),
    }
    if search_method:
        request["searchMethod"] = search_method

    result = run_command("manage_probuilder", _normalize_pb_params(request), config)
    click.echo(format_output(result, config.format))
    if result.get("success"):
        print_success("Pivot set")


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
        flip_normals, merge_faces, combine_meshes, merge_objects,
        duplicate_and_flip, create_polygon,
        merge_vertices, weld_vertices, split_vertices, move_vertices,
        insert_vertex, append_vertices_to_edge,
        select_faces,
        set_face_material, set_face_color, set_face_uvs,
        get_mesh_info, convert_to_probuilder,
        set_smoothing, auto_smooth,
        center_pivot, set_pivot, freeze_transform, validate_mesh, repair_mesh

    \\b
    Examples:
        unity-mcp probuilder raw extrude_faces "MyCube" --params '{"faceIndices": [0], "distance": 1.0}'
        unity-mcp probuilder raw bevel_edges "MyCube" --params '{"edges": [{"a":0,"b":1}], "amount": 0.2}'
        unity-mcp probuilder raw detach_faces "MyCube" --params '{"faceIndices": [0], "deleteSourceFaces": true}'
        unity-mcp probuilder raw weld_vertices "MyCube" --params '{"vertexIndices": [0,1,2], "radius": 0.1}'
        unity-mcp probuilder raw select_faces "MyCube" --params '{"direction": "up", "tolerance": 0.9}'
        unity-mcp probuilder raw insert_vertex "MyCube" --params '{"edge": {"a":0,"b":1}, "point": [0.5,0,0]}'
        unity-mcp probuilder raw set_pivot "MyCube" --params '{"position": [0, 0, 0]}'
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
