---
title: import_model
sidebar_label: import_model
description: "Import 3D models from the Sketchfab marketplace into the Unity project."
---

# `import_model`

> **Auto-generated** from the Python tool registry. Do not hand-edit outside `<!-- examples:start --><!-- examples:end -->` blocks — the generator (`tools/generate_docs_reference.py`) will overwrite them.

**Group:** `asset_gen` &nbsp;·&nbsp; **Module:** `services.tools.import_model`

## Description

Import 3D models from the Sketchfab marketplace into the Unity project. Bring-your-own-key: the Sketchfab token lives in the editor's secure store and never crosses the bridge.

ACTIONS:
- search: Search Sketchfab. Params: query, categories, downloadable, count, cursor -> results with model uids.
- preview: Fetch model metadata (name, thumbnail URLs, license, vertex/face counts) for a uid before import.
- import: Download + import a model by uid. Returns { job_id } immediately; poll with the status action. Params: uid, target_size, name, output_folder.
- status: Poll an async import job by job_id -> { state, progress, assetPath?, error? }.
- cancel: Cancel an in-flight import by job_id.
- list_providers: List configured marketplace providers (no key values).

## Parameters

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `action` | `Literal['search', 'preview', 'import', 'status', 'cancel', 'list_providers']` | yes | Action to perform. |
| `query` | `str \| None` | — | Search query for the search action. |
| `categories` | `str \| None` | — | Filter search by category. |
| `downloadable` | `bool \| None` | — | Restrict search to downloadable models. |
| `count` | `int \| None` | — | Maximum number of search results. |
| `cursor` | `str \| None` | — | Pagination cursor for search. |
| `uid` | `str \| None` | — | Sketchfab model uid for preview/import. |
| `target_size` | `float \| None` | — | Normalize the largest dimension to this size (meters). |
| `name` | `str \| None` | — | Base name for the imported asset. |
| `output_folder` | `str \| None` | — | Destination folder under Assets/ for the import. |
| `job_id` | `str \| None` | — | Job id for status/cancel. |

## Returns

A `dict` containing the Unity response. The exact shape depends on the action.

## Examples

<!-- examples:start -->
*No examples yet. Add usage examples here — they will be preserved across regenerations.*
<!-- examples:end -->

