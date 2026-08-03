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

MODELS (all via fal.ai): fal-ai/stable-audio-25/text-to-audio (music + SFX, <=190s), cassetteai/sound-effects-generator (SFX, <=30s), cassetteai/music-generator (music), fal-ai/lyria2 (music). Omit model to use the model selected in the MCP for Unity -> Asset Generation tab.

ACTIONS:
- generate: Submit an audio job from a text prompt. Returns { job_id }; poll with the status action. Params: provider (fal), prompt, model, duration (seconds), name, output_folder.
- status: Poll an async job by job_id -> { state, progress, assetPath?, error? }.
- cancel: Cancel an in-flight job by job_id.
- list_providers: List configured audio providers and capabilities (no key values).

## Parameters

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `action` | `Literal['generate', 'status', 'cancel', 'list_providers']` | yes | Action to perform. |
| `provider` | `str \| None` | — | Provider id (fal). |
| `prompt` | `str \| None` | — | Text prompt describing the sound or music. |
| `model` | `str \| None` | — | fal model id (e.g. fal-ai/stable-audio-25/text-to-audio). Omit to use the GUI-selected default. |
| `duration` | `float \| None` | — | Requested length in seconds (soft-clamped per model). |
| `name` | `str \| None` | — | Base name for the imported asset. |
| `output_folder` | `str \| None` | — | Destination folder under Assets/ for the import. |
| `job_id` | `str \| None` | — | Job id for status/cancel. |

## Returns

A `dict` containing the Unity response. The exact shape depends on the action.

## Examples

<!-- examples:start -->
*No examples yet. Add usage examples here — they will be preserved across regenerations.*
<!-- examples:end -->

