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

Use list_models to discover current models; compatibility is checked before generation. Omit model to use the Asset Generation tab selection.

ACTIONS:
- generate: Submit an audio job from a text prompt. Returns { job_id }; poll with the status action. Params: provider (fal), prompt, model, duration (seconds), name, output_folder.
- status: Poll an async job by job_id -> { state, progress, assetPath?, error? }.
- cancel: Cancel an in-flight job by job_id.
- list_providers: List configured audio providers and capabilities (no key values).
- list_models: List models and freshness; refresh stale fal data in the background.
- refresh_models: Force a fal refresh. Repeat list_models while catalogs[].refreshing is true.

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
| `search` | `str \| None` | — | Filter list_models by name, id or use case. |
| `mode` | `str \| None` | — | Filter list_models by input mode (text). |
| `limit` | `int \| None` | — | Model page size (1..200; default 50). |
| `offset` | `int \| None` | — | Model page offset (default 0). |

## Returns

A `dict` containing the Unity response. The exact shape depends on the action.

## Examples

<!-- examples:start -->
```json
{"action": "list_models", "provider": "fal"}
```

CLI: `unity-mcp asset-gen list-models --kind audio --refresh`.
<!-- examples:end -->

