---
title: generate_audio
sidebar_label: generate_audio
description: "Generate audio (sound effects and background music) with fal.ai models and import them as AudioClips into the Unity project."
---

# `generate_audio`

> **Auto-generated** from the Python tool registry. Do not hand-edit outside `<!-- examples:start --><!-- examples:end -->` blocks — the generator (`tools/generate_docs_reference.py`) will overwrite them.

**Group:** `asset_gen` &nbsp;·&nbsp; **Module:** `services.tools.generate_audio`

## Description

Generate audio (sound effects and background music) with fal.ai models and import them as AudioClips into the Unity project. Bring-your-own-key: the fal key lives in the editor's secure store (shared with image generation) and never crosses the bridge.

Use list_models to discover current compatible sound/music models, their duration limits and catalog freshness. Omit model to use the model selected in the MCP for Unity -> Asset Generation tab.

ACTIONS:
- generate: Submit an audio job from a text prompt. Returns { job_id }; poll with the status action. Params: provider (fal), prompt, model, duration (seconds), name, output_folder.
- status: Poll an async job by job_id -> { state, progress, assetPath?, error? }.
- cancel: Cancel an in-flight job by job_id.
- list_providers: List configured audio providers and capabilities (no key values).
- list_models: List models from the editor's shared catalog; refresh stale fal data in the background. If catalogs[].refreshing is true, call list_models again later.
- refresh_models: Force a background fal catalog refresh; returns the current snapshot.

## Parameters

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `action` | `Literal['generate', 'status', 'cancel', 'list_providers', 'list_models', 'refresh_models']` | yes | Action to perform. |
| `provider` | `str \| None` | — | Provider id (fal). |
| `prompt` | `str \| None` | — | Text prompt describing the sound or music. |
| `model` | `str \| None` | — | fal model id returned by list_models. Omit to use the GUI-selected default. |
| `duration` | `float \| None` | — | Requested length in seconds (soft-clamped per model). |
| `name` | `str \| None` | — | Base name for the imported asset. |
| `output_folder` | `str \| None` | — | Destination folder under Assets/ for the import. |
| `job_id` | `str \| None` | — | Job id for status/cancel. |

## Returns

A `dict` containing the Unity response. The exact shape depends on the action.

## Examples

<!-- examples:start -->
Discover the current sound/music models:

```json
{"action": "list_models", "provider": "fal"}
```

Use `refresh_models` to force a background fetch. If `data.catalogs[].refreshing` is true,
query `list_models` again later. Check `source`, `stale` and `last_verified` before choosing
an ID from `data.models`. The selected endpoint is checked again before paid generation.

CLI: `unity-mcp asset-gen list-models --kind audio --refresh`.
<!-- examples:end -->

