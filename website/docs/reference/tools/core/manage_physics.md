---
title: manage_physics
sidebar_label: manage_physics
description: "Manage physics settings, collision matrix, materials, joints, queries, and validation."
---

# `manage_physics`

> **Auto-generated** from the Python tool registry. Do not hand-edit outside `<!-- examples:start --><!-- examples:end -->` blocks — the generator (`tools/generate_docs_reference.py`) will overwrite them.

**Group:** `core` &nbsp;·&nbsp; **Module:** `services.tools.manage_physics`

## Description

Manage physics settings, collision matrix, materials, joints, queries, and validation.

SETTINGS: ping, get_settings, set_settings
COLLISION MATRIX: get_collision_matrix, set_collision_matrix
MATERIALS: create_physics_material, configure_physics_material, assign_physics_material
JOINTS: add_joint, configure_joint, remove_joint
QUERIES: raycast, raycast_all, linecast, shapecast, overlap
FORCES: apply_force
RIGIDBODY: get_rigidbody, configure_rigidbody
VALIDATION: validate
SIMULATION: simulate_step

## Parameters

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `action` | `Literal['ping', 'get_settings', 'set_settings', 'get_collision_matrix', 'set_collision_matrix', 'create_physics_material', 'configure_physics_material', 'assign_physics_material', 'add_joint', 'configure_joint', 'remove_joint', 'raycast', 'raycast_all', 'linecast', 'shapecast', 'overlap', 'validate', 'simulate_step', 'apply_force', 'get_rigidbody', 'configure_rigidbody']` | yes | The physics action to perform. |
| `dimension` | `str \| None` | — | Physics dimension: '3d' (default) or '2d'. |
| `settings` | `dict[str, Any] \| None` | — | Key-value settings for set_settings. |
| `layer_a` | `str \| None` | — | Layer name or index for collision matrix. |
| `layer_b` | `str \| None` | — | Layer name or index for collision matrix. |
| `collide` | `bool \| None` | — | Whether layers should collide (set_collision_matrix). |
| `name` | `str \| None` | — | Name for new physics material. |
| `path` | `str \| None` | — | Asset path for materials. |
| `dynamic_friction` | `float \| None` | — | Dynamic friction (0-1). |
| `static_friction` | `float \| None` | — | Static friction (0-1). |
| `bounciness` | `float \| None` | — | Bounciness (0-1). |
| `friction` | `float \| None` | — | Friction for 2D materials. |
| `friction_combine` | `str \| None` | — | Friction combine mode: Average, Minimum, Multiply, Maximum. |
| `bounce_combine` | `str \| None` | — | Bounce combine mode: Average, Minimum, Multiply, Maximum. |
| `material_path` | `str \| None` | — | Path to physics material asset for assign. |
| `target` | `str \| None` | — | Target GameObject name or instance ID. |
| `collider_type` | `str \| None` | — | Specific collider type to target. |
| `search_method` | `str \| None` | — | Search method for target resolution. |
| `joint_type` | `str \| None` | — | Joint type: fixed, hinge, spring, character, configurable (3D); distance, fixed, friction, hinge, relative, slider, spring, target, wheel (2D). |
| `connected_body` | `str \| None` | — | Connected body target for joints. |
| `motor` | `dict[str, Any] \| None` | — | Motor config: {targetVelocity, force, freeSpin}. |
| `limits` | `dict[str, Any] \| None` | — | Limits config: {min, max, bounciness}. |
| `spring` | `dict[str, Any] \| None` | — | Spring config: {spring, damper, targetPosition}. |
| `drive` | `dict[str, Any] \| None` | — | Drive config for ConfigurableJoint. |
| `properties` | `dict[str, Any] \| None` | — | Direct property dict for joints or materials. |
| `origin` | `list[float] \| None` | — | Ray origin [x,y,z] or [x,y]. |
| `direction` | `list[float] \| None` | — | Ray direction [x,y,z] or [x,y]. |
| `max_distance` | `float \| None` | — | Max raycast distance. |
| `layer_mask` | `str \| None` | — | Layer mask for queries (name or int). |
| `query_trigger_interaction` | `str \| None` | — | Trigger interaction: UseGlobal, Ignore, Collide. |
| `shape` | `str \| None` | — | Overlap shape: sphere, box, capsule (3D); circle, box, capsule (2D). |
| `position` | `list[float] \| None` | — | Overlap position [x,y,z] or [x,y]. |
| `size` | `Any \| None` | — | Overlap size: float (radius) or [x,y,z] (half-extents). |
| `start` | `list[float] \| None` | — | Linecast start point [x,y,z] or [x,y]. |
| `end` | `list[float] \| None` | — | Linecast end point [x,y,z] or [x,y]. |
| `point1` | `list[float] \| None` | — | Capsule shapecast point1 [x,y,z]. |
| `point2` | `list[float] \| None` | — | Capsule shapecast point2 [x,y,z]. |
| `height` | `float \| None` | — | Capsule height for shapecast. |
| `capsule_direction` | `int \| None` | — | Capsule direction: 0=X, 1=Y (default), 2=Z. |
| `angle` | `float \| None` | — | Rotation angle for 2D shape casts. |
| `force` | `list[float] \| None` | — | Force vector [x,y,z] or [x,y] for apply_force. |
| `force_mode` | `str \| None` | — | Force mode: Force, Impulse, Acceleration, VelocityChange (3D); Force, Impulse (2D). |
| `force_type` | `str \| None` | — | Force type: 'normal' (default) or 'explosion' (3D only). |
| `torque` | `list[float] \| None` | — | Torque vector [x,y,z] (3D) or [z] (2D). |
| `explosion_position` | `list[float] \| None` | — | Explosion center [x,y,z]. |
| `explosion_radius` | `float \| None` | — | Explosion radius. |
| `explosion_force` | `float \| None` | — | Explosion force magnitude. |
| `upwards_modifier` | `float \| None` | — | Explosion upwards modifier. |
| `steps` | `int \| None` | — | Number of simulation steps (max 100). |
| `step_size` | `float \| None` | — | Step size in seconds. |
| `page_size` | `int \| None` | — | Page size for validate results (default 50). |
| `cursor` | `int \| None` | — | Cursor offset for validate pagination. |
| `component_index` | `int \| None` | — | Zero-based index to select which component when multiple of the same type exist (e.g., multiple HingeJoints or BoxColliders). If omitted, targets the first instance. |

## Returns

A `dict` containing the Unity response. The exact shape depends on the action.

## Examples

<!-- examples:start -->
*No examples yet. Add usage examples here — they will be preserved across regenerations.*
<!-- examples:end -->

