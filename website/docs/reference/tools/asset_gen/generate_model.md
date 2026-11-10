---
title: generate_model
sidebar_label: generate_model
description: "Generate 3D models with AI providers (Tripo, Meshy, fal) and import them into the Unity project."
---

# `generate_model`

> **Auto-generated** from the Python tool registry. Do not hand-edit outside `<!-- examples:start --><!-- examples:end -->` blocks — the generator (`tools/generate_docs_reference.py`) will overwrite them.

**Group:** `asset_gen` &nbsp;·&nbsp; **Module:** `services.tools.generate_model`

## Description

Generate 3D models with AI providers (Tripo, Meshy, fal) and import them into the Unity project. Bring-your-own-key: provider keys live in the editor's secure store and never cross the bridge.

ACTIONS:
- generate: Submit a generation job (text->3D or image->3D). Returns { job_id } immediately; poll with the status action. Params: provider, mode (text|image), prompt, image_path|image_url, format (glb|fbx|obj|usdz), target_size, texture, tier, model, name, output_folder.
- status: Poll an async job by job_id -> { state, progress, assetPath?, error? }.
- cancel: Cancel an in-flight job by job_id.
- list_providers: List configured 3D providers and capabilities (no key values).
- list_models: Search/paginate live fal 3D models and bundled Tripo/Meshy models.
- refresh_models: Refresh fal discovery. Repeat list_models while catalogs[].refreshing is true.

## Parameters

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `action` | `Literal['generate', 'status', 'cancel', 'list_providers', 'list_models', 'refresh_models']` | yes | Action to perform. |
| `provider` | `str \| None` | — | Provider id (tripo, meshy, fal). fal supports GLB output. |
| `mode` | `str \| None` | — | Generation mode: text or image. |
| `prompt` | `str \| None` | — | Text prompt for text->3D. |
| `image_path` | `str \| None` | — | Path to a source image for image->3D. |
| `image_url` | `str \| None` | — | URL of a source image for image->3D. |
| `format` | `str \| None` | — | Output model format: glb, fbx, obj, or usdz. |
| `target_size` | `float \| None` | — | Normalize the largest dimension to this size (meters). |
| `texture` | `bool \| None` | — | Whether to generate textures for the model. |
| `tier` | `str \| None` | — | Provider quality/cost tier. |
| `model` | `str \| None` | — | Provider model id/version (e.g. Tripo v3.1, Meshy meshy-6). Omit for the GUI-selected default. |
| `name` | `str \| None` | — | Base name for the imported asset. |
| `output_folder` | `str \| None` | — | Destination folder under Assets/ for the import. |
| `job_id` | `str \| None` | — | Job id for status/cancel. |
| `search` | `str \| None` | — | Filter list_models by name, id or use case. |
| `limit` | `int \| None` | — | Model page size (1..200; default 50). |
| `offset` | `int \| None` | — | Model page offset (default 0). |

## Returns

A `dict` containing the Unity response. The exact shape depends on the action.

## Examples

<!-- examples:start -->
```json
{"action": "list_models", "provider": "fal", "mode": "image"}
```

CLI: `unity-mcp asset-gen list-models --kind model --provider fal --refresh` (GLB output).
<!-- examples:end -->

