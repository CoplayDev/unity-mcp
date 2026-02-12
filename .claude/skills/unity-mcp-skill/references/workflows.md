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

Unity UI (Canvas-based UGUI) requires specific component hierarchies. Use `batch_execute` with `fail_fast=True` to create complete UI elements in a single call.

> **Template warning:** This section is a skill template library, not a guaranteed source of truth. Examples may be inaccurate for your Unity version, package setup, or project conventions.
> **Use safely:**
> 1. Validate component/property names against the current project.
> 2. Prefer targeting by instance ID or full path over generic names.
> 3. Assume complex controls (Slider/Toggle/TMP Input) may need extra reference wiring.
> 4. Treat numeric enum values as placeholders and verify before reuse.

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

If no EventSystem exists in the scene, buttons and other interactive UI elements won't respond to input. Create one alongside your first Canvas.

```python
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
```

> **Note:** For projects using legacy Input Manager instead of Input System, use `"component_type": "UnityEngine.EventSystems.StandaloneInputModule"` instead.

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
    # Set semi-transparent dark background
    {"tool": "manage_components", "params": {
        "action": "set_property", "target": "MenuPanel",
        "component_type": "Image", "property": "color",
        "value": [0.1, 0.1, 0.1, 0.8]
    }}
])
```

### Create Text (TextMeshPro)

TextMeshProUGUI automatically adds a RectTransform when added to a child of a Canvas.

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
    }}
])
```

> **TextMeshPro alignment values:** 257=TopLeft, 258=TopCenter, 260=TopRight, 513=MiddleLeft, 514=MiddleCenter, 516=MiddleRight, 1025=BottomLeft, 1026=BottomCenter, 1028=BottomRight.

### Create Button (With Label)

A Button needs an `Image` (visual) + `Button` (interaction) on the parent, and a child with `TextMeshProUGUI` for the label.

```python
batch_execute(fail_fast=True, commands=[
    # Button container with Image + Button components
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
    # Child text label
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
    }}
])
```

### Create Slider

A Slider requires a specific hierarchy: the slider root, a background, a fill area with fill, and a handle area with handle.

```python
batch_execute(fail_fast=True, commands=[
    # Slider root
    {"tool": "manage_gameobject", "params": {
        "action": "create", "name": "HealthSlider", "parent": "MainCanvas"
    }},
    {"tool": "manage_components", "params": {
        "action": "add", "target": "HealthSlider", "component_type": "Slider"
    }},
    {"tool": "manage_components", "params": {
        "action": "add", "target": "HealthSlider", "component_type": "Image"
    }},
    # Background
    {"tool": "manage_gameobject", "params": {
        "action": "create", "name": "Background", "parent": "HealthSlider"
    }},
    {"tool": "manage_components", "params": {
        "action": "add", "target": "Background", "component_type": "Image"
    }},
    {"tool": "manage_components", "params": {
        "action": "set_property", "target": "Background",
        "component_type": "Image", "property": "color",
        "value": [0.3, 0.3, 0.3, 1.0]
    }},
    # Fill Area + Fill
    {"tool": "manage_gameobject", "params": {
        "action": "create", "name": "Fill Area", "parent": "HealthSlider"
    }},
    {"tool": "manage_gameobject", "params": {
        "action": "create", "name": "Fill", "parent": "Fill Area"
    }},
    {"tool": "manage_components", "params": {
        "action": "add", "target": "Fill", "component_type": "Image"
    }},
    {"tool": "manage_components", "params": {
        "action": "set_property", "target": "Fill",
        "component_type": "Image", "property": "color",
        "value": [0.2, 0.8, 0.2, 1.0]
    }},
    # Handle Area + Handle
    {"tool": "manage_gameobject", "params": {
        "action": "create", "name": "Handle Slide Area", "parent": "HealthSlider"
    }},
    {"tool": "manage_gameobject", "params": {
        "action": "create", "name": "Handle", "parent": "Handle Slide Area"
    }},
    {"tool": "manage_components", "params": {
        "action": "add", "target": "Handle", "component_type": "Image"
    }}
])
```

### Create Input Field (TextMeshPro)

```python
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
    # Text area child
    {"tool": "manage_gameobject", "params": {
        "action": "create", "name": "Text Area", "parent": "NameInput"
    }},
    {"tool": "manage_components", "params": {
        "action": "add", "target": "Text Area",
        "component_type": "RectMask2D"
    }},
    # Placeholder
    {"tool": "manage_gameobject", "params": {
        "action": "create", "name": "Placeholder", "parent": "Text Area"
    }},
    {"tool": "manage_components", "params": {
        "action": "add", "target": "Placeholder",
        "component_type": "TextMeshProUGUI"
    }},
    {"tool": "manage_components", "params": {
        "action": "set_property", "target": "Placeholder",
        "component_type": "TextMeshProUGUI",
        "properties": {"text": "Enter name...", "fontStyle": 2, "color": [0.5, 0.5, 0.5, 0.5]}
    }},
    # Actual text
    {"tool": "manage_gameobject", "params": {
        "action": "create", "name": "Text", "parent": "Text Area"
    }},
    {"tool": "manage_components", "params": {
        "action": "add", "target": "Text",
        "component_type": "TextMeshProUGUI"
    }}
])
```

### Create Toggle (Checkbox)

```python
batch_execute(fail_fast=True, commands=[
    {"tool": "manage_gameobject", "params": {
        "action": "create", "name": "SoundToggle", "parent": "MenuPanel"
    }},
    {"tool": "manage_components", "params": {
        "action": "add", "target": "SoundToggle", "component_type": "Toggle"
    }},
    # Background box
    {"tool": "manage_gameobject", "params": {
        "action": "create", "name": "Background", "parent": "SoundToggle"
    }},
    {"tool": "manage_components", "params": {
        "action": "add", "target": "Background", "component_type": "Image"
    }},
    # Checkmark
    {"tool": "manage_gameobject", "params": {
        "action": "create", "name": "Checkmark", "parent": "Background"
    }},
    {"tool": "manage_components", "params": {
        "action": "add", "target": "Checkmark", "component_type": "Image"
    }},
    # Label
    {"tool": "manage_gameobject", "params": {
        "action": "create", "name": "Label", "parent": "SoundToggle"
    }},
    {"tool": "manage_components", "params": {
        "action": "add", "target": "Label", "component_type": "TextMeshProUGUI"
    }},
    {"tool": "manage_components", "params": {
        "action": "set_property", "target": "Label",
        "component_type": "TextMeshProUGUI",
        "properties": {"text": "Sound Effects", "fontSize": 18, "alignment": 513}
    }}
])
```

### Add Layout Group (Vertical/Horizontal/Grid)

Layout groups auto-arrange child elements. Add to any container.

```python
# Vertical layout for a menu panel
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
            "childAlignment": 1,
            "childForceExpandWidth": True,
            "childForceExpandHeight": False
        }
    }},
    # Add ContentSizeFitter to auto-resize
    {"tool": "manage_components", "params": {
        "action": "add", "target": "MenuPanel",
        "component_type": "ContentSizeFitter"
    }},
    {"tool": "manage_components", "params": {
        "action": "set_property", "target": "MenuPanel",
        "component_type": "ContentSizeFitter",
        "properties": {
            "verticalFit": 2
        }
    }}
])
```

> **childAlignment values:** 0=UpperLeft, 1=UpperCenter, 2=UpperRight, 3=MiddleLeft, 4=MiddleCenter, 5=MiddleRight, 6=LowerLeft, 7=LowerCenter, 8=LowerRight.
> **ContentSizeFitter fit modes:** 0=Unconstrained, 1=MinSize, 2=PreferredSize.

### Complete Example: Main Menu Screen

Combines multiple templates into a full menu screen in two batch calls (default 25 command limit per batch, configurable in Unity MCP Tools window up to 100).

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
    # EventSystem
    {"tool": "manage_gameobject", "params": {"action": "create", "name": "EventSystem"}},
    {"tool": "manage_components", "params": {"action": "add", "target": "EventSystem", "component_type": "UnityEngine.EventSystems.EventSystem"}},
    {"tool": "manage_components", "params": {"action": "add", "target": "EventSystem", "component_type": "UnityEngine.EventSystems.StandaloneInputModule"}},
    # Panel
    {"tool": "manage_gameobject", "params": {"action": "create", "name": "MenuPanel", "parent": "MenuCanvas"}},
    {"tool": "manage_components", "params": {"action": "add", "target": "MenuPanel", "component_type": "Image"}},
    {"tool": "manage_components", "params": {"action": "set_property", "target": "MenuPanel", "component_type": "Image", "property": "color", "value": [0.1, 0.1, 0.15, 0.9]}},
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
    {"tool": "manage_gameobject", "params": {"action": "create", "name": "PlayButton_Label", "parent": "PlayButton"}},
    {"tool": "manage_components", "params": {"action": "add", "target": "PlayButton_Label", "component_type": "TextMeshProUGUI"}},
    {"tool": "manage_components", "params": {"action": "set_property", "target": "PlayButton_Label", "component_type": "TextMeshProUGUI", "properties": {"text": "Play", "fontSize": 32, "alignment": 514}}},
    # Settings Button
    {"tool": "manage_gameobject", "params": {"action": "create", "name": "SettingsButton", "parent": "MenuPanel"}},
    {"tool": "manage_components", "params": {"action": "add", "target": "SettingsButton", "component_type": "Image"}},
    {"tool": "manage_components", "params": {"action": "add", "target": "SettingsButton", "component_type": "Button"}},
    {"tool": "manage_components", "params": {"action": "set_property", "target": "SettingsButton", "component_type": "Image", "property": "color", "value": [0.3, 0.3, 0.35, 1.0]}},
    {"tool": "manage_gameobject", "params": {"action": "create", "name": "SettingsButton_Label", "parent": "SettingsButton"}},
    {"tool": "manage_components", "params": {"action": "add", "target": "SettingsButton_Label", "component_type": "TextMeshProUGUI"}},
    {"tool": "manage_components", "params": {"action": "set_property", "target": "SettingsButton_Label", "component_type": "TextMeshProUGUI", "properties": {"text": "Settings", "fontSize": 32, "alignment": 514}}},
    # Quit Button
    {"tool": "manage_gameobject", "params": {"action": "create", "name": "QuitButton", "parent": "MenuPanel"}},
    {"tool": "manage_components", "params": {"action": "add", "target": "QuitButton", "component_type": "Image"}},
    {"tool": "manage_components", "params": {"action": "add", "target": "QuitButton", "component_type": "Button"}},
    {"tool": "manage_components", "params": {"action": "set_property", "target": "QuitButton", "component_type": "Image", "property": "color", "value": [0.8, 0.2, 0.2, 1.0]}},
    {"tool": "manage_gameobject", "params": {"action": "create", "name": "QuitButton_Label", "parent": "QuitButton"}},
    {"tool": "manage_components", "params": {"action": "add", "target": "QuitButton_Label", "component_type": "TextMeshProUGUI"}},
    {"tool": "manage_components", "params": {"action": "set_property", "target": "QuitButton_Label", "component_type": "TextMeshProUGUI", "properties": {"text": "Quit", "fontSize": 32, "alignment": 514}}}
])
```

### UI Component Quick Reference

| UI Element | Required Components | Notes |
| ---------- | ------------------- | ----- |
| **Canvas** | Canvas + CanvasScaler + GraphicRaycaster | Root for all UI. One per screen. |
| **EventSystem** | EventSystem + StandaloneInputModule (or InputSystemUIInputModule) | One per scene. Required for interaction. |
| **Panel** | Image | Container. Set color for background. |
| **Text** | TextMeshProUGUI | Auto-adds RectTransform under Canvas. |
| **Button** | Image + Button + child(TextMeshProUGUI) | Image = visual, Button = click handler. |
| **Image** | Image | Set sprite property for custom graphics. |
| **Slider** | Slider + Image + children(Background, Fill Area/Fill, Handle Slide Area/Handle) | Complex hierarchy. |
| **Toggle** | Toggle + children(Background/Checkmark, Label) | Checkbox/radio button. |
| **Input Field** | Image + TMP_InputField + children(Text Area/Placeholder/Text) | Text input. |
| **Scroll View** | ScrollRect + Image + children(Viewport/Content, Scrollbar) | Scrollable container. |
| **Dropdown** | Image + TMP_Dropdown + children(Label, Arrow, Template) | Selection menu. |
| **Layout Group** | VerticalLayoutGroup / HorizontalLayoutGroup / GridLayoutGroup | Add to any container to auto-arrange children. |

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
