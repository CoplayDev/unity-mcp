---
title: manage_prefabs
sidebar_label: manage_prefabs
description: "Manages Unity Prefab assets."
---

# `manage_prefabs`

> **Auto-generated** from the Python tool registry. Do not hand-edit outside `<!-- examples:start --><!-- examples:end -->` blocks — the generator (`tools/generate_docs_reference.py`) will overwrite them.

**Group:** `core` &nbsp;·&nbsp; **Module:** `services.tools.manage_prefabs`

## Description

Manages Unity Prefab assets. Actions: get_info, get_hierarchy, create_from_gameobject, modify_contents, open_prefab_stage, save_prefab_stage, close_prefab_stage. Two approaches to prefab editing: (1) Headless: use modify_contents for automated/scripted edits without opening the prefab in the editor. (2) Interactive: use open_prefab_stage to open a prefab, then manage_gameobject/manage_components to edit objects inside the prefab stage, then save_prefab_stage to save and close_prefab_stage to return to the main scene. Use create_child parameter with modify_contents to add child GameObjects or nested prefab instances to a prefab (single object or array for batch creation in one save). Example: create_child=[{"name": "Child1", "primitive_type": "Sphere", "position": [1,0,0]}, {"name": "Nested", "source_prefab_path": "Assets/Prefabs/Bullet.prefab", "position": [0,2,0]}]. Use delete_child parameter to remove child GameObjects from the prefab (single name/path or array of paths for batch deletion. Example: delete_child=["Child1", "Child2/Grandchild"]). Use component_properties with modify_contents to set serialized fields on existing components (e.g. component_properties={"Rigidbody": {"mass": 5.0}, "MyScript": {"health": 100}}). Supports object references via {"guid": "..."}, {"path": "Assets/..."}, or {"instanceID": 123}. Use manage_asset action=search filterType=Prefab to list prefabs.

## Parameters

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `action` | `Literal['create_from_gameobject', 'get_info', 'get_hierarchy', 'modify_contents', 'open_prefab_stage', 'save_prefab_stage', 'close_prefab_stage']` | yes | Prefab operation to perform. |
| `prefab_path` | `str \| None` | — | Prefab asset path (e.g., Assets/Prefabs/MyPrefab.prefab). |
| `target` | `str \| None` | — | Target GameObject: scene object for create_from_gameobject, or object within prefab for modify_contents (name or path like 'Parent/Child'). |
| `allow_overwrite` | `bool \| None` | — | Allow replacing existing prefab. |
| `search_inactive` | `bool \| None` | — | Include inactive GameObjects in search. |
| `unlink_if_instance` | `bool \| None` | — | Unlink from existing prefab before creating new one. |
| `position` | `list[float] \| dict[str, float] \| str \| None` | — | New local position [x, y, z] or {x, y, z} for modify_contents. |
| `rotation` | `list[float] \| dict[str, float] \| str \| None` | — | New local rotation (euler angles) [x, y, z] or {x, y, z} for modify_contents. |
| `scale` | `list[float] \| dict[str, float] \| str \| None` | — | New local scale [x, y, z] or {x, y, z} for modify_contents. |
| `name` | `str \| None` | — | New name for the target object in modify_contents. |
| `tag` | `str \| None` | — | New tag for the target object in modify_contents. |
| `layer` | `str \| None` | — | New layer name for the target object in modify_contents. |
| `set_active` | `bool \| None` | — | Set active state of target object in modify_contents. |
| `parent` | `str \| None` | — | New parent object name/path within prefab for modify_contents. |
| `components_to_add` | `list[str] \| None` | — | Component types to add in modify_contents. |
| `components_to_remove` | `list[str] \| None` | — | Component types to remove in modify_contents. |
| `create_child` | `dict[str, Any] \| list[dict[str, Any]] \| None` | — | Create child GameObject(s) in the prefab. Single object or array of objects, each with: name (required), parent (optional, defaults to target), source_prefab_path (optional: asset path to instantiate as nested prefab, e.g. 'Assets/Prefabs/Bullet.prefab'), primitive_type (optional: Cube, Sphere, Capsule, Cylinder, Plane, Quad), position, rotation, scale, components_to_add, tag, layer, set_active. source_prefab_path and primitive_type are mutually exclusive. |
| `delete_child` | `str \| list[str] \| None` | — | Child name(s) or path(s) to remove from the prefab. Supports single string or array for batch deletion (e.g. 'Child1' or ['Child1', 'Child1/Grandchild']). |
| `component_properties` | `dict[str, dict[str, Any]] \| None` | — | Set properties on existing components in modify_contents. Keys are component type names, values are dicts of property name to value. Example: {"Rigidbody": {"mass": 5.0}, "MyScript": {"health": 100}}. Supports object references via {"guid": "..."}, {"path": "Assets/..."}, or {"instanceID": 123}. For Sprite sub-assets: {"guid": "...", "spriteName": "<name>"}. Single-sprite textures auto-resolve. |

## Returns

A `dict` containing the Unity response. The exact shape depends on the action.

## Examples

<!-- examples:start -->
*No examples yet. Add usage examples here — they will be preserved across regenerations.*
<!-- examples:end -->

