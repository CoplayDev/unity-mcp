---
title: manage_camera
sidebar_label: manage_camera
description: "Manage cameras (Unity Camera + Cinemachine)."
---

# `manage_camera`

> **Auto-generated** from the Python tool registry. Do not hand-edit outside `<!-- examples:start --><!-- examples:end -->` blocks — the generator (`tools/generate_docs_reference.py`) will overwrite them.

**Group:** `core` &nbsp;·&nbsp; **Module:** `services.tools.manage_camera`

## Description

Manage cameras (Unity Camera + Cinemachine). Works without Cinemachine using basic Camera; unlocks presets, pipelines, and blending when Cinemachine is installed. Use ping to check Cinemachine availability.

SETUP:
- ping: Check if Cinemachine is available
- ensure_brain: Ensure CinemachineBrain exists on main camera
- get_brain_status: Get Brain state (active camera, blend, etc.)

CAMERA CREATION:
- create_camera: Create camera with preset (third_person, freelook, follow, dolly, static, top_down, side_scroller). Falls back to basic Camera without Cinemachine.

CAMERA CONFIGURATION:
- set_target: Set Follow and/or LookAt targets on a camera
- set_priority: Set camera priority for Brain selection
- set_lens: Configure lens (fieldOfView, nearClipPlane, farClipPlane, orthographicSize, dutch)
- set_body: Configure Body component (bodyType to swap, plus component properties)
- set_aim: Configure Aim component (aimType to swap, plus component properties)
- set_noise: Configure Noise component (amplitudeGain, frequencyGain)

EXTENSIONS:
- add_extension: Add extension (extensionType: CinemachineConfiner2D, CinemachineDeoccluder, CinemachineImpulseListener, CinemachineFollowZoom, CinemachineRecomposer, etc.)
- remove_extension: Remove extension by type

CAMERA CONTROL:
- set_blend: Configure default blend (style: Cut/EaseInOut/Linear/etc., duration)
- force_camera: Override Brain to use specific camera
- release_override: Release camera override
- list_cameras: List all cameras with status

CAPTURE:
- screenshot: Capture a screenshot. By default (no camera specified) uses ScreenCapture API, which captures all render layers including Screen Space - Overlay UI canvases. Specifying a camera uses direct camera rendering, which EXCLUDES Screen Space - Overlay canvases (use only when you need a specific viewpoint without UI). Supports include_image=true for inline base64 PNG, batch='surround' for 6-angle contact sheet, batch='orbit' for configurable grid, view_target/view_position for positioned capture, and capture_source='scene_view' to capture the active Unity Scene View viewport.
- screenshot_multiview: Shorthand for screenshot with batch='surround' and include_image=true.

## Parameters

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `action` | `str` | yes | The camera action to perform. |
| `target` | `str \| None` | — | Target camera (name, path, or instance ID). |
| `search_method` | `Literal['by_id', 'by_name', 'by_path'] \| None` | — | How to find target. |
| `properties` | `dict[str, Any] \| str \| None` | — | Action-specific parameters (dict or JSON string). |
| `screenshot_file_name` | `str \| None` | — | Screenshot file name (optional). Defaults to timestamp. |
| `screenshot_super_size` | `int \| str \| None` | — | Screenshot supersize multiplier (integer >= 1). |
| `camera` | `str \| None` | — | Camera to capture from (name, path, or instance ID). Omit to use ScreenCapture API (captures all layers including Screen Space Overlay UI). Specify only when you need a particular camera viewpoint; note that Screen Space - Overlay canvases will NOT appear in camera-rendered captures. |
| `include_image` | `bool \| str \| None` | — | If true, return screenshot as inline base64 PNG. Default false. |
| `max_resolution` | `int \| str \| None` | — | Max resolution (longest edge px) for inline image. Default 640. |
| `capture_source` | `Literal['game_view', 'scene_view'] \| None` | — | Screenshot source. 'game_view' (default) captures the game/camera path; 'scene_view' captures the active Unity Scene View viewport. |
| `batch` | `str \| None` | — | Batch capture mode: 'surround' (6 angles) or 'orbit' (configurable grid). |
| `view_target` | `str \| int \| list[float] \| None` | — | Target to focus on. GameObject name/path/ID or [x,y,z]. For game_view: aims camera at target. For scene_view: frames the Scene View on the target. |
| `view_position` | `list[float] \| str \| None` | — | World position [x,y,z] to place camera for positioned capture. |
| `view_rotation` | `list[float] \| str \| None` | — | Euler rotation [x,y,z] for camera. Overrides view_target if both provided. |
| `orbit_angles` | `int \| str \| None` | — | Number of azimuth samples for batch='orbit' (default 8, max 36). |
| `orbit_elevations` | `list[float] \| str \| None` | — | Elevation angles in degrees for batch='orbit' (default [0, 30, -15]). |
| `orbit_distance` | `float \| str \| None` | — | Camera distance from target for batch='orbit' (default auto). |
| `orbit_fov` | `float \| str \| None` | — | Camera FOV in degrees for batch='orbit' (default 60). |
| `output_folder` | `str \| None` | — | Optional folder for screenshot output. Project-relative (e.g. 'Assets/Screenshots' or 'Captures') or absolute path inside the project. Overrides the user's Editor preference. If omitted, falls back to the Editor preference, then to the built-in default (Assets/Screenshots). |

## Returns

A `dict` containing the Unity response. The exact shape depends on the action.

## Examples

<!-- examples:start -->
*No examples yet. Add usage examples here — they will be preserved across regenerations.*
<!-- examples:end -->

