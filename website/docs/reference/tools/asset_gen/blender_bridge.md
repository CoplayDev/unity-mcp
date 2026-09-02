---
title: blender_bridge
sidebar_label: blender_bridge
description: "Bridge to a running Blender that has the BlenderMCP addon connected (socket, default 127.0.0.1:9876; configured in Window > MCP for Unity > Generative > Blender Bridge)."
---

# `blender_bridge`

> **Auto-generated** from the Python tool registry. Do not hand-edit outside `<!-- examples:start --><!-- examples:end -->` blocks — the generator (`tools/generate_docs_reference.py`) will overwrite them.

**Group:** `asset_gen` &nbsp;·&nbsp; **Module:** `services.tools.blender_bridge`

## Description

Bridge to a running Blender that has the BlenderMCP addon connected (socket, default 127.0.0.1:9876; configured in Window > MCP for Unity > Generative > Blender Bridge). Unity talks to the addon directly, so no BlenderMCP client is needed.

Actions:
- status: is Blender reachable, is the checkout configured, does the installed addon match it.
- scene_info / object_info(object_name): read Blender scene / one object.
- screenshot(max_size, output_folder): Blender viewport → PNG under Library/BlenderBridge (or copied under Assets/ when output_folder is given). Returns the path.
- run_python(code): execute Python inside Blender; returns stdout.
- import_model: export from Blender (object_names with their children, or selection_only, else the whole scene) as glb (default; keeps PBR, emission, animation) or fbx, import it through the shared model pipeline, place it in the open scene at position, and scale it so its largest dimension equals target_size meters (0 keeps the imported scale). Set apply_modifiers=false for rigs / shape keys. animation_type applies to FBX only. Returns asset_path, asset_guid, game_object, bounds.
- check_updates: git fetch the blender-mcp checkout and report how far behind its remotes it is, plus whether Blender's installed addon.py matches the checkout.
- sync_addon(force): copy the checkout's addon.py into Blender's addons folder (backs up the old file); restart Blender afterwards.

check_updates and sync_addon need the checkout path to be set; the other actions only need Blender running with the addon connected.

## Parameters

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `action` | `Literal['status', 'scene_info', 'object_info', 'screenshot', 'run_python', 'import_model', 'check_updates', 'sync_addon']` | yes | Operation to perform. |
| `object_name` | `str \| None` | — | object_info: name of the Blender object to inspect. |
| `object_names` | `list[str] \| None` | — | import_model: Blender objects to export (children included). Omit for selection_only or the whole scene. |
| `selection_only` | `bool \| None` | — | import_model: export only what is currently selected in Blender. |
| `format` | `Literal['glb', 'fbx'] \| None` | — | import_model: export format (default glb). |
| `name` | `str \| None` | — | import_model: asset and GameObject name (defaults to the single exported object). |
| `target_size` | `float \| None` | — | import_model: final size in meters of the largest dimension; 0 keeps the imported scale. |
| `position` | `list[float] \| None` | — | import_model: world position [x, y, z] for the placed instance. |
| `place_in_scene` | `bool \| None` | — | import_model: instantiate the imported asset into the open scene (default true). |
| `apply_modifiers` | `bool \| None` | — | import_model: bake modifiers on export (default true; false for skinned meshes / shape keys). |
| `output_folder` | `str \| None` | — | import_model / screenshot: destination folder under Assets/. |
| `animation_type` | `Literal['none', 'generic', 'humanoid', 'legacy'] \| None` | — | import_model, FBX only: rig/animation import mode (FBX imports zero clips unless set). |
| `code` | `str \| None` | — | run_python: Python source to execute inside Blender. |
| `max_size` | `int \| None` | — | screenshot: max pixels on the longest side (default 1000). |
| `force` | `bool \| None` | — | sync_addon: overwrite even when the installed addon already matches. |
| `timeout_seconds` | `int \| None` | — | Seconds to wait for Blender (default 180; big exports can be slow). |

## Returns

A `dict` containing the Unity response. The exact shape depends on the action.

## Examples

<!-- examples:start -->
*No examples yet. Add usage examples here — they will be preserved across regenerations.*
<!-- examples:end -->

