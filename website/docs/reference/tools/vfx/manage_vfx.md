---
title: manage_vfx
sidebar_label: manage_vfx
description: "Manage Unity VFX components (ParticleSystem, VisualEffect, LineRenderer, TrailRenderer)."
---

# `manage_vfx`

> **Auto-generated** from the Python tool registry. Do not hand-edit outside `<!-- examples:start --><!-- examples:end -->` blocks — the generator (`tools/generate_docs_reference.py`) will overwrite them.

**Group:** `vfx` &nbsp;·&nbsp; **Module:** `services.tools.manage_vfx`

## Description

Manage Unity VFX components (ParticleSystem, VisualEffect, LineRenderer, TrailRenderer). Action prefixes: particle_*, vfx_*, line_*, trail_*. Action-specific parameters go in `properties` (keys match ManageVFX.cs).

## Parameters

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `action` | `str` | yes | Action to perform (prefix: particle_, vfx_, line_, trail_). |
| `target` | `str \| None` | — | Target GameObject (name/path/id). |
| `search_method` | `Literal['by_id', 'by_name', 'by_path', 'by_tag', 'by_layer'] \| None` | — | How to find the target GameObject. |
| `properties` | `dict[str, Any] \| str \| None` | — | Action-specific parameters (dict or JSON string). |
| `component_index` | `int \| None` | — | Zero-based index to select which component when multiple of the same type exist (e.g., multiple ParticleSystems). If omitted, targets the first instance. |

## Returns

A `dict` containing the Unity response. The exact shape depends on the action.

## Examples

<!-- examples:start -->
*No examples yet. Add usage examples here — they will be preserved across regenerations.*
<!-- examples:end -->

