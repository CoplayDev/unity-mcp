---
title: generate_audio
sidebar_label: generate_audio
description: "Generate audio and import it as an AudioClip into the Unity project."
---

# `generate_audio`

> **Auto-generated** from the Python tool registry. Do not hand-edit outside `<!-- examples:start --><!-- examples:end -->` blocks — the generator (`tools/generate_docs_reference.py`) will overwrite them.

**Group:** `asset_gen` &nbsp;·&nbsp; **Module:** `services.tools.generate_audio`

## Description

Generate audio and import it as an AudioClip into the Unity project. Provider keys live in the editor's secure store and never cross the bridge. Omit model to use the model selected in the MCP for Unity -> Asset Generation tab.

ACTIONS:
- generate: Submit an audio job. Cover models require one of audio_url or audio_base64 and also accept cover_feature_id. Returns { job_id }; poll with the status action. URL results expire after 24 hours.
- status: Poll an async job by job_id -> { state, progress, assetPath?, error? }.
- cancel: Cancel an in-flight job by job_id.
- list_providers: List configured audio providers and capabilities (no key values).

## Parameters

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `action` | `Literal['generate', 'status', 'cancel', 'list_providers']` | yes | Action to perform. |
| `provider` | `str \| None` | — | Provider id. |
| `prompt` | `str \| None` | — | Text prompt describing the sound or music. |
| `model` | `str \| None` | — | Provider model id. Omit to use the GUI-selected default. |
| `duration` | `float \| None` | — | Requested length in seconds (soft-clamped per model). |
| `lyrics` | `str \| None` | — | Optional lyrics for a cover. |
| `lyrics_optimizer` | `bool \| None` | — | Whether to optimize the supplied lyrics. |
| `is_instrumental` | `bool \| None` | — | Whether to generate an instrumental cover. |
| `audio_url` | `str \| None` | — | Reference-audio URL for a cover (6-360 seconds, at most 50 MB). |
| `audio_base64` | `str \| None` | — | Base64 reference audio for a cover (6-360 seconds, at most 50 MB). |
| `cover_feature_id` | `str \| None` | — | Optional preprocessed cover feature id. |
| `output_format` | `Literal['url', 'hex'] \| None` | — | Provider response format; URL results expire after 24 hours. |
| `audio_format` | `Literal['mp3', 'wav', 'pcm'] \| None` | — | Generated audio encoding. |
| `aigc_watermark` | `bool \| None` | — | Whether to add the regional AIGC watermark. |
| `name` | `str \| None` | — | Base name for the imported asset. |
| `output_folder` | `str \| None` | — | Destination folder under Assets/ for the import. |
| `job_id` | `str \| None` | — | Job id for status/cancel. |

## Returns

A `dict` containing the Unity response. The exact shape depends on the action.

## Examples

<!-- examples:start -->
*No examples yet. Add usage examples here — they will be preserved across regenerations.*
<!-- examples:end -->

