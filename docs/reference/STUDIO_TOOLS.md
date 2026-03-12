# The1Studio Custom Tools Reference

Tools added by The1Studio on top of the upstream [CoplayDev/unity-mcp](https://github.com/CoplayDev/unity-mcp). These are the only additions in our fork — everything else is upstream.

## manage_dots

**Purpose:** Unity DOTS ECS debugging, entity inspection, system monitoring, and performance analysis.

**Prerequisite:** `com.unity.entities` package installed in the Unity project. The C# handler is compiled only when `UNITY_ENTITIES` is defined.

### Files

| File | Role |
|------|------|
| `MCPForUnity/Editor/Tools/ManageDots.cs` | C# tool handler (guarded by `#if UNITY_ENTITIES`) |
| `Server/src/services/tools/manage_dots.py` | Python MCP tool (auto-registered) |
| `Server/src/cli/commands/dots.py` | CLI commands (`unity-mcp dots ...`) |
| `Server/tests/integration/test_manage_dots.py` | 17 integration tests |

### Actions

| Action | Description | Key Params |
|--------|-------------|------------|
| `list_worlds` | List all ECS Worlds with entity/system counts | — |
| `query_entities` | Find entities matching component types | `component_types` (comma-separated), `page_size` |
| `get_entity` | Full entity inspection — components, buffers, shared, enableable state | `entity_index`, `entity_version` |
| `list_systems` | List all systems with enabled status | `group` (filter) |
| `get_system` | System detail — queries, ordering (UpdateBefore/After), group status | `system_name` |
| `performance_snapshot` | Chunk utilization, archetype stats, empty chunk detection | `limit` |
| `toggle_system` | Enable/disable a system for debugging | `system_name`, `enabled` |
| `list_component_types` | Discover all registered ECS component types | `filter`, `category`, `page_size` |
| `create_entity` | Create a debug entity with optional components | `component_types` |
| `destroy_entity` | Destroy an entity by index/version | `entity_index`, `entity_version` |
| `set_component` | Modify a component field value at runtime | `entity_index`, `component_name`, `field_name`, `field_value` |
| `add_component` | Add a component type to an existing entity | `entity_index`, `component_name` |
| `remove_component` | Remove a component type from an entity | `entity_index`, `component_name` |
| `query_count` | Count entities matching component filter | `component_types` |

All actions accept an optional `world` parameter (defaults to `DefaultGameObjectInjectionWorld`).

### CLI Examples

```bash
unity-mcp dots worlds                                # List all ECS Worlds
unity-mcp dots entities "LocalTransform,Velocity"    # Query by components
unity-mcp dots entity 42                             # Inspect entity 42
unity-mcp dots entity 42 --version 2                 # With specific version
unity-mcp dots systems                               # List all systems
unity-mcp dots systems --group "Simulation"           # Filter by group
unity-mcp dots system "TransformSystemGroup"          # System details
unity-mcp dots perf                                   # Performance snapshot
unity-mcp dots perf --limit 10                        # Top 10 archetypes only
unity-mcp dots toggle "MySystem" false                # Disable system
unity-mcp dots toggle "MySystem" true --world "Server World"  # Re-enable in specific world
unity-mcp dots types                                  # List all ECS component types
unity-mcp dots types --filter "Transform"             # Filter by name
unity-mcp dots types --category "BufferData"          # Filter by category
unity-mcp dots create --components "LocalTransform,Velocity"  # Create debug entity
unity-mcp dots destroy 42                             # Destroy entity
unity-mcp dots destroy 42 --version 2                 # With specific version
unity-mcp dots set 42 "Health" "value" "75"           # Set component field
unity-mcp dots add-component 42 "Velocity"            # Add component to entity
unity-mcp dots remove-component 42 "Velocity"         # Remove component
unity-mcp dots count "LocalTransform,Health"          # Count matching entities
```

### MCP Tool Examples

```python
# From any MCP client (Claude, Cursor, etc.)
manage_dots(action="list_worlds")
manage_dots(action="query_entities", component_types="LocalTransform,Health", page_size=10)
manage_dots(action="get_entity", entity_index=42)
manage_dots(action="performance_snapshot", world="Server World", limit=5)
manage_dots(action="toggle_system", system_name="PhysicsSimulationGroup", enabled=False)
manage_dots(action="list_component_types", filter="Transform", category="ComponentData")
manage_dots(action="create_entity", component_types="LocalTransform,Velocity")
manage_dots(action="destroy_entity", entity_index=42, entity_version=1)
manage_dots(action="set_component", entity_index=42, component_name="Health", field_name="value", field_value="75")
manage_dots(action="add_component", entity_index=42, component_name="Velocity")
manage_dots(action="remove_component", entity_index=42, component_name="Velocity")
manage_dots(action="query_count", component_types="LocalTransform,Health")
```

### Implementation Notes

- **Component type resolution** iterates `TypeManager` registered types, matching by short name (`LocalTransform`) or full name (`Unity.Transforms.LocalTransform`).
- **Entity field reading** uses `EntityManager.Debug.GetComponentBoxed()` + reflection. Some components may show `<unreadable>` for unsupported field types.
- **Buffer component reading** uses reflection to call `EntityManager.GetBuffer<T>()` and reads up to 10 elements per buffer.
- **Shared component reading** uses `GetComponentBoxed()` + reflection for `ISharedComponentData` fields.
- **Enableable component state** checks `EntityManager.IsComponentEnabled()` for `IEnableableComponent` types.
- **System ordering** reads `[UpdateBefore]` and `[UpdateAfter]` attributes. `[UpdateInGroup]` determines group.
- **Archetype entity count** uses reflection fallback since `EntityArchetype` doesn't expose `EntityCount` publicly. Falls back to `ChunkCapacity * ChunkCount` as upper bound.
- **set_component** uses `EntityManager.Debug.GetComponentBoxed()` + `Convert.ChangeType()` + `EntityManager.Debug.SetComponentBoxed()` for runtime field modification. Supports numeric, bool, enum, and string fields.
- **add_component / remove_component** resolve types via `TypeManager` and use `EntityManager.AddComponent` / `RemoveComponent` by `ComponentType`.

---

## manage_dots_physics

**Purpose:** Unity DOTS Physics debugging — raycasts, overlap queries, collider listing, and rigid body inspection at runtime.

**Prerequisite:** `com.unity.physics` package installed in the Unity project. The C# handler is compiled only when `UNITY_PHYSICS` is defined.

### Files

| File | Role |
|------|------|
| `MCPForUnity/Editor/Tools/ManageDotsPhysics.cs` | C# tool handler (guarded by `#if UNITY_PHYSICS`) |
| `Server/src/services/tools/manage_dots_physics.py` | Python MCP tool (auto-registered) |
| `Server/src/cli/commands/dots_physics.py` | CLI commands (`unity-mcp dots-physics ...`) |
| `Server/tests/integration/test_manage_dots_physics.py` | 7 integration tests |

### Actions

| Action | Description | Key Params |
|--------|-------------|------------|
| `get_physics_world` | Physics world stats — body counts, dynamic bodies, joints | `world` |
| `raycast` | Cast a ray and get hit entities with position/normal/distance | `origin`, `direction`, `max_distance` |
| `overlap_aabb` | Find bodies inside an axis-aligned bounding box | `min`, `max`, `page_size` |
| `list_colliders` | List entities with PhysicsCollider components | `world`, `page_size` |
| `get_body` | Inspect a physics body — position, rotation, velocity, collider type | `body_index` |

All actions accept an optional `world` parameter.

### CLI Examples

```bash
unity-mcp dots-physics world                           # Physics world stats
unity-mcp dots-physics world --world "Server World"    # Specific world
unity-mcp dots-physics raycast "0,10,0" "0,-1,0"       # Cast ray downward
unity-mcp dots-physics raycast "0,10,0" "0,-1,0" --distance 50  # Max distance
unity-mcp dots-physics overlap "-5,-5,-5" "5,5,5"      # Find bodies in AABB
unity-mcp dots-physics overlap "0,0,0" "10,10,10" --page-size 50
unity-mcp dots-physics colliders                       # List all colliders
unity-mcp dots-physics colliders --page-size 100       # More results
unity-mcp dots-physics body 0                          # Inspect body 0
unity-mcp dots-physics body 5 --world "Server World"   # Specific world
```

### MCP Tool Examples

```python
manage_dots_physics(action="get_physics_world")
manage_dots_physics(action="raycast", origin="0,10,0", direction="0,-1,0", max_distance=50.0)
manage_dots_physics(action="overlap_aabb", min="-5,-5,-5", max="5,5,5", page_size=20)
manage_dots_physics(action="list_colliders", world="Server World")
manage_dots_physics(action="get_body", body_index=5)
```

### Implementation Notes

- **PhysicsWorldSingleton** accessed via `SystemAPI.GetSingleton<PhysicsWorldSingleton>()` through the default World.
- **Raycast** uses `CollisionWorld.CastRay()` with `RaycastInput` and `NativeList<RaycastHit>`. Returns position, normal, distance, body index per hit.
- **OverlapAabb** uses `CollisionWorld.OverlapAabb()` with `OverlapAabbInput` and `NativeList<int>` body indices.
- **Body inspection** reads `RigidBody` for static info (position, rotation, collider type) and `MotionVelocities`/`MotionDatas` for dynamic bodies (linear/angular velocity, inverse mass).
- **Float3 parsing** accepts `"x,y,z"` string format, e.g. `"0,10,0"` → `float3(0, 10, 0)`.

---

## manage_dots_graphics

**Purpose:** Unity DOTS Entities Graphics debugging — render stats, material/mesh inspection, per-entity rendering detail.

**Prerequisite:** `com.unity.entities.graphics` package installed in the Unity project. The C# handler is compiled only when `UNITY_ENTITIES_GRAPHICS` is defined (auto-defined via asmdef versionDefines).

### Files

| File | Role |
|------|------|
| `MCPForUnity/Editor/Tools/ManageDotsGraphics.cs` | C# tool handler (guarded by `#if UNITY_ENTITIES_GRAPHICS`) |
| `Server/src/services/tools/manage_dots_graphics.py` | Python MCP tool (auto-registered) |
| `Server/src/cli/commands/dots_graphics.py` | CLI commands (`unity-mcp dots-graphics ...`) |
| `Server/tests/integration/test_manage_dots_graphics.py` | 7 integration tests |

### Actions

| Action | Description | Key Params |
|--------|-------------|------------|
| `get_render_stats` | Count entities with rendering components (MaterialMeshInfo, RenderBounds, LOD) | `world` |
| `list_rendered_entities` | List entities with MaterialMeshInfo + RenderBounds | `page_size`, `world` |
| `get_entity_rendering` | Full entity rendering detail — material, mesh, bounds, filter settings | `entity_index`, `world` |
| `list_registered_materials` | Unique materials from RenderMeshArray shared components | `page_size`, `world` |
| `list_registered_meshes` | Unique meshes with vertex counts and bounds | `page_size`, `world` |

All actions accept an optional `world` parameter.

### CLI Examples

```bash
unity-mcp dots-graphics stats                        # Render entity counts
unity-mcp dots-graphics entities                     # List rendered entities
unity-mcp dots-graphics entities --page-size 50      # More results
unity-mcp dots-graphics entity 42                    # Entity render details
unity-mcp dots-graphics materials                    # List unique materials
unity-mcp dots-graphics meshes                       # List unique meshes
```

### MCP Tool Examples

```python
manage_dots_graphics(action="get_render_stats")
manage_dots_graphics(action="list_rendered_entities", page_size=50)
manage_dots_graphics(action="get_entity_rendering", entity_index=42)
manage_dots_graphics(action="list_registered_materials", world="Server World")
manage_dots_graphics(action="list_registered_meshes", page_size=30)
```

### Implementation Notes

- **MaterialMeshInfo** is the primary rendering component (IComponentData). Contains `MaterialID` and `MeshID` for Burst-compatible mesh/material switching.
- **RenderMeshArray** is a shared component containing arrays of meshes and materials. Materials are iterated to collect unique entries.
- **RenderFilterSettings** is a shared component with layer, shadow casting mode, and receive shadows flags.
- **WorldRenderBounds** gives the world-space AABB for culling.
- **LODGroupWorldReferencePoint** counted for LOD group statistics.

---

## manage_dots_subscene

**Purpose:** Unity DOTS SubScene management — discover, load, unload, and inspect streaming state of SubScenes at runtime.

**Prerequisite:** `com.unity.entities` package installed in the Unity project. The C# handler is compiled only when `UNITY_ENTITIES` is defined. SubScene/SceneSystem is part of `Unity.Scenes` namespace.

### Files

| File | Role |
|------|------|
| `MCPForUnity/Editor/Tools/ManageDotsSubscene.cs` | C# tool handler (guarded by `#if UNITY_ENTITIES`) |
| `Server/src/services/tools/manage_dots_subscene.py` | Python MCP tool (auto-registered) |
| `Server/src/cli/commands/dots_subscene.py` | CLI commands (`unity-mcp dots-subscene ...`) |
| `Server/tests/integration/test_manage_dots_subscene.py` | 7 integration tests |

### Actions

| Action | Description | Key Params |
|--------|-------------|------------|
| `list_subscenes` | Find all SubScene components in the hierarchy with auto-load and loaded status | — |
| `load_subscene` | Request async loading of a SubScene (adds RequestSceneLoaded) | `scene_name` |
| `unload_subscene` | Unload a SubScene and destroy its meta entities | `scene_name` |
| `get_subscene_status` | Detailed status — streaming state, section count, asset path | `scene_name` |
| `list_sections` | List individual scene sections with their streaming state | `scene_name` |

### CLI Examples

```bash
unity-mcp dots-subscene list                         # List all SubScenes
unity-mcp dots-subscene load "Environment"           # Load SubScene
unity-mcp dots-subscene unload "Environment"         # Unload SubScene
unity-mcp dots-subscene status "Environment"         # Streaming status
unity-mcp dots-subscene sections "Environment"       # List sections
```

### MCP Tool Examples

```python
manage_dots_subscene(action="list_subscenes")
manage_dots_subscene(action="load_subscene", scene_name="Environment")
manage_dots_subscene(action="unload_subscene", scene_name="Enemies")
manage_dots_subscene(action="get_subscene_status", scene_name="Environment")
manage_dots_subscene(action="list_sections", scene_name="Level01")
```

### Implementation Notes

- **SubScene discovery** uses `FindObjectsByType<SubScene>()` to find all SubScene MonoBehaviours in the loaded scene hierarchy.
- **Scene loading** activates the SubScene GameObject and adds `RequestSceneLoaded` component to the scene entity.
- **Scene unloading** uses `SceneSystem.UnloadScene()` with `DestroyMetaEntities` parameter.
- **Section inspection** reads `ResolvedSectionEntity` buffer from the scene entity and checks `RequestSceneLoaded` presence on each section entity.
- **Streaming state** uses `SceneSystem.GetSceneStreamingState()` and `GetSectionStreamingState()` for detailed status.

---

## manage_mesh

**Purpose:** Inspect and modify Unity Mesh data on GameObjects — vertex attributes, colors, geometry sampling.

### Files

| File | Role |
|------|------|
| `MCPForUnity/Editor/Tools/ManageMesh.cs` | C# tool handler |
| `Server/src/services/tools/manage_mesh.py` | Python MCP tool (auto-registered) |

### Actions

| Action | Description | Key Params |
|--------|-------------|------------|
| `inspect` | All-in-one: info + attributes + color samples (use this first) | `target`, `count` |
| `get_info` | Vertex/triangle count, bounds, index format, submesh count, isReadable | `target` |
| `get_attributes` | List VertexAttributeDescriptor for each attribute | `target` |
| `has_attribute` | Check if mesh has a specific attribute (Position/Normal/Color/TexCoord0/Tangent) | `target`, `attribute` |
| `sample_colors` | Sample vertex colors evenly spaced across mesh | `target`, `count`, `offset` |
| `sample_vertices` | Sample vertex positions evenly spaced across mesh | `target`, `count`, `offset` |
| `set_colors` | Set all vertex colors to a solid RGBA color | `target`, `color` (r,g,b,a 0-1) |
| `force_upload` | Call mesh.UploadMeshData(false) to upload pending changes | `target` |

Target is a GameObject name or instance ID; mesh is read from its `MeshFilter.sharedMesh`.

### MCP Tool Examples

```python
manage_mesh(action="inspect", target="MyCube", count=10)
manage_mesh(action="get_info", target="MyCube")
manage_mesh(action="has_attribute", target="MyCube", attribute="Color")
manage_mesh(action="set_colors", target="MyCube", color="1,0,0,1")
manage_mesh(action="sample_vertices", target="MyCube", count=20, offset=0)
```

---

## manage_terrain

**Purpose:** Unity Terrain inspection and modification — heightmap sampling, brush painting, texture layer queries. Works in Edit or Play mode.

### Files

| File | Role |
|------|------|
| `MCPForUnity/Editor/Tools/ManageTerrain.cs` | C# tool handler |
| `Server/src/services/tools/manage_terrain.py` | Python MCP tool (auto-registered) |

### Actions

| Action | Description | Key Params |
|--------|-------------|------------|
| `get_info` | Heightmap resolution, size, layer/tree counts | `target` |
| `get_height` | Sample world-space height at x/z | `x`, `z` |
| `set_heights` | Paint circular brush with set/raise/lower/smooth modes | `x`, `z`, `radius`, `height`, `mode` |
| `flatten` | Set entire heightmap to uniform normalized height | `height` |
| `get_splat_weights` | Texture layer weights at world position | `x`, `z` |
| `paint_texture` | Paint terrain texture layer with circular brush | `x`, `z`, `radius`, `layer_index`, `strength` |
| `get_heightmap_sample` | Read NxN heightmap patch around world position | `x`, `z`, `size` (max 64) |

All actions accept optional `target` param (GameObject name or instance ID); defaults to active terrain.

### MCP Tool Examples

```python
manage_terrain(action="get_info")
manage_terrain(action="get_height", x=10.0, z=20.0)
manage_terrain(action="set_heights", x=10.0, z=20.0, radius=5.0, height=0.5, mode="set")
manage_terrain(action="flatten", height=0.3)
manage_terrain(action="get_splat_weights", x=10.0, z=20.0)
manage_terrain(action="paint_texture", x=10.0, z=20.0, radius=3.0, layer_index=1, strength=0.8)
manage_terrain(action="get_heightmap_sample", x=10.0, z=20.0, size=16)
```

---

## manage_probuilder

**Purpose:** ProBuilder mesh creation and editing — spawn shapes, extrude, paint, subdivide, merge.

**Prerequisite:** `com.unity.probuilder` package installed (defines `PROBUILDER` scripting symbol).

### Files

| File | Role |
|------|------|
| `MCPForUnity/Editor/Tools/ManageProBuilder.cs` | C# tool handler (guarded by `#if PROBUILDER`) |
| `Server/src/services/tools/manage_probuilder.py` | Python MCP tool (auto-registered) |

### Actions

| Action | Description | Key Params |
|--------|-------------|------------|
| `create_shape` | Spawn cube/cylinder/sphere/plane/stair/arch/prism/torus | `shape`, `size` (x,y,z), `position` (x,y,z) |
| `get_info` | Face/vertex/edge counts and properties | `target` |
| `extrude` | Push faces outward by distance | `target`, `face_indices`, `distance` |
| `set_vertex_colors` | Paint faces with RGBA color | `target`, `face_indices`, `color` |
| `subdivide` | Tessellate faces for more geometry | `target`, `face_indices` |
| `merge` | Combine multiple ProBuilder meshes into one | `targets` (comma-separated) |
| `to_mesh` | Finalize and optimize the mesh | `target` |

### MCP Tool Examples

```python
manage_probuilder(action="create_shape", shape="cube", size="2,1,2", position="0,0,0")
manage_probuilder(action="get_info", target="MyCube")
manage_probuilder(action="extrude", target="MyCube", face_indices="0,1", distance=0.5)
manage_probuilder(action="set_vertex_colors", target="MyCube", color="1,0,0,1")
manage_probuilder(action="merge", targets="Cube1,Cube2")
manage_probuilder(action="to_mesh", target="MyCube")
```

---

## manage_shader_tool

**Purpose:** Shader inspection, error checking, and reimport operations. Named `manage_shader_tool` to avoid conflict with upstream `manage_shader` (CRUD).

### Files

| File | Role |
|------|------|
| `MCPForUnity/Editor/Tools/ManageShaderTool.cs` | C# tool handler |
| `Server/src/services/tools/manage_shader_tool.py` | Python MCP tool (auto-registered) |

### Actions

| Action | Description | Key Params |
|--------|-------------|------------|
| `reimport` | Force reimport a shader asset by path | `path` |
| `get_errors` | List compiler errors/warnings with file and line info | `path` or `name` |
| `get_info` | Shader name, isSupported, pass count, subshader count, render queue | `path` or `name` |
| `get_passes` | List all passes with names and enabled state per subshader | `path` or `name` |
| `find` | Locate shader by name, returns asset path and info | `name` |
| `is_compiling` | Check if any shaders are currently compiling | — |

Use `path` for asset-path-based lookup, or `name` for shader declaration name (e.g. `Universal Render Pipeline/Lit`).

### MCP Tool Examples

```python
manage_shader_tool(action="find", name="Universal Render Pipeline/Lit")
manage_shader_tool(action="get_info", path="Assets/Shaders/MyShader.shader")
manage_shader_tool(action="get_errors", name="Custom/MyShader")
manage_shader_tool(action="get_passes", path="Assets/Shaders/MyShader.shader")
manage_shader_tool(action="reimport", path="Assets/Shaders/MyShader.shader")
manage_shader_tool(action="is_compiling")
```

---

## rendering_stats

**Purpose:** Read Unity rendering and performance statistics — draw calls, batches, FPS, memory, profiler data.

### Files

| File | Role |
|------|------|
| `MCPForUnity/Editor/Tools/RenderingStats.cs` | C# tool handler |
| `Server/src/services/tools/rendering_stats.py` | Python MCP tool (auto-registered) |

### Actions

| Action | Description | Key Params |
|--------|-------------|------------|
| `get_stats` | Draw calls, batches, triangles, vertices, FPS, setPassCalls, shadowCasters, texture memory, cpuMainMs and renderThreadMs (via FrameTimingManager) | — |
| `get_memory` | Total allocated/reserved, mono heap, graphics driver memory | — |
| `get_profiler` | Frame timing, time scale, system info (GPU/CPU) | — |

Most stats require Play mode with Game view visible.

### MCP Tool Examples

```python
rendering_stats(action="get_stats")
rendering_stats(action="get_memory")
rendering_stats(action="get_profiler")
```

---

## validation_snapshot

**Purpose:** Aggregated runtime validation in ONE call — replaces 15-20 individual MCP calls with 1-2 calls (capture + compare).

**Prerequisite:** `com.unity.entities` package + DOTSRPG components in the Unity project. Play mode required for entity data.

### Files

| File | Role |
|------|------|
| `MCPForUnity/Editor/Tools/ValidationSnapshot.cs` | C# tool handler |
| `Server/src/services/tools/validation_snapshot.py` | Python MCP tool (auto-registered) |

### Actions

| Action | Description | Key Params |
|--------|-------------|------------|
| `capture` | Entity counts (total/alive/dead/by-team), health distribution (min/max/mean), position samples, NaN bounds check, rendering stats (FPS/draw calls/batches), battle state, console errors, editor state | `sample_size` (default 20, max 100) |
| `compare` | Diff two snapshots — detect movement, anomalies, deltas | `snapshot_a`, `snapshot_b` |

### MCP Tool Examples

```python
validation_snapshot(action="capture")
validation_snapshot(action="capture", sample_size=50)
validation_snapshot(action="compare", snapshot_a=prev_snap, snapshot_b=curr_snap)
```

---

## Phase 1: Core Physics & Sensory Tools

### manage_physics

**Purpose:** 3D physics operations — raycasting, rigidbody/collider inspection, physics settings.

| Action | Description |
|--------|-------------|
| `raycast` | Single 3D raycast |
| `raycast_all` | All 3D hits along ray |
| `overlap_sphere` | Entities in sphere |
| `overlap_box` | Entities in box |
| `list_rigidbodies` | All Rigidbody components |
| `get_rigidbody` | Body type, mass, velocity |
| `set_rigidbody` | Modify rigidbody properties |
| `list_colliders` | All Collider components |
| `get_physics_settings` | Gravity, collision matrix |
| `set_physics_settings` | Modify gravity/settings |

Files: `ManagePhysics.cs`, `manage_physics.py`, `test_manage_physics.py` (10 tests)

### manage_audio

**Purpose:** Audio sources, clips, and mixer control.

| Action | Description |
|--------|-------------|
| `list_sources` | All AudioSource components |
| `get_source` | Clip, volume, spatial settings |
| `set_source` | Modify source properties |
| `play` / `stop` / `pause` | Playback control |
| `list_clips` | Audio clips in project |
| `get_clip_info` | Clip metadata |
| `list_mixers` / `get_mixer` / `set_mixer_param` | AudioMixer control |

Files: `ManageAudio.cs`, `manage_audio.py`, `test_manage_audio.py` (10 tests)

### manage_lighting

**Purpose:** Light, probes, baking, and environment settings.

| Action | Description |
|--------|-------------|
| `list_lights` / `get_light` / `set_light` | Light CRUD |
| `bake` / `cancel_bake` / `get_bake_status` | Lightmap baking |
| `list_probes` / `get_probe` | Light/reflection probes |
| `get_environment` / `set_environment` | RenderSettings |
| `get_lightmap_settings` | Lightmap configuration |

Files: `ManageLighting.cs`, `manage_lighting.py`, `test_manage_lighting.py` (11 tests)

### manage_camera

**Purpose:** Camera inspection, control, and coordinate conversion.

| Action | Description |
|--------|-------------|
| `list_cameras` / `get_camera` / `set_camera` | Camera CRUD |
| `render_to_file` | Capture screenshot |
| `world_to_screen` / `screen_to_ray` | Coordinate conversion |
| `get_main_camera` | Quick access to Camera.main |

Files: `ManageCamera.cs`, `manage_camera.py`, `test_manage_camera.py` (9 tests)

---

## Phase 2: Editor Automation Tools

### manage_build

**Purpose:** Player settings, quality settings, build pipeline, scripting defines.

| Action | Description |
|--------|-------------|
| `get_player_settings` / `set_player_settings` | Bundle ID, version, etc. |
| `get_quality_settings` / `set_quality_level` | Quality levels |
| `get_build_settings` / `set_build_scenes` | Build scene list |
| `build` | Trigger a build |
| `get_scripting_defines` / `set_scripting_defines` | Scripting symbols |

Files: `ManageBuild.cs`, `manage_build.py`, `test_manage_build.py` (9 tests)

### manage_packages

**Purpose:** Unity Package Manager operations.

| Action | Description |
|--------|-------------|
| `list` | Installed packages |
| `get_info` | Package details |
| `add` / `remove` | Install/uninstall |
| `search` | Search registry |

Files: `ManagePackages.cs`, `manage_packages.py`, `test_manage_packages.py` (7 tests)

### manage_navigation

**Purpose:** AI Navigation — NavMesh surfaces, agents, obstacles, pathfinding.

**Prerequisite:** `com.unity.ai.navigation` (`#if UNITY_AI_NAVIGATION`)

| Action | Description |
|--------|-------------|
| `list_surfaces` / `bake` / `clear` | NavMesh management |
| `list_agents` / `get_agent` / `set_agent_destination` | NavMeshAgent control |
| `list_obstacles` | NavMeshObstacle listing |
| `sample_position` / `calculate_path` | Pathfinding queries |

Files: `ManageNavigation.cs`, `manage_navigation.py`, `test_manage_navigation.py` (9 tests)

### manage_render_pipeline

**Purpose:** SRP pipeline info, Volume/post-processing, renderer features.

| Action | Description |
|--------|-------------|
| `get_pipeline_info` | Active pipeline type |
| `list_volumes` / `get_volume` | Volume components |
| `set_volume_override` / `toggle_volume_override` | Volume control |
| `list_renderer_features` | URP renderer features |
| `get_render_pipeline_asset` | Pipeline asset details |
| `list_post_processing` | Post-processing overrides |

Files: `ManageRenderPipeline.cs`, `manage_render_pipeline.py`, `test_manage_render_pipeline.py` (9 tests)

---

## Phase 3: Popular Package Tools

### manage_timeline

**Purpose:** Timeline/PlayableDirector control.

**Prerequisite:** `com.unity.timeline` (`#if UNITY_TIMELINE`)

| Action | Description |
|--------|-------------|
| `list_directors` / `get_director` | PlayableDirector inspection |
| `play` / `pause` / `stop` / `set_time` | Playback control |
| `list_tracks` / `get_bindings` | Track and binding inspection |

Files: `ManageTimeline.cs`, `manage_timeline.py`, `test_manage_timeline.py` (8 tests)

### manage_input_system

**Purpose:** New Input System — action assets, devices, PlayerInput.

**Prerequisite:** `com.unity.inputsystem` (`#if UNITY_INPUT_SYSTEM`)

| Action | Description |
|--------|-------------|
| `list_action_assets` / `get_action_map` / `get_action` | Input action inspection |
| `list_devices` / `get_device` | Connected devices |
| `list_player_inputs` | PlayerInput components |

Files: `ManageInputSystem.cs`, `manage_input_system.py`, `test_manage_input_system.py` (8 tests)

### manage_cinemachine

**Purpose:** Cinemachine v3 cameras, brain, blending.

**Prerequisite:** `com.unity.cinemachine` (`#if UNITY_CINEMACHINE`)

| Action | Description |
|--------|-------------|
| `list_vcams` / `get_vcam` / `set_vcam` | CinemachineCamera CRUD |
| `get_brain` | CinemachineBrain state |
| `set_priority` | Camera priority control |
| `list_blends` | Custom blend definitions |

Files: `ManageCinemachine.cs`, `manage_cinemachine.py`, `test_manage_cinemachine.py` (8 tests)

### manage_physics2d

**Purpose:** 2D physics — raycasting, overlap queries, Rigidbody2D, Collider2D.

| Action | Description |
|--------|-------------|
| `raycast` / `raycast_all` | 2D raycasting |
| `overlap_circle` / `overlap_box` | 2D overlap queries |
| `list_rigidbodies` / `get_rigidbody` | Rigidbody2D inspection |
| `list_colliders` | Collider2D listing |
| `get_physics2d_settings` | Physics2D global settings |

Files: `ManagePhysics2D.cs`, `manage_physics2d.py`, `test_manage_physics2d.py` (10 tests)

---

## Phase 4: 2D & Content Tools

### manage_tilemap

**Purpose:** 2D Tilemap — inspect, place/remove tiles, fill areas.

**Prerequisite:** `com.unity.2d.tilemap` (`#if UNITY_TILEMAP`)

| Action | Description |
|--------|-------------|
| `list_tilemaps` / `get_info` / `get_bounds` | Tilemap inspection |
| `get_tile` / `set_tile` / `clear_tile` / `clear_all` | Tile operations |
| `fill_area` | Fill rectangle with tile |

Files: `ManageTilemap.cs`, `manage_tilemap.py`, `test_manage_tilemap.py` (8 tests)

### manage_addressables

**Purpose:** Addressable asset system — groups, entries, labels, builds.

**Prerequisite:** `com.unity.addressables` (`#if UNITY_ADDRESSABLES`)

| Action | Description |
|--------|-------------|
| `list_groups` / `get_group` | Group inspection |
| `list_entries` / `get_entry` | Entry detail |
| `list_labels` | Label listing |
| `build` / `analyze` | Content build and analysis |

Files: `ManageAddressables.cs`, `manage_addressables.py`, `test_manage_addressables.py` (7 tests)

### manage_splines

**Purpose:** Unity Splines — knot inspection, editing, evaluation.

**Prerequisite:** `com.unity.splines` (`#if UNITY_SPLINES`)

| Action | Description |
|--------|-------------|
| `list_splines` / `get_spline` | SplineContainer inspection |
| `get_knot` / `add_knot` / `remove_knot` / `set_knot` | Knot CRUD |
| `evaluate` | Position/tangent at t (0-1) |

Files: `ManageSplines.cs`, `manage_splines.py`, `test_manage_splines.py` (7 tests)

### manage_video

**Purpose:** VideoPlayer — inspection and playback control.

| Action | Description |
|--------|-------------|
| `list_players` / `get_player` / `set_player` | VideoPlayer CRUD |
| `play` / `pause` / `stop` / `set_time` | Playback control |

Files: `ManageVideo.cs`, `manage_video.py`, `test_manage_video.py` (7 tests)

---

## Phase 5: Specialized Tools

### manage_ui_toolkit

**Purpose:** UI Toolkit (UIElements) — UIDocument, VisualElement queries, style.

| Action | Description |
|--------|-------------|
| `list_documents` / `get_document` | UIDocument inspection |
| `query_elements` / `get_element` | USS selector queries |
| `set_style` | Inline style modification |
| `list_uxml_assets` | UXML asset search |

Files: `ManageUIToolkit.cs`, `manage_ui_toolkit.py`, `test_manage_ui_toolkit.py` (6 tests)

### manage_localization

**Purpose:** Unity Localization — locales, string tables, entries.

**Prerequisite:** `com.unity.localization` (`#if UNITY_LOCALIZATION`)

| Action | Description |
|--------|-------------|
| `list_locales` / `get_active_locale` / `set_active_locale` | Locale management |
| `list_tables` | String/asset table collections |
| `get_entry` / `set_entry` | Localized string access |

Files: `ManageLocalization.cs`, `manage_localization.py`, `test_manage_localization.py` (6 tests)

### manage_netcode

**Purpose:** Netcode for GameObjects — network manager, objects, connection.

**Prerequisite:** `com.unity.netcode.gameobjects` (`#if UNITY_NETCODE`)

| Action | Description |
|--------|-------------|
| `get_network_manager` | Transport, connection state, clients |
| `list_network_objects` / `get_network_object` | NetworkObject inspection |
| `start_host` / `start_server` / `start_client` / `shutdown` | Network lifecycle |

Files: `ManageNetcode.cs`, `manage_netcode.py`, `test_manage_netcode.py` (7 tests)

### manage_profiler

**Purpose:** Deep profiler API — counters, recording, memory snapshots.

| Action | Description |
|--------|-------------|
| `get_counters` | Read named profiler counters |
| `list_categories` | Known ProfilerCategory names |
| `start_recording` / `stop_recording` | Profiler recording to file |
| `get_frame_data` | Frame timing data |
| `get_memory_snapshot` | Detailed memory breakdown |

Files: `ManageProfiler.cs`, `manage_profiler.py`, `test_manage_profiler.py` (6 tests)

### manage_behavior

**Purpose:** Unity Behavior (AI) — graph agents, blackboard variables.

**Prerequisite:** `com.unity.behavior` (`#if UNITY_BEHAVIOR`)

| Action | Description |
|--------|-------------|
| `list_agents` / `get_agent` | BehaviorGraphAgent inspection |
| `list_variables` / `get_variable` / `set_variable` | Blackboard access |

Files: `ManageBehavior.cs`, `manage_behavior.py`, `test_manage_behavior.py` (5 tests)
