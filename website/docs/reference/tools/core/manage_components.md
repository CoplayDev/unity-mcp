---
title: manage_components
sidebar_label: manage_components
description: "Add, remove, or set properties on components attached to GameObjects."
---

# `manage_components`

> **Auto-generated** from the Python tool registry. Do not hand-edit outside `<!-- examples:start --><!-- examples:end -->` blocks — the generator (`tools/generate_docs_reference.py`) will overwrite them.

**Group:** `core` &nbsp;·&nbsp; **Module:** `services.tools.manage_components`

## Description

Add, remove, or set properties on components attached to GameObjects. Actions: add, remove, set_property. Requires target (instance ID or name) and component_type. For READING component data, use the mcpforunity://scene/gameobject/{id}/components resource or mcpforunity://scene/gameobject/{id}/component/{name} for a single component. For creating/deleting GameObjects themselves, use manage_gameobject instead.

## Parameters

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `action` | `Literal['add', 'remove', 'set_property']` | yes | Action to perform: add (add component), remove (remove component), set_property (set component property) |
| `target` | `str \| int` | yes | Target GameObject - instance ID (preferred) or name/path |
| `component_type` | `str` | yes | Component type name (e.g., 'Rigidbody', 'BoxCollider', 'MyScript') |
| `search_method` | `Literal['by_id', 'by_name', 'by_path'] \| None` | — | How to find the target GameObject |
| `property` | `str \| None` | — | Property name to set (for set_property action) |
| `value` | `str \| int \| float \| bool \| dict[Any] \| list[Any] \| None` | — | Value to set (for set_property action). For object references: instance ID (int), asset path (string), or {"guid": "..."} / {"path": "..."}. For Sprite sub-assets: {"guid": "...", "spriteName": "<name>"} or {"guid": "...", "fileID": <id>}. Single-sprite textures auto-resolve. |
| `properties` | `dict[str, Any] \| str \| None` | — | Dictionary of property names to values. Example: {"mass": 5.0, "useGravity": false} |
| `component_index` | `int \| None` | — | Zero-based index to select which component when multiple of the same type exist. Use the components resource to discover indices. If omitted, targets the first instance. |

## Returns

A `dict` containing the Unity response. The exact shape depends on the action.

## Examples

<!-- examples:start -->
*No examples yet. Add usage examples here — they will be preserved across regenerations.*
<!-- examples:end -->

