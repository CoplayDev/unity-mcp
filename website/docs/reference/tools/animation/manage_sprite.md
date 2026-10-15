---
title: manage_sprite
sidebar_label: manage_sprite
description: "Slice 2D sprite sheets and build AnimationClips and an AnimatorController from the frames."
---

# `manage_sprite`

> **Auto-generated** from the Python tool registry. Do not hand-edit outside `<!-- examples:start --><!-- examples:end -->` blocks — the generator (`tools/generate_docs_reference.py`) will overwrite them.

**Group:** `animation` &nbsp;·&nbsp; **Module:** `services.tools.manage_sprite`

## Description

Slice 2D sprite sheets and build AnimationClips and an AnimatorController from the frames. Actions: get_info returns a sheet's import settings and slices (paged with page_size / cursor) and, for a PNG or JPEG source, the sheet as an image block for vision analysis; slice_sheet applies a grid, replacing the sheet's existing slices; setup_clips creates AnimationClips from the slices; setup_controller builds a controller from clip names (idle = default state, one walk/run clip = a plain state and two or more = a Speed-driven 1D blend tree, jump/attack/hurt-type names = trigger states that fire from any state, other names = plain states); full_setup runs slice → clips → controller in one call.

## Parameters

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `action` | `Literal['get_info', 'slice_sheet', 'setup_clips', 'setup_controller', 'full_setup']` | yes | Action to perform. |
| `path` | `str \| None` | — | Sprite texture asset path (e.g. 'Assets/Sprites/hero_walk.png'). Required for get_info, slice_sheet, setup_clips, full_setup. |
| `cols` | `int \| None` | — | Number of columns in the sprite sheet grid. Used by slice_sheet and full_setup. |
| `rows` | `int \| None` | — | Number of rows in the sprite sheet grid. Default: 1, or derived from frame_height when that is given. |
| `frame_width` | `int \| None` | — | Frame width in pixels. Alternative to cols. |
| `frame_height` | `int \| None` | — | Frame height in pixels. Alternative to rows. |
| `base_name` | `str \| None` | — | Base name for sliced sprite frames (default: texture filename). |
| `filter_mode` | `Literal['point', 'bilinear', 'trilinear'] \| None` | — | slice_sheet and full_setup: texture filter the sliced sheet is imported with, in lowercase (get_info reports it as Point, Bilinear or Trilinear). Default: point, which keeps pixel art sharp; bilinear or trilinear suits high-resolution art. Every slice sets it, so a filter set by hand does not survive a re-slice. |
| `clips` | `list[dict[str, Any]] \| None` | — | Clip definitions: [{name, start_frame (default 0), end_frame (default the last frame), fps (default 12), loop (default from the name)}]. For setup_controller: [{name, path}] where path is the .anim asset path. |
| `animation_name` | `str \| None` | — | full_setup without clips: name of the one clip made from every frame at 12 fps (default: the sheet's file name). It loops only for an idle, walk or run-type name. |
| `output_dir` | `str \| None` | — | setup_clips and full_setup: folder for the .anim assets, and for full_setup's default controller (default: the sprite's folder). |
| `controller_path` | `str \| None` | — | Path for the .controller asset (e.g. 'Assets/Animators/Hero.controller'); '.controller' is appended if missing. Required for setup_controller; full_setup defaults to '<output_dir>/<sheet_name>_Controller.controller'. |
| `overwrite` | `bool` | — | Replace an existing .anim or .controller at the target path. Off by default: an existing clip is skipped with a CLIP_EXISTS warning, and an existing controller fails the call with CONTROLLER_EXISTS. Slicing is not covered: it always replaces the sheet's slices. |
| `add_to_scene` | `bool` | — | full_setup: give scene_target an Animator with the new controller, replacing any controller it had, and a SpriteRenderer if it has none (warning SCENE_SPRITE_RENDERER_ADDED). |
| `scene_target` | `str \| None` | — | full_setup with add_to_scene: name of exactly one existing GameObject (inactive ones count). A missing, unmatched or ambiguous target fails at step 'add_to_scene', after the clips and controller are written. |
| `page_size` | `int \| None` | — | get_info: how many entries of the 'slices' list to return (1-4096, default 512). A sheet sliced by hand can hold more slices than one response should carry. |
| `cursor` | `int \| None` | — | get_info: index to start the 'slices' page at. Pass back the 'next_cursor' from the previous response; next_cursor is null on the last page. The image is returned only on the first page. |

## Returns

A `dict` containing the Unity response. The exact shape depends on the action.

## Examples

<!-- examples:start -->
### Read the sheet before slicing it

The grid is the one thing the tool cannot infer. `get_info` returns the texture's
dimensions as JSON and the sheet itself as an image, so a vision-capable caller can count
the frames before committing to a grid.

```json
{ "action": "get_info", "path": "Assets/Sprites/hero_walk.png" }
```

`width` and `height` are the source file's pixels, which `slice_sheet` cuts in; Max Size or
NPOT scaling can make the imported texture smaller. The image is left out, with the reason
in `image_omitted_reason`, when the source is not PNG or JPEG, is over 8000 px on a side, or
would exceed 4 MB as base64 (about a 3 MB file).

The `slices` list is paged. A sheet can hold more entries than one response should carry —
`slice_sheet` alone allows up to 4096 — so `slice_count` reports the total and
`next_cursor` is non-null only while entries remain. Follow it until it is null rather
than assuming a sheet arrives whole.
Walk it by passing the previous `next_cursor` back; the image comes with the first page
only, since it is the same picture on every one.

```json
{ "action": "get_info", "path": "Assets/Sprites/atlas.png", "cursor": 512 }
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

Clip names decide the controller's shape: here `idle` becomes the default state, `walk` and
`run` share a `Speed`-driven 1D blend tree, and `attack` gets an `Attack` trigger. A name is
split into words (on `_`, `-`, spaces, camelCase and digits, so `heroAttack2` reads as
`hero`, `attack`, `2`), and the first row that matches decides:

| Words in the clip name | State | Loops by default |
|---|---|---|
| `idle`, `stand` | `Idle`, the default state. Only the first such clip is used: each later one gets no state, and the response warns `IDLE_CLIP_UNUSED`. | yes |
| `walk`; `run`, `sprint` | One clip: a state of that name. Two or more: a `Locomotion` state with a 1D blend tree on `Speed` (walk at 1, run at 2). Idle switches to it when `Speed` rises above 0.1 and back when it drops below 0.1. | yes |
| `jump`, `fall`, `land`; `attack`, `slash`, `punch`, `combo`, `cast`, `shoot`; `open`, `close`, `activate`, `die`, `death`, `hurt`, `hit` | A state entered from any state by a trigger named after the first of these words it contains (`heroAttack` → `Attack`); the trigger also restarts it. A non-looping one returns to Idle, else Locomotion, when it ends; a death, whose name has `die` or `death` in it, stays on its last frame instead. | no |
| anything else | A state no transition leads to. Unless it is the default state, it plays only from a script, and the response warns `STATE_UNREACHABLE`. | no |

Every transition is instant, since sprite frames cannot blend. Each trigger enters one state:
when clips share a word, the first of them takes the trigger, and each later one gets a state
that no transition leads to, with a `TRIGGER_SHARED` warning, so give each one-shot its own
action word. A death has no exit of its own, but triggers fire from any state: one set after
the death, `Hurt` included, still takes the Animator out of it, so stop setting them once the
character is dead. An explicit `"loop"` on a clip overrides the default.

### Slicing on its own

```json
{ "action": "slice_sheet", "path": "Assets/Sprites/hero.png", "frame_width": 32, "frame_height": 32 }
```

`frame_width`/`frame_height` are the alternative to `cols`/`rows`; supply either pair. A
grid that does not fit inside the texture is refused rather than silently dropping the
frames that fall outside it. Slicing sets the texture to Sprite (Multiple) import with NPOT
scaling off.
The sheet is imported with point filtering, which keeps pixel art sharp; pass
`"filter_mode": "bilinear"` (or `"trilinear"`) for high-resolution art.

### Replacing what is already there

`overwrite` covers the `.anim` and `.controller` files, not the sheet. Without it, an
existing clip is skipped with a `CLIP_EXISTS` warning and an existing controller stops the
call with `CONTROLLER_EXISTS`. Repeating a `full_setup` therefore ends with
`success: false`: at `step: "setup_clips"` with `ALL_CLIPS_EXIST` when every clip already
exists, or at `step: "setup_controller"` with `CONTROLLER_EXISTS` once the new clips are
written. To replace what exists, pass `"overwrite": true`:

```json
{ "action": "setup_clips", "path": "Assets/Sprites/hero.png",
  "clips": [{ "name": "walk", "start_frame": 0, "end_frame": 5 }], "overwrite": true }
```

To keep the clips and only rebuild the controller, call `setup_controller` with their
`.anim` paths, which the `CLIP_EXISTS` warnings name, and `"overwrite": true` if the
controller already exists.

Slicing has no such guard: every `slice_sheet` and `full_setup` replaces the sheet's slices
and sets its filter to `filter_mode` (point unless given), so a filter set by hand does not
survive. A frame keeps its identity only while the new grid reuses its name: a re-slice that
removes frames warns `SLICE_REMOVED_FRAMES`, and clips that used them lose those frames.
Slice again with the previous grid and `base_name` to bring them back, or rebuild the clips
with `overwrite`.

### Reading the result

Every action except `get_info` returns `diagnostics`, a list of
`{code, severity, message, fix_options}`. A bad clip entry (`CLIP_BAD_RANGE`,
`CLIP_BAD_FPS`, `CLIP_EXISTS` and the like) is a warning that skips that clip, so
`setup_clips` can succeed with `clip_count: 0`; compare `clip_count` with the clips you
sent. A failed `full_setup` names the step it stopped at in `step`: `slice_sheet`,
`setup_clips`, `setup_controller` or `add_to_scene`. At `add_to_scene` the clips and
controller are already written, and the response still carries `controller_path` and
`clip_count`.
<!-- examples:end -->

