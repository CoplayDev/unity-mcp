# Unity-MCP Tools Reference

Complete reference for all MCP tools. Each tool includes parameters, types, and usage examples.

## Table of Contents

- [Infrastructure Tools](#infrastructure-tools)
- [Scene Tools](#scene-tools)
- [GameObject Tools](#gameobject-tools)
- [Script Tools](#script-tools)
- [Asset Tools](#asset-tools)
- [Material & Shader Tools](#material--shader-tools)
- [Editor Control Tools](#editor-control-tools)
- [Testing Tools](#testing-tools)

---

## Infrastructure Tools

### batch_execute

Execute multiple MCP commands in a single batch (10-100x faster).

```python
batch_execute(
    commands=[                    # list[dict], required, max 25
        {"tool": "tool_name", "params": {...}},
        ...
    ],
    parallel=False,              # bool, optional - run read-only ops in parallel
    fail_fast=False,             # bool, optional - stop on first failure
    max_parallelism=None         # int, optional - max parallel workers
)
```

### set_active_instance

Route commands to a specific Unity instance (multi-instance workflows).

```python
set_active_instance(
    instance="ProjectName@abc123"  # str, required - Name@hash or hash prefix
)
```

### refresh_unity

Refresh asset database and trigger script compilation.

```python
refresh_unity(
    mode="if_dirty",             # "if_dirty" | "force"
    scope="all",                 # "assets" | "scripts" | "all"
    compile="none",              # "none" | "request"
    wait_for_ready=True          # bool - wait until editor ready
)
```

---

## Scene Tools

### manage_scene

Scene CRUD operations and hierarchy queries.

```python
# Get hierarchy (paginated)
manage_scene(
    action="get_hierarchy",
    page_size=50,                # int, default 50, max 500
    cursor=0,                    # int, pagination cursor
    parent=None,                 # str|int, optional - filter by parent
    include_transform=False      # bool - include local transforms
)

# Screenshot
manage_scene(action="screenshot")  # Returns base64 PNG

# Other actions
manage_scene(action="get_active")        # Current scene info
manage_scene(action="get_build_settings") # Build settings
manage_scene(action="create", name="NewScene", path="Assets/Scenes/")
manage_scene(action="load", path="Assets/Scenes/Main.unity")
manage_scene(action="save")
```

### find_gameobjects

Search for GameObjects (returns instance IDs only).

```python
find_gameobjects(
    search_term="Player",        # str, required
    search_method="by_name",     # "by_name"|"by_tag"|"by_layer"|"by_component"|"by_path"|"by_id"
    include_inactive=False,      # bool|str
    page_size=50,                # int, default 50, max 500
    cursor=0                     # int, pagination cursor
)
# Returns: {"ids": [12345, 67890], "next_cursor": 50, ...}
```

---

## GameObject Tools

### manage_gameobject

Create, modify, delete, duplicate GameObjects.

```python
# Create
manage_gameobject(
    action="create",
    name="MyCube",               # str, required
    primitive_type="Cube",       # "Cube"|"Sphere"|"Capsule"|"Cylinder"|"Plane"|"Quad"
    position=[0, 1, 0],          # list[float] or JSON string "[0,1,0]"
    rotation=[0, 45, 0],         # euler angles
    scale=[1, 1, 1],
    components_to_add=["Rigidbody", "BoxCollider"],
    save_as_prefab=False,
    prefab_path="Assets/Prefabs/MyCube.prefab"
)

# Modify
manage_gameobject(
    action="modify",
    target="Player",             # name, path, or instance ID
    search_method="by_name",     # how to find target
    position=[10, 0, 0],
    rotation=[0, 90, 0],
    scale=[2, 2, 2],
    set_active=True,
    layer="Player",
    components_to_add=["AudioSource"],
    components_to_remove=["OldComponent"],
    component_properties={       # nested dict for property setting
        "Rigidbody": {
            "mass": 10.0,
            "useGravity": True
        }
    }
)

# Delete
manage_gameobject(action="delete", target="OldObject")

# Duplicate
manage_gameobject(
    action="duplicate",
    target="Player",
    new_name="Player2",
    offset=[5, 0, 0]             # position offset from original
)

# Move relative
manage_gameobject(
    action="move_relative",
    target="Player",
    reference_object="Enemy",    # optional reference
    direction="left",            # "left"|"right"|"up"|"down"|"forward"|"back"
    distance=5.0,
    world_space=True
)
```

### manage_components

Add, remove, or set properties on components.

```python
# Add component
manage_components(
    action="add",
    target=12345,                # instance ID (preferred) or name
    component_type="Rigidbody",
    search_method="by_id"
)

# Remove component
manage_components(
    action="remove",
    target="Player",
    component_type="OldScript"
)

# Set single property
manage_components(
    action="set_property",
    target=12345,
    component_type="Rigidbody",
    property="mass",
    value=5.0
)

# Set multiple properties
manage_components(
    action="set_property",
    target=12345,
    component_type="Transform",
    properties={
        "position": [1, 2, 3],
        "localScale": [2, 2, 2]
    }
)
```

---

## Script Tools

### create_script

Create a new C# script.

```python
create_script(
    path="Assets/Scripts/MyScript.cs",  # str, required
    contents='''using UnityEngine;

public class MyScript : MonoBehaviour
{
    void Start() { }
    void Update() { }
}''',
    script_type="MonoBehaviour",  # optional hint
    namespace="MyGame"            # optional namespace
)
```

### script_apply_edits

Apply structured edits to C# scripts (safer than raw text edits).

```python
script_apply_edits(
    name="MyScript",             # script name (no .cs)
    path="Assets/Scripts",       # folder path
    edits=[
        # Replace entire method
        {
            "op": "replace_method",
            "methodName": "Update",
            "replacement": "void Update() { transform.Rotate(Vector3.up); }"
        },
        # Insert new method
        {
            "op": "insert_method",
            "afterMethod": "Start",
            "code": "void OnEnable() { Debug.Log(\"Enabled\"); }"
        },
        # Delete method
        {
            "op": "delete_method",
            "methodName": "OldMethod"
        },
        # Anchor-based insert
        {
            "op": "anchor_insert",
            "anchor": "void Start()",
            "position": "before",  # "before" | "after"
            "text": "// Called before Start\n"
        },
        # Regex replace
        {
            "op": "regex_replace",
            "pattern": "Debug\\.Log\\(",
            "text": "Debug.LogWarning("
        },
        # Prepend/append to file
        {"op": "prepend", "text": "// File header\n"},
        {"op": "append", "text": "\n// File footer"}
    ]
)
```

### apply_text_edits

Apply precise character-position edits (1-indexed lines/columns).

```python
apply_text_edits(
    uri="mcpforunity://path/Assets/Scripts/MyScript.cs",
    edits=[
        {
            "startLine": 10,
            "startCol": 5,
            "endLine": 10,
            "endCol": 20,
            "newText": "replacement text"
        }
    ],
    precondition_sha256="abc123...",  # optional, prevents stale edits
    strict=True                        # optional, stricter validation
)
```

### validate_script

Check script for syntax/semantic errors.

```python
validate_script(
    uri="mcpforunity://path/Assets/Scripts/MyScript.cs",
    level="standard",            # "basic" | "standard"
    include_diagnostics=True     # include full error details
)
```

### get_sha

Get file hash without content (for preconditions).

```python
get_sha(uri="mcpforunity://path/Assets/Scripts/MyScript.cs")
# Returns: {"sha256": "...", "lengthBytes": 1234, "lastModifiedUtc": "..."}
```

### delete_script

Delete a script file.

```python
delete_script(uri="mcpforunity://path/Assets/Scripts/OldScript.cs")
```

---

## Asset Tools

### manage_asset

Asset operations: search, import, create, modify, delete.

```python
# Search assets (paginated)
manage_asset(
    action="search",
    path="Assets",               # search scope
    search_pattern="*.prefab",   # glob or "t:MonoScript" filter
    filter_type="Prefab",        # optional type filter
    page_size=25,                # keep small to avoid large payloads
    page_number=1,               # 1-based
    generate_preview=False       # avoid base64 bloat
)

# Get asset info
manage_asset(action="get_info", path="Assets/Prefabs/Player.prefab")

# Create asset
manage_asset(
    action="create",
    path="Assets/Materials/NewMaterial.mat",
    asset_type="Material",
    properties={"color": [1, 0, 0, 1]}
)

# Duplicate/move/rename
manage_asset(action="duplicate", path="Assets/A.prefab", destination="Assets/B.prefab")
manage_asset(action="move", path="Assets/A.prefab", destination="Assets/Prefabs/A.prefab")
manage_asset(action="rename", path="Assets/A.prefab", destination="Assets/B.prefab")

# Create folder
manage_asset(action="create_folder", path="Assets/NewFolder")

# Delete
manage_asset(action="delete", path="Assets/OldAsset.asset")
```

### manage_prefabs

Headless prefab operations.

```python
# Get prefab info
manage_prefabs(action="get_info", prefab_path="Assets/Prefabs/Player.prefab")

# Get prefab hierarchy
manage_prefabs(action="get_hierarchy", prefab_path="Assets/Prefabs/Player.prefab")

# Create prefab from scene GameObject
manage_prefabs(
    action="create_from_gameobject",
    target="Player",             # GameObject in scene
    prefab_path="Assets/Prefabs/Player.prefab",
    allow_overwrite=False
)

# Modify prefab contents (headless)
manage_prefabs(
    action="modify_contents",
    prefab_path="Assets/Prefabs/Player.prefab",
    target="ChildObject",        # object within prefab
    position=[0, 1, 0],
    components_to_add=["AudioSource"]
)
```

---

## Material & Shader Tools

### manage_material

Create and modify materials.

```python
# Create material
manage_material(
    action="create",
    material_path="Assets/Materials/Red.mat",
    shader="Standard",
    properties={"_Color": [1, 0, 0, 1]}
)

# Get material info
manage_material(action="get_material_info", material_path="Assets/Materials/Red.mat")

# Set shader property
manage_material(
    action="set_material_shader_property",
    material_path="Assets/Materials/Red.mat",
    property="_Metallic",
    value=0.8
)

# Set color
manage_material(
    action="set_material_color",
    material_path="Assets/Materials/Red.mat",
    property="_BaseColor",
    color=[0, 1, 0, 1]           # RGBA
)

# Assign to renderer
manage_material(
    action="assign_material_to_renderer",
    target="MyCube",
    material_path="Assets/Materials/Red.mat",
    slot=0                       # material slot index
)

# Set renderer color directly
manage_material(
    action="set_renderer_color",
    target="MyCube",
    color=[1, 0, 0, 1],
    mode="instance"              # "shared"|"instance"|"property_block"
)
```

### manage_texture

Create procedural textures.

```python
manage_texture(
    action="create",
    path="Assets/Textures/Checker.png",
    width=64,
    height=64,
    fill_color=[255, 255, 255, 255]  # or [1.0, 1.0, 1.0, 1.0]
)

# Apply pattern
manage_texture(
    action="apply_pattern",
    path="Assets/Textures/Checker.png",
    pattern="checkerboard",      # "checkerboard"|"stripes"|"dots"|"grid"|"brick"
    palette=[[0,0,0,255], [255,255,255,255]],
    pattern_size=8
)

# Apply gradient
manage_texture(
    action="apply_gradient",
    path="Assets/Textures/Gradient.png",
    gradient_type="linear",      # "linear"|"radial"
    gradient_angle=45,
    palette=[[255,0,0,255], [0,0,255,255]]
)
```

---

## Editor Control Tools

### manage_editor

Control Unity Editor state.

```python
manage_editor(action="play")               # Enter play mode
manage_editor(action="pause")              # Pause play mode
manage_editor(action="stop")               # Exit play mode

manage_editor(action="set_active_tool", tool_name="Move")  # Move/Rotate/Scale/etc.

manage_editor(action="add_tag", tag_name="Enemy")
manage_editor(action="remove_tag", tag_name="OldTag")

manage_editor(action="add_layer", layer_name="Projectiles")
manage_editor(action="remove_layer", layer_name="OldLayer")
```

### execute_menu_item

Execute any Unity menu item.

```python
execute_menu_item(menu_path="File/Save Project")
execute_menu_item(menu_path="GameObject/3D Object/Cube")
execute_menu_item(menu_path="Window/General/Console")
```

### read_console

Read or clear Unity console messages.

```python
# Get recent messages
read_console(
    action="get",
    types=["error", "warning", "log"],  # or ["all"]
    count=10,                    # max messages (ignored with paging)
    filter_text="NullReference", # optional text filter
    since_timestamp="2024-01-01T00:00:00Z",  # optional time filter
    page_size=50,
    cursor=0,
    format="detailed",           # "plain"|"detailed"|"json"
    include_stacktrace=True
)

# Clear console
read_console(action="clear")
```

---

## Testing Tools

### run_tests

Start async test execution.

```python
result = run_tests(
    mode="EditMode",             # "EditMode"|"PlayMode"
    test_names=["MyTests.TestA", "MyTests.TestB"],  # specific tests
    group_names=["Integration*"],  # regex patterns
    category_names=["Unit"],     # NUnit categories
    assembly_names=["Tests"],    # assembly filter
    include_failed_tests=True,   # include failure details
    include_details=False        # include all test details
)
# Returns: {"job_id": "abc123", ...}
```

### get_test_job

Poll test job status.

```python
result = get_test_job(
    job_id="abc123",
    wait_timeout=60,             # wait up to N seconds
    include_failed_tests=True,
    include_details=False
)
# Returns: {"status": "complete"|"running"|"failed", "results": {...}}
```

---

## Search Tools

### find_in_file

Search file contents with regex.

```python
find_in_file(
    uri="mcpforunity://path/Assets/Scripts/MyScript.cs",
    pattern="public void \\w+",  # regex pattern
    max_results=200,
    ignore_case=True
)
# Returns: line numbers, content excerpts, match positions
```

---

## Custom Tools

### execute_custom_tool

Execute project-specific custom tools.

```python
execute_custom_tool(
    tool_name="my_custom_tool",
    parameters={"param1": "value", "param2": 42}
)
```

Discover available custom tools via `mcpforunity://custom-tools` resource.
