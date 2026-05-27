---
title: manage_gameobject
sidebar_label: manage_gameobject
description: "Performs CRUD operations on GameObjects."
---

# `manage_gameobject`

> **Auto-generated** from the Python tool registry. Do not hand-edit outside `<!-- examples:start --><!-- examples:end -->` blocks — the generator (`tools/generate_docs_reference.py`) will overwrite them.

**Group:** `core` &nbsp;·&nbsp; **Module:** `services.tools.manage_gameobject`

## Description

Performs CRUD operations on GameObjects. Actions: create, modify, delete, duplicate, move_relative, look_at. NOT for searching — use the find_gameobjects tool to search by name/tag/layer/component/path. NOT for component management — use the manage_components tool (add/remove/set_property) or mcpforunity://scene/gameobject/{id}/components resource (read).

## Parameters

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `action` | `Literal['create', 'modify', 'delete', 'duplicate', 'move_relative', 'look_at'] \| None` | — | Action to perform on GameObject. |
| `target` | `str \| None` | — | GameObject identifier by name, path, or instance ID for modify/delete/duplicate actions |
| `search_method` | `Literal['by_id', 'by_name', 'by_path', 'by_tag', 'by_layer', 'by_component'] \| None` | — | How to resolve 'target'. If omitted, Unity infers: instance ID -> by_id, path (contains '/') -> by_path, otherwise by_name. |
| `name` | `str \| None` | — | GameObject name for 'create' (initial name) and 'modify' (rename) actions. |
| `tag` | `str \| None` | — | Tag name - used for both 'create' (initial tag) and 'modify' (change tag) |
| `parent` | `str \| None` | — | Parent GameObject reference - used for both 'create' (initial parent) and 'modify' (change parent) |
| `position` | `list[float] \| dict[str, float] \| str \| None` | — | Position as [x, y, z] array, {x, y, z} object, or JSON string |
| `rotation` | `list[float] \| dict[str, float] \| str \| None` | — | Rotation as [x, y, z] euler angles array, {x, y, z} object, or JSON string |
| `scale` | `list[float] \| dict[str, float] \| str \| None` | — | Scale as [x, y, z] array, {x, y, z} object, or JSON string |
| `components_to_add` | `list[str] \| str \| None` | — | List of component names to add during 'create' or 'modify' |
| `primitive_type` | `str \| None` | — | Primitive type for 'create' action |
| `save_as_prefab` | `bool \| str \| None` | — | If True, saves the created GameObject as a prefab (accepts true/false or 'true'/'false') |
| `prefab_path` | `str \| None` | — | Path for prefab creation |
| `prefab_folder` | `str \| None` | — | Folder for prefab creation |
| `set_active` | `bool \| str \| None` | — | If True, sets the GameObject active (accepts true/false or 'true'/'false') |
| `layer` | `str \| None` | — | Layer name |
| `is_static` | `bool \| str \| None` | — | Set the GameObject's static flag. true = all StaticEditorFlags, false = none (accepts true/false or 'true'/'false') |
| `components_to_remove` | `list[str] \| str \| None` | — | List of component names to remove |
| `component_properties` | `dict[str, dict[str, Any]] \| str \| None` | — | Dictionary of component names to their properties to set. For example:                                     `{"MyScript": {"otherObject": {"find": "Player", "method": "by_name"}}}` assigns GameObject                                     `{"MyScript": {"playerHealth": {"find": "Player", "component": "HealthComponent"}}}` assigns Component                                     Example set nested property:                                     - Access shared material: `{"MeshRenderer": {"sharedMaterial.color": [1, 0, 0, 1]}}` |
| `new_name` | `str \| None` | — | New name for the duplicated object (default: SourceName_Copy) |
| `offset` | `list[float] \| str \| None` | — | Offset from original/reference position as [x, y, z] array (list or JSON string) |
| `reference_object` | `str \| None` | — | Reference object for relative movement (required for move_relative) |
| `direction` | `Literal['left', 'right', 'up', 'down', 'forward', 'back', 'front', 'backward', 'behind'] \| None` | — | Direction for relative movement (e.g., 'right', 'up', 'forward') |
| `distance` | `float \| None` | — | Distance to move in the specified direction (default: 1.0) |
| `world_space` | `bool \| str \| None` | — | If True (default), use world space directions; if False, use reference object's local directions |
| `look_at_target` | `list[float] \| str \| None` | — | World position [x,y,z] or GameObject name/path/ID to look at (for look_at action). |
| `look_at_up` | `list[float] \| str \| None` | — | Optional up vector [x,y,z] for look_at. Defaults to [0,1,0]. |

## Returns

A `dict` containing the Unity response. The exact shape depends on the action.

## Examples

<!-- examples:start -->
### Create a red cube at the world origin

> Create a cube called `RedCube` at (0, 0, 0).

```json
{
  "action": "create",
  "name": "RedCube",
  "primitive_type": "Cube",
  "position": [0, 0, 0]
}
```

Pair with [`manage_material`](./manage_material) to make it actually red.

### Find a GameObject by name and move it

> Move the `Player` 5 units up.

```json
{
  "action": "modify",
  "target": "Player",
  "search_method": "by_name",
  "position": [0, 5, 0]
}
```

`search_method: by_name` matches the first GameObject whose name equals `Player`. Use `by_path` for `Parent/Child/Leaf` lookups or `by_id` for instance IDs.

### Parent one GameObject under another

> Make `Sword` a child of `Player/RightHand`.

```json
{
  "action": "modify",
  "target": "Sword",
  "search_method": "by_name",
  "parent": "Player/RightHand"
}
```

### Delete every GameObject tagged `Cleanup`

> Delete all objects tagged `Cleanup` in the current scene.

```json
{
  "action": "delete",
  "target": "Cleanup",
  "search_method": "by_tag"
}
```

### Multi-instance routing

> In the `Editor2` instance, duplicate `MainCamera`.

```json
{
  "action": "duplicate",
  "target": "MainCamera",
  "search_method": "by_name",
  "unity_instance": "Editor2"
}
```

See [Multi-Instance Routing](/guides/multi-instance) for the full routing model.
<!-- examples:end -->

