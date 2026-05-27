---
title: manage_material
sidebar_label: manage_material
description: "Manages Unity materials (set properties, colors, shaders, etc)."
---

# `manage_material`

> **Auto-generated** from the Python tool registry. Do not hand-edit outside `<!-- examples:start --><!-- examples:end -->` blocks — the generator (`tools/generate_docs_reference.py`) will overwrite them.

**Group:** `core` &nbsp;·&nbsp; **Module:** `services.tools.manage_material`

## Description

Manages Unity materials (set properties, colors, shaders, etc). Read-only actions: ping, get_material_info. Modifying actions: create, set_material_shader_property, set_material_color, assign_material_to_renderer, set_renderer_color.

## Parameters

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `action` | `Literal['ping', 'create', 'set_material_shader_property', 'set_material_color', 'assign_material_to_renderer', 'set_renderer_color', 'get_material_info']` | yes | Action to perform. |
| `material_path` | `str \| None` | — | Path to material asset (Assets/...) |
| `property` | `str \| None` | — | Shader property name (e.g., _BaseColor, _MainTex) |
| `shader` | `str \| None` | — | Shader name (default: Standard) |
| `properties` | `dict[str, Any] \| str \| None` | — | Initial properties to set as {name: value} dict. |
| `value` | `list \| float \| int \| str \| bool \| None` | — | Value to set (color array, float, texture path/instruction) |
| `color` | `list[float] \| dict[str, float] \| str \| None` | — | Color as [r, g, b] or [r, g, b, a] array, {r, g, b, a} object, or JSON string. |
| `target` | `str \| None` | — | Target GameObject (name, path, or find instruction) |
| `search_method` | `Literal['by_id', 'by_name', 'by_path', 'by_tag', 'by_layer', 'by_component'] \| None` | — | Search method for target |
| `slot` | `int \| None` | — | Material slot index (0-based) |
| `mode` | `Literal['shared', 'instance', 'property_block', 'create_unique'] \| None` | — | Assignment/modification mode; behavior when omitted is action-specific on the Unity side. |

## Returns

A `dict` containing the Unity response. The exact shape depends on the action.

## Examples

<!-- examples:start -->
### Create a red material from scratch

> Create `Assets/Materials/Red.mat` using the Standard shader, base color red.

```json
{
  "action": "create",
  "material_path": "Materials/Red.mat",
  "shader": "Standard",
  "properties": { "_Color": [1, 0, 0, 1] }
}
```

For URP, use `"shader": "Universal Render Pipeline/Lit"` and property `_BaseColor`.

### Assign an existing material to a GameObject

> Apply `Assets/Materials/Red.mat` to the `RedCube`.

```json
{
  "action": "assign_material_to_renderer",
  "target": "RedCube",
  "search_method": "by_name",
  "material_path": "Materials/Red.mat",
  "slot": 0,
  "mode": "shared"
}
```

`mode: shared` reuses the asset. `mode: instance` clones it per-renderer (use sparingly — costs draw call batching).

### Change just one shader property

> Set `_Metallic` on `Materials/Red.mat` to 0.8.

```json
{
  "action": "set_material_shader_property",
  "material_path": "Materials/Red.mat",
  "property": "_Metallic",
  "value": 0.8
}
```

### Tint a renderer without touching the shared material

> Tint the cube's MeshRenderer blue using a MaterialPropertyBlock.

```json
{
  "action": "set_renderer_color",
  "target": "RedCube",
  "search_method": "by_name",
  "color": [0, 0, 1, 1],
  "mode": "property_block"
}
```

`property_block` mode avoids creating a per-instance material clone, preserving batching.
<!-- examples:end -->

