---
phase: 2
status: pending
priority: high
---

# Phase 2: Editor Automation

**Theme:** Build pipeline, project settings, package management, navigation.
**Package deps:** `com.unity.ai.navigation` (navigation only — rest is built-in Editor APIs).

---

## Tool 5: `manage_build`

**Purpose:** Build pipeline control, player settings, quality settings, scripting defines.

### Actions (9)

| Action | Description | Key Params | Mode |
|--------|-------------|------------|------|
| `get_player_settings` | Company, product, bundle ID, version, icons | `platform` (optional) | Edit |
| `set_player_settings` | Modify player settings | `properties` (JSON) | Edit |
| `get_quality_settings` | Quality levels, current level, per-level config | — | Edit |
| `set_quality_level` | Switch active quality level | `level` (name or index) | Edit |
| `get_build_settings` | Active target, scenes list, dev mode | — | Edit |
| `set_build_scenes` | Set scene list for build | `scenes` (JSON array of paths) | Edit |
| `build` | Trigger player build (async job pattern) | `target`, `output_path`, `options` | Edit |
| `get_scripting_defines` | Current defines per platform | `platform` (optional) | Edit |
| `set_scripting_defines` | Add/remove defines | `platform`, `defines` (comma-sep) | Edit |

### Async Build Pattern

Follows `run_tests` pattern:
- `build` returns a `job_id` immediately
- Poll with a separate action or reuse `get_build_status` action
- Build runs on main thread but uses `BuildPipeline.BuildPlayer` which returns `BuildReport`

### Files

| File | Notes |
|------|-------|
| `Server/src/services/tools/manage_build.py` | |
| `MCPForUnity/Editor/Tools/ManageBuild.cs` | `BuildPipeline`, `PlayerSettings`, `QualitySettings`, `EditorBuildSettings` |
| `Server/tests/integration/test_manage_build.py` | ~9 tests |

### C# Notes

```csharp
// get_player_settings: PlayerSettings.companyName, productName, applicationIdentifier, bundleVersion
// get_quality_settings: QualitySettings.names, QualitySettings.GetQualityLevel()
// build: BuildPipeline.BuildPlayer() — returns BuildReport with summary, steps, errors
// get_scripting_defines: PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget)
// set_scripting_defines: PlayerSettings.SetScriptingDefineSymbols(NamedBuildTarget, string[])
```

---

## Tool 6: `manage_packages`

**Purpose:** Unity Package Manager — list, add, remove, inspect packages.

### Actions (5)

| Action | Description | Key Params | Mode |
|--------|-------------|------------|------|
| `list` | All installed packages | `include_built_in` (bool) | Edit |
| `get_info` | Package detail — version, deps, description | `package_name` | Edit |
| `add` | Install by name or git URL | `package_id` (e.g. `com.unity.cinemachine` or git URL) | Edit |
| `remove` | Uninstall package | `package_name` | Edit |
| `search` | Search Unity registry | `query` | Edit |

### Files

| File | Notes |
|------|-------|
| `Server/src/services/tools/manage_packages.py` | |
| `MCPForUnity/Editor/Tools/ManagePackages.cs` | `UnityEditor.PackageManager.Client` — async API needs polling via `EditorApplication.update` |
| `Server/tests/integration/test_manage_packages.py` | ~5 tests |

### C# Notes

```csharp
// Client.List/Add/Remove/Search all return Request objects
// Must poll via EditorApplication.update until IsCompleted
// Return PackageInfo: name, version, displayName, description, dependencies, resolvedPath
// add: accepts "com.unity.cinemachine" or "https://github.com/..." or "file:../../local-path"
```

---

## Tool 7: `manage_navigation`

**Purpose:** AI Navigation — NavMesh baking, agents, obstacles, path queries.

### Actions (9)

| Action | Description | Key Params | Mode |
|--------|-------------|------------|------|
| `list_surfaces` | All NavMeshSurface components | `page_size` | Any |
| `bake` | Bake NavMesh for a surface | `target` (surface GO name) | Edit |
| `clear` | Clear baked NavMesh | `target` | Edit |
| `list_agents` | All NavMeshAgent components | `page_size` | Any |
| `get_agent` | Speed, radius, destination, path status | `target` | Any |
| `set_agent_destination` | Set agent destination | `target`, `position` | Play |
| `list_obstacles` | All NavMeshObstacle components | `page_size` | Any |
| `sample_position` | Nearest point on NavMesh | `position`, `max_distance` | Any |
| `calculate_path` | Path between two points | `start`, `end`, `area_mask` | Any |

### asmdef Changes

```json
// versionDefines:
{"name": "com.unity.ai.navigation", "expression": "", "define": "UNITY_AI_NAVIGATION"}
// references:
"Unity.AI.Navigation", "Unity.AI.Navigation.Editor"
```

### Files

| File | Notes |
|------|-------|
| `Server/src/services/tools/manage_navigation.py` | |
| `MCPForUnity/Editor/Tools/ManageNavigation.cs` | Guarded by `#if UNITY_AI_NAVIGATION` |
| `Server/tests/integration/test_manage_navigation.py` | ~9 tests |

### C# Notes

```csharp
#if UNITY_AI_NAVIGATION
[McpForUnityTool("manage_navigation", AutoRegister = true)]
public static class ManageNavigation
{
    // list_surfaces: FindObjectsByType<NavMeshSurface>()
    // bake: surface.BuildNavMesh()
    // sample_position: NavMesh.SamplePosition(position, out hit, maxDistance, NavMesh.AllAreas)
    // calculate_path: NavMesh.CalculatePath(start, end, areaMask, path) — return corners[]
    // get_agent: agent.speed, radius, destination, remainingDistance, pathStatus, isOnNavMesh
}
#endif
```

---

## Tool 8: `manage_render_pipeline`

**Purpose:** URP/HDRP settings — volume profiles, post-processing, renderer features.

### Actions (8)

| Action | Description | Key Params | Mode |
|--------|-------------|------------|------|
| `get_pipeline_info` | Active render pipeline type, asset name | — | Any |
| `list_volumes` | All Volume components (global/local) | `page_size` | Any |
| `get_volume` | Volume profile overrides and values | `target` | Any |
| `set_volume_override` | Modify a volume override value | `target`, `override_type`, `property`, `value` | Any |
| `list_renderer_features` | URP renderer features (if URP) | — | Edit |
| `get_render_pipeline_asset` | Active pipeline asset settings | — | Edit |
| `list_post_processing` | Active post-processing effects summary | — | Any |
| `toggle_volume_override` | Enable/disable a specific override | `target`, `override_type`, `enabled` | Any |

### Files

| File | Notes |
|------|-------|
| `Server/src/services/tools/manage_render_pipeline.py` | |
| `MCPForUnity/Editor/Tools/ManageRenderPipeline.cs` | Uses `GraphicsSettings`, `Volume`, reflection for URP/HDRP detection |
| `Server/tests/integration/test_manage_render_pipeline.py` | ~8 tests |

### C# Notes

```csharp
// No package guard needed — uses GraphicsSettings.currentRenderPipeline (always available)
// Volume profiles: iterate VolumeProfile.components for overrides
// URP detection: check if GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset
// Renderer features: reflection into UniversalRendererData.rendererFeatures
// set_volume_override: find override by type name, set property via reflection
```

---

## Implementation Order

5. `manage_build` — high-impact for CI/CD workflows
6. `manage_packages` — smallest scope, quick win
7. `manage_navigation` — important for game dev
8. `manage_render_pipeline` — visual quality control

## Definition of Done

- [ ] All 4 tools registered and callable
- [ ] Navigation tool properly guarded with `#if UNITY_AI_NAVIGATION`
- [ ] asmdef updated with navigation references + versionDefines
- [ ] ~31 integration tests passing
- [ ] STUDIO_TOOLS.md updated
