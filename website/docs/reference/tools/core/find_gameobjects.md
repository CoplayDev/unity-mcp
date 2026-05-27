---
title: find_gameobjects
sidebar_label: find_gameobjects
description: "Search for GameObjects in the scene by name, tag, layer, component type, or path."
---

# `find_gameobjects`

> **Auto-generated** from the Python tool registry. Do not hand-edit outside `<!-- examples:start --><!-- examples:end -->` blocks — the generator (`tools/generate_docs_reference.py`) will overwrite them.

**Group:** `core` &nbsp;·&nbsp; **Module:** `services.tools.find_gameobjects`

## Description

Search for GameObjects in the scene by name, tag, layer, component type, or path. Returns instance IDs only (paginated). Then use mcpforunity://scene/gameobject/{id} resource for full data, or mcpforunity://scene/gameobject/{id}/components for component details. For CRUD operations (create/modify/delete), use manage_gameobject instead.

## Parameters

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `search_term` | `str` | yes |  |
| `search_method` | `Literal['by_name', 'by_tag', 'by_layer', 'by_component', 'by_path', 'by_id']` | — |  |
| `include_inactive` | `bool \| str \| None` | — |  |
| `page_size` | `int \| str \| None` | — |  |
| `cursor` | `int \| str \| None` | — |  |

## Returns

A `dict` containing the Unity response. The exact shape depends on the action.

## Examples

<!-- examples:start -->
*No examples yet. Add usage examples here — they will be preserved across regenerations.*
<!-- examples:end -->

