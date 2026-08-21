---
title: manage_sprite
sidebar_label: manage_sprite
description: "2D sprite animation tool. get_info: read sprite import settings + return image for vision analysis. slice_sheet: apply grid slicing to a sprite sheet. setup_clips: create AnimationClips from sliced sprites. setup_controller: build Animat…"
---

# `manage_sprite`

> **Auto-generated** from the Python tool registry. Do not hand-edit outside `<!-- examples:start --><!-- examples:end -->` blocks — the generator (`tools/generate_docs_reference.py`) will overwrite them.

**Group:** `animation` &nbsp;·&nbsp; **Module:** `services.tools.manage_sprite`

## Description

2D sprite animation tool. get_info: read sprite import settings + return image for vision analysis. slice_sheet: apply grid slicing to a sprite sheet. setup_clips: create AnimationClips from sliced sprites. setup_controller: build AnimatorController with smart complexity (1D blend tree for locomotion, trigger states for combat, simple state for single animations). full_setup: one command — slice → clips → controller.

## Parameters

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `action` | `Literal['get_info', 'slice_sheet', 'setup_clips', 'setup_controller', 'full_setup']` | yes | Action to perform. |
| `path` | `str \| None` | — | Sprite texture asset path (e.g. 'Assets/Sprites/hero_walk.png'). Required for get_info, slice_sheet, setup_clips, full_setup. |
| `cols` | `int \| None` | — | Number of columns in the sprite sheet grid. Used by slice_sheet and full_setup. |
| `rows` | `int \| None` | — | Number of rows in the sprite sheet grid. Default: 1. |
| `frame_width` | `int \| None` | — | Frame width in pixels. Alternative to cols. |
| `frame_height` | `int \| None` | — | Frame height in pixels. Alternative to rows. |
| `base_name` | `str \| None` | — | Base name for sliced sprite frames (default: texture filename). |
| `clips` | `list[dict[str, Any]] \| None` | — | Clip definitions: [{name, start_frame, end_frame, fps (default 12), loop (auto-detect if omitted)}]. For setup_controller: [{name, path}] where path is the .anim asset path. |
| `animation_name` | `str \| None` | — | Animation name for full_setup when clips are not specified (all frames = one clip). |
| `output_dir` | `str \| None` | — | Output directory for .anim and .controller assets (default: same folder as sprite). |
| `controller_path` | `str \| None` | — | Path for the .controller asset (e.g. 'Assets/Animators/Hero.controller'). |
| `overwrite` | `bool` | — | Replace an existing .anim or .controller at the target path. Off by default: without it an existing asset is kept and reported back, not silently replaced. |
| `add_to_scene` | `bool` | — | Attach Animator + controller to a scene GameObject. |
| `scene_target` | `str \| None` | — | Existing GameObject name to attach Animator to. |

## Returns

A `dict` containing the Unity response. The exact shape depends on the action.

## Examples

<!-- examples:start -->
### Read the sheet before slicing it

The grid is the one thing the tool cannot infer. `get_info` returns the texture's
dimensions and the sheet itself as `image_base64`, so a vision-capable caller can count the
frames before committing to a grid.

```json
{ "action": "get_info", "path": "Assets/Sprites/hero_walk.png" }
```

### One command from sheet to controller

```json
{
  "action": "full_setup",
  "path": "Assets/Sprites/hero.png",
  "cols": 6,
  "rows": 4,
  "clips": [
    { "name": "idle",   "start_frame": 0,  "end_frame": 5  },
    { "name": "walk",   "start_frame": 6,  "end_frame": 11 },
    { "name": "run",    "start_frame": 12, "end_frame": 17 },
    { "name": "attack", "start_frame": 18, "end_frame": 23, "fps": 18 }
  ],
  "controller_path": "Assets/Animators/Hero.controller",
  "add_to_scene": true,
  "scene_target": "Hero"
}
```

Clip names decide the controller's shape: `idle` becomes the default state, `walk` and
`run` collapse into a `Speed`-driven 1D blend tree, and `attack` gets an `Attack` trigger.
Looping follows from the same names — locomotion and idle loop, a one-shot does not — and
an explicit `"loop"` on a clip overrides that.

### Slicing on its own

```json
{ "action": "slice_sheet", "path": "Assets/Sprites/hero.png", "frame_width": 32, "frame_height": 32 }
```

`frame_width`/`frame_height` are the alternative to `cols`/`rows`; supply either pair. A
grid that does not fit inside the texture is refused rather than silently dropping the
frames that fall outside it.

### Replacing what is already there

Existing `.anim` and `.controller` assets are kept unless `overwrite` is set, so a repeated
`full_setup` reports what it found instead of overwriting work:

```json
{ "action": "setup_clips", "path": "Assets/Sprites/hero.png",
  "clips": [{ "name": "walk", "start_frame": 0, "end_frame": 5 }], "overwrite": true }
```
<!-- examples:end -->

