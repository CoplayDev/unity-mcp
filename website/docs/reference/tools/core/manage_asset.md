---
title: manage_asset
sidebar_label: manage_asset
description: "Performs asset operations (import, create, modify, delete, etc.) in Unity."
---

# `manage_asset`

> **Auto-generated** from the Python tool registry. Do not hand-edit outside `<!-- examples:start --><!-- examples:end -->` blocks — the generator (`tools/generate_docs_reference.py`) will overwrite them.

**Group:** `core` &nbsp;·&nbsp; **Module:** `services.tools.manage_asset`

## Description

Performs asset operations (import, create, modify, delete, etc.) in Unity.

Tip (payload safety): for `action="search"`, prefer paging (`page_size`, `page_number`) and keep `generate_preview=false` (previews can add large base64 blobs).

## Parameters

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `action` | `Literal['import', 'create', 'modify', 'delete', 'duplicate', 'move', 'rename', 'search', 'get_info', 'create_folder', 'get_components']` | yes | Perform CRUD operations on assets. |
| `path` | `str` | yes | Asset path (e.g., 'Materials/MyMaterial.mat') or search scope (e.g., 'Assets'). |
| `asset_type` | `str \| None` | — | Asset type (e.g., 'Material', 'Folder') - required for 'create'. Note: For ScriptableObjects, use manage_scriptable_object. |
| `properties` | `dict[str, Any] \| str \| None` | — | Dictionary of properties for 'create'/'modify'. Keys are property names, values are property values. |
| `destination` | `str \| None` | — | Target path for 'duplicate'/'move'. |
| `generate_preview` | `bool` | — | Generate a preview/thumbnail for the asset when supported. Warning: previews may include large base64 payloads; keep false unless needed. |
| `search_pattern` | `str \| None` | — | Search pattern (e.g., '*.prefab' or AssetDatabase filters like 't:MonoScript'). Recommended: put queries like 't:MonoScript' here and set path='Assets'. |
| `filter_type` | `str \| None` | — | Filter type for search |
| `filter_date_after` | `str \| None` | — | Date after which to filter |
| `page_size` | `int \| float \| str \| None` | — | Page size for pagination. Recommended: 25 (smaller for LLM-friendly responses). |
| `page_number` | `int \| float \| str \| None` | — | Page number for pagination (1-based). |

## Returns

A `dict` containing the Unity response. The exact shape depends on the action.

## Examples

<!-- examples:start -->
*No examples yet. Add usage examples here — they will be preserved across regenerations.*
<!-- examples:end -->

