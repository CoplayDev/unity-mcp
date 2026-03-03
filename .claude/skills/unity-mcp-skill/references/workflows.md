# Unity-MCP Workflow Patterns

Common workflows and patterns for effective Unity-MCP usage.

## Table of Contents

- [Setup & Verification](#setup--verification)
- [Scene Creation Workflows](#scene-creation-workflows)
- [Script Development Workflows](#script-development-workflows)
- [Asset Management Workflows](#asset-management-workflows)
- [Testing Workflows](#testing-workflows)
- [Debugging Workflows](#debugging-workflows)
- [UI Creation Workflows](#ui-creation-workflows)
- [Batch Operations](#batch-operations)

---

## Setup & Verification

### Initial Connection Verification

```python
# 1. Check editor state
# Read mcpforunity://editor/state

# 2. Verify ready_for_tools == true
# If false, wait for recommended_retry_after_ms

# 3. Check active scene
# Read mcpforunity://editor/state → active_scene

# 4. List available instances (multi-instance)
# Read mcpforunity://instances
```

### Before Any Operation

```python
# Quick readiness check pattern:
editor_state = read_resource("mcpforunity://editor/state")

if not editor_state["ready_for_tools"]:
    # Check blocking_reasons
    # Wait recommended_retry_after_ms
    pass

if editor_state["is_compiling"]:
    # Wait for compilation to complete
    pass
```

---

## Scene Generator Build Workflow

### Fresh Scene Before Building

**Always start a generated scene build with `manage_scene(action="create")`** to get a clean empty scene. This avoids conflicts with existing default objects (Camera, Light) that would cause "already exists" errors when the execution plan tries to create its own.

```python
# Step 0: Create fresh empty scene (replaces current scene entirely)
manage_scene(action="create", name="MyGeneratedScene", path="Assets/Scenes/")

# Now proceed with the phased execution plan...
# Phase 1: Environment (camera, lights) — no conflicts
# Phase 2: Objects (GameObjects)
# Phase 3: Materials
# etc.
```

### Wiring Object References Between Components

After creating scripts and attaching components, use `set_property` to wire cross-references between GameObjects. Use the `{"name": "ObjectName"}` format to reference scene objects by name:

```python
# Wire a list of target GameObjects into a script's serialized field
manage_components(
    action="set_property",
    target="BeeManager",
    component_type="BeeManagerScript",
    property="targetObjects",
    value=[{"name": "Flower_1"}, {"name": "Flower_2"}, {"name": "Flower_3"}]
)
```

### Physics Requirements for Trigger-Based Interactions

When scripts use `OnTriggerEnter` / `OnTriggerStay` / `OnTriggerExit`, at least one of the two colliding objects **must** have a `Rigidbody` component. Common pattern:

```python
# Moving objects (bees, players) need Rigidbody for triggers to fire
batch_execute(commands=[
    {"tool": "manage_components", "params": {
        "action": "add", "target": "Bee_1", "component_type": "Rigidbody"
    }},
    {"tool": "manage_components", "params": {
        "action": "set_property", "target": "Bee_1",
        "component_type": "Rigidbody",
        "properties": {"useGravity": false, "isKinematic": true}
    }}
])
```

### Script Overwrites with `manage_script(action="update")`

When a generated script needs to be rewritten (e.g., to add auto-wiring logic), use `update` instead of deleting and recreating:

```python
manage_script(
    action="update",
    path="Assets/Scripts/MyScript.cs",
    contents="using UnityEngine;\n\npublic class MyScript : MonoBehaviour { ... }"
)
# Then refresh and check console
refresh_unity(mode="force", scope="scripts", compile="request", wait_for_ready=True)
read_console(types=["error"], count=10)
```

---

## Scene Creation Workflows

### Create Complete Scene from Scratch

```python
# 1. Create new scene
manage_scene(action="create", name="GameLevel", path="Assets/Scenes/")

# 2. Batch create environment objects
batch_execute(commands=[
    {"tool": "manage_gameobject", "params": {
        "action": "create", "name": "Ground", "primitive_type": "Plane",
        "position": [0, 0, 0], "scale": [10, 1, 10]
    }},
    {"tool": "manage_gameobject", "params": {
        "action": "create", "name": "Light", "primitive_type": "Cube"
    }},
    {"tool": "manage_gameobject", "params": {
        "action": "create", "name": "Player", "primitive_type": "Capsule",
        "position": [0, 1, 0]
    }}
])

# 3. Add light component (delete cube mesh, add light)
manage_components(action="remove", target="Light", component_type="MeshRenderer")
manage_components(action="remove", target="Light", component_type="MeshFilter")
manage_components(action="remove", target="Light", component_type="BoxCollider")
manage_components(action="add", target="Light", component_type="Light")
manage_components(action="set_property", target="Light", component_type="Light",
    property="type", value="Directional")

# 4. Set up camera
manage_gameobject(action="modify", target="Main Camera", position=[0, 5, -10],
    rotation=[30, 0, 0])

# 5. Verify with screenshot
manage_scene(action="screenshot")

# 6. Save scene
manage_scene(action="save")
```

### Populate Scene with Grid of Objects

```python
# Create 5x5 grid of cubes using batch
commands = []
for x in range(5):
    for z in range(5):
        commands.append({
            "tool": "manage_gameobject",
            "params": {
                "action": "create",
                "name": f"Cube_{x}_{z}",
                "primitive_type": "Cube",
                "position": [x * 2, 0, z * 2]
            }
        })

# Execute in batches of 25
batch_execute(commands=commands[:25], parallel=True)
```

### Clone and Arrange Objects

```python
# Find template object
result = find_gameobjects(search_term="Template", search_method="by_name")
template_id = result["ids"][0]

# Duplicate in a line
for i in range(10):
    manage_gameobject(
        action="duplicate",
        target=template_id,
        new_name=f"Instance_{i}",
        offset=[i * 2, 0, 0]
    )
```

---

## Script Development Workflows

### Create New Script and Attach

```python
# 1. Create script
create_script(
    path="Assets/Scripts/EnemyAI.cs",
    contents='''using UnityEngine;

public class EnemyAI : MonoBehaviour
{
    public float speed = 5f;
    public Transform target;
    
    void Update()
    {
        if (target != null)
        {
            Vector3 direction = (target.position - transform.position).normalized;
            transform.position += direction * speed * Time.deltaTime;
        }
    }
}'''
)

# 2. CRITICAL: Refresh and compile
refresh_unity(mode="force", scope="scripts", compile="request", wait_for_ready=True)

# 3. Check for errors
console = read_console(types=["error"], count=10)
if console["messages"]:
    # Handle compilation errors
    print("Compilation errors:", console["messages"])
else:
    # 4. Attach to GameObject
    manage_gameobject(action="modify", target="Enemy", components_to_add=["EnemyAI"])
    
    # 5. Set component properties
    manage_components(
        action="set_property",
        target="Enemy",
        component_type="EnemyAI",
        properties={
            "speed": 10.0
        }
    )
```

### Edit Existing Script Safely

```python
# 1. Get current SHA
sha_info = get_sha(uri="mcpforunity://path/Assets/Scripts/PlayerController.cs")

# 2. Find the method to edit
matches = find_in_file(
    uri="mcpforunity://path/Assets/Scripts/PlayerController.cs",
    pattern="void Update\\(\\)"
)

# 3. Apply structured edit
script_apply_edits(
    name="PlayerController",
    path="Assets/Scripts",
    edits=[{
        "op": "replace_method",
        "methodName": "Update",
        "replacement": '''void Update()
    {
        float h = Input.GetAxis("Horizontal");
        float v = Input.GetAxis("Vertical");
        transform.Translate(new Vector3(h, 0, v) * speed * Time.deltaTime);
    }'''
    }]
)

# 4. Validate
validate_script(
    uri="mcpforunity://path/Assets/Scripts/PlayerController.cs",
    level="standard"
)

# 5. Refresh
refresh_unity(mode="force", scope="scripts", compile="request", wait_for_ready=True)

# 6. Check console
read_console(types=["error"], count=10)
```

### Add Method to Existing Class

```python
script_apply_edits(
    name="GameManager",
    path="Assets/Scripts",
    edits=[
        {
            "op": "insert_method",
            "afterMethod": "Start",
            "code": '''
    public void ResetGame()
    {
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }'''
        },
        {
            "op": "anchor_insert",
            "anchor": "using UnityEngine;",
            "position": "after",
            "text": "\nusing UnityEngine.SceneManagement;"
        }
    ]
)
```

---

## Asset Management Workflows

### Create and Apply Material

```python
# 1. Create material
manage_material(
    action="create",
    material_path="Assets/Materials/PlayerMaterial.mat",
    shader="Standard",
    properties={
        "_Color": [0.2, 0.5, 1.0, 1.0],
        "_Metallic": 0.5,
        "_Glossiness": 0.8
    }
)

# 2. Assign to renderer
manage_material(
    action="assign_material_to_renderer",
    target="Player",
    material_path="Assets/Materials/PlayerMaterial.mat",
    slot=0
)

# 3. Verify visually
manage_scene(action="screenshot")
```

### Create Procedural Texture

```python
# 1. Create base texture
manage_texture(
    action="create",
    path="Assets/Textures/Checkerboard.png",
    width=256,
    height=256,
    fill_color=[255, 255, 255, 255]
)

# 2. Apply checkerboard pattern
manage_texture(
    action="apply_pattern",
    path="Assets/Textures/Checkerboard.png",
    pattern="checkerboard",
    palette=[[0, 0, 0, 255], [255, 255, 255, 255]],
    pattern_size=32
)

# 3. Create material with texture
manage_material(
    action="create",
    material_path="Assets/Materials/CheckerMaterial.mat",
    shader="Standard"
)

# 4. Assign texture to material (via manage_material set_material_shader_property)
```

### Organize Assets into Folders

```python
# 1. Create folder structure
batch_execute(commands=[
    {"tool": "manage_asset", "params": {"action": "create_folder", "path": "Assets/Prefabs"}},
    {"tool": "manage_asset", "params": {"action": "create_folder", "path": "Assets/Materials"}},
    {"tool": "manage_asset", "params": {"action": "create_folder", "path": "Assets/Scripts"}},
    {"tool": "manage_asset", "params": {"action": "create_folder", "path": "Assets/Textures"}}
])

# 2. Move existing assets
manage_asset(action="move", path="Assets/MyMaterial.mat", destination="Assets/Materials/MyMaterial.mat")
manage_asset(action="move", path="Assets/MyScript.cs", destination="Assets/Scripts/MyScript.cs")
```

### Search and Process Assets

```python
# Find all prefabs
result = manage_asset(
    action="search",
    path="Assets",
    search_pattern="*.prefab",
    page_size=50,
    generate_preview=False
)

# Process each prefab
for asset in result["assets"]:
    prefab_path = asset["path"]
    # Get prefab info
    info = manage_prefabs(action="get_info", prefab_path=prefab_path)
    print(f"Prefab: {prefab_path}, Children: {info['childCount']}")
```

---

## Testing Workflows

### Run Specific Tests

```python
# 1. List available tests
# Read mcpforunity://tests/EditMode

# 2. Run specific tests
result = run_tests(
    mode="EditMode",
    test_names=["MyTests.TestPlayerMovement", "MyTests.TestEnemySpawn"],
    include_failed_tests=True
)
job_id = result["job_id"]

# 3. Wait for results
final_result = get_test_job(
    job_id=job_id,
    wait_timeout=60,
    include_failed_tests=True
)

# 4. Check results
if final_result["status"] == "complete":
    for test in final_result.get("failed_tests", []):
        print(f"FAILED: {test['name']}: {test['message']}")
```

### Run Tests by Category

```python
# Run all unit tests
result = run_tests(
    mode="EditMode",
    category_names=["Unit"],
    include_failed_tests=True
)

# Poll until complete
while True:
    status = get_test_job(job_id=result["job_id"], wait_timeout=30)
    if status["status"] in ["complete", "failed"]:
        break
```

### Test-Driven Development Pattern

```python
# 1. Write test first
create_script(
    path="Assets/Tests/Editor/PlayerTests.cs",
    contents='''using NUnit.Framework;
using UnityEngine;

public class PlayerTests
{
    [Test]
    public void TestPlayerStartsAtOrigin()
    {
        var player = new GameObject("TestPlayer");
        Assert.AreEqual(Vector3.zero, player.transform.position);
        Object.DestroyImmediate(player);
    }
}'''
)

# 2. Refresh
refresh_unity(mode="force", scope="scripts", compile="request", wait_for_ready=True)

# 3. Run test (expect pass for this simple test)
result = run_tests(mode="EditMode", test_names=["PlayerTests.TestPlayerStartsAtOrigin"])
get_test_job(job_id=result["job_id"], wait_timeout=30)
```

---

## Debugging Workflows

### Diagnose Compilation Errors

```python
# 1. Check console for errors
errors = read_console(
    types=["error"],
    count=20,
    include_stacktrace=True,
    format="detailed"
)

# 2. For each error, find the file and line
for error in errors["messages"]:
    # Parse error message for file:line info
    # Use find_in_file to locate the problematic code
    pass

# 3. After fixing, refresh and check again
refresh_unity(mode="force", scope="scripts", compile="request", wait_for_ready=True)
read_console(types=["error"], count=10)
```

### Investigate Missing References

```python
# 1. Find the GameObject
result = find_gameobjects(search_term="Player", search_method="by_name")

# 2. Get all components
# Read mcpforunity://scene/gameobject/{id}/components

# 3. Check for null references in serialized fields
# Look for fields with null/missing values

# 4. Find the referenced object
result = find_gameobjects(search_term="Target", search_method="by_name")

# 5. Set the reference
manage_components(
    action="set_property",
    target="Player",
    component_type="PlayerController",
    property="target",
    value={"instanceID": result["ids"][0]}  # Reference by ID
)
```

### Check Scene State

```python
# 1. Get hierarchy
hierarchy = manage_scene(action="get_hierarchy", page_size=100, include_transform=True)

# 2. Find objects at unexpected positions
for item in hierarchy["data"]["items"]:
    if item.get("transform", {}).get("position", [0,0,0])[1] < -100:
        print(f"Object {item['name']} fell through floor!")

# 3. Visual verification
manage_scene(action="screenshot")
```

---

## UI Creation Workflows

Unity has two UI systems: **UI Toolkit** (modern, recommended) and **uGUI** (Canvas-based, legacy). Use `manage_ui` for UI Toolkit workflows, and `batch_execute` with `manage_gameobject` + `manage_components` for uGUI.

> **Template warning:** This section is a skill template library, not a guaranteed source of truth. Examples may be inaccurate for your Unity version, package setup, or project conventions.
> **Use safely:**
> 1. **Always read `mcpforunity://project/info` first** to detect installed packages and input system.
> 2. Validate component/property names against the current project.
> 3. Prefer targeting by instance ID or full path over generic names.
> 4. Treat numeric enum values as placeholders and verify before reuse.

### Step 0: Detect Project UI Capabilities

**Before creating any UI**, read project info to determine which packages and input system are available.

```python
# Read mcpforunity://project/info — returns:
# {
#   "renderPipeline": "BuiltIn" | "Universal" | "HighDefinition" | "Custom",
#   "activeInputHandler": "Old" | "New" | "Both",
#   "packages": {
#     "ugui": true/false,        — com.unity.ugui (Canvas, Image, Button, etc.)
#     "textmeshpro": true/false,  — com.unity.textmeshpro (TextMeshProUGUI)
#     "inputsystem": true/false,  — com.unity.inputsystem (new Input System)
#     "uiToolkit": true/false,    — UI Toolkit (always true for Unity 2021.3+)
#     "screenCapture": true/false  — ScreenCapture module enabled
#   }
# }
```

**Decision matrix:**

| project_info field | Value | What to use |
|---|---|---|
| `packages.uiToolkit` | `true` | **Preferred:** Use `manage_ui` for UI Toolkit (UXML/USS) |
| `packages.ugui` | `true` | Canvas-based UI (Image, Button, etc.) via `batch_execute` |
| `packages.textmeshpro` | `true` | `TextMeshProUGUI` for text (uGUI) |
| `packages.textmeshpro` | `false` | `UnityEngine.UI.Text` (legacy, lower quality) |
| `activeInputHandler` | `"Old"` | `StandaloneInputModule` for EventSystem (uGUI) |
| `activeInputHandler` | `"New"` | `InputSystemUIInputModule` for EventSystem (uGUI) |
| `activeInputHandler` | `"Both"` | Either works; prefer `InputSystemUIInputModule` for UI |

### UI Toolkit Workflows (manage_ui)

UI Toolkit uses a web-like approach: **UXML** (like HTML) for structure, **USS** (like CSS) for styling. This is the preferred UI system for new projects.

#### Create a Complete UI Screen

```python
# 1. Create UXML document (structure)
manage_ui(
    action="create",
    path="Assets/UI/MainMenu.uxml",
    contents='''<ui:UXML xmlns:ui="UnityEngine.UIElements" xmlns:uie="UnityEditor.UIElements">
    <ui:Style src="Assets/UI/MainMenu.uss" />
    <ui:VisualElement name="root" class="root-container">
        <ui:Label text="My Game" class="title" />
        <ui:Button text="Play" name="play-btn" class="menu-button" />
        <ui:Button text="Settings" name="settings-btn" class="menu-button" />
        <ui:Button text="Quit" name="quit-btn" class="menu-button" />
    </ui:VisualElement>
</ui:UXML>'''
)

# 2. Create USS stylesheet (styling)
manage_ui(
    action="create",
    path="Assets/UI/MainMenu.uss",
    contents='''.root-container {
    flex-grow: 1;
    justify-content: center;
    align-items: center;
    background-color: rgba(0, 0, 0, 0.8);
}
.title {
    font-size: 48px;
    color: white;
    -unity-font-style: bold;
    margin-bottom: 40px;
}
.menu-button {
    width: 300px;
    height: 60px;
    font-size: 24px;
    margin: 8px;
    background-color: rgb(50, 120, 200);
    color: white;
    border-radius: 8px;
}
.menu-button:hover {
    background-color: rgb(70, 140, 220);
}'''
)

# 3. Create a GameObject and attach UIDocument
manage_gameobject(action="create", name="UIRoot")
manage_ui(
    action="attach_ui_document",
    target="UIRoot",
    source_asset="Assets/UI/MainMenu.uxml"
    # panel_settings auto-created if omitted
)

# 4. Verify the visual tree
manage_ui(action="get_visual_tree", target="UIRoot", max_depth=5)
```

#### Update Existing UI

```python
# Read current content
result = manage_ui(action="read", path="Assets/UI/MainMenu.uss")
# Modify and update
manage_ui(
    action="update",
    path="Assets/UI/MainMenu.uss",
    contents=".title { font-size: 64px; color: yellow; }"
)
```

#### Custom PanelSettings

```python
# Create PanelSettings with ScaleWithScreenSize
manage_ui(
    action="create_panel_settings",
    path="Assets/UI/GamePanelSettings.asset",
    scale_mode="ScaleWithScreenSize",
    reference_resolution={"width": 1920, "height": 1080}
)

# Attach UIDocument with custom PanelSettings
manage_ui(
    action="attach_ui_document",
    target="UIRoot",
    source_asset="Assets/UI/MainMenu.uxml",
    panel_settings="Assets/UI/GamePanelSettings.asset"
)
```

### uGUI (Canvas-Based) Workflows

The sections below cover legacy Canvas-based UI using `batch_execute`. Use these when working with existing uGUI projects or when UI Toolkit is not suitable.

### RectTransform Sizing (Critical for All UI Children)

Every GameObject under a Canvas gets a `RectTransform` instead of `Transform`. **Without setting anchor/size, UI elements default to zero size and won't be visible.** Use `set_property` on `RectTransform`:

```python
# Stretch to fill parent (common for panels/backgrounds)
{"tool": "manage_components", "params": {
    "action": "set_property", "target": "MyPanel",
    "component_type": "RectTransform",
    "properties": {
        "anchorMin": [0, 0],        # bottom-left corner
        "anchorMax": [1, 1],        # top-right corner
        "sizeDelta": [0, 0],        # no extra size beyond anchors
        "anchoredPosition": [0, 0]  # centered on anchors
    }
}}

# Fixed-size centered element (e.g. 300x50 button)
{"tool": "manage_components", "params": {
    "action": "set_property", "target": "MyButton",
    "component_type": "RectTransform",
    "properties": {
        "anchorMin": [0.5, 0.5],
        "anchorMax": [0.5, 0.5],
        "sizeDelta": [300, 50],
        "anchoredPosition": [0, 0]
    }
}}

# Top-anchored bar (e.g. health bar at top of screen)
{"tool": "manage_components", "params": {
    "action": "set_property", "target": "TopBar",
    "component_type": "RectTransform",
    "properties": {
        "anchorMin": [0, 1],        # left-top
        "anchorMax": [1, 1],        # right-top (stretch horizontally)
        "sizeDelta": [0, 60],       # 60px tall, full width
        "anchoredPosition": [0, -30] # offset down by half height
    }
}}
```

> **Note:** Vector2 properties accept both `[x, y]` array format and `{"x": ..., "y": ...}` object format.

### Create Canvas (Foundation for All UI)

Every UI element must be under a Canvas. A Canvas requires three components: `Canvas`, `CanvasScaler`, and `GraphicRaycaster`.

```python
batch_execute(fail_fast=True, commands=[
    {"tool": "manage_gameobject", "params": {
        "action": "create", "name": "MainCanvas"
    }},
    {"tool": "manage_components", "params": {
        "action": "add", "target": "MainCanvas", "component_type": "Canvas"
    }},
    {"tool": "manage_components", "params": {
        "action": "add", "target": "MainCanvas", "component_type": "CanvasScaler"
    }},
    {"tool": "manage_components", "params": {
        "action": "add", "target": "MainCanvas", "component_type": "GraphicRaycaster"
    }},
    # renderMode: 0=ScreenSpaceOverlay, 1=ScreenSpaceCamera, 2=WorldSpace
    {"tool": "manage_components", "params": {
        "action": "set_property", "target": "MainCanvas",
        "component_type": "Canvas", "property": "renderMode", "value": 0
    }},
    # CanvasScaler: uiScaleMode 1=ScaleWithScreenSize
    {"tool": "manage_components", "params": {
        "action": "set_property", "target": "MainCanvas",
        "component_type": "CanvasScaler", "property": "uiScaleMode", "value": 1
    }},
    {"tool": "manage_components", "params": {
        "action": "set_property", "target": "MainCanvas",
        "component_type": "CanvasScaler", "property": "referenceResolution",
        "value": [1920, 1080]
    }}
])
```

### Create EventSystem (Required Once Per Scene for UI Interaction)

If no EventSystem exists in the scene, buttons and other interactive UI elements won't respond to input. Create one alongside your first Canvas. **Check `project_info.activeInputHandler` to pick the correct input module.**

```python
# For activeInputHandler == "New" or "Both" (project has Input System package):
batch_execute(fail_fast=True, commands=[
    {"tool": "manage_gameobject", "params": {
        "action": "create", "name": "EventSystem"
    }},
    {"tool": "manage_components", "params": {
        "action": "add", "target": "EventSystem",
        "component_type": "UnityEngine.EventSystems.EventSystem"
    }},
    {"tool": "manage_components", "params": {
        "action": "add", "target": "EventSystem",
        "component_type": "UnityEngine.InputSystem.UI.InputSystemUIInputModule"
    }}
])

# For activeInputHandler == "Old" (legacy Input Manager only):
batch_execute(fail_fast=True, commands=[
    {"tool": "manage_gameobject", "params": {
        "action": "create", "name": "EventSystem"
    }},
    {"tool": "manage_components", "params": {
        "action": "add", "target": "EventSystem",
        "component_type": "UnityEngine.EventSystems.EventSystem"
    }},
    {"tool": "manage_components", "params": {
        "action": "add", "target": "EventSystem",
        "component_type": "UnityEngine.EventSystems.StandaloneInputModule"
    }}
])
```

### Create Panel (Background Container)

A Panel is an Image component used as a background/container for other UI elements.

```python
batch_execute(fail_fast=True, commands=[
    {"tool": "manage_gameobject", "params": {
        "action": "create", "name": "MenuPanel", "parent": "MainCanvas"
    }},
    {"tool": "manage_components", "params": {
        "action": "add", "target": "MenuPanel", "component_type": "Image"
    }},
    {"tool": "manage_components", "params": {
        "action": "set_property", "target": "MenuPanel",
        "component_type": "Image", "property": "color",
        "value": [0.1, 0.1, 0.1, 0.8]
    }},
    # Size the panel (stretch to 60% of canvas, centered)
    {"tool": "manage_components", "params": {
        "action": "set_property", "target": "MenuPanel",
        "component_type": "RectTransform",
        "properties": {
            "anchorMin": [0.2, 0.1], "anchorMax": [0.8, 0.9],
            "sizeDelta": [0, 0], "anchoredPosition": [0, 0]
        }
    }}
])
```

### Create Text (TextMeshPro)

TextMeshProUGUI automatically adds a RectTransform when added to a child of a Canvas. If `packages.textmeshpro` is `false`, use `UnityEngine.UI.Text` instead.

```python
batch_execute(fail_fast=True, commands=[
    {"tool": "manage_gameobject", "params": {
        "action": "create", "name": "TitleText", "parent": "MenuPanel"
    }},
    {"tool": "manage_components", "params": {
        "action": "add", "target": "TitleText",
        "component_type": "TextMeshProUGUI"
    }},
    {"tool": "manage_components", "params": {
        "action": "set_property", "target": "TitleText",
        "component_type": "TextMeshProUGUI",
        "properties": {
            "text": "My Game Title",
            "fontSize": 48,
            "alignment": 514,
            "color": [1, 1, 1, 1]
        }
    }},
    {"tool": "manage_components", "params": {
        "action": "set_property", "target": "TitleText",
        "component_type": "RectTransform",
        "properties": {
            "anchorMin": [0, 0.8], "anchorMax": [1, 1],
            "sizeDelta": [0, 0], "anchoredPosition": [0, 0]
        }
    }}
])
```

> **TextMeshPro alignment values:** 257=TopLeft, 258=TopCenter, 260=TopRight, 513=MiddleLeft, 514=MiddleCenter, 516=MiddleRight, 1025=BottomLeft, 1026=BottomCenter, 1028=BottomRight.

### Create Button (With Label)

A Button needs an `Image` (visual) + `Button` (interaction) on the parent, and a child with `TextMeshProUGUI` for the label.

```python
batch_execute(fail_fast=True, commands=[
    {"tool": "manage_gameobject", "params": {
        "action": "create", "name": "StartButton", "parent": "MenuPanel"
    }},
    {"tool": "manage_components", "params": {
        "action": "add", "target": "StartButton", "component_type": "Image"
    }},
    {"tool": "manage_components", "params": {
        "action": "add", "target": "StartButton", "component_type": "Button"
    }},
    {"tool": "manage_components", "params": {
        "action": "set_property", "target": "StartButton",
        "component_type": "Image", "property": "color",
        "value": [0.2, 0.6, 1.0, 1.0]
    }},
    {"tool": "manage_components", "params": {
        "action": "set_property", "target": "StartButton",
        "component_type": "RectTransform",
        "properties": {
            "anchorMin": [0.5, 0.5], "anchorMax": [0.5, 0.5],
            "sizeDelta": [300, 60], "anchoredPosition": [0, 0]
        }
    }},
    # Child text label (stretches to fill button)
    {"tool": "manage_gameobject", "params": {
        "action": "create", "name": "StartButton_Label", "parent": "StartButton"
    }},
    {"tool": "manage_components", "params": {
        "action": "add", "target": "StartButton_Label",
        "component_type": "TextMeshProUGUI"
    }},
    {"tool": "manage_components", "params": {
        "action": "set_property", "target": "StartButton_Label",
        "component_type": "TextMeshProUGUI",
        "properties": {"text": "Start Game", "fontSize": 24, "alignment": 514}
    }},
    {"tool": "manage_components", "params": {
        "action": "set_property", "target": "StartButton_Label",
        "component_type": "RectTransform",
        "properties": {
            "anchorMin": [0, 0], "anchorMax": [1, 1],
            "sizeDelta": [0, 0], "anchoredPosition": [0, 0]
        }
    }}
])
```

### Create Slider (With Reference Wiring)

A Slider requires a specific hierarchy and **must have its `fillRect` and `handleRect` references wired** to function.

```python
# Step 1: Create hierarchy
batch_execute(fail_fast=True, commands=[
    {"tool": "manage_gameobject", "params": {
        "action": "create", "name": "HealthSlider", "parent": "MainCanvas"
    }},
    {"tool": "manage_components", "params": {
        "action": "add", "target": "HealthSlider", "component_type": "Slider"
    }},
    {"tool": "manage_components", "params": {
        "action": "add", "target": "HealthSlider", "component_type": "Image"
    }},
    {"tool": "manage_components", "params": {
        "action": "set_property", "target": "HealthSlider",
        "component_type": "RectTransform",
        "properties": {
            "anchorMin": [0.5, 0.5], "anchorMax": [0.5, 0.5],
            "sizeDelta": [400, 30], "anchoredPosition": [0, 0]
        }
    }},
    # Background
    {"tool": "manage_gameobject", "params": {
        "action": "create", "name": "SliderBG", "parent": "HealthSlider"
    }},
    {"tool": "manage_components", "params": {
        "action": "add", "target": "SliderBG", "component_type": "Image"
    }},
    {"tool": "manage_components", "params": {
        "action": "set_property", "target": "SliderBG",
        "component_type": "Image", "property": "color", "value": [0.3, 0.3, 0.3, 1.0]
    }},
    {"tool": "manage_components", "params": {
        "action": "set_property", "target": "SliderBG",
        "component_type": "RectTransform",
        "properties": {"anchorMin": [0, 0], "anchorMax": [1, 1], "sizeDelta": [0, 0]}
    }},
    # Fill Area + Fill
    {"tool": "manage_gameobject", "params": {
        "action": "create", "name": "FillArea", "parent": "HealthSlider"
    }},
    {"tool": "manage_components", "params": {
        "action": "set_property", "target": "FillArea",
        "component_type": "RectTransform",
        "properties": {"anchorMin": [0, 0], "anchorMax": [1, 1], "sizeDelta": [0, 0]}
    }},
    {"tool": "manage_gameobject", "params": {
        "action": "create", "name": "SliderFill", "parent": "FillArea"
    }},
    {"tool": "manage_components", "params": {
        "action": "add", "target": "SliderFill", "component_type": "Image"
    }},
    {"tool": "manage_components", "params": {
        "action": "set_property", "target": "SliderFill",
        "component_type": "Image", "property": "color", "value": [0.2, 0.8, 0.2, 1.0]
    }},
    {"tool": "manage_components", "params": {
        "action": "set_property", "target": "SliderFill",
        "component_type": "RectTransform",
        "properties": {"anchorMin": [0, 0], "anchorMax": [1, 1], "sizeDelta": [0, 0]}
    }},
    # Handle Area + Handle
    {"tool": "manage_gameobject", "params": {
        "action": "create", "name": "HandleArea", "parent": "HealthSlider"
    }},
    {"tool": "manage_components", "params": {
        "action": "set_property", "target": "HandleArea",
        "component_type": "RectTransform",
        "properties": {"anchorMin": [0, 0], "anchorMax": [1, 1], "sizeDelta": [0, 0]}
    }},
    {"tool": "manage_gameobject", "params": {
        "action": "create", "name": "SliderHandle", "parent": "HandleArea"
    }},
    {"tool": "manage_components", "params": {
        "action": "add", "target": "SliderHandle", "component_type": "Image"
    }},
    {"tool": "manage_components", "params": {
        "action": "set_property", "target": "SliderHandle",
        "component_type": "RectTransform",
        "properties": {"anchorMin": [0.5, 0], "anchorMax": [0.5, 1], "sizeDelta": [20, 0]}
    }}
])

# Step 2: Wire Slider references (CRITICAL — slider won't work without this)
batch_execute(fail_fast=True, commands=[
    {"tool": "manage_components", "params": {
        "action": "set_property", "target": "HealthSlider",
        "component_type": "Slider", "property": "fillRect",
        "value": {"name": "SliderFill"}
    }},
    {"tool": "manage_components", "params": {
        "action": "set_property", "target": "HealthSlider",
        "component_type": "Slider", "property": "handleRect",
        "value": {"name": "SliderHandle"}
    }}
])
```

### Create Input Field (With Reference Wiring)

```python
# Step 1: Create hierarchy
batch_execute(fail_fast=True, commands=[
    {"tool": "manage_gameobject", "params": {
        "action": "create", "name": "NameInput", "parent": "MenuPanel"
    }},
    {"tool": "manage_components", "params": {
        "action": "add", "target": "NameInput", "component_type": "Image"
    }},
    {"tool": "manage_components", "params": {
        "action": "add", "target": "NameInput",
        "component_type": "TMP_InputField"
    }},
    {"tool": "manage_components", "params": {
        "action": "set_property", "target": "NameInput",
        "component_type": "RectTransform",
        "properties": {
            "anchorMin": [0.5, 0.5], "anchorMax": [0.5, 0.5],
            "sizeDelta": [400, 50], "anchoredPosition": [0, 0]
        }
    }},
    # Text Area child (clips text to input bounds)
    {"tool": "manage_gameobject", "params": {
        "action": "create", "name": "InputTextArea", "parent": "NameInput"
    }},
    {"tool": "manage_components", "params": {
        "action": "add", "target": "InputTextArea", "component_type": "RectMask2D"
    }},
    {"tool": "manage_components", "params": {
        "action": "set_property", "target": "InputTextArea",
        "component_type": "RectTransform",
        "properties": {"anchorMin": [0, 0], "anchorMax": [1, 1], "sizeDelta": [-16, -8]}
    }},
    # Placeholder
    {"tool": "manage_gameobject", "params": {
        "action": "create", "name": "InputPlaceholder", "parent": "InputTextArea"
    }},
    {"tool": "manage_components", "params": {
        "action": "add", "target": "InputPlaceholder", "component_type": "TextMeshProUGUI"
    }},
    {"tool": "manage_components", "params": {
        "action": "set_property", "target": "InputPlaceholder",
        "component_type": "TextMeshProUGUI",
        "properties": {"text": "Enter name...", "fontStyle": 2, "color": [0.5, 0.5, 0.5, 0.5]}
    }},
    {"tool": "manage_components", "params": {
        "action": "set_property", "target": "InputPlaceholder",
        "component_type": "RectTransform",
        "properties": {"anchorMin": [0, 0], "anchorMax": [1, 1], "sizeDelta": [0, 0]}
    }},
    # Actual text display
    {"tool": "manage_gameobject", "params": {
        "action": "create", "name": "InputText", "parent": "InputTextArea"
    }},
    {"tool": "manage_components", "params": {
        "action": "add", "target": "InputText", "component_type": "TextMeshProUGUI"
    }},
    {"tool": "manage_components", "params": {
        "action": "set_property", "target": "InputText",
        "component_type": "RectTransform",
        "properties": {"anchorMin": [0, 0], "anchorMax": [1, 1], "sizeDelta": [0, 0]}
    }}
])

# Step 2: Wire TMP_InputField references (CRITICAL)
batch_execute(fail_fast=True, commands=[
    {"tool": "manage_components", "params": {
        "action": "set_property", "target": "NameInput",
        "component_type": "TMP_InputField", "property": "textViewport",
        "value": {"name": "InputTextArea"}
    }},
    {"tool": "manage_components", "params": {
        "action": "set_property", "target": "NameInput",
        "component_type": "TMP_InputField", "property": "textComponent",
        "value": {"name": "InputText"}
    }},
    {"tool": "manage_components", "params": {
        "action": "set_property", "target": "NameInput",
        "component_type": "TMP_InputField", "property": "placeholder",
        "value": {"name": "InputPlaceholder"}
    }}
])
```

### Create Toggle (With Reference Wiring)

```python
batch_execute(fail_fast=True, commands=[
    {"tool": "manage_gameobject", "params": {
        "action": "create", "name": "SoundToggle", "parent": "MenuPanel"
    }},
    {"tool": "manage_components", "params": {
        "action": "add", "target": "SoundToggle", "component_type": "Toggle"
    }},
    {"tool": "manage_components", "params": {
        "action": "set_property", "target": "SoundToggle",
        "component_type": "RectTransform",
        "properties": {
            "anchorMin": [0.5, 0.5], "anchorMax": [0.5, 0.5],
            "sizeDelta": [200, 30], "anchoredPosition": [0, 0]
        }
    }},
    # Background box
    {"tool": "manage_gameobject", "params": {
        "action": "create", "name": "ToggleBG", "parent": "SoundToggle"
    }},
    {"tool": "manage_components", "params": {
        "action": "add", "target": "ToggleBG", "component_type": "Image"
    }},
    {"tool": "manage_components", "params": {
        "action": "set_property", "target": "ToggleBG",
        "component_type": "RectTransform",
        "properties": {"anchorMin": [0, 0.5], "anchorMax": [0, 0.5], "sizeDelta": [26, 26], "anchoredPosition": [13, 0]}
    }},
    # Checkmark
    {"tool": "manage_gameobject", "params": {
        "action": "create", "name": "ToggleCheckmark", "parent": "ToggleBG"
    }},
    {"tool": "manage_components", "params": {
        "action": "add", "target": "ToggleCheckmark", "component_type": "Image"
    }},
    {"tool": "manage_components", "params": {
        "action": "set_property", "target": "ToggleCheckmark",
        "component_type": "RectTransform",
        "properties": {"anchorMin": [0.1, 0.1], "anchorMax": [0.9, 0.9], "sizeDelta": [0, 0]}
    }},
    # Label
    {"tool": "manage_gameobject", "params": {
        "action": "create", "name": "ToggleLabel", "parent": "SoundToggle"
    }},
    {"tool": "manage_components", "params": {
        "action": "add", "target": "ToggleLabel", "component_type": "TextMeshProUGUI"
    }},
    {"tool": "manage_components", "params": {
        "action": "set_property", "target": "ToggleLabel",
        "component_type": "TextMeshProUGUI",
        "properties": {"text": "Sound Effects", "fontSize": 18, "alignment": 513}
    }},
    {"tool": "manage_components", "params": {
        "action": "set_property", "target": "ToggleLabel",
        "component_type": "RectTransform",
        "properties": {"anchorMin": [0, 0], "anchorMax": [1, 1], "sizeDelta": [-35, 0], "anchoredPosition": [17.5, 0]}
    }}
])

# Wire Toggle references (CRITICAL)
batch_execute(fail_fast=True, commands=[
    {"tool": "manage_components", "params": {
        "action": "set_property", "target": "SoundToggle",
        "component_type": "Toggle", "property": "graphic",
        "value": {"name": "ToggleCheckmark"}
    }}
])
```

### Add Layout Group (Vertical/Horizontal/Grid)

Layout groups auto-arrange child elements, so you can skip manual RectTransform positioning for children.

```python
batch_execute(fail_fast=True, commands=[
    {"tool": "manage_components", "params": {
        "action": "add", "target": "MenuPanel",
        "component_type": "VerticalLayoutGroup"
    }},
    {"tool": "manage_components", "params": {
        "action": "set_property", "target": "MenuPanel",
        "component_type": "VerticalLayoutGroup",
        "properties": {
            "spacing": 10,
            "childAlignment": 4,
            "childForceExpandWidth": True,
            "childForceExpandHeight": False,
            "padding": {"left": 20, "right": 20, "top": 20, "bottom": 20}
        }
    }},
    {"tool": "manage_components", "params": {
        "action": "add", "target": "MenuPanel",
        "component_type": "ContentSizeFitter"
    }},
    {"tool": "manage_components", "params": {
        "action": "set_property", "target": "MenuPanel",
        "component_type": "ContentSizeFitter",
        "properties": { "verticalFit": 2 }
    }}
])
```

> **childAlignment values:** 0=UpperLeft, 1=UpperCenter, 2=UpperRight, 3=MiddleLeft, 4=MiddleCenter, 5=MiddleRight, 6=LowerLeft, 7=LowerCenter, 8=LowerRight.
> **ContentSizeFitter fit modes:** 0=Unconstrained, 1=MinSize, 2=PreferredSize.

### Complete Example: Main Menu Screen

Combines multiple templates into a full menu screen in two batch calls (default 25 command limit per batch, configurable in Unity MCP Tools window up to 100). **Assumes `project_info` has been read and `activeInputHandler` is known.**

```python
# Batch 1: Canvas + EventSystem + Panel + Title
batch_execute(fail_fast=True, commands=[
    # Canvas
    {"tool": "manage_gameobject", "params": {"action": "create", "name": "MenuCanvas"}},
    {"tool": "manage_components", "params": {"action": "add", "target": "MenuCanvas", "component_type": "Canvas"}},
    {"tool": "manage_components", "params": {"action": "add", "target": "MenuCanvas", "component_type": "CanvasScaler"}},
    {"tool": "manage_components", "params": {"action": "add", "target": "MenuCanvas", "component_type": "GraphicRaycaster"}},
    {"tool": "manage_components", "params": {"action": "set_property", "target": "MenuCanvas", "component_type": "Canvas", "property": "renderMode", "value": 0}},
    {"tool": "manage_components", "params": {"action": "set_property", "target": "MenuCanvas", "component_type": "CanvasScaler", "properties": {"uiScaleMode": 1, "referenceResolution": [1920, 1080]}}},
    # EventSystem — use StandaloneInputModule OR InputSystemUIInputModule based on project_info
    {"tool": "manage_gameobject", "params": {"action": "create", "name": "EventSystem"}},
    {"tool": "manage_components", "params": {"action": "add", "target": "EventSystem", "component_type": "UnityEngine.EventSystems.EventSystem"}},
    {"tool": "manage_components", "params": {"action": "add", "target": "EventSystem", "component_type": "UnityEngine.EventSystems.StandaloneInputModule"}},
    # Panel (centered, 60% width)
    {"tool": "manage_gameobject", "params": {"action": "create", "name": "MenuPanel", "parent": "MenuCanvas"}},
    {"tool": "manage_components", "params": {"action": "add", "target": "MenuPanel", "component_type": "Image"}},
    {"tool": "manage_components", "params": {"action": "set_property", "target": "MenuPanel", "component_type": "Image", "property": "color", "value": [0.1, 0.1, 0.15, 0.9]}},
    {"tool": "manage_components", "params": {"action": "set_property", "target": "MenuPanel", "component_type": "RectTransform", "properties": {"anchorMin": [0.2, 0.15], "anchorMax": [0.8, 0.85], "sizeDelta": [0, 0]}}},
    {"tool": "manage_components", "params": {"action": "add", "target": "MenuPanel", "component_type": "VerticalLayoutGroup"}},
    {"tool": "manage_components", "params": {"action": "set_property", "target": "MenuPanel", "component_type": "VerticalLayoutGroup", "properties": {"spacing": 20, "childAlignment": 4, "childForceExpandWidth": True, "childForceExpandHeight": False}}},
    # Title
    {"tool": "manage_gameobject", "params": {"action": "create", "name": "Title", "parent": "MenuPanel"}},
    {"tool": "manage_components", "params": {"action": "add", "target": "Title", "component_type": "TextMeshProUGUI"}},
    {"tool": "manage_components", "params": {"action": "set_property", "target": "Title", "component_type": "TextMeshProUGUI", "properties": {"text": "My Game", "fontSize": 64, "alignment": 514, "color": [1, 1, 1, 1]}}}
])

# Batch 2: Buttons
batch_execute(fail_fast=True, commands=[
    # Play Button
    {"tool": "manage_gameobject", "params": {"action": "create", "name": "PlayButton", "parent": "MenuPanel"}},
    {"tool": "manage_components", "params": {"action": "add", "target": "PlayButton", "component_type": "Image"}},
    {"tool": "manage_components", "params": {"action": "add", "target": "PlayButton", "component_type": "Button"}},
    {"tool": "manage_components", "params": {"action": "set_property", "target": "PlayButton", "component_type": "Image", "property": "color", "value": [0.2, 0.6, 1.0, 1.0]}},
    {"tool": "manage_gameobject", "params": {"action": "create", "name": "PlayLabel", "parent": "PlayButton"}},
    {"tool": "manage_components", "params": {"action": "add", "target": "PlayLabel", "component_type": "TextMeshProUGUI"}},
    {"tool": "manage_components", "params": {"action": "set_property", "target": "PlayLabel", "component_type": "TextMeshProUGUI", "properties": {"text": "Play", "fontSize": 32, "alignment": 514}}},
    {"tool": "manage_components", "params": {"action": "set_property", "target": "PlayLabel", "component_type": "RectTransform", "properties": {"anchorMin": [0, 0], "anchorMax": [1, 1], "sizeDelta": [0, 0]}}},
    # Settings Button
    {"tool": "manage_gameobject", "params": {"action": "create", "name": "SettingsButton", "parent": "MenuPanel"}},
    {"tool": "manage_components", "params": {"action": "add", "target": "SettingsButton", "component_type": "Image"}},
    {"tool": "manage_components", "params": {"action": "add", "target": "SettingsButton", "component_type": "Button"}},
    {"tool": "manage_components", "params": {"action": "set_property", "target": "SettingsButton", "component_type": "Image", "property": "color", "value": [0.3, 0.3, 0.35, 1.0]}},
    {"tool": "manage_gameobject", "params": {"action": "create", "name": "SettingsLabel", "parent": "SettingsButton"}},
    {"tool": "manage_components", "params": {"action": "add", "target": "SettingsLabel", "component_type": "TextMeshProUGUI"}},
    {"tool": "manage_components", "params": {"action": "set_property", "target": "SettingsLabel", "component_type": "TextMeshProUGUI", "properties": {"text": "Settings", "fontSize": 32, "alignment": 514}}},
    {"tool": "manage_components", "params": {"action": "set_property", "target": "SettingsLabel", "component_type": "RectTransform", "properties": {"anchorMin": [0, 0], "anchorMax": [1, 1], "sizeDelta": [0, 0]}}},
    # Quit Button
    {"tool": "manage_gameobject", "params": {"action": "create", "name": "QuitButton", "parent": "MenuPanel"}},
    {"tool": "manage_components", "params": {"action": "add", "target": "QuitButton", "component_type": "Image"}},
    {"tool": "manage_components", "params": {"action": "add", "target": "QuitButton", "component_type": "Button"}},
    {"tool": "manage_components", "params": {"action": "set_property", "target": "QuitButton", "component_type": "Image", "property": "color", "value": [0.8, 0.2, 0.2, 1.0]}},
    {"tool": "manage_gameobject", "params": {"action": "create", "name": "QuitLabel", "parent": "QuitButton"}},
    {"tool": "manage_components", "params": {"action": "add", "target": "QuitLabel", "component_type": "TextMeshProUGUI"}},
    {"tool": "manage_components", "params": {"action": "set_property", "target": "QuitLabel", "component_type": "TextMeshProUGUI", "properties": {"text": "Quit", "fontSize": 32, "alignment": 514}}},
    {"tool": "manage_components", "params": {"action": "set_property", "target": "QuitLabel", "component_type": "RectTransform", "properties": {"anchorMin": [0, 0], "anchorMax": [1, 1], "sizeDelta": [0, 0]}}}
])
```

### UI Component Quick Reference

| UI Element | Required Components | Notes |
| ---------- | ------------------- | ----- |
| **Canvas** | Canvas + CanvasScaler + GraphicRaycaster | Root for all UI. One per screen. |
| **EventSystem** | EventSystem + input module (see below) | One per scene. Required for interaction. |
| **Panel** | Image + RectTransform sizing | Container. Set color for background. |
| **Text** | TextMeshProUGUI (or Text if no TMP) + RectTransform | Check `packages.textmeshpro`. |
| **Button** | Image + Button + child(TextMeshProUGUI) + RectTransform | Image = visual, Button = click handler. |
| **Slider** | Slider + Image + children + **wire fillRect/handleRect** | Won't function without wiring. |
| **Toggle** | Toggle + children + **wire graphic** | Wire checkmark Image to `graphic`. |
| **Input Field** | Image + TMP_InputField + children + **wire textViewport/textComponent/placeholder** | Won't function without wiring. |
| **Layout Group** | VerticalLayoutGroup / HorizontalLayoutGroup / GridLayoutGroup | Auto-arranges children; skip manual RectTransform on children. |

---

## Input System: Old vs New

Unity has two input systems that affect UI interaction, script input handling, and EventSystem configuration. **Always check `project_info.activeInputHandler` before creating EventSystems or writing input code.**

### Detection

```python
# Read mcpforunity://project/info
# activeInputHandler: "Old" | "New" | "Both"
# packages.inputsystem: true/false (whether com.unity.inputsystem is installed)
```

### EventSystem — Old Input Manager

Used when `activeInputHandler` is `"Old"`. Uses `StandaloneInputModule` which reads from `Input.GetAxis()` / `Input.GetButton()`.

```python
batch_execute(fail_fast=True, commands=[
    {"tool": "manage_gameobject", "params": {"action": "create", "name": "EventSystem"}},
    {"tool": "manage_components", "params": {
        "action": "add", "target": "EventSystem",
        "component_type": "UnityEngine.EventSystems.EventSystem"
    }},
    {"tool": "manage_components", "params": {
        "action": "add", "target": "EventSystem",
        "component_type": "UnityEngine.EventSystems.StandaloneInputModule"
    }}
])
```

Script pattern (old Input Manager):

```csharp
// Input.GetAxis / Input.GetKey — works with old Input Manager
void Update()
{
    float h = Input.GetAxis("Horizontal");
    float v = Input.GetAxis("Vertical");
    transform.Translate(new Vector3(h, 0, v) * speed * Time.deltaTime);

    if (Input.GetKeyDown(KeyCode.Space))
        Jump();

    if (Input.GetMouseButtonDown(0))
        Fire();
}
```

### EventSystem — New Input System

Used when `activeInputHandler` is `"New"` or `"Both"`. Uses `InputSystemUIInputModule` from the `com.unity.inputsystem` package.

```python
batch_execute(fail_fast=True, commands=[
    {"tool": "manage_gameobject", "params": {"action": "create", "name": "EventSystem"}},
    {"tool": "manage_components", "params": {
        "action": "add", "target": "EventSystem",
        "component_type": "UnityEngine.EventSystems.EventSystem"
    }},
    {"tool": "manage_components", "params": {
        "action": "add", "target": "EventSystem",
        "component_type": "UnityEngine.InputSystem.UI.InputSystemUIInputModule"
    }}
])
```

Script pattern (new Input System with `PlayerInput` component):

```csharp
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerController : MonoBehaviour
{
    public float speed = 5f;
    private Vector2 moveInput;

    // Called by PlayerInput component via SendMessages or UnityEvents
    public void OnMove(InputValue value)
    {
        moveInput = value.Get<Vector2>();
    }

    public void OnJump(InputValue value)
    {
        if (value.isPressed)
            Jump();
    }

    void Update()
    {
        Vector3 move = new Vector3(moveInput.x, 0, moveInput.y);
        transform.Translate(move * speed * Time.deltaTime);
    }
}
```

### When `activeInputHandler` is `"Both"`

Both systems are active simultaneously. For UI, prefer `InputSystemUIInputModule`. For gameplay scripts, either approach works — `Input.GetAxis()` still functions alongside the new Input System.

```python
# UI: use new Input System module
{"tool": "manage_components", "params": {
    "action": "add", "target": "EventSystem",
    "component_type": "UnityEngine.InputSystem.UI.InputSystemUIInputModule"
}}

# Gameplay scripts: Input.GetAxis() still works in "Both" mode
# But prefer the new Input System for consistency
```

> **Gotcha:** Adding `StandaloneInputModule` when `activeInputHandler` is `"New"` will cause a runtime error. Always check first.

---

## Batch Operations

### Mass Property Update

```python
# Find all enemies
enemies = find_gameobjects(search_term="Enemy", search_method="by_tag")

# Update health on all enemies
commands = []
for enemy_id in enemies["ids"]:
    commands.append({
        "tool": "manage_components",
        "params": {
            "action": "set_property",
            "target": enemy_id,
            "component_type": "EnemyHealth",
            "property": "maxHealth",
            "value": 100
        }
    })

# Execute in batches
for i in range(0, len(commands), 25):
    batch_execute(commands=commands[i:i+25], parallel=True)
```

### Mass Object Creation with Variations

```python
import random

commands = []
for i in range(20):
    commands.append({
        "tool": "manage_gameobject",
        "params": {
            "action": "create",
            "name": f"Tree_{i}",
            "primitive_type": "Capsule",
            "position": [random.uniform(-50, 50), 0, random.uniform(-50, 50)],
            "scale": [1, random.uniform(2, 5), 1]
        }
    })

batch_execute(commands=commands, parallel=True)
```

### Cleanup Pattern

```python
# Find all temporary objects
temps = find_gameobjects(search_term="Temp_", search_method="by_name")

# Delete in batch
commands = [
    {"tool": "manage_gameobject", "params": {"action": "delete", "target": id}}
    for id in temps["ids"]
]

batch_execute(commands=commands, fail_fast=False)
```

---

## Error Recovery Patterns

### Stale File Recovery

```python
try:
    apply_text_edits(uri=script_uri, edits=[...], precondition_sha256=old_sha)
except Exception as e:
    if "stale_file" in str(e):
        # Re-fetch SHA
        new_sha = get_sha(uri=script_uri)
        # Retry with new SHA
        apply_text_edits(uri=script_uri, edits=[...], precondition_sha256=new_sha["sha256"])
```

### Domain Reload Recovery

```python
# After domain reload, connection may be lost
# Wait and retry pattern:
import time

max_retries = 5
for attempt in range(max_retries):
    try:
        editor_state = read_resource("mcpforunity://editor/state")
        if editor_state["ready_for_tools"]:
            break
    except:
        time.sleep(2 ** attempt)  # Exponential backoff
```

### Compilation Block Recovery

```python
# If tools fail due to compilation:
# 1. Check console for errors
errors = read_console(types=["error"], count=20)

# 2. Fix the script errors
# ... edit scripts ...

# 3. Force refresh
refresh_unity(mode="force", scope="scripts", compile="request", wait_for_ready=True)

# 4. Verify clean console
errors = read_console(types=["error"], count=5)
if not errors["messages"]:
    # Safe to proceed with tools
    pass
```
