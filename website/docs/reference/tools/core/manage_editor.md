---
title: manage_editor
sidebar_label: manage_editor
description: "Controls and queries the Unity editor's state and settings."
---

# `manage_editor`

> **Auto-generated** from the Python tool registry. Do not hand-edit outside `<!-- examples:start --><!-- examples:end -->` blocks — the generator (`tools/generate_docs_reference.py`) will overwrite them.

**Group:** `core` &nbsp;·&nbsp; **Module:** `services.tools.manage_editor`

## Description

Controls and queries the Unity editor's state and settings. Read-only actions: telemetry_status, telemetry_ping. Modifying actions: play, pause, stop, set_active_tool, add_tag, remove_tag, add_layer, remove_layer, deploy_package, restore_package, undo, redo. For prefab editing (open/save/close prefab stage), use manage_prefabs. deploy_package copies the configured MCPForUnity source folder into the project's installed package location (triggers recompile, no confirmation dialog). restore_package reverts to the pre-deployment backup. undo/redo perform Unity editor undo/redo and return the affected group name.

## Parameters

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `action` | `Literal['telemetry_status', 'telemetry_ping', 'play', 'pause', 'stop', 'set_active_tool', 'add_tag', 'remove_tag', 'add_layer', 'remove_layer', 'deploy_package', 'restore_package', 'undo', 'redo']` | yes | Get and update the Unity Editor state. deploy_package copies the configured MCPForUnity source into the project's package location (triggers recompile). restore_package reverts the last deployment from backup. undo/redo perform editor undo/redo. For prefab editing (open/save/close prefab stage), use manage_prefabs. |
| `tool_name` | `str \| None` | — | Tool name when setting active tool |
| `tag_name` | `str \| None` | — | Tag name when adding and removing tags |
| `layer_name` | `str \| None` | — | Layer name when adding and removing layers |

## Returns

A `dict` containing the Unity response. The exact shape depends on the action.

## Examples

<!-- examples:start -->
*No examples yet. Add usage examples here — they will be preserved across regenerations.*
<!-- examples:end -->

