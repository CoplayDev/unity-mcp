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
| `Server/tests/integration/test_manage_dots.py` | 13 integration tests |

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
```

### Implementation Notes

- **Component type resolution** iterates `TypeManager` registered types, matching by short name (`LocalTransform`) or full name (`Unity.Transforms.LocalTransform`).
- **Entity field reading** uses `EntityManager.Debug.GetComponentBoxed()` + reflection. Some components may show `<unreadable>` for unsupported field types.
- **Buffer component reading** uses reflection to call `EntityManager.GetBuffer<T>()` and reads up to 10 elements per buffer.
- **Shared component reading** uses `GetComponentBoxed()` + reflection for `ISharedComponentData` fields.
- **Enableable component state** checks `EntityManager.IsComponentEnabled()` for `IEnableableComponent` types.
- **System ordering** reads `[UpdateBefore]` and `[UpdateAfter]` attributes. `[UpdateInGroup]` determines group.
- **Archetype entity count** uses reflection fallback since `EntityArchetype` doesn't expose `EntityCount` publicly. Falls back to `ChunkCapacity * ChunkCount` as upper bound.
