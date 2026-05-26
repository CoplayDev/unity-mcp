---
title: manage_animation
sidebar_label: manage_animation
description: "Manage Unity animation: Animator control and AnimationClip creation."
---

# `manage_animation`

> **Auto-generated** from the Python tool registry. Do not hand-edit outside `<!-- examples:start --><!-- examples:end -->` blocks — the generator (`tools/generate_docs_reference.py`) will overwrite them.

**Group:** `animation` &nbsp;·&nbsp; **Module:** `services.tools.manage_animation`

## Description

Manage Unity animation: Animator control and AnimationClip creation. Action prefixes: animator_* (play, crossfade, set parameters, get info), controller_* (create AnimatorControllers, add states/transitions/parameters), clip_* (create clips, add keyframe curves, assign to GameObjects). Action-specific parameters go in `properties` (keys match ManageAnimation.cs).

## Parameters

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `action` | `str` | yes | Action to perform (prefix: animator_, controller_, clip_). |
| `target` | `str \| None` | — | Target GameObject (name/path/id). |
| `search_method` | `Literal['by_id', 'by_name', 'by_path', 'by_tag', 'by_layer'] \| None` | — | How to find the target GameObject. |
| `clip_path` | `str \| None` | — | Asset path for AnimationClip (e.g. 'Assets/Animations/Walk.anim'). |
| `controller_path` | `str \| None` | — | Asset path for AnimatorController (e.g. 'Assets/Animators/Player.controller'). |
| `properties` | `dict[str, Any] \| str \| None` | — | Action-specific parameters (dict or JSON string). |

## Returns

A `dict` containing the Unity response. The exact shape depends on the action.

## Examples

<!-- examples:start -->
*No examples yet. Add usage examples here — they will be preserved across regenerations.*
<!-- examples:end -->

