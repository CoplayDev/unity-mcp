---
title: manage_asset
sidebar_label: manage_asset
description: "Performs asset operations (import, create, modify, delete, etc.) in Unity."
---

# `manage_asset`

> **Auto-generated** from the Python tool registry. Do not hand-edit outside `<!-- examples:start --><!-- examples:end -->` blocks — the generator (`tools/generate_docs_reference.py`) will overwrite them.

**Group:** `core` &nbsp;·&nbsp; **Module:** `services.tools.manage_asset`

## Description

Performs asset operations (import, create, modify, delete, etc.) in Unity. Read built AssetBundles with list_asset_bundles (directory navigation) or get_asset_bundle_info (asset/scene names without loading assets). Both use project-local paths and bounded page_size/page_number output.

Tip (payload safety): for `action="search"`, prefer paging (`page_size`, `page_number`) and keep `generate_preview=false` (previews can add large base64 blobs).

## Parameters

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `action` | `Literal['import', 'create', 'modify', 'delete', 'duplicate', 'move', 'rename', 'search', 'get_info', 'create_folder', 'get_components', 'list_asset_bundles', 'get_asset_bundle_info']` | yes | Perform asset operations or read built AssetBundles. |
| `path` | `str` | yes | Asset path or search scope. For bundles: a project-relative directory/file, e.g., 'Builds/AssetBundles' or 'Builds/AssetBundles/characters'. Symbolic links are not traversed. |
| `asset_type` | `str \| None` | — | Asset type (e.g., 'Material', 'Folder') - required for 'create'. Note: For ScriptableObjects, use manage_scriptable_object. |
| `properties` | `dict[str, Any] \| str \| None` | — | Dictionary of properties for 'create'/'modify'. Keys are property names, values are property values. |
| `destination` | `str \| None` | — | Target path for 'duplicate'/'move'. |
| `generate_preview` | `bool` | — | Generate a preview/thumbnail for the asset when supported. Warning: previews may include large base64 payloads; keep false unless needed. |
| `search_pattern` | `str \| None` | — | Search pattern (e.g., '*.prefab' or AssetDatabase filters like 't:MonoScript'). Recommended: put queries like 't:MonoScript' here and set path='Assets'. |
| `filter_type` | `str \| None` | — | Filter type for search |
| `filter_date_after` | `str \| None` | — | Date after which to filter |
| `page_size` | `int \| float \| str \| None` | — | Page size for pagination. Recommended: 25. Bundle actions default to 25 and allow 1-200. |
| `page_number` | `int \| float \| str \| None` | — | Page number for pagination (1-based). |

## Returns

A `dict` containing the Unity response. The exact shape depends on the action.

## Examples

<!-- examples:start -->
### Explore built AssetBundles

List a project-local output directory without loading bundles:

```json
{"action": "list_asset_bundles", "path": "Builds/AssetBundles", "page_size": 25, "page_number": 1}
```

The returned `entries` contain `type: "bundle"` or `type: "directory"`, project-relative paths, and file sizes. Follow directory paths to browse nested bundles. Header detection includes UnityFS, UnityRaw, and UnityWeb files, even when they have no extension; a recognized header does not guarantee a valid bundle.

Inspect one built bundle's asset or scene names:

```json
{"action": "get_asset_bundle_info", "path": "Builds/AssetBundles/characters", "page_size": 25, "page_number": 1}
```

Both actions return `totalEntries`, `pageSize`, `pageNumber`, and `nextPageNumber` (null on the last page). Inspection also returns `bundleName`, `sizeBytes`, `assetCount`, `sceneCount`, and `isStreamedSceneAssetBundle`. Each content entry has a `type` (`asset` or `scene`) and `name`.

Paths must stay inside the project; symbolic links and reparse points are not traversed. Page sizes range from 1 to 200 and default to 25. Inspection requires the built-in AssetBundle module and a bundle compatible with the active Editor platform. It releases only its own temporary bundle handle, loads no assets or scenes, and leaves files unchanged. An already loaded selected bundle reports an error and stays loaded.

```bash
unity-mcp asset bundles "Builds/AssetBundles" --limit 25
unity-mcp asset bundle-info "Builds/AssetBundles/characters" --limit 25 --page 1
```
<!-- examples:end -->

