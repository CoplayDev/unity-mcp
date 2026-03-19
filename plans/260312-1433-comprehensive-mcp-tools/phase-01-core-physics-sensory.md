---
phase: 1
status: pending
priority: highest
---

# Phase 1: Core Physics & Sensory

**Theme:** Fill the biggest gaps — physics, audio, lighting, camera.
**Package deps:** All built-in (no asmdef changes needed).

---

## Tool 1: `manage_physics`

**Purpose:** Classic 3D physics — raycasting, rigidbodies, colliders, joints, physics settings.

### Actions (10)

| Action | Description | Key Params | Mode |
|--------|-------------|------------|------|
| `raycast` | Cast ray, return hits | `origin`, `direction`, `max_distance`, `layer_mask` | Play/Edit |
| `raycast_all` | All hits along ray | Same as raycast | Play/Edit |
| `overlap_sphere` | Entities in sphere | `center`, `radius`, `layer_mask` | Play/Edit |
| `overlap_box` | Entities in box | `center`, `half_extents`, `layer_mask` | Play/Edit |
| `list_rigidbodies` | All Rigidbodies with summary | `page_size` | Any |
| `get_rigidbody` | Full rigidbody detail | `target` (name or ID) | Any |
| `set_rigidbody` | Modify mass, drag, constraints | `target`, `properties` (JSON) | Any |
| `list_colliders` | All colliders with type info | `page_size` | Any |
| `get_physics_settings` | Gravity, layer matrix, defaults | — | Any |
| `set_physics_settings` | Modify gravity, sleep thresholds | `properties` (JSON) | Edit |

### Files

| File | Notes |
|------|-------|
| `Server/src/services/tools/manage_physics.py` | Vector3 params as `"x,y,z"` strings (reuse `normalize_vector3` from utils) |
| `MCPForUnity/Editor/Tools/ManagePhysics.cs` | Uses `Physics.Raycast`, `FindObjectsByType<Rigidbody>()`, etc. |
| `Server/tests/integration/test_manage_physics.py` | ~10 tests: one per action + error cases |

### C# Implementation Notes

```csharp
// No #if guard needed — Physics is always available
[McpForUnityTool("manage_physics", AutoRegister = true)]
public static class ManagePhysics
{
    public static object HandleCommand(JObject @params) { ... }

    // Raycast returns: position, normal, distance, collider name, gameobject name, layer
    // list_rigidbodies: name, instanceID, mass, isKinematic, velocity (play mode only), sleep state
    // get_physics_settings: Physics.gravity, Physics.defaultSolverIterations, etc.
}
```

---

## Tool 2: `manage_audio`

**Purpose:** Audio sources, clips, mixers, listener — inspect, control playback, modify properties.

### Actions (10)

| Action | Description | Key Params | Mode |
|--------|-------------|------------|------|
| `list_sources` | All AudioSource components | `page_size` | Any |
| `get_source` | Full AudioSource detail | `target` | Any |
| `set_source` | Modify volume, pitch, spatial blend, etc. | `target`, `properties` | Any |
| `play` | Play/resume an AudioSource | `target` | Play |
| `stop` | Stop an AudioSource | `target` | Play |
| `pause` | Pause an AudioSource | `target` | Play |
| `list_clips` | AudioClip assets in project | `filter`, `page_size` | Any |
| `get_clip_info` | Clip length, frequency, channels, load type | `target` (asset path or name) | Any |
| `list_mixers` | AudioMixer assets | `page_size` | Any |
| `get_mixer` | Mixer groups, exposed params, current values | `target` | Any |
| `set_mixer_param` | Set exposed mixer float | `target`, `param_name`, `value` | Any |

### Files

| File | Notes |
|------|-------|
| `Server/src/services/tools/manage_audio.py` | 11 actions |
| `MCPForUnity/Editor/Tools/ManageAudio.cs` | `FindObjectsByType<AudioSource>()`, `AssetDatabase.FindAssets("t:AudioClip")` |
| `Server/tests/integration/test_manage_audio.py` | ~11 tests |

### C# Implementation Notes

```csharp
// list_sources returns: gameObject name, clip name, volume, isPlaying, loop, spatialBlend, mixerGroup
// get_source returns all AudioSource properties
// play/stop/pause only work in Play mode — return error if in Edit mode
// list_clips uses AssetDatabase — returns path, length, frequency, channels, loadType
// get_mixer iterates AudioMixerGroup children, reads exposed params via GetFloat()
```

---

## Tool 3: `manage_lighting`

**Purpose:** Lights, probes, lightmapping, environment/render settings.

### Actions (11)

| Action | Description | Key Params | Mode |
|--------|-------------|------------|------|
| `list_lights` | All Light components | `page_size`, `type_filter` | Any |
| `get_light` | Full light properties | `target` | Any |
| `set_light` | Modify color, intensity, range, shadows | `target`, `properties` | Any |
| `bake` | Trigger lightmap bake (async) | — | Edit |
| `cancel_bake` | Cancel in-progress bake | — | Edit |
| `get_bake_status` | Is baking? Progress? | — | Edit |
| `list_probes` | Light probes + reflection probes | `page_size` | Any |
| `get_probe` | Probe detail (type, bounds, mode) | `target` | Any |
| `get_environment` | RenderSettings — ambient, fog, skybox, sun | — | Any |
| `set_environment` | Modify RenderSettings | `properties` | Edit |
| `get_lightmap_settings` | Lightmapper config | — | Edit |

### Files

| File | Notes |
|------|-------|
| `Server/src/services/tools/manage_lighting.py` | |
| `MCPForUnity/Editor/Tools/ManageLighting.cs` | Mixes runtime (`Light`) + editor (`Lightmapping`, `RenderSettings`) APIs |
| `Server/tests/integration/test_manage_lighting.py` | ~11 tests |

### C# Implementation Notes

```csharp
// list_lights returns: name, type (Directional/Point/Spot/Area), color, intensity, range, shadows
// bake: Lightmapping.BakeAsync() — returns immediately, use get_bake_status to poll
// get_environment: RenderSettings.ambientMode, ambientLight, fog, fogColor, fogDensity, skybox material
// get_lightmap_settings: LightmapEditorSettings (lightmapper, bounces, resolution, etc.)
```

---

## Tool 4: `manage_camera`

**Purpose:** Camera inspection, modification, screen/world conversions.

### Actions (7)

| Action | Description | Key Params | Mode |
|--------|-------------|------------|------|
| `list_cameras` | All cameras with depth, culling mask | `page_size` | Any |
| `get_camera` | Full camera properties | `target` | Any |
| `set_camera` | Modify FOV, near/far, clear flags, etc. | `target`, `properties` | Any |
| `render_to_file` | Render camera view to PNG file | `target`, `path`, `width`, `height` | Play |
| `world_to_screen` | World point → screen coordinates | `target`, `position` | Any |
| `screen_to_ray` | Screen point → world ray | `target`, `position` | Any |
| `get_main_camera` | Quick access to Camera.main info | — | Any |

### Files

| File | Notes |
|------|-------|
| `Server/src/services/tools/manage_camera.py` | |
| `MCPForUnity/Editor/Tools/ManageCamera.cs` | `Camera.allCameras`, `Camera.main` |
| `Server/tests/integration/test_manage_camera.py` | ~7 tests |

### Overlap with `manage_scene`

`manage_scene` has a `screenshot` action that captures the Game view. `manage_camera` > `render_to_file` is different: renders a specific camera to a specific resolution, useful for debugging camera setups. Keep both.

---

## Implementation Order

1. `manage_physics` — most universally needed
2. `manage_audio` — no workaround exists
3. `manage_lighting` — critical for visual workflows
4. `manage_camera` — complements existing scene tools

## Definition of Done

- [ ] All 4 Python MCP tools registered and callable
- [ ] All 4 C# handlers responding to commands
- [ ] ~39 integration tests passing
- [ ] `docs/reference/STUDIO_TOOLS.md` updated with all 4 tools
- [ ] Manual smoke test: each tool works with a live Unity instance

## Estimated Effort

~12-16 files, ~2000-3000 LOC total (Python + C#). Each tool is ~1-2 hours if following the established `manage_dots` pattern exactly.
