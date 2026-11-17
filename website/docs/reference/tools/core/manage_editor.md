---
title: manage_editor
sidebar_label: manage_editor
description: "Controls and queries the Unity editor's state and settings."
---

# `manage_editor`

> **Auto-generated** from the Python tool registry. Do not hand-edit outside `<!-- examples:start --><!-- examples:end -->` blocks — the generator (`tools/generate_docs_reference.py`) will overwrite them.

**Group:** `core` &nbsp;·&nbsp; **Module:** `services.tools.manage_editor`

## Description

Controls and queries the Unity editor's state and settings. Read-only actions: telemetry_status, telemetry_ping, get_play_mode_startup. Modifying actions: play, pause, stop, set_active_tool, add_tag, remove_tag, add_layer, remove_layer, deploy_package, restore_package, undo, redo. play can wait_until='scene_loaded' or 'first_frame', with wait_timeout (1-300 seconds; default 30). on_ready_code is a C# body executed once after first-frame startup using execute_code's compiler and safety checks; its result is returned as data.on_ready_result. Startup state survives domain reload without changing Enter Play Mode settings. After a timeout or disconnected reply, poll get_play_mode_startup with job_id instead of resubmitting code. Scene loading alone does not guarantee Start methods have run; use first_frame or on_ready_code for that. For prefab editing (open/save/close prefab stage), use manage_prefabs. deploy_package copies the configured MCPForUnity source folder into the project's installed package location (triggers recompile, no confirmation dialog). restore_package reverts to the pre-deployment backup. undo/redo perform Unity editor undo/redo and return the affected group name.

## Parameters

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `action` | `Literal['telemetry_status', 'telemetry_ping', 'play', 'get_play_mode_startup', 'pause', 'stop', 'set_active_tool', 'add_tag', 'remove_tag', 'add_layer', 'remove_layer', 'deploy_package', 'restore_package', 'undo', 'redo']` | yes | Get and update the Unity Editor state. deploy_package copies the configured MCPForUnity source into the project's package location (triggers recompile). restore_package reverts the last deployment from backup. undo/redo perform editor undo/redo. For prefab editing (open/save/close prefab stage), use manage_prefabs. |
| `tool_name` | `str \| None` | — | Tool name when setting active tool |
| `tag_name` | `str \| None` | — | Tag name when adding and removing tags |
| `layer_name` | `str \| None` | — | Layer name when adding and removing layers |
| `wait_until` | `Literal['scene_loaded', 'first_frame'] \| None` | — | Startup milestone to wait for on play. first_frame waits until Start and at least one frame have completed. |
| `on_ready_code` | `str \| None` | — | C# body to execute once after first-frame startup on play. Uses execute_code's default compiler and safety checks. |
| `wait_timeout` | `float \| None` | — | Maximum startup wait in seconds (1-300; default 30). A timeout does not stop Play Mode; poll the returned job_id. |
| `job_id` | `str \| None` | — | Startup job id returned by play; required for get_play_mode_startup. |

## Returns

A `dict` containing the Unity response. The exact shape depends on the action.

## Examples

<!-- examples:start -->
```python
manage_editor(action="play", wait_until="first_frame", wait_timeout=60)

response = manage_editor(
    action="play", on_ready_code="return UnityEngine.Time.frameCount;"
)
# Read an existing job after a timeout or lost reply, on the same Unity instance.
manage_editor(action="get_play_mode_startup", job_id=response["data"]["job_id"])
```

`scene_loaded` does not guarantee that `Start` methods have run. Use
`first_frame` or `on_ready_code` when startup must finish first. Startup code
always waits for first-frame completion and uses `execute_code`'s default
compiler and safety checks; its result is returned in `data.on_ready_result`.

Waits default to 30 seconds and accept 1–300 seconds. A timeout does not stop
Play Mode. Poll the returned `job_id` instead of submitting the code again.
Pending code expires with its Unity-side wait; code already executing may still
finish. Jobs survive domain reloads without changing Enter Play Mode settings.
Results are retained for the latest 16 jobs in the current Editor session.
<!-- examples:end -->

