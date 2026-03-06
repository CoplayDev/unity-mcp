# ProBuilder Workflow Guide

Patterns and best practices for AI-driven ProBuilder mesh editing through MCP for Unity.

## Availability

ProBuilder is an **optional** Unity package (`com.unity.probuilder`). Check `mcpforunity://project/info` or call `manage_probuilder(action="ping")` to verify it's installed before using any ProBuilder tools. If available, **prefer ProBuilder over primitive GameObjects** for any geometry that needs editing, multi-material faces, or non-trivial shapes.

## Core Workflow: Always Get Info First

Before any mesh edit, call `get_mesh_info` with `include='faces'` to understand the geometry:

```python
# Step 1: Get face info with directions
result = manage_probuilder(action="get_mesh_info", target="MyCube",
    properties={"include": "faces"})

# Response includes per-face:
#   index: 0, normal: [0, 1, 0], center: [0, 0.5, 0], direction: "top"
#   index: 1, normal: [0, -1, 0], center: [0, -0.5, 0], direction: "bottom"
#   index: 2, normal: [0, 0, 1], center: [0, 0, 0.5], direction: "front"
#   ...

# Step 2: Use the direction labels to pick faces
# Want to extrude the top? Find the face with direction="top"
manage_probuilder(action="extrude_faces", target="MyCube",
    properties={"faceIndices": [0], "distance": 1.5})
```

### Include Parameter

| Value | Returns | Use When |
|-------|---------|----------|
| `"summary"` | Counts, bounds, materials | Quick check / validation |
| `"faces"` | + normals, centers, directions | Selecting faces for editing |
| `"edges"` | + edge vertex pairs with world positions (max 200) | Edge-based operations |
| `"all"` | Everything | Full mesh analysis |

## Shape Creation

### All 12 Shape Types

```python
# Basic shapes
manage_probuilder(action="create_shape", properties={"shape_type": "Cube", "name": "MyCube"})
manage_probuilder(action="create_shape", properties={"shape_type": "Sphere", "name": "MySphere"})
manage_probuilder(action="create_shape", properties={"shape_type": "Cylinder", "name": "MyCyl"})
manage_probuilder(action="create_shape", properties={"shape_type": "Plane", "name": "MyPlane"})
manage_probuilder(action="create_shape", properties={"shape_type": "Cone", "name": "MyCone"})
manage_probuilder(action="create_shape", properties={"shape_type": "Prism", "name": "MyPrism"})

# Parametric shapes
manage_probuilder(action="create_shape", properties={
    "shape_type": "Torus", "name": "MyTorus",
    "rows": 16, "columns": 24, "innerRadius": 0.5, "outerRadius": 1.0
})
manage_probuilder(action="create_shape", properties={
    "shape_type": "Pipe", "name": "MyPipe",
    "radius": 1.0, "height": 2.0, "thickness": 0.2
})
manage_probuilder(action="create_shape", properties={
    "shape_type": "Arch", "name": "MyArch",
    "radius": 2.0, "angle": 180, "segments": 12
})

# Architectural shapes
manage_probuilder(action="create_shape", properties={
    "shape_type": "Stair", "name": "MyStairs", "steps": 10
})
manage_probuilder(action="create_shape", properties={
    "shape_type": "CurvedStair", "name": "Spiral", "steps": 12
})
manage_probuilder(action="create_shape", properties={
    "shape_type": "Door", "name": "MyDoor"
})

# Custom polygon
manage_probuilder(action="create_poly_shape", properties={
    "points": [[0,0,0], [5,0,0], [5,0,5], [2.5,0,7], [0,0,5]],
    "extrudeHeight": 3.0, "name": "Pentagon"
})
```

## Common Editing Operations

### Extrude a Roof

```python
# 1. Create a building base
manage_probuilder(action="create_shape", properties={
    "shape_type": "Cube", "name": "Building", "size": [4, 3, 6]
})

# 2. Find the top face
info = manage_probuilder(action="get_mesh_info", target="Building",
    properties={"include": "faces"})
# Find face with direction="top" -> e.g. index 2

# 3. Extrude upward for a flat roof extension
manage_probuilder(action="extrude_faces", target="Building",
    properties={"faceIndices": [2], "distance": 0.5})
```

### Cut a Hole (Delete Faces)

```python
# 1. Get face info
info = manage_probuilder(action="get_mesh_info", target="Wall",
    properties={"include": "faces"})
# Find the face with direction="front" -> e.g. index 4

# 2. Subdivide to create more faces
manage_probuilder(action="subdivide", target="Wall",
    properties={"faceIndices": [4]})

# 3. Get updated face info (indices changed after subdivide!)
info = manage_probuilder(action="get_mesh_info", target="Wall",
    properties={"include": "faces"})

# 4. Delete the center face(s) for the hole
manage_probuilder(action="delete_faces", target="Wall",
    properties={"faceIndices": [6]})
```

### Bevel Edges

```python
# Get edge info
info = manage_probuilder(action="get_mesh_info", target="MyCube",
    properties={"include": "edges"})

# Bevel specific edges
manage_probuilder(action="bevel_edges", target="MyCube",
    properties={"edgeIndices": [0, 1, 2, 3], "amount": 0.1})
```

### Detach Faces to New Object

```python
# Detach and keep original (default)
manage_probuilder(action="detach_faces", target="MyCube",
    properties={"faceIndices": [0, 1], "deleteSourceFaces": False})

# Detach and remove from source
manage_probuilder(action="detach_faces", target="MyCube",
    properties={"faceIndices": [0, 1], "deleteSourceFaces": True})
```

### Select Faces by Direction

```python
# Select all upward-facing faces
manage_probuilder(action="select_faces", target="MyMesh",
    properties={"direction": "up", "tolerance": 0.7})

# Grow selection from a seed face
manage_probuilder(action="select_faces", target="MyMesh",
    properties={"growFrom": [0], "growAngle": 45})
```

### Double-Sided Geometry

```python
# Create inside faces for a room (duplicate and flip normals)
manage_probuilder(action="duplicate_and_flip", target="Room",
    properties={"faceIndices": [0, 1, 2, 3, 4, 5]})
```

### Create Polygon from Existing Vertices

```python
# Connect existing vertices into a new face (auto-finds winding order)
manage_probuilder(action="create_polygon", target="MyMesh",
    properties={"vertexIndices": [0, 3, 7, 4]})
```

## Vertex Operations

```python
# Move vertices by offset
manage_probuilder(action="move_vertices", target="MyCube",
    properties={"vertexIndices": [0, 1, 2, 3], "offset": [0, 1, 0]})

# Weld nearby vertices (proximity-based merge)
manage_probuilder(action="weld_vertices", target="MyCube",
    properties={"vertexIndices": [0, 1, 2, 3], "radius": 0.1})

# Insert vertex on an edge
manage_probuilder(action="insert_vertex", target="MyCube",
    properties={"edge": {"a": 0, "b": 1}, "point": [0.5, 0, 0]})

# Add evenly-spaced points along edges
manage_probuilder(action="append_vertices_to_edge", target="MyCube",
    properties={"edgeIndices": [0, 1], "count": 3})
```

## Smoothing Workflow

### Auto-Smooth (Recommended Default)

```python
# Apply auto-smoothing with default 30 degree threshold
manage_probuilder(action="auto_smooth", target="MyMesh",
    properties={"angleThreshold": 30})
```

- **Low angle (15-25)**: More hard edges, faceted look
- **Medium angle (30-45)**: Good default, smooth curves + sharp corners
- **High angle (60-80)**: Very smooth, only sharpest edges remain hard

### Manual Smoothing Groups

```python
# Set specific faces to smooth group 1
manage_probuilder(action="set_smoothing", target="MyMesh",
    properties={"faceIndices": [0, 1, 2], "smoothingGroup": 1})

# Set other faces to hard edges (group 0)
manage_probuilder(action="set_smoothing", target="MyMesh",
    properties={"faceIndices": [3, 4, 5], "smoothingGroup": 0})
```

## Mesh Cleanup Pattern

After editing, always clean up:

```python
# 1. Center the pivot (important after extrusions that shift geometry)
manage_probuilder(action="center_pivot", target="MyMesh")

# 2. Optionally freeze transform if you moved/rotated the object
manage_probuilder(action="freeze_transform", target="MyMesh")

# 3. Validate mesh health
result = manage_probuilder(action="validate_mesh", target="MyMesh")
# Check result.data.healthy -- if false, repair

# 4. Auto-repair if needed
manage_probuilder(action="repair_mesh", target="MyMesh")
```

## Building Complex Objects with ProBuilder

When ProBuilder is available, prefer it over primitive GameObjects for complex geometry. ProBuilder lets you create, edit, and combine shapes into detailed objects without external 3D tools.

### Example: Simple House

```python
# 1. Create base building
manage_probuilder(action="create_shape", properties={
    "shape_type": "Cube", "name": "House", "width": 6, "height": 3, "depth": 8
})

# 2. Get face info to find the top face
info = manage_probuilder(action="get_mesh_info", target="House",
    properties={"include": "faces"})
# Find direction="top" -> e.g. index 2

# 3. Extrude the top face to create a flat raised section
manage_probuilder(action="extrude_faces", target="House",
    properties={"faceIndices": [2], "distance": 0.3})

# 4. Re-query faces, then move top vertices inward to form a ridge
info = manage_probuilder(action="get_mesh_info", target="House",
    properties={"include": "faces"})
# Find the new top face after extrude, get its vertex indices
# Move them to form a peaked roof shape
manage_probuilder(action="move_vertices", target="House",
    properties={"vertexIndices": [0, 1, 2, 3], "offset": [0, 2, 0]})

# 5. Cut a doorway: subdivide front face, delete center sub-face
info = manage_probuilder(action="get_mesh_info", target="House",
    properties={"include": "faces"})
# Find direction="front", subdivide it
manage_probuilder(action="subdivide", target="House",
    properties={"faceIndices": [4]})

# Re-query, find bottom-center face, delete it
info = manage_probuilder(action="get_mesh_info", target="House",
    properties={"include": "faces"})
manage_probuilder(action="delete_faces", target="House",
    properties={"faceIndices": [12]})

# 6. Add a door frame with arch
manage_probuilder(action="create_shape", properties={
    "shape_type": "Door", "name": "Doorway",
    "position": [0, 0, 4], "width": 1.5, "height": 2.5
})

# 7. Add stairs to the door
manage_probuilder(action="create_shape", properties={
    "shape_type": "Stair", "name": "FrontSteps",
    "position": [0, 0, 5], "steps": 3, "width": 2
})

# 8. Smooth organic parts, keep architectural edges sharp
manage_probuilder(action="auto_smooth", target="House",
    properties={"angleThreshold": 30})

# 9. Assign materials per face
manage_probuilder(action="set_face_material", target="House",
    properties={"faceIndices": [0, 1, 2, 3], "materialPath": "Assets/Materials/Brick.mat"})
manage_probuilder(action="set_face_material", target="House",
    properties={"faceIndices": [4, 5], "materialPath": "Assets/Materials/Roof.mat"})

# 10. Cleanup
manage_probuilder(action="center_pivot", target="House")
manage_probuilder(action="validate_mesh", target="House")
```

### Example: Pillared Corridor (Batch)

```python
# Create multiple columns efficiently
batch_execute(commands=[
    {"tool": "manage_probuilder", "params": {
        "action": "create_shape",
        "properties": {"shape_type": "Cylinder", "name": f"Pillar_{i}",
                       "radius": 0.3, "height": 4, "segments": 12,
                       "position": [i * 3, 0, 0]}
    }} for i in range(6)
] + [
    # Floor
    {"tool": "manage_probuilder", "params": {
        "action": "create_shape",
        "properties": {"shape_type": "Plane", "name": "Floor",
                       "width": 18, "height": 6, "position": [7.5, 0, 0]}
    }},
    # Ceiling
    {"tool": "manage_probuilder", "params": {
        "action": "create_shape",
        "properties": {"shape_type": "Plane", "name": "Ceiling",
                       "width": 18, "height": 6, "position": [7.5, 4, 0]}
    }},
])

# Bevel all pillar tops for decoration
for i in range(6):
    info = manage_probuilder(action="get_mesh_info", target=f"Pillar_{i}",
        properties={"include": "edges"})
    # Find top ring edges, bevel them
    manage_probuilder(action="bevel_edges", target=f"Pillar_{i}",
        properties={"edgeIndices": [0, 1, 2, 3], "amount": 0.05})

# Smooth the pillars
for i in range(6):
    manage_probuilder(action="auto_smooth", target=f"Pillar_{i}",
        properties={"angleThreshold": 45})
```

### Example: Custom L-Shaped Room

```python
# Use polygon shape for non-rectangular footprint
manage_probuilder(action="create_poly_shape", properties={
    "points": [
        [0, 0, 0], [10, 0, 0], [10, 0, 6],
        [4, 0, 6], [4, 0, 10], [0, 0, 10]
    ],
    "extrudeHeight": 3.0,
    "name": "LRoom"
})

# Create inside faces for the room interior
info = manage_probuilder(action="get_mesh_info", target="LRoom",
    properties={"include": "faces"})
# Duplicate and flip all faces to make interior visible
all_faces = list(range(info["data"]["faceCount"]))
manage_probuilder(action="duplicate_and_flip", target="LRoom",
    properties={"faceIndices": all_faces})

# Cut a window: subdivide a wall face, delete center
# (follow the get_mesh_info -> subdivide -> get_mesh_info -> delete pattern)
```

### Example: Torus Knot / Decorative Ring

```python
# Create a torus
manage_probuilder(action="create_shape", properties={
    "shape_type": "Torus", "name": "Ring",
    "innerRadius": 0.3, "outerRadius": 2.0,
    "rows": 24, "columns": 32
})

# Smooth it for organic look
manage_probuilder(action="auto_smooth", target="Ring",
    properties={"angleThreshold": 60})

# Assign metallic material
manage_probuilder(action="set_face_material", target="Ring",
    properties={"faceIndices": [], "materialPath": "Assets/Materials/Gold.mat"})
# Note: empty faceIndices = all faces
```

## Batch Patterns

Use `batch_execute` for multi-step workflows to reduce round-trips:

```python
batch_execute(commands=[
    {"tool": "manage_probuilder", "params": {
        "action": "create_shape",
        "properties": {"shape_type": "Cube", "name": "Column1", "position": [0, 0, 0]}
    }},
    {"tool": "manage_probuilder", "params": {
        "action": "create_shape",
        "properties": {"shape_type": "Cube", "name": "Column2", "position": [5, 0, 0]}
    }},
    {"tool": "manage_probuilder", "params": {
        "action": "create_shape",
        "properties": {"shape_type": "Cube", "name": "Column3", "position": [10, 0, 0]}
    }},
])
```

## Known Limitations

### Not Yet Working

These actions exist in the API but have known bugs that prevent them from working correctly:

| Action | Issue | Workaround |
|--------|-------|------------|
| `set_pivot` | Vertex positions don't persist through `ToMesh()`/`RefreshMesh()`. The `positions` property setter is overwritten when ProBuilder rebuilds the mesh. Needs `SetVertices(IList<Vertex>)` or direct `m_Positions` field access. | Use `center_pivot` instead, or position objects via Transform. |
| `convert_to_probuilder` | `MeshImporter` constructor throws internally. May need ProBuilder's editor-only `ProBuilderize` API instead of runtime `MeshImporter`. | Create shapes natively with `create_shape` or `create_poly_shape` instead of converting existing meshes. |

### General Limitations

- Face indices are **not stable** across edits -- always re-query `get_mesh_info` after any modification
- Edge data is capped at **200 edges** in `get_mesh_info` results
- Face data is capped at **100 faces** in `get_mesh_info` results
- `subdivide` uses `ConnectElements.Connect` internally (ProBuilder has no public `Subdivide` API), which connects face midpoints rather than traditional quad subdivision

## Key Rules

1. **Always get_mesh_info before editing** -- face indices are not stable across edits
2. **Re-query after modifications** -- subdivide, extrude, delete all change face indices
3. **Use direction labels** -- don't guess face indices, use the direction field
4. **Cleanup after editing** -- center_pivot + validate is good practice
5. **Auto-smooth for organic shapes** -- 30 degrees is a good default
6. **Prefer ProBuilder over primitives** -- when the package is available and you need editable geometry
7. **Use batch_execute** -- for creating multiple shapes or repetitive operations
8. **Screenshot to verify** -- use `manage_scene(action="screenshot", include_image=True)` to check visual results after complex edits
