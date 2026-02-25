---
name: unity-mcp-orchestrator
description: Orchestrate Unity Editor via MCP (Model Context Protocol) tools and resources. Use when working with Unity projects through MCP for Unity - creating/modifying GameObjects, editing scripts, managing scenes, running tests, or any Unity Editor automation. Provides best practices, tool schemas, and workflow patterns for effective Unity-MCP integration.
---

# Unity-MCP Operator Guide

This skill helps you effectively use the Unity Editor with MCP tools and resources.

## Template Notice

Examples in `references/workflows.md` and `references/tools-reference.md` are reusable templates. They may be inaccurate across Unity versions, package setups (UGUI/TMP/Input System), and project-specific conventions. Please check console, compilation errors, or use screenshot after implementation.

Before applying a template:
- Validate targets/components first via resources and `find_gameobjects`.
- Treat names, enum values, and property payloads as placeholders to adapt.

## Quick Start: Resource-First Workflow

**Always read relevant resources before using tools.** This prevents errors and provides the necessary context.

```
1. Check editor state     → mcpforunity://editor/state
2. Understand the scene   → mcpforunity://scene/gameobject-api
3. Find what you need     → find_gameobjects or resources
4. Take action            → tools (manage_gameobject, create_script, script_apply_edits, apply_text_edits, validate_script, delete_script, get_sha, etc.)
5. Verify results         → read_console, capture_screenshot (in manage_scene), resources
```

## Critical Best Practices

### 1. After Writing/Editing Scripts: Always Refresh and Check Console

```python
# After create_script or script_apply_edits:
refresh_unity(mode="force", scope="scripts", compile="request", wait_for_ready=True)
read_console(types=["error"], count=10, include_stacktrace=True)
```

**Why:** Unity must compile scripts before they're usable. Compilation errors block all tool execution.

### 2. Use `batch_execute` for Multiple Operations

```python
# 10-100x faster than sequential calls
batch_execute(
    commands=[
        {"tool": "manage_gameobject", "params": {"action": "create", "name": "Cube1", "primitive_type": "Cube"}},
        {"tool": "manage_gameobject", "params": {"action": "create", "name": "Cube2", "primitive_type": "Cube"}},
        {"tool": "manage_gameobject", "params": {"action": "create", "name": "Cube3", "primitive_type": "Cube"}}
    ],
    parallel=True  # Hint only: Unity may still execute sequentially
)
```

**Max 25 commands per batch by default (configurable in Unity MCP Tools window, max 100).** Use `fail_fast=True` for dependent operations.

### 3. Use Screenshots to Verify Visual Results

```python
# Basic screenshot (saves to Assets/, returns file path only)
manage_scene(action="screenshot")

# Inline screenshot (returns base64 PNG directly to the AI)
manage_scene(action="screenshot", include_image=True)

# Use a specific camera and cap resolution for smaller payloads
manage_scene(action="screenshot", camera="MainCamera", include_image=True, max_resolution=512)

# Batch surround: captures front/back/left/right/top/bird_eye around the scene
manage_scene(action="screenshot", batch="surround", max_resolution=256)

# Batch surround centered on a specific object
manage_scene(action="screenshot", batch="surround", look_at="Player", max_resolution=256)

# Positioned screenshot: place a temp camera and capture in one call
manage_scene(action="screenshot", look_at="Player", view_position=[0, 10, -10], max_resolution=512)
```

**Best practices for AI scene understanding:**
- Use `include_image=True` when you need to *see* the scene, not just save a file.
- Use `batch="surround"` for a comprehensive overview (6 angles, one command).
- Use `look_at`/`view_position` to capture from a specific viewpoint without needing a scene camera.
- Keep `max_resolution` at 256–512 to balance quality vs. token cost.
- Combine with `look_at` on `manage_gameobject` to orient a game camera before capturing.

```python
# Agentic camera loop: point, shoot, analyze
manage_gameobject(action="look_at", target="MainCamera", look_at_target="Player")
manage_scene(action="screenshot", camera="MainCamera", include_image=True, max_resolution=512)
# → Analyze image, decide next action
```

### 4. Check Console After Major Changes

```python
read_console(
    action="get",
    types=["error", "warning"],  # Focus on problems
    count=10,
    format="detailed"
)
```

### 5. Always Check `editor_state` Before Complex Operations

```python
# Read mcpforunity://editor/state to check:
# - is_compiling: Wait if true
# - is_domain_reload_pending: Wait if true  
# - ready_for_tools: Only proceed if true
# - blocking_reasons: Why tools might fail
```

## Parameter Type Conventions

These are common patterns, not strict guarantees. `manage_components.set_property` payload shapes can vary by component/property; if a template fails, inspect the component resource payload and adjust.

### Vectors (position, rotation, scale, color)
```python
# Both forms accepted:
position=[1.0, 2.0, 3.0]        # List
position="[1.0, 2.0, 3.0]"     # JSON string
```

### Booleans
```python
# Both forms accepted:
include_inactive=True           # Boolean
include_inactive="true"         # String
```

### Colors
```python
# Auto-detected format:
color=[255, 0, 0, 255]         # 0-255 range
color=[1.0, 0.0, 0.0, 1.0]    # 0.0-1.0 normalized (auto-converted)
```

### Paths
```python
# Assets-relative (default):
path="Assets/Scripts/MyScript.cs"

# URI forms:
uri="mcpforunity://path/Assets/Scripts/MyScript.cs"
uri="file:///full/path/to/file.cs"
```

## Core Tool Categories

| Category | Key Tools | Use For |
|----------|-----------|---------|
| **Scene** | `manage_scene`, `find_gameobjects` | Scene operations, finding objects |
| **Objects** | `manage_gameobject`, `manage_components` | Creating/modifying GameObjects |
| **Scripts** | `create_script`, `script_apply_edits`, `refresh_unity` | C# code management |
| **Assets** | `manage_asset`, `manage_prefabs` | Asset operations |
| **Editor** | `manage_editor`, `execute_menu_item`, `read_console` | Editor control |
| **Testing** | `run_tests`, `get_test_job` | Unity Test Framework |
| **Batch** | `batch_execute` | Parallel/bulk operations |
| **UI** | `manage_ui`, `batch_execute` with `manage_gameobject` + `manage_components` | **UI Toolkit**: Use `manage_ui` to create UXML/USS files, attach UIDocument, inspect visual trees. **uGUI (Canvas)**: Use `batch_execute` for Canvas, Panel, Button, Text, Slider, Toggle, Input Field. **Read `mcpforunity://project/info` first** to detect uGUI/TMP/Input System/UI Toolkit availability. (see [UI workflows](references/workflows.md#ui-creation-workflows)) |

## Common Workflows

### Creating a New Script and Using It

```python
# 1. Create the script
create_script(
    path="Assets/Scripts/PlayerController.cs",
    contents="using UnityEngine;\n\npublic class PlayerController : MonoBehaviour\n{\n    void Update() { }\n}"
)

# 2. CRITICAL: Refresh and wait for compilation
refresh_unity(mode="force", scope="scripts", compile="request", wait_for_ready=True)

# 3. Check for compilation errors
read_console(types=["error"], count=10)

# 4. Only then attach to GameObject
manage_gameobject(action="modify", target="Player", components_to_add=["PlayerController"])
```

### Finding and Modifying GameObjects

```python
# 1. Find by name/tag/component (returns IDs only)
result = find_gameobjects(search_term="Enemy", search_method="by_tag", page_size=50)

# 2. Get full data via resource
# mcpforunity://scene/gameobject/{instance_id}

# 3. Modify using the ID
manage_gameobject(action="modify", target=instance_id, position=[10, 0, 0])
```

### Running and Monitoring Tests

```python
# 1. Start test run (async)
result = run_tests(mode="EditMode", test_names=["MyTests.TestSomething"])
job_id = result["job_id"]

# 2. Poll for completion
result = get_test_job(job_id=job_id, wait_timeout=60, include_failed_tests=True)
```

## Pagination Pattern

Large queries return paginated results. Always follow `next_cursor`:

```python
cursor = 0
all_items = []
while True:
    result = manage_scene(action="get_hierarchy", page_size=50, cursor=cursor)
    all_items.extend(result["data"]["items"])
    if not result["data"].get("next_cursor"):
        break
    cursor = result["data"]["next_cursor"]
```

## Multi-Instance Workflow

When multiple Unity Editors are running:

```python
# 1. List instances via resource: mcpforunity://instances
# 2. Set active instance
set_active_instance(instance="MyProject@abc123")
# 3. All subsequent calls route to that instance
```

## Error Recovery

| Symptom | Cause | Solution |
|---------|-------|----------|
| Tools return "busy" | Compilation in progress | Wait, check `editor_state` |
| "stale_file" error | File changed since SHA | Re-fetch SHA with `get_sha`, retry |
| Connection lost | Domain reload | Wait ~5s, reconnect |
| Commands fail silently | Wrong instance | Check `set_active_instance` |

## Reference Files

For detailed schemas and examples:

- **[tools-reference.md](references/tools-reference.md)**: Complete tool documentation with all parameters
- **[resources-reference.md](references/resources-reference.md)**: All available resources and their data
- **[workflows.md](references/workflows.md)**: Extended workflow examples and patterns
