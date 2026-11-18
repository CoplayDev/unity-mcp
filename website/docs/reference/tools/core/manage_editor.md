---
title: manage_editor
sidebar_label: manage_editor
description: "Controls and queries the Unity editor's state and settings."
---

# `manage_editor`

> **Auto-generated** from the Python tool registry. Do not hand-edit outside `<!-- examples:start --><!-- examples:end -->` blocks — the generator (`tools/generate_docs_reference.py`) will overwrite them.

**Group:** `core` &nbsp;·&nbsp; **Module:** `services.tools.manage_editor`

## Description

Controls and queries the Unity editor's state and settings. Read-only actions: telemetry_status, telemetry_ping, get_game_view_size. Modifying actions: play, pause, stop, set_active_tool, add_tag, remove_tag, add_layer, remove_layer, deploy_package, restore_package, undo, redo, set_game_view_size. Game View size actions require a graphical Editor and an open Game View (focused, or the only open Game View). Make it visible before setting its size so Unity can repaint. set_game_view_size requires integer width and height (10..8192); it reuses one MCP-owned preset per platform group in shared Editor preferences and waits for render-target size readback. It preserves other presets and does not change focus. Read requested_size, target_size, render_size and settled separately: GPU limits can reduce the actual size; a timeout can leave the new preset selected. For prefab editing (open/save/close prefab stage), use manage_prefabs. deploy_package copies the configured MCPForUnity source folder into the project's installed package location (triggers recompile, no confirmation dialog). restore_package reverts to the pre-deployment backup. undo/redo perform Unity editor undo/redo and return the affected group name.

## Parameters

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `action` | `Literal['telemetry_status', 'telemetry_ping', 'play', 'pause', 'stop', 'set_active_tool', 'add_tag', 'remove_tag', 'add_layer', 'remove_layer', 'deploy_package', 'restore_package', 'undo', 'redo', 'get_game_view_size', 'set_game_view_size']` | yes | Get and update the Unity Editor state. deploy_package copies the configured MCPForUnity source into the project's package location (triggers recompile). restore_package reverts the last deployment from backup. undo/redo perform editor undo/redo. For prefab editing (open/save/close prefab stage), use manage_prefabs. |
| `tool_name` | `str \| None` | — | Tool name when setting active tool |
| `tag_name` | `str \| None` | — | Tag name when adding and removing tags |
| `layer_name` | `str \| None` | — | Layer name when adding and removing layers |
| `width` | `int \| None` | — | Fixed Game View width in pixels (10..8192), required for set_game_view_size |
| `height` | `int \| None` | — | Fixed Game View height in pixels (10..8192), required for set_game_view_size |

## Returns

A `dict` containing the Unity response. The exact shape depends on the action.

## Examples

<!-- examples:start -->
### Verify a mobile layout at a fixed resolution

Make the Game View visible in a graphical Editor. If multiple Game Views are
open, focus the one to use. These actions never open a window or change focus.

```python
manage_editor(action="set_game_view_size", width=2400, height=1080)
manage_editor(action="get_game_view_size")
```

Width and height must both be integers from 10 through 8192. Unity clamps fixed
presets below 10 pixels and limits Game View render targets to at most 8192;
GPU limits and available memory can reduce the actual target further.

Check the returned fields before judging the layout:

- `requested_size`: the dimensions supplied to the set action; null for a read.
- `selected_size`, `preset_name`, `mode`: the selected preset, which can differ
  from the rendered result. Reads also work with existing aspect-ratio presets.
- `target_size`: the render target size calculated by Unity after its limits.
- `render_size`: dimensions read from the allocated, created render texture;
  null when no render target is available.
- `settled`: whether that texture matches Unity's calculated target.
- `matches_requested`: whether settled render dimensions match the request;
  null for a read. A successful set can return false if Unity limited the size.

The set action waits up to five seconds for readback. A hidden or minimized Game
View may not repaint. On `game_view_size_readback_timeout`, the preset can already
be applied: make the view visible and read its size again. Closing the window,
selecting another preset or changing its platform group interrupts the wait.
The command does not advance frames, enter Play Mode or restore the previous size.

Presets are stored in Unity's Editor preferences, shared across projects. The tool
reuses one clearly named MCP preset per platform group. Ownership is tracked by
an exact name and dimensions under `MCPForUnity.GameViewSize.<group>` in EditorPrefs;
other presets, including similarly named ones, stay intact. If you rename or
resize the managed preset manually, the next request preserves it and creates a
new managed entry. Removing the ownership preference likewise relinquishes the
old preset. Aspect-ratio setting, choosing user presets by name and temporary
screenshot-size switching are outside these actions.
<!-- examples:end -->

