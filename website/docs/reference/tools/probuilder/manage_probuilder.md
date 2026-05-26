---
title: manage_probuilder
sidebar_label: manage_probuilder
description: "Manage ProBuilder meshes for in-editor 3D modeling."
---

# `manage_probuilder`

> **Auto-generated** from the Python tool registry. Do not hand-edit outside `<!-- examples:start --><!-- examples:end -->` blocks — the generator (`tools/generate_docs_reference.py`) will overwrite them.

**Group:** `probuilder` &nbsp;·&nbsp; **Module:** `services.tools.manage_probuilder`

## Description

Manage ProBuilder meshes for in-editor 3D modeling. Requires com.unity.probuilder package.

SHAPE CREATION:
- create_shape: Create a ProBuilder primitive (shape_type: Cube/Cylinder/Sphere/Plane/Cone/Torus/Pipe/Arch/Stair/CurvedStair/Door/Prism). Shape-specific params in properties (size, radius, height, depth, width, segments, rows, columns, innerRadius, outerRadius, etc.).
- create_poly_shape: Create mesh from 2D polygon footprint (points: [[x,y,z],...], extrudeHeight, flipNormals).

MESH EDITING:
- extrude_faces: Extrude faces outward (faceIndices, distance, method: FaceNormal/VertexNormal/IndividualFaces).
- extrude_edges: Extrude edges (edgeIndices or edges [{a,b},...], distance, asGroup).
- bevel_edges: Bevel edges (edgeIndices or edges [{a,b},...], amount 0-1).
- subdivide: Subdivide faces (faceIndices optional, all if omitted).
- delete_faces: Delete faces (faceIndices).
- bridge_edges: Bridge two open edges (edgeA, edgeB as {a,b} pairs, allowNonManifold).
- connect_elements: Connect edges or faces (edgeIndices/edges or faceIndices).
- detach_faces: Detach faces (faceIndices, deleteSourceFaces: bool).
- flip_normals: Flip face normals (faceIndices).
- merge_faces: Merge faces into one (faceIndices).
- combine_meshes: Combine multiple ProBuilder objects (targets: list of GameObjects).
- merge_objects: Merge objects into one ProBuilder mesh (targets list, auto-converts).
- duplicate_and_flip: Create double-sided geometry (faceIndices).
- create_polygon: Connect existing vertices into a new face (vertexIndices, unordered).

VERTEX OPERATIONS:
- merge_vertices: Collapse vertices to single point (vertexIndices, collapseToFirst).
- weld_vertices: Weld vertices within proximity radius (vertexIndices, radius).
- split_vertices: Split shared vertices (vertexIndices).
- move_vertices: Translate vertices (vertexIndices, offset [x,y,z]).
- insert_vertex: Insert vertex on edge ({a,b}) or face (faceIndex) at point [x,y,z].
- append_vertices_to_edge: Insert evenly-spaced points on edges (edgeIndices/edges, count).

SELECTION:
- select_faces: Select faces by criteria (direction: up/down/forward/back/left/right, tolerance, growFrom, growAngle, floodFrom, floodAngle, loopFrom, ring). Returns faceIndices array for use with other actions.

UV & MATERIALS:
- set_face_material: Assign material to faces (faceIndices optional — all faces when omitted, materialPath).
- set_face_color: Set vertex color on faces (faceIndices optional — all faces when omitted, color [r,g,b,a]).
- set_face_uvs: Set UV auto-unwrap params (faceIndices optional — all faces when omitted, scale, offset, rotation, flipU, flipV).

QUERY:
- get_mesh_info: Get ProBuilder mesh details. Use include parameter to control detail level: 'summary' (default: counts, bounds, materials), 'faces' (+ face normals/centers/directions), 'edges' (+ edge vertex pairs), 'all' (everything). Each face includes direction ('top','bottom','front','back','left','right') for semantic selection.
- convert_to_probuilder: Convert a standard Unity mesh into ProBuilder for editing.

SMOOTHING:
- set_smoothing: Set smoothing group on faces (faceIndices, smoothingGroup: 0=hard, 1+=smooth).
- auto_smooth: Auto-assign smoothing groups by angle (angleThreshold: default 30).

MESH UTILITIES:
- center_pivot: Move pivot point to mesh bounds center.
- set_pivot: Set pivot to arbitrary world position (position [x,y,z]).
- freeze_transform: Bake position/rotation/scale into vertex data, reset transform.
- validate_mesh: Check mesh health (degenerate triangles, unused vertices). Read-only.
- repair_mesh: Auto-fix degenerate triangles and unused vertices.

WORKFLOW TIP: Call get_mesh_info with include='faces' to see face normals and directions before editing. Each face shows its direction ('top','bottom','front','back','left','right') so you can pick the right indices for operations like extrude_faces or delete_faces.

## Parameters

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `action` | `str` | yes | Action to perform. |
| `target` | `str \| None` | — | Target GameObject (name/path/id). |
| `search_method` | `Literal['by_id', 'by_name', 'by_path', 'by_tag', 'by_layer'] \| None` | — | How to find the target GameObject. |
| `properties` | `dict[str, Any] \| str \| None` | — | Action-specific parameters (dict or JSON string). |

## Returns

A `dict` containing the Unity response. The exact shape depends on the action.

## Examples

<!-- examples:start -->
*No examples yet. Add usage examples here — they will be preserved across regenerations.*
<!-- examples:end -->

