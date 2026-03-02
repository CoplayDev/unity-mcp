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
