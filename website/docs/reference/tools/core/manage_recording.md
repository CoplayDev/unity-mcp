---
title: manage_recording
sidebar_label: manage_recording
description: "Record a view while controlling Timeline, AnimationClip, Animator, DOTween or a custom state machine."
---

# `manage_recording`

> **Auto-generated** from the Python tool registry. Do not hand-edit outside `<!-- examples:start --><!-- examples:end -->` blocks — the generator (`tools/generate_docs_reference.py`) will overwrite them.

**Group:** `core` &nbsp;·&nbsp; **Module:** `services.tools.manage_recording`

## Description

Record a view while controlling Timeline, AnimationClip, Animator, DOTween or a custom state machine. Discover targets/source FPS first; resolve ambiguous names. prepare validates without changing playback; start returns immediately. Poll status until state is completed, failed, cancelled or interrupted. Use the artifact only when ready=true; otherwise report error when present. stop finalizes early, cancel preserves partial output. Frame indices are zero-based, end_frame is exclusive. source_fps addresses the source; fps is output FPS. game_view includes Overlay UI and requires unpaused graphical Play Mode. camera/scene_view work in Edit Mode but exclude Overlay UI/editor gizmos. fixed_step controls the selected source, not deterministic replay of arbitrary game logic. No audio. Resolve natural-language requirements into explicit parameters; never guess source FPS or state names. Use capabilities for platform limitations.

## Parameters

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `action` | `Literal['capabilities', 'discover', 'prepare', 'start', 'status', 'stop', 'cancel']` | yes | Operation to perform. |
| `recording_id` | `str \| None` | — | Prepared/running job ID; required for stop/cancel. Omit for status to list jobs. |
| `request_id` | `str \| None` | — | Optional start idempotency key; generated automatically if omitted. |
| `query` | `str \| None` | — | Filter discovered paths/custom IDs, or query an exact DOTween SetId string. |
| `page_size` | `int \| None` | — | Discovery page size, 1..100. |
| `cursor` | `int \| None` | — | Discovery offset from next_cursor. |
| `capture_source` | `Literal['game_view', 'camera', 'scene_view'] \| None` | — | Viewport to record; default game_view. |
| `camera` | `str \| None` | — | Unique camera component ID/path; required for camera capture. |
| `playback` | `services.tools.manage_recording.RecordingPlaybackSpec \| None` | — | Playback source and state from discover. |
| `mode` | `Literal['realtime', 'fixed_step'] \| None` | — | Defaults to realtime without playback, fixed_step otherwise. |
| `duration_seconds` | `float \| None` | — | Output duration, at most 300s, containing whole output frames; default 5s. |
| `start_time` | `float \| None` | — | Source start seconds; mutually exclusive with start_frame. |
| `start_frame` | `int \| None` | — | Zero-based source start frame. Requires discovered or explicit source_fps. |
| `end_frame` | `int \| None` | — | Exclusive source end frame; mutually exclusive with duration_seconds. |
| `source_fps` | `float \| None` | — | Override for source FPS. Never substitute output FPS for unknown source FPS. |
| `fps` | `int \| None` | — | Output FPS, 1..120; default 30. |
| `width` | `int \| None` | — | Even output width; default 1280. Fits/letterboxes the existing viewport. |
| `height` | `int \| None` | — | Even output height; default 720. Maximum 3840x2160 pixels. |
| `format` | `Literal['mp4', 'webm', 'png_sequence'] \| None` | — | Default mp4; webm on Linux; png_sequence is the portable fallback. |
| `output_folder` | `str \| None` | — | Project-relative folder outside Assets/Packages/ProjectSettings. Default Library/MCPForUnity/Recordings. |
| `file_name` | `str \| None` | — | Base name without extension. Unique suffix prevents overwrites. |
| `restore_state` | `bool \| None` | — | Restore captured playback state/settings; default true. Cannot undo arbitrary callback side effects. |

## Returns

A `dict` containing the Unity response. The exact shape depends on the action.

## Examples

<!-- examples:start -->
*No examples yet. Add usage examples here — they will be preserved across regenerations.*
<!-- examples:end -->

