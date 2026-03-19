---
phase: 3
status: pending
priority: medium-high
---

# Phase 3: Popular Packages

**Theme:** Timeline, Input System, Cinemachine, Physics 2D.
**Package deps:** `com.unity.timeline`, `com.unity.inputsystem`, `com.unity.cinemachine` (Physics2D is built-in).

---

## Tool 9: `manage_timeline`

**Purpose:** Timeline/PlayableDirector — inspect, control playback, list tracks/clips.

### Actions (8)

| Action | Description | Key Params | Mode |
|--------|-------------|------------|------|
| `list_directors` | All PlayableDirector components | `page_size` | Any |
| `get_director` | State, time, duration, wrap mode, asset name | `target` | Any |
| `play` | Play timeline | `target` | Any |
| `pause` | Pause timeline | `target` | Any |
| `stop` | Stop timeline | `target` | Any |
| `set_time` | Seek to time | `target`, `time` | Any |
| `list_tracks` | Tracks in timeline asset | `target` | Any |
| `get_bindings` | Track-to-object bindings | `target` | Any |

### asmdef

```json
{"name": "com.unity.timeline", "expression": "", "define": "UNITY_TIMELINE"}
// references: "Unity.Timeline", "Unity.Timeline.Editor"
```

### Files

| File | Notes |
|------|-------|
| `Server/src/services/tools/manage_timeline.py` | |
| `MCPForUnity/Editor/Tools/ManageTimeline.cs` | `#if UNITY_TIMELINE` |
| `Server/tests/integration/test_manage_timeline.py` | ~8 tests |

---

## Tool 10: `manage_input_system`

**Purpose:** New Input System — inspect action assets, bindings, devices.

### Actions (6)

| Action | Description | Key Params | Mode |
|--------|-------------|------------|------|
| `list_action_assets` | Find InputActionAsset in project | — | Any |
| `get_action_map` | Actions in a map with bindings | `asset`, `map_name` | Any |
| `get_action` | Bindings, interactions, processors | `asset`, `action_name` | Any |
| `list_devices` | Connected input devices | — | Any |
| `get_device` | Device layout, controls | `device_name` | Any |
| `list_player_inputs` | Find PlayerInput components | `page_size` | Any |

### asmdef

```json
{"name": "com.unity.inputsystem", "expression": "", "define": "UNITY_INPUT_SYSTEM"}
// references: "Unity.InputSystem"
```

### Files

| File | Notes |
|------|-------|
| `Server/src/services/tools/manage_input_system.py` | |
| `MCPForUnity/Editor/Tools/ManageInputSystem.cs` | `#if UNITY_INPUT_SYSTEM` |
| `Server/tests/integration/test_manage_input_system.py` | ~6 tests |

---

## Tool 11: `manage_cinemachine`

**Purpose:** Cinemachine virtual cameras, brain, blends.

### Actions (6)

| Action | Description | Key Params | Mode |
|--------|-------------|------------|------|
| `list_vcams` | All CinemachineCamera components | `page_size` | Any |
| `get_vcam` | Priority, follow/look-at, body/aim settings | `target` | Any |
| `set_vcam` | Modify priority, follow target, etc. | `target`, `properties` | Any |
| `get_brain` | Active camera, blend state, default blend | — | Any |
| `set_priority` | Change camera priority | `target`, `priority` | Any |
| `list_blends` | Custom blend definitions | — | Any |

### asmdef

```json
{"name": "com.unity.cinemachine", "expression": "", "define": "UNITY_CINEMACHINE"}
// references: "Cinemachine"
```

### Files

| File | Notes |
|------|-------|
| `Server/src/services/tools/manage_cinemachine.py` | |
| `MCPForUnity/Editor/Tools/ManageCinemachine.cs` | `#if UNITY_CINEMACHINE` |
| `Server/tests/integration/test_manage_cinemachine.py` | ~6 tests |

### C# Notes

```csharp
// Unity 6 uses CinemachineCamera (not CinemachineVirtualCamera — that's v2)
// CinemachineBrain is on the main camera
// Priority: higher = more preferred
// Follow/LookAt: Transform references on the virtual camera
```

---

## Tool 12: `manage_physics2d`

**Purpose:** 2D physics — raycasting, rigidbodies, colliders, physics settings.

### Actions (8)

| Action | Description | Key Params | Mode |
|--------|-------------|------------|------|
| `raycast` | 2D raycast | `origin`, `direction`, `max_distance`, `layer_mask` | Play/Edit |
| `raycast_all` | All 2D hits | Same | Play/Edit |
| `overlap_circle` | Entities in circle | `center`, `radius`, `layer_mask` | Play/Edit |
| `overlap_box` | Entities in box | `center`, `size`, `angle`, `layer_mask` | Play/Edit |
| `list_rigidbodies` | All Rigidbody2D | `page_size` | Any |
| `get_rigidbody` | Body type, mass, velocity, gravity scale | `target` | Any |
| `list_colliders` | All Collider2D | `page_size` | Any |
| `get_physics2d_settings` | Gravity, collision matrix | — | Any |

### Files

| File | Notes |
|------|-------|
| `Server/src/services/tools/manage_physics2d.py` | Vector2 params as `"x,y"` strings |
| `MCPForUnity/Editor/Tools/ManagePhysics2D.cs` | No `#if` guard — Physics2D is always available |
| `Server/tests/integration/test_manage_physics2d.py` | ~8 tests |

---

## Implementation Order

9. `manage_timeline` — commonly used, well-defined API
10. `manage_input_system` — high debug value
11. `manage_cinemachine` — complements camera tool
12. `manage_physics2d` — mirrors Phase 1's 3D physics

## Definition of Done

- [ ] All 4 tools registered and callable
- [ ] 3 tools properly guarded with `#if` defines
- [ ] asmdef updated with Timeline, InputSystem, Cinemachine
- [ ] ~28 integration tests passing
- [ ] STUDIO_TOOLS.md updated
