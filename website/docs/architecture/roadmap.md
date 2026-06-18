# Unity MCP Feature Roadmap 2026

## Research Summary

Six parallel research agents investigated 12+ domains across Unity's API surface. Every domain was assessed for: API stability, implementation complexity, developer value, dead ends, and dependencies.

### Current Tool Coverage (19 tools)
Animation, Asset, Audio, Camera, Components, Editor, GameObjects, Graphics, Lighting, Material, Prefabs, ProBuilder, Scene, Script, ScriptableObject, Shader, Texture, UI, VFX

---

## Prioritized Implementation Plan

### Tier 1: Foundation (Unblocks Everything Else)

#### 1. `manage_packages` — Package Management
| Dimension | Assessment |
|-----------|-----------|
| **Value** | Very High |
| **Complexity** | Medium |
| **Actions** | ~14 |
| **Dependencies** | None (core Unity) |
| **Audience** | 100% of users |

**Why first**: Directly unblocks XR, Addressables, Input System, and any workflow requiring optional packages. Currently the #1 gap — AI can detect missing packages but cannot install them.

**Key APIs**: `PackageManager.Client.Add/Remove/List/Search/Embed` (all public, async). Scoped registries via `manifest.json` editing. Assembly definitions as JSON files.

**Actions**: `add_package`, `remove_package`, `add_and_remove`, `list_packages`, `search_packages`, `get_package_info`, `embed_package`, `resolve_packages`, `list_registries`, `add_registry`, `remove_registry`, `list_assemblies`, `create_asmdef`, `ping`

**Challenge**: Domain reload after install/remove kills state. Solution: `PendingResponse` + `McpJobStateStore` (existing patterns).

#### 2. QoL: Extend Existing Tools
| Dimension | Assessment |
|-----------|-----------|
| **Value** | High |
| **Complexity** | Low |
| **Effort** | 1-2 days each |

**2a. Multi-scene editing** (extend `manage_scene`):
- `open_additive`, `close_scene`, `set_active_scene`, `get_loaded_scenes`, `save_setup`, `restore_setup`
- `add_to_build`, `remove_from_build`, `set_build_enabled`

**2b. Scene validation** (extend `manage_scene` or new resource):
- `validate` — detect missing scripts, broken prefab references, null references
- `repair` — auto-fix missing scripts
- Uses `GameObjectUtility.GetMonoBehavioursWithMissingScriptCount()`, `PrefabUtility.GetPrefabInstanceStatus()`

**2c. Undo/Redo** (extend `manage_editor`):
- `undo`, `redo` — `Undo.PerformUndo()` / `Undo.PerformRedo()`

**2d. Scene templates** (extend `manage_scene`):
- `create_from_template` — presets: `3d_basic`, `3d_urp`, `2d_basic`, `empty`

---

### Tier 2: Core Game Systems (High Value, Low-Medium Complexity)

#### 3. `manage_physics` — Physics System
| Dimension | Assessment |
|-----------|-----------|
| **Value** | High |
| **Complexity** | Low-Medium |
| **Actions** | ~18 |
| **Dependencies** | None (core Unity) |
| **Audience** | ~90% of games |

**Why**: Almost every game uses physics. Existing `manage_components` can add Rigidbody/Collider but lacks global settings, collision matrix, raycasting, and simulation.

**Key APIs**: All public and stable since Unity 5+. `Physics.*` static class, all component properties, `PhysicsMaterial` asset creation.

**Actions by category**:
- Rigidbody: `add_rigidbody`, `configure_rigidbody`
- Colliders: `add_collider`, `configure_collider`, `fit_collider`
- Materials: `create_physics_material`, `configure_physics_material`
- Joints: `add_joint`, `configure_joint`, `remove_joint`
- Global: `get_settings`, `set_settings`, `get_collision_matrix`, `set_collision_matrix`
- Simulation: `simulate_step`, `sync_transforms`
- Queries: `raycast`, `overlap_sphere`

**Dead ends**: Physics callbacks don't fire in editor simulation. Can't simulate individual objects. Physics debug viz is internal.

#### 4. `manage_input` — Input System
| Dimension | Assessment |
|-----------|-----------|
| **Value** | High |
| **Complexity** | Medium-Low |
| **Actions** | ~18 |
| **Dependencies** | `com.unity.inputsystem` (must install) |
| **Audience** | ~95% of games |

**Why**: Every game needs input. Tedious manual setup. Perfect for presets ("set up FPS controls with gamepad support").

**Key APIs**: `InputActionSetupExtensions` — clean fluent API. `InputActionAsset` is a ScriptableObject serialized as JSON. All public.

**Actions**: `create_input_actions`, `get_input_actions_info`, `add_action_map`, `remove_action_map`, `add_action`, `remove_action`, `set_action_properties`, `add_binding`, `add_composite_binding`, `remove_binding`, `add_control_scheme`, `remove_control_scheme`, `setup_player_input`, `create_preset` (fps, third_person, platformer, vehicle, ui), `ping`

**Dead ends**: InputSettings modification is fragile. Active input handler switching requires restart. Generated C# wrapper class toggling is fragile.

#### 5. `manage_navigation` — NavMesh & AI Navigation
| Dimension | Assessment |
|-----------|-----------|
| **Value** | High |
| **Complexity** | Low-Medium |
| **Actions** | ~22 |
| **Dependencies** | None (core) + `com.unity.ai.navigation` (optional) |
| **Audience** | ~70% of 3D games |

**Why**: Navigation is fundamental. NavMesh baking is a pain point for beginners. Enables complete AI character workflow.

**Key APIs**: Core `NavMesh.*` static methods are built-in (no package). `NavMeshSurface`/`NavMeshLink`/`NavMeshModifier` from AI Navigation package (optional).

**Actions**: `navmesh_bake`, `navmesh_clear`, `navmesh_sample_position`, `navmesh_calculate_path`, `navmesh_raycast`, `agent_add`, `agent_configure`, `agent_set_destination`, `obstacle_add`, `obstacle_configure`, `surface_add`, `surface_configure`, `link_add`, `link_configure`, `modifier_add`, `modifier_volume_add`, `ping`, etc.

**Resource**: `mcpforunity://scene/navigation`

**Dead ends**: Can't create new NavMesh area types programmatically. No visual NavMesh debugging API.

---

### Tier 3: Content Creation (High Value, Medium Complexity)

#### 6. `manage_terrain` — Terrain System
| Dimension | Assessment |
|-----------|-----------|
| **Value** | High |
| **Complexity** | Medium |
| **Actions** | ~24 |
| **Dependencies** | None (core Unity) |
| **Audience** | ~40% of 3D games |

**Why**: Enables prompt-driven terrain generation. "Create mountainous terrain with snow above 80% height" becomes possible. Procedural generation (Perlin, height/slope-based painting) is the killer feature.

**Key APIs**: `TerrainData.SetHeights/GetHeights`, `SetAlphamaps`, `SetHoles`, tree/detail placement. All public.

**Actions**: `create_terrain`, `get_info`, `get_heights`, `set_heights`, `generate_heights` (Perlin, ridged, fbm), `flatten`, `smooth_heights`, `add_layer`, `paint_layer`, `paint_layer_by_height`, `paint_layer_by_slope`, `set_tree_prototypes`, `add_trees`, `scatter_trees`, `set_detail_prototypes`, `scatter_details`, `set_holes`, `set_neighbors`, `set_settings`, `ping`

**Critical design**: Heightmap operations must be region-based (never full map). Procedural generation runs C#-side. Large data stays server-side.

#### 7. `manage_timeline` — Timeline System
| Dimension | Assessment |
|-----------|-----------|
| **Value** | High |
| **Complexity** | Medium |
| **Actions** | ~25 |
| **Dependencies** | `com.unity.timeline` (included by default) |
| **Audience** | ~35% of games |

**Why**: Unlocks programmatic cutscene/sequence construction — a unique AI workflow. "Create a 10-second cutscene where the camera pans, the door opens at 2s, and music fades in at 5s."

**Key APIs**: `TimelineAsset.CreateTrack<T>()`, `TrackAsset.CreateClip<T>()`, `PlayableDirector.SetGenericBinding()`, `SignalAsset/Emitter/Receiver`. All public, remarkably complete.

**Actions**: `create_timeline`, `get_timeline_info`, `add_track`, `remove_track`, `add_clip`, `set_clip_properties`, `move_clip`, `setup_director`, `set_binding`, `get_bindings`, `set_director_properties`, `create_signal`, `add_signal_emitter`, `setup_signal_receiver`, `add_group`, `ping`

**Synergies**: `manage_animation` (clips), `manage_camera` (Cinemachine bindings), `manage_scene`.

#### 8. `manage_netcode` — Networking / Multiplayer
| Dimension | Assessment |
|-----------|-----------|
| **Value** | High |
| **Complexity** | Medium |
| **Actions** | ~20 |
| **Dependencies** | `com.unity.netcode.gameobjects` |
| **Audience** | ~64% of developers |

**Why**: Largest untapped audience. Zero competition in MCP space. Networking setup is error-prone and boilerplate-heavy.

**Key APIs**: `NetworkManager`, `NetworkObject`, `NetworkTransform`, `NetworkAnimator`, `NetworkRigidbody` — all standard MonoBehaviours. `NetworkPrefabsList` — ScriptableObject.

**Actions**: `setup_create_manager`, `setup_configure_manager`, `prefab_create_list`, `prefab_register`, `prefab_make_network`, `component_add_network_object`, `component_add_network_transform`, `component_add_network_animator`, `component_add_network_rigidbody`, `codegen_network_behaviour`, `codegen_player_controller`, `validate`, `list_network_objects`, `ping`

**Unique value**: Code generation for `NetworkBehaviour` scripts with RPCs, `NetworkVariable<T>` declarations. The `validate` action catches common misconfigurations before runtime.

---

### Tier 4: Build & Deploy (Very High Value, Higher Complexity)

#### 9. `manage_build` — Build Pipeline
| Dimension | Assessment |
|-----------|-----------|
| **Value** | Very High |
| **Complexity** | Medium-High |
| **Actions** | ~15 |
| **Dependencies** | None (Addressables optional) |

**Why**: Essential for CI/CD workflows, platform management, and project configuration.

**Actions**: `build_player`, `build_status`, `switch_platform`, `get_platform`, `get_player_settings`, `set_player_settings`, `get_define_symbols`, `set_define_symbols`, `get_build_scenes`, `set_build_scenes`, `get_build_profile`, `set_build_profile`, `list_build_profiles`, `ping`

**Challenge**: `BuildPipeline.BuildPlayer` is synchronous and blocks the editor thread. Non-blocking actions (PlayerSettings, define symbols) are straightforward. Build triggering needs `EditorApplication.delayCall` + poll-based status.

#### 10. `manage_addressables` — Addressable Assets
| Dimension | Assessment |
|-----------|-----------|
| **Value** | High |
| **Complexity** | Medium |
| **Actions** | ~23 |
| **Dependencies** | `com.unity.addressables` |

**Why**: Unity's recommended asset management for any project needing dynamic loading, DLC, or reduced build sizes.

**Actions**: `group_create`, `group_remove`, `group_list`, `entry_add`, `entry_remove`, `entry_move`, `entry_set_address`, `entry_find`, `label_add`, `label_remove`, `label_list`, `label_set`, `profile_list`, `profile_get`, `profile_set`, `profile_set_active`, `build_content`, `build_update`, `build_clean`, `get_settings`, `set_settings`, `ping`

**Resources**: `mcpforunity://addressables/groups`, `mcpforunity://addressables/settings`

---

### Tier 5: Specialized Domains (Medium-High Value, Higher Complexity)

#### 11. `manage_xr` — XR / VR / AR
| Dimension | Assessment |
|-----------|-----------|
| **Value** | Medium-High |
| **Complexity** | HIGH |
| **Actions** | ~25 |
| **Dependencies** | 3-6 packages |
| **Audience** | ~18% of developers |

**Why**: XR setup is notoriously painful. Meta has 10 tools but only covers Meta-specific SDK — cross-platform XRI, AR Foundation, and project-level setup are our opportunity.

**Recommendation**: Target XRI 3.0+ only. Focus on project setup + XR Origin creation first. Defer AR Foundation to later phase. Requires `manage_packages` first.

#### 12. `manage_tilemap` — 2D Tilemap (split from broader 2D)
| Dimension | Assessment |
|-----------|-----------|
| **Value** | Medium-High |
| **Complexity** | Medium |
| **Actions** | ~15 |
| **Dependencies** | None (core) + optional extras |

**Why**: Tilemap is the highest-value subset of 2D tooling. AI-assisted tilemap population is a strong use case.

**Actions**: `create`, `set_tile`, `set_tiles`, `fill_region`, `clear`, `get_info`, `swap_tile`, `add_collider`, `tile_create`, `tile_create_rule`, `ping`

#### 13. `manage_optimization` — Scene Optimization
| Dimension | Assessment |
|-----------|-----------|
| **Value** | Medium |
| **Complexity** | Medium |
| **Actions** | ~10 |
| **Dependencies** | None (core Unity) |

**Actions**: `set_static_flags`, `get_static_flags`, `set_static_flags_recursive`, `occlusion_bake`, `occlusion_cancel`, `occlusion_status`, `occlusion_clear`, `configure_lod`, `get_lod_info`, `auto_lod`

---

## Master Priority Matrix

| # | Tool | Value | Complexity | Deps | Actions | Audience |
|---|------|-------|-----------|------|---------|----------|
| 1 | `manage_packages` | Very High | Medium | 0 | 14 | 100% |
| 2 | QoL extensions | High | Low | 0 | ~15 | 100% |
| 3 | `manage_physics` | High | Low-Med | 0 | 18 | 90% |
| 4 | `manage_input` | High | Med-Low | 1 | 18 | 95% |
| 5 | `manage_navigation` | High | Low-Med | 0-1 | 22 | 70% |
| 6 | `manage_terrain` | High | Medium | 0 | 24 | 40% |
| 7 | `manage_timeline` | High | Medium | 1 | 25 | 35% |
| 8 | `manage_netcode` | High | Medium | 1-2 | 20 | 64% |
| 9 | `manage_build` | Very High | Med-High | 0 | 15 | 100% |
| 10 | `manage_addressables` | High | Medium | 1 | 23 | 30% |
| 11 | `manage_xr` | Med-High | HIGH | 3-6 | 25 | 18% |
| 12 | `manage_tilemap` | Med-High | Medium | 0-2 | 15 | 25% |
| 13 | `manage_optimization` | Medium | Medium | 0 | 10 | 50% |

## Confirmed Dead Ends (Do Not Implement)

| Feature | Reason |
|---------|--------|
| Shader Graph node creation | Internal API, no public editor scripting |
| VFX Graph node editing | Internal visual graph API |
| Terrain brush stroke simulation | UI-based painting system, no programmatic API |
| XR runtime testing | Requires headset or Play mode |
| Multiplayer session testing | Requires multiple Play mode instances |
| Physics callbacks in editor sim | Unity blocks for safety |
| Tile Palette editing | No public API |
| 2D Animation bone rigging | Deep, undocumented editor API |
| Unity Cloud Build | Separate REST API, not Editor scripting |
| Frame Debugger window | Internal editor utility |
| Custom NavMesh area creation | Hard-coded to 32 slots, no creation API |

## Dependencies Graph

```
manage_packages (Tier 1)
    |
    +---> manage_input (requires com.unity.inputsystem)
    +---> manage_netcode (requires com.unity.netcode.gameobjects)
    +---> manage_addressables (requires com.unity.addressables)
    +---> manage_xr (requires 3-6 packages)
    +---> manage_timeline (usually pre-installed)
    +---> manage_navigation (optional com.unity.ai.navigation)

No package dependency:
    manage_physics, manage_terrain, manage_build,
    manage_optimization, QoL extensions, manage_tilemap (core)
```

## Estimated Total Scope

- **13 new tools/extensions** across 5 tiers
- **~250+ new actions** total
- **~5 new resources**
- Zero internal/private API hacks needed — all public, stable Unity APIs

---

*Generated 2026-03-08 by 6 parallel research agents analyzing Unity 6 APIs, documentation, forums, and source references.*
