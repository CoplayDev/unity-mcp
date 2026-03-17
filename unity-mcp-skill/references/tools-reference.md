# Unity-MCP Tools Reference

Complete reference for all MCP tools. Each tool includes parameters, types, and usage examples.

> **Template warning:** Examples in this file are skill templates and may be inaccurate for some Unity versions, packages, or project setups. Validate parameters and payload shapes against your active tool schema and runtime behavior.

## Table of Contents

- [Infrastructure Tools](#infrastructure-tools)
- [Scene Tools](#scene-tools)
- [GameObject Tools](#gameobject-tools)
- [Script Tools](#script-tools)
- [Asset Tools](#asset-tools)
- [Material & Shader Tools](#material--shader-tools)
- [UI Tools](#ui-tools)
- [Editor Control Tools](#editor-control-tools)
- [Testing Tools](#testing-tools)
- [Camera Tools](#camera-tools)
- [Graphics Tools](#graphics-tools)
- [Package Tools](#package-tools)
- [ProBuilder Tools](#probuilder-tools)
- [Docs Tools](#docs-tools)

---

## Project Info Resource

Read `mcpforunity://project/info` to detect project capabilities before making assumptions about UI, input, or rendering setup.

**Returned fields:**

| Field | Type | Description |
|-------|------|-------------|
| `projectRoot` | string | Absolute path to project root |
| `projectName` | string | Project folder name |
| `unityVersion` | string | e.g. `"2022.3.20f1"` |
| `platform` | string | Active build target e.g. `"StandaloneWindows64"` |
| `assetsPath` | string | Absolute path to Assets folder |
| `renderPipeline` | string | `"BuiltIn"`, `"Universal"`, `"HighDefinition"`, or `"Custom"` |
| `activeInputHandler` | string | `"Old"`, `"New"`, or `"Both"` |
| `packages.ugui` | bool | `com.unity.ugui` installed (Canvas, Image, Button, etc.) |
| `packages.textmeshpro` | bool | `com.unity.textmeshpro` installed (TMP_Text, TMP_InputField) |
| `packages.inputsystem` | bool | `com.unity.inputsystem` installed (InputAction, PlayerInput) |
| `packages.uiToolkit` | bool | Always `true` for Unity 2021.3+ (UIDocument, VisualElement, UXML/USS) |
| `packages.screenCapture` | bool | `com.unity.modules.screencapture` enabled (ScreenCapture API for screenshots) |

**Key decision points:**

- **UI system**: If `packages.uiToolkit` is true (always for Unity 2021+), use `manage_ui` for UI Toolkit workflows (UXML/USS). If `packages.ugui` is true, use Canvas + uGUI components via `batch_execute`. UI Toolkit is preferred for new UI — it uses a frontend-like workflow (UXML for structure, USS for styling).
- **Text**: If `packages.textmeshpro` is true, use `TextMeshProUGUI` instead of legacy `Text`.
- **Input**: Use `activeInputHandler` to decide EventSystem module — `StandaloneInputModule` (Old) vs `InputSystemUIInputModule` (New). See [workflows.md — Input System](workflows.md#input-system-old-vs-new).
- **Shaders**: Use `renderPipeline` to pick correct shader names — `Standard` (BuiltIn) vs `Universal Render Pipeline/Lit` (URP) vs `HDRP/Lit` (HDRP).

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
    parallel=False,              # bool, optional - advisory only (Unity may still run sequentially)
    fail_fast=False,             # bool, optional - stop on first failure
    max_parallelism=None         # int, optional - max parallel workers
)
```

`batch_execute` is not transactional: earlier commands are not rolled back if a later command fails.

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

Scene CRUD operations, hierarchy queries, screenshots, and scene view control.

```python
# Get hierarchy (paginated)
manage_scene(
    action="get_hierarchy",
    page_size=50,                # int, default 50, max 500
    cursor=0,                    # int, pagination cursor
    parent=None,                 # str|int, optional - filter by parent
    include_transform=False      # bool - include local transforms
)

# Screenshot (file only — saves to Assets/Screenshots/)
manage_camera(action="screenshot")

# Screenshot with inline image (base64 PNG returned to AI)
manage_scene(
    action="screenshot",
    camera="MainCamera",         # str, optional - camera name, path, or instance ID
    include_image=True,          # bool, default False - return base64 PNG inline
    max_resolution=512           # int, optional - downscale cap (default 640)
)

# Batch surround — contact sheet of 6 fixed angles (front/back/left/right/top/bird_eye)
manage_scene(
    action="screenshot",
    batch="surround",            # str - "surround" for 6-angle contact sheet
    max_resolution=256           # int - per-tile resolution cap
)
# Returns: single composite contact sheet image with labeled tiles

# Batch surround centered on a specific target
manage_scene(
    action="screenshot",
    batch="surround",
    view_target="Player",        # str|int|list[float] - center surround on this target
    max_resolution=256
)

# Batch orbit — configurable multi-angle grid around a target
manage_scene(
    action="screenshot",
    batch="orbit",               # str - "orbit" for configurable angle grid
    view_target="Player",        # str|int|list[float] - target to orbit around
    orbit_angles=8,              # int, default 8 - number of azimuth steps
    orbit_elevations=[0, 30],    # list[float], default [0, 30, -15] - vertical angles in degrees
    orbit_distance=10,           # float, optional - camera distance (auto-fit if omitted)
    orbit_fov=60,                # float, default 60 - camera FOV in degrees
    max_resolution=256           # int - per-tile resolution cap
)
# Returns: single composite contact sheet (angles × elevations tiles in a grid)

# Positioned screenshot (temp camera at viewpoint, no file saved)
manage_scene(
    action="screenshot",
    view_target="Enemy",         # str|int|list[float] - target to aim at
    view_position=[0, 10, -10],  # list[float], optional - camera position
    view_rotation=[45, 0, 0],    # list[float], optional - euler angles (overrides view_target aim)
    max_resolution=512
)

# Frame scene view on target
manage_scene(
    action="scene_view_frame",
    scene_view_target="Player"   # str|int - GO name, path, or instance ID to frame
)

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

# Prefab instantiation — place a prefab instance in the scene
manage_gameobject(
    action="create",
    name="Enemy_1",
    prefab_path="Assets/Prefabs/Enemy.prefab",
    position=[5, 0, 3],
    parent="Enemies"                # optional parent GameObject
)
# Smart lookup — just the prefab name works too:
manage_gameobject(action="create", name="Enemy_2", prefab_path="Enemy", position=[10, 0, 3])

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

# Look at target (rotates GO to face a point or another GO)
manage_gameobject(
    action="look_at",
    target="MainCamera",         # the GO to rotate
    look_at_target="Player",     # str (GO name/path) or list[float] world position
    look_at_up=[0, 1, 0]        # optional up vector, default [0,1,0]
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

# Set object reference property (reference another GameObject by name)
manage_components(
    action="set_property",
    target="GameManager",
    component_type="GameManagerScript",
    property="targetObjects",
    value=[{"name": "Flower_1"}, {"name": "Flower_2"}, {"name": "Bee_1"}]
)

# Object reference formats supported:
# - {"name": "ObjectName"}     → Find GameObject in scene by name
# - {"instanceID": 12345}      → Direct instance ID reference
# - {"guid": "abc123..."}      → Asset GUID reference
# - {"path": "Assets/..."}     → Asset path reference
# - "Assets/Prefabs/My.prefab" → String shorthand for asset paths
# - "ObjectName"               → String shorthand for scene name lookup
# - 12345                      → Integer shorthand for instanceID
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
    mode="create_unique"          # Creates a unique .mat asset per object (persistent)
    # Other modes: "property_block" (default, not persistent),
    #              "shared" (mutates shared material — avoid for primitives),
    #              "instance" (runtime only, not persistent)
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

## UI Tools

### manage_ui

Manage Unity UI Toolkit elements: UXML documents, USS stylesheets, UIDocument components, and visual tree inspection.

```python
# Create a UXML file
manage_ui(
    action="create",
    path="Assets/UI/MainMenu.uxml",
    contents='<ui:UXML xmlns:ui="UnityEngine.UIElements"><ui:Label text="Hello" /></ui:UXML>'
)

# Create a USS stylesheet
manage_ui(
    action="create",
    path="Assets/UI/Styles.uss",
    contents=".title { font-size: 32px; color: white; }"
)

# Read a UXML/USS file
manage_ui(
    action="read",
    path="Assets/UI/MainMenu.uxml"
)
# Returns: {"success": true, "data": {"contents": "...", "path": "..."}}

# Update an existing file
manage_ui(
    action="update",
    path="Assets/UI/Styles.uss",
    contents=".title { font-size: 48px; color: yellow; -unity-font-style: bold; }"
)

# Attach UIDocument to a GameObject
manage_ui(
    action="attach_ui_document",
    target="UICanvas",                    # GameObject name or path
    source_asset="Assets/UI/MainMenu.uxml",
    panel_settings="Assets/UI/Panel.asset",  # optional, auto-creates if omitted
    sort_order=0                          # optional, default 0
)

# Create PanelSettings asset
manage_ui(
    action="create_panel_settings",
    path="Assets/UI/Panel.asset",
    scale_mode="ScaleWithScreenSize",     # optional: "ConstantPixelSize"|"ConstantPhysicalSize"|"ScaleWithScreenSize"
    reference_resolution={"width": 1920, "height": 1080}  # optional, for ScaleWithScreenSize
)

# Inspect the visual tree of a UIDocument
manage_ui(
    action="get_visual_tree",
    target="UICanvas",                    # GameObject with UIDocument
    max_depth=10                          # optional, default 10
)
# Returns: hierarchy of VisualElements with type, name, classes, styles, text, children
```

**UI Toolkit workflow:**

1. Create UXML (structure, like HTML) and USS (styling, like CSS) files
2. Create a PanelSettings asset (or let `attach_ui_document` auto-create one)
3. Create an empty GameObject and attach UIDocument with the UXML source
4. Use `get_visual_tree` to inspect the result

**Important:** Always use `<ui:Style>` (with the `ui:` namespace prefix) in UXML files, not bare `<Style>`. UI Builder will fail to open files that use `<Style>` without the prefix.

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

manage_editor(action="open_prefab_stage", prefab_path="Assets/Prefabs/Enemy.prefab")
manage_editor(action="close_prefab_stage")  # Exit prefab editing mode back to main scene

# Package deployment (no confirmation dialog — designed for LLM-driven iteration)
manage_editor(action="deploy_package")     # Copy configured MCPForUnity source into installed package
manage_editor(action="restore_package")    # Revert to pre-deployment backup
```

**Deploy workflow:** Set the source path in MCP for Unity Advanced Settings first. `deploy_package` copies the source into the project's package location, creates a backup, and triggers `AssetDatabase.Refresh`. Follow with `refresh_unity(wait_for_ready=True)` to wait for recompilation.

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

---

## Camera Tools

### manage_camera

Unified camera management (Unity Camera + Cinemachine). Works without Cinemachine using basic Camera; unlocks presets, pipelines, and blending when Cinemachine is installed. Use `ping` to check availability.

**Parameters:**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `action` | string | Yes | Action to perform (see categories below) |
| `target` | string | Sometimes | Target camera (name, path, or instance ID) |
| `search_method` | string | No | `by_id`, `by_name`, `by_path` |
| `properties` | dict \| string | No | Action-specific parameters |

**Screenshot parameters** (for `screenshot` and `screenshot_multiview` actions):

| Parameter | Type | Description |
|-----------|------|-------------|
| `capture_source` | string | `"game_view"` (default) or `"scene_view"` (editor viewport) |
| `view_target` | string\|int\|list | Target to focus on (GO name/path/ID or [x,y,z]). game_view: aims camera; scene_view: frames viewport |
| `camera` | string | Camera to capture from (defaults to Camera.main). game_view only |
| `include_image` | bool | Return base64 PNG inline (default false) |
| `max_resolution` | int | Downscale cap in px (default 640) |
| `batch` | string | `"surround"` (6 angles) or `"orbit"` (configurable grid). game_view only |
| `view_position` | list[float] | World position [x,y,z] to place camera. game_view only |
| `view_rotation` | list[float] | Euler rotation [x,y,z] (overrides view_target). game_view only |

**Actions by category:**

**Setup:**
- `ping` — Check Cinemachine availability and version
- `ensure_brain` — Ensure CinemachineBrain exists on main camera. Properties: `camera` (target camera), `defaultBlendStyle`, `defaultBlendDuration`
- `get_brain_status` — Get Brain state (active camera, blend status)

**Creation:**
- `create_camera` — Create camera with optional preset. Properties: `name`, `preset` (follow/third_person/freelook/dolly/static/top_down/side_scroller), `follow`, `lookAt`, `priority`, `fieldOfView`. Falls back to basic Camera without Cinemachine.

**Configuration:**
- `set_target` — Set Follow and/or LookAt targets. Properties: `follow`, `lookAt` (GO name/path/ID)
- `set_priority` — Set camera priority for Brain selection. Properties: `priority` (int)
- `set_lens` — Configure lens. Properties: `fieldOfView`, `nearClipPlane`, `farClipPlane`, `orthographicSize`, `dutch`
- `set_body` — Configure Body component (Cinemachine). Properties: `bodyType` (to swap), plus component-specific properties
- `set_aim` — Configure Aim component (Cinemachine). Properties: `aimType` (to swap), plus component-specific properties
- `set_noise` — Configure Noise (Cinemachine). Properties: `amplitudeGain`, `frequencyGain`

**Extensions (Cinemachine):**
- `add_extension` — Add extension. Properties: `extensionType` (CinemachineConfiner2D, CinemachineDeoccluder, CinemachineImpulseListener, CinemachineFollowZoom, CinemachineRecomposer, etc.)
- `remove_extension` — Remove extension by type. Properties: `extensionType`

**Control:**
- `list_cameras` — List all cameras with status
- `set_blend` — Configure default blend on Brain. Properties: `style` (Cut/EaseInOut/Linear/etc.), `duration`
- `force_camera` — Override Brain to use specific camera
- `release_override` — Release camera override

**Capture:**
- `screenshot` — Capture screenshot. Supports `capture_source="game_view"` (default, camera-based) or `"scene_view"` (editor viewport). game_view supports inline base64, batch surround/orbit, positioned capture. scene_view supports `view_target` for framing.
- `screenshot_multiview` — Shorthand for screenshot with batch='surround' and include_image=true.

**Examples:**

```python
# Check Cinemachine availability
manage_camera(action="ping")

# Create a third-person camera following the player
manage_camera(action="create_camera", properties={
    "name": "FollowCam", "preset": "third_person",
    "follow": "Player", "lookAt": "Player", "priority": 20
})

# Ensure Brain exists on main camera
manage_camera(action="ensure_brain")

# Configure body component
manage_camera(action="set_body", target="FollowCam", properties={
    "bodyType": "CinemachineThirdPersonFollow",
    "cameraDistance": 5.0, "shoulderOffset": [0.5, 0.5, 0]
})

# Set aim
manage_camera(action="set_aim", target="FollowCam", properties={
    "aimType": "CinemachineRotationComposer"
})

# Add camera shake
manage_camera(action="set_noise", target="FollowCam", properties={
    "amplitudeGain": 0.5, "frequencyGain": 1.0
})

# Set priority to make this the active camera
manage_camera(action="set_priority", target="FollowCam", properties={"priority": 50})

# Force a specific camera
manage_camera(action="force_camera", target="CinematicCam")

# Release override (return to priority-based selection)
manage_camera(action="release_override")

# Configure blend transitions
manage_camera(action="set_blend", properties={"style": "EaseInOut", "duration": 2.0})

# Add deoccluder extension
manage_camera(action="add_extension", target="FollowCam", properties={
    "extensionType": "CinemachineDeoccluder"
})

# Screenshot from a specific camera (game_view, default)
manage_camera(action="screenshot", camera="FollowCam", include_image=True, max_resolution=512)

# Scene View screenshot (captures editor viewport — gizmos, wireframes, grid)
manage_camera(action="screenshot", capture_source="scene_view", include_image=True)

# Scene View screenshot framed on a specific object
manage_camera(action="screenshot", capture_source="scene_view", view_target="Canvas", include_image=True)

# Multi-view screenshot (6-angle contact sheet)
manage_camera(action="screenshot_multiview", max_resolution=480)

# List all cameras
manage_camera(action="list_cameras")
```

**Tier system:**
- Tier 1 actions (ping, create_camera, set_target, set_lens, set_priority, list_cameras, screenshot, screenshot_multiview) work without Cinemachine — they fall back to basic Unity Camera.
- Tier 2 actions (ensure_brain, get_brain_status, set_body, set_aim, set_noise, add/remove_extension, set_blend, force_camera, release_override) require `com.unity.cinemachine`. If called without Cinemachine, they return an error with a fallback suggestion.

**Resource:** Read `mcpforunity://scene/cameras` for current camera state before modifying.

---

## Graphics Tools

### manage_graphics

Unified rendering and graphics management: volumes/post-processing, light baking, rendering stats, pipeline configuration, and URP renderer features. Requires URP or HDRP for volume/feature actions. Use `ping` to check pipeline status and available features.

**Parameters:**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `action` | string | Yes | Action to perform (see categories below) |
| `target` | string | Sometimes | Target object name or instance ID |
| `effect` | string | Sometimes | Effect type name (e.g., `Bloom`, `Vignette`) |
| `properties` | dict | No | Action-specific properties to set |
| `parameters` | dict | No | Effect parameter values |
| `settings` | dict | No | Bake or pipeline settings |
| `name` | string | No | Name for created objects |
| `profile_path` | string | No | Asset path for VolumeProfile |
| `path` | string | No | Asset path (for `volume_create_profile`) |
| `position` | list[float] | No | Position [x,y,z] |

**Actions by category:**

**Status:**
- `ping` — Check render pipeline type, available features, and package status

**Volume (require URP/HDRP):**
- `volume_create` — Create a Volume GameObject with optional effects. Properties: `name`, `is_global` (default true), `weight` (0-1), `priority`, `profile_path` (existing profile), `effects` (list of effect defs)
- `volume_add_effect` — Add effect override to a Volume. Params: `target` (Volume GO), `effect` (e.g., "Bloom")
- `volume_set_effect` — Set effect parameters. Params: `target`, `effect`, `parameters` (dict of param name to value)
- `volume_remove_effect` — Remove effect override. Params: `target`, `effect`
- `volume_get_info` — Get Volume details (profile, effects, parameters). Params: `target`
- `volume_set_properties` — Set Volume component properties (weight, priority, isGlobal). Params: `target`, `properties`
- `volume_list_effects` — List all available volume effects for the active pipeline
- `volume_create_profile` — Create a standalone VolumeProfile asset. Params: `path`, `effects` (optional)

**Bake (Edit mode only):**
- `bake_start` — Start lightmap bake. Params: `async_bake` (default true)
- `bake_cancel` — Cancel in-progress bake
- `bake_status` — Check bake progress
- `bake_clear` — Clear baked lightmap data
- `bake_reflection_probe` — Bake a specific reflection probe. Params: `target`
- `bake_get_settings` — Get current Lightmapping settings
- `bake_set_settings` — Set Lightmapping settings. Params: `settings` (dict)
- `bake_create_light_probe_group` — Create a Light Probe Group. Params: `name`, `position`, `grid_size` [x,y,z], `spacing`
- `bake_create_reflection_probe` — Create a Reflection Probe. Params: `name`, `position`, `size` [x,y,z], `resolution`, `mode`, `hdr`, `box_projection`
- `bake_set_probe_positions` — Set Light Probe positions manually. Params: `target`, `positions` (array of [x,y,z])

**Stats:**
- `stats_get` — Get rendering counters (draw calls, batches, triangles, vertices, etc.)
- `stats_list_counters` — List all available ProfilerRecorder counters
- `stats_set_scene_debug` — Set Scene View debug/draw mode. Params: `mode`
- `stats_get_memory` — Get rendering memory usage

**Pipeline:**
- `pipeline_get_info` — Get active render pipeline info (type, quality level, asset paths)
- `pipeline_set_quality` — Switch quality level. Params: `level` (name or index)
- `pipeline_get_settings` — Get pipeline asset settings
- `pipeline_set_settings` — Set pipeline asset settings. Params: `settings` (dict)

**Features (URP only):**
- `feature_list` — List renderer features on the active URP renderer
- `feature_add` — Add a renderer feature. Params: `feature_type`, `name`, `material` (for full-screen effects)
- `feature_remove` — Remove a renderer feature. Params: `index` or `name`
- `feature_configure` — Set feature properties. Params: `index` or `name`, `properties` (dict)
- `feature_toggle` — Enable/disable a feature. Params: `index` or `name`, `active` (bool)
- `feature_reorder` — Reorder features. Params: `order` (list of indices)

**Examples:**

```python
# Check pipeline status
manage_graphics(action="ping")

# Create a global post-processing volume with Bloom and Vignette
manage_graphics(action="volume_create", name="PostProcessing", is_global=True,
    effects=[
        {"type": "Bloom", "parameters": {"intensity": 1.5, "threshold": 0.9}},
        {"type": "Vignette", "parameters": {"intensity": 0.4}}
    ])

# Add an effect to an existing volume
manage_graphics(action="volume_add_effect", target="PostProcessing", effect="ColorAdjustments")

# Configure effect parameters
manage_graphics(action="volume_set_effect", target="PostProcessing",
    effect="ColorAdjustments", parameters={"postExposure": 0.5, "saturation": 10})

# Get volume info
manage_graphics(action="volume_get_info", target="PostProcessing")

# List all available effects for the active pipeline
manage_graphics(action="volume_list_effects")

# Create a VolumeProfile asset
manage_graphics(action="volume_create_profile", path="Assets/Settings/MyProfile.asset",
    effects=[{"type": "Bloom"}, {"type": "Tonemapping"}])

# Start async lightmap bake
manage_graphics(action="bake_start", async_bake=True)

# Check bake progress
manage_graphics(action="bake_status")

# Create a Light Probe Group with a 3x2x3 grid
manage_graphics(action="bake_create_light_probe_group", name="ProbeGrid",
    position=[0, 1, 0], grid_size=[3, 2, 3], spacing=2.0)

# Create a Reflection Probe
manage_graphics(action="bake_create_reflection_probe", name="RoomProbe",
    position=[0, 2, 0], size=[10, 5, 10], resolution=256, hdr=True)

# Get rendering stats
manage_graphics(action="stats_get")

# Get memory usage
manage_graphics(action="stats_get_memory")

# Get pipeline info
manage_graphics(action="pipeline_get_info")

# Switch quality level
manage_graphics(action="pipeline_set_quality", level="High")

# List URP renderer features
manage_graphics(action="feature_list")

# Add a full-screen renderer feature
manage_graphics(action="feature_add", feature_type="FullScreenPassRendererFeature",
    name="NightVision", material="Assets/Materials/NightVision.mat")

# Toggle a feature off
manage_graphics(action="feature_toggle", index=0, active=False)

# Reorder features
manage_graphics(action="feature_reorder", order=[2, 0, 1])
```

**Resources:**
- `mcpforunity://scene/volumes` — Lists all Volume components in the scene with their profiles and effects
- `mcpforunity://rendering/stats` — Current rendering performance counters
- `mcpforunity://pipeline/renderer-features` — URP renderer features on the active renderer

---

## Package Tools

### manage_packages

Manage Unity packages: query, install, remove, embed, and configure registries. Install/remove trigger domain reload.

**Query Actions (read-only):**

| Action | Parameters | Description |
|--------|-----------|-------------|
| `list_packages` | — | List all installed packages (async, returns job_id) |
| `search_packages` | `query` | Search Unity registry by keyword (async, returns job_id) |
| `get_package_info` | `package` | Get details about a specific installed package |
| `list_registries` | — | List all scoped registries (names, URLs, scopes); immediate result |
| `ping` | — | Check package manager availability, Unity version, package count |
| `status` | `job_id` (required for list/search; optional for add/remove/embed) | Poll async job status; omit job_id to poll latest add/remove/embed job |

**Mutating Actions:**

| Action | Parameters | Description |
|--------|-----------|-------------|
| `add_package` | `package` | Install a package (name, name@version, git URL, or file: path) |
| `remove_package` | `package`, `force` (optional) | Remove a package; blocked if dependents exist unless `force=true` |
| `embed_package` | `package` | Copy package to local Packages/ for editing |
| `resolve_packages` | — | Force re-resolution of all packages |
| `add_registry` | `name`, `url`, `scopes` | Add a scoped registry (e.g., OpenUPM) |
| `remove_registry` | `name` or `url` | Remove a scoped registry |

**Input validation:**
- Valid package IDs: `com.unity.inputsystem`, `com.unity.cinemachine@3.1.6`
- Git URLs: allowed with warning ("ensure this is a trusted source")
- `file:` paths: allowed with warning
- Invalid names (uppercase, missing dots): rejected

**Example — List installed packages:**
```python
manage_packages(action="list_packages")
# Returns job_id, then poll:
manage_packages(action="status", job_id="<job_id>")
```

**Example — Search for a package:**
```python
manage_packages(action="search_packages", query="input system")
```

**Example — Install a package:**
```python
manage_packages(action="add_package", package="com.unity.inputsystem")
# Poll until complete:
manage_packages(action="status", job_id="<job_id>")
```

**Example — Remove with dependency check:**
```python
manage_packages(action="remove_package", package="com.unity.modules.ui")
# Error: "Cannot remove: 3 package(s) depend on it: ..."
manage_packages(action="remove_package", package="com.unity.modules.ui", force=True)
# Proceeds anyway
```

**Example — Add OpenUPM registry:**
```python
manage_packages(
    action="add_registry",
    name="OpenUPM",
    url="https://package.openupm.com",
    scopes=["com.cysharp", "com.neuecc"]
)
```

---

## ProBuilder Tools

### manage_probuilder

Unified tool for ProBuilder mesh operations. Requires `com.unity.probuilder` package. When available, **prefer ProBuilder over primitive GameObjects** for editable geometry, multi-material faces, or complex shapes.

**Parameters:**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `action` | string | Yes | Action to perform (see categories below) |
| `target` | string | Sometimes | Target GameObject name/path/id |
| `search_method` | string | No | How to find target: `by_id`, `by_name`, `by_path`, `by_tag`, `by_layer` |
| `properties` | dict \| string | No | Action-specific parameters (dict or JSON string) |

**Actions by category:**

**Shape Creation:**
- `create_shape` — Create ProBuilder primitive (shape_type, size, position, rotation, name). 12 types: Cube, Cylinder, Sphere, Plane, Cone, Torus, Pipe, Arch, Stair, CurvedStair, Door, Prism
- `create_poly_shape` — Create from 2D polygon footprint (points, extrudeHeight, flipNormals)

**Mesh Editing:**
- `extrude_faces` — Extrude faces (faceIndices, distance, method: FaceNormal/VertexNormal/IndividualFaces)
- `extrude_edges` — Extrude edges (edgeIndices or edges [{a,b},...], distance, asGroup)
- `bevel_edges` — Bevel edges (edgeIndices or edges [{a,b},...], amount 0-1)
- `subdivide` — Subdivide faces via ConnectElements (faceIndices optional)
- `delete_faces` — Delete faces (faceIndices)
- `bridge_edges` — Bridge two open edges (edgeA, edgeB as {a,b} pairs, allowNonManifold)
- `connect_elements` — Connect edges/faces (edgeIndices or faceIndices)
- `detach_faces` — Detach faces to new object (faceIndices, deleteSourceFaces)
- `flip_normals` — Flip face normals (faceIndices)
- `merge_faces` — Merge faces into one (faceIndices)
- `combine_meshes` — Combine ProBuilder objects (targets list)
- `merge_objects` — Merge objects with auto-convert (targets, name)
- `duplicate_and_flip` — Create double-sided geometry (faceIndices)
- `create_polygon` — Connect existing vertices into a new face (vertexIndices, unordered)

**Vertex Operations:**
- `merge_vertices` — Collapse vertices to single point (vertexIndices, collapseToFirst)
- `weld_vertices` — Weld vertices within proximity radius (vertexIndices, radius)
- `split_vertices` — Split shared vertices (vertexIndices)
- `move_vertices` — Translate vertices (vertexIndices, offset [x,y,z])
- `insert_vertex` — Insert vertex on edge or face (edge {a,b} or faceIndex + point [x,y,z])
- `append_vertices_to_edge` — Insert evenly-spaced points on edges (edgeIndices or edges, count)

**Selection:**
- `select_faces` — Select faces by criteria (direction + tolerance, growFrom + growAngle)

**UV & Materials:**
- `set_face_material` — Assign material to faces (faceIndices, materialPath)
- `set_face_color` — Set vertex color on faces (faceIndices, color [r,g,b,a])
- `set_face_uvs` — Set UV params (faceIndices, scale, offset, rotation, flipU, flipV)

**Query:**
- `get_mesh_info` — Get mesh details with `include` parameter:
  - `"summary"` (default): counts, bounds, materials
  - `"faces"`: + face normals, centers, and direction labels (capped at 100)
  - `"edges"`: + edge vertex pairs with world positions (capped at 200, deduplicated)
  - `"all"`: everything
- `ping` — Check if ProBuilder is available

**Smoothing:**
- `set_smoothing` — Set smoothing group on faces (faceIndices, smoothingGroup: 0=hard, 1+=smooth)
- `auto_smooth` — Auto-assign smoothing groups by angle (angleThreshold: default 30)

**Mesh Utilities:**
- `center_pivot` — Move pivot to mesh bounds center
- `freeze_transform` — Bake transform into vertices, reset transform
- `validate_mesh` — Check mesh health (read-only diagnostics)
- `repair_mesh` — Auto-fix degenerate triangles

**Not Yet Working (known bugs):**
- `set_pivot` — Vertex positions don't persist through mesh rebuild. Use `center_pivot` or Transform positioning instead.
- `convert_to_probuilder` — MeshImporter throws internally. Create shapes natively instead.

**Examples:**

```python
# Check availability
manage_probuilder(action="ping")

# Create a cube
manage_probuilder(action="create_shape", properties={"shape_type": "Cube", "name": "MyCube"})

# Get face info with directions
manage_probuilder(action="get_mesh_info", target="MyCube", properties={"include": "faces"})

# Extrude the top face (find it via direction="top" in get_mesh_info results)
manage_probuilder(action="extrude_faces", target="MyCube",
    properties={"faceIndices": [2], "distance": 1.5})

# Select all upward-facing faces
manage_probuilder(action="select_faces", target="MyCube",
    properties={"direction": "up", "tolerance": 0.7})

# Create double-sided geometry (for room interiors)
manage_probuilder(action="duplicate_and_flip", target="Room",
    properties={"faceIndices": [0, 1, 2, 3, 4, 5]})

# Weld nearby vertices
manage_probuilder(action="weld_vertices", target="MyCube",
    properties={"vertexIndices": [0, 1, 2, 3], "radius": 0.1})

# Auto-smooth
manage_probuilder(action="auto_smooth", target="MyCube", properties={"angleThreshold": 30})

# Cleanup workflow
manage_probuilder(action="center_pivot", target="MyCube")
manage_probuilder(action="validate_mesh", target="MyCube")
```

See also: [ProBuilder Workflow Guide](probuilder-guide.md) for detailed patterns and complex object examples.

---

## Docs Tools

Tools for verifying Unity C# APIs and fetching official documentation. Group: `docs`.

### `unity_reflect`

Inspect Unity's live C# API via reflection. **Always use this before writing C# code that references Unity APIs** — LLM training data frequently contains incorrect, outdated, or hallucinated APIs.

Requires Unity connection.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `action` | string | Yes | `search`, `get_type`, or `get_member` |
| `class_name` | string | For get_type, get_member | Fully qualified or simple C# class name |
| `member_name` | string | For get_member | Method, property, or field name to inspect |
| `query` | string | For search | Search query for type name search |
| `scope` | string | No | Assembly scope for search: `unity`, `packages`, `project`, `all` (default: `unity`) |

**Actions:**

- **`search`**: Search for types by name across loaded assemblies. Returns matching type names.
- **`get_type`**: Get a member summary (names only) for a class. Returns list of methods, properties, fields.
- **`get_member`**: Get full signature detail for one member. Returns parameter types, return type, overloads.

```python
# Search for types matching a name
unity_reflect(action="search", query="NavMesh")
unity_reflect(action="search", query="Camera", scope="all")

# Get all members of a type
unity_reflect(action="get_type", class_name="UnityEngine.AI.NavMeshAgent")

# Get detailed signature for a specific member
unity_reflect(action="get_member", class_name="Physics", member_name="Raycast")
unity_reflect(action="get_member", class_name="NavMeshAgent", member_name="SetDestination")
```

### `unity_docs`

Fetch official Unity documentation from docs.unity3d.com. Returns descriptions, parameter details, code examples, and caveats. Use after `unity_reflect` confirms a type exists.

No Unity connection needed for doc fetching. The `lookup` action with asset-related queries will also search project assets (requires Unity connection).

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `action` | string | Yes | `get_doc`, `get_manual`, `get_package_doc`, or `lookup` |
| `class_name` | string | For get_doc | Unity class name (e.g., `Physics`, `Transform`) |
| `member_name` | string | No | Method or property name for get_doc |
| `version` | string | No | Unity version (e.g., `6000.0.38f1`). Auto-extracts major.minor. |
| `slug` | string | For get_manual | Manual page slug (e.g., `execution-order`) |
| `package` | string | For get_package_doc, optional for lookup | Package name (e.g., `com.unity.render-pipelines.universal`) |
| `page` | string | For get_package_doc | Package doc page (e.g., `index`, `2d-index`) |
| `pkg_version` | string | For get_package_doc, optional for lookup | Package version major.minor (e.g., `17.0`) |
| `query` | string | For lookup (single) | Single search query |
| `queries` | string | For lookup (batch) | Comma-separated queries (e.g., `Physics.Raycast,NavMeshAgent,Light2D`) |

**Actions:**

- **`get_doc`**: Fetch ScriptReference docs for a class or member. Parses HTML to extract description, signatures, parameters, return type, and code examples.
- **`get_manual`**: Fetch a Unity Manual page by slug. Returns title, sections, and code examples.
- **`get_package_doc`**: Fetch package documentation. Requires package name, page slug, and package version.
- **`lookup`**: Search doc sources in parallel (ScriptReference + Manual; also package docs if `package` + `pkg_version` provided). Supports batch queries. For asset-related queries (shader, material, texture, etc.), also searches project assets via `manage_asset`.

```python
# Fetch ScriptReference for a class
unity_docs(action="get_doc", class_name="Physics")
unity_docs(action="get_doc", class_name="Physics", member_name="Raycast")
unity_docs(action="get_doc", class_name="Transform", version="6000.0.38f1")

# Fetch a Manual page
unity_docs(action="get_manual", slug="execution-order")
unity_docs(action="get_manual", slug="urp/urp-introduction")

# Fetch package documentation
unity_docs(action="get_package_doc", package="com.unity.render-pipelines.universal",
           page="2d-index", pkg_version="17.0")

# Parallel lookup across all sources (single query)
unity_docs(action="lookup", query="Physics.Raycast")

# Batch lookup (multiple queries in one call)
unity_docs(action="lookup", queries="Physics.Raycast,NavMeshAgent,Light2D")

# Lookup with package docs included
unity_docs(action="lookup", query="VolumeProfile",
           package="com.unity.render-pipelines.universal", pkg_version="17.0")
```
