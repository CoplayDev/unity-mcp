#if UNITY_ENTITIES
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using Unity.Collections;
using Unity.Entities;

namespace MCPForUnity.Editor.Tools
{
    /// <summary>
    /// MCP tool for Unity DOTS ECS debugging, inspection, and performance monitoring.
    /// Actions: list_worlds, query_entities, get_entity, list_systems, get_system,
    ///          performance_snapshot, toggle_system, list_component_types,
    ///          create_entity, destroy_entity, set_component,
    ///          add_component, remove_component, query_count
    /// Requires com.unity.entities package.
    /// </summary>
    [McpForUnityTool("manage_dots", AutoRegister = false)]
    public static class ManageDots
    {
        public static object HandleCommand(JObject @params)
        {
            if (@params == null)
                return new ErrorResponse("Parameters cannot be null.");

            var p = new ToolParams(@params);

            var actionResult = p.GetRequired("action");
            if (!actionResult.IsSuccess)
                return new ErrorResponse(actionResult.ErrorMessage);

            string action = actionResult.Value.ToLowerInvariant();

            try
            {
                return action switch
                {
                    "list_worlds"           => ListWorlds(p),
                    "query_entities"        => QueryEntities(p),
                    "get_entity"            => GetEntity(p),
                    "list_systems"          => ListSystems(p),
                    "get_system"            => GetSystem(p),
                    "performance_snapshot"  => PerformanceSnapshot(p),
                    "toggle_system"         => ToggleSystem(p),
                    "list_component_types"  => ListComponentTypes(p),
                    "create_entity"         => CreateEntity(p),
                    "destroy_entity"        => DestroyEntity(p),
                    "set_component"         => SetComponent(p),
                    "add_component"         => AddComponent(p),
                    "remove_component"      => RemoveComponent(p),
                    "query_count"           => QueryCount(p),
                    _ => new ErrorResponse(
                        $"Unknown action: '{action}'. Supported: list_worlds, query_entities, get_entity, " +
                        "list_systems, get_system, performance_snapshot, toggle_system, " +
                        "list_component_types, create_entity, destroy_entity, " +
                        "set_component, add_component, remove_component, query_count")
                };
            }
            catch (Exception e)
            {
                McpLog.Error($"[ManageDots] Action '{action}' failed: {e}");
                return new ErrorResponse($"Internal error processing action '{action}': {e.Message}");
            }
        }

        #region World Operations

        private static object ListWorlds(ToolParams p)
        {
            var worlds = new List<object>();
            foreach (var world in World.All)
            {
                worlds.Add(new Dictionary<string, object>
                {
                    ["name"]         = world.Name,
                    ["is_created"]   = world.IsCreated,
                    ["system_count"] = world.Systems.Count,
                    ["entity_count"] = world.EntityManager.UniversalQuery.CalculateEntityCount(),
                    ["flags"]        = world.Flags.ToString()
                });
            }
            return new SuccessResponse($"Found {worlds.Count} world(s).", worlds);
        }

        #endregion

        #region Entity Operations

        private static object QueryEntities(ToolParams p)
        {
            var world = ResolveWorld(p);
            if (world == null)
                return new ErrorResponse("World not found. Use list_worlds to see available worlds.");

            string componentTypesStr = p.Get("component_types");
            if (string.IsNullOrEmpty(componentTypesStr))
                return new ErrorResponse("'component_types' parameter is required. Comma-separated component type names.");

            string[] typeNames = componentTypesStr.Split(',')
                .Select(t => t.Trim())
                .Where(t => !string.IsNullOrEmpty(t))
                .ToArray();

            var componentTypes = new List<ComponentType>();
            foreach (string typeName in typeNames)
            {
                var resolvedType = ResolveComponentType(typeName);
                if (resolvedType == null)
                    return new ErrorResponse($"Component type '{typeName}' not found. Check spelling or ensure the assembly is loaded.");
                componentTypes.Add(resolvedType.Value);
            }

            var em = world.EntityManager;
            using var query = em.CreateEntityQuery(componentTypes.ToArray());
            int totalCount = query.CalculateEntityCount();

            int pageSize = p.GetInt("page_size") ?? 20;
            pageSize = Math.Clamp(pageSize, 1, 100);

            var entities = query.ToEntityArray(Allocator.Temp);
            int sampleCount = Math.Min(pageSize, entities.Length);

            var samples = new List<object>();
            for (int i = 0; i < sampleCount; i++)
            {
                samples.Add(SerializeEntityBrief(em, entities[i]));
            }
            entities.Dispose();

            return new SuccessResponse($"Found {totalCount} entities matching [{string.Join(", ", typeNames)}].", new Dictionary<string, object>
            {
                ["total_count"]     = totalCount,
                ["page_size"]       = pageSize,
                ["component_types"] = typeNames,
                ["entities"]        = samples
            });
        }

        private static object GetEntity(ToolParams p)
        {
            var world = ResolveWorld(p);
            if (world == null)
                return new ErrorResponse("World not found. Use list_worlds to see available worlds.");

            int? entityIndex = p.GetInt("entity_index");
            int? entityVersion = p.GetInt("entity_version");
            if (entityIndex == null)
                return new ErrorResponse("'entity_index' parameter is required.");

            var entity = new Entity { Index = entityIndex.Value, Version = entityVersion ?? 1 };
            var em = world.EntityManager;

            if (!em.Exists(entity))
                return new ErrorResponse($"Entity (Index={entityIndex}, Version={entityVersion ?? 1}) does not exist.");

            return new SuccessResponse($"Entity {entity} details.", SerializeEntityFull(em, entity));
        }

        #endregion

        #region System Operations

        private static object ListSystems(ToolParams p)
        {
            var world = ResolveWorld(p);
            if (world == null)
                return new ErrorResponse("World not found. Use list_worlds to see available worlds.");

            string groupFilter = p.Get("group");
            var systems = new List<object>();

            foreach (var sys in world.Systems)
            {
                var sysType = sys.GetType();

                string groupName = GetSystemGroupName(sysType);
                if (!string.IsNullOrEmpty(groupFilter) &&
                    !groupName.Contains(groupFilter, StringComparison.OrdinalIgnoreCase))
                    continue;

                systems.Add(new Dictionary<string, object>
                {
                    ["name"]    = sysType.Name,
                    ["type"]    = sysType.FullName,
                    ["group"]   = groupName,
                    ["enabled"] = sys.Enabled
                });
            }

            return new SuccessResponse($"Found {systems.Count} system(s) in '{world.Name}'.", systems);
        }

        private static object GetSystem(ToolParams p)
        {
            var world = ResolveWorld(p);
            if (world == null)
                return new ErrorResponse("World not found. Use list_worlds to see available worlds.");

            string systemName = p.Get("system_name");
            if (string.IsNullOrEmpty(systemName))
                return new ErrorResponse("'system_name' parameter is required.");

            ComponentSystemBase system = FindSystem(world, systemName);
            if (system == null)
                return new ErrorResponse($"System '{systemName}' not found in world '{world.Name}'.");

            var sysType = system.GetType();
            var queries = new List<object>();
            foreach (var q in system.EntityQueries)
            {
                queries.Add(new Dictionary<string, object>
                {
                    ["entity_count"]    = q.CalculateEntityCount(),
                    ["component_types"] = GetQueryComponentTypeNames(q)
                });
            }

            // Collect ordering attributes
            var updateBefore = sysType.GetCustomAttributes(typeof(UpdateBeforeAttribute), true)
                .Cast<UpdateBeforeAttribute>()
                .Select(a => a.SystemType.Name)
                .ToList();
            var updateAfter = sysType.GetCustomAttributes(typeof(UpdateAfterAttribute), true)
                .Cast<UpdateAfterAttribute>()
                .Select(a => a.SystemType.Name)
                .ToList();

            bool isGroup = typeof(ComponentSystemGroup).IsAssignableFrom(sysType);

            return new SuccessResponse($"System '{systemName}' details.", new Dictionary<string, object>
            {
                ["name"]           = sysType.Name,
                ["full_name"]      = sysType.FullName,
                ["group"]          = GetSystemGroupName(sysType),
                ["enabled"]        = system.Enabled,
                ["is_group"]       = isGroup,
                ["update_before"]  = updateBefore,
                ["update_after"]   = updateAfter,
                ["query_count"]    = system.EntityQueries.Length,
                ["queries"]        = queries
            });
        }

        private static object ToggleSystem(ToolParams p)
        {
            var world = ResolveWorld(p);
            if (world == null)
                return new ErrorResponse("World not found. Use list_worlds to see available worlds.");

            string systemName = p.Get("system_name");
            if (string.IsNullOrEmpty(systemName))
                return new ErrorResponse("'system_name' parameter is required.");

            bool? enabled = p.GetBool("enabled");
            if (enabled == null)
                return new ErrorResponse("'enabled' parameter is required (true/false).");

            ComponentSystemBase system = FindSystem(world, systemName);
            if (system == null)
                return new ErrorResponse($"System '{systemName}' not found in world '{world.Name}'.");

            system.Enabled = enabled.Value;
            return new SuccessResponse(
                $"System '{systemName}' {(enabled.Value ? "enabled" : "disabled")} in world '{world.Name}'.");
        }

        #endregion

        #region Component Type Discovery

        private static object ListComponentTypes(ToolParams p)
        {
            string filter = p.Get("filter");
            string categoryFilter = p.Get("category"); // ComponentData, BufferData, SharedComponentData, etc.
            int pageSize = p.GetInt("page_size") ?? 50;
            pageSize = Math.Clamp(pageSize, 1, 200);

            int typeCount = TypeManager.GetTypeCount();
            var types = new List<object>();

            for (int i = 1; i < typeCount; i++)
            {
                var typeInfo = TypeManager.GetTypeInfo(i);
                string debugName = typeInfo.DebugTypeName.ToString();

                if (string.IsNullOrEmpty(debugName) || debugName == "null")
                    continue;

                // Apply name filter
                if (!string.IsNullOrEmpty(filter) &&
                    !debugName.Contains(filter, StringComparison.OrdinalIgnoreCase))
                    continue;

                // Apply category filter
                string category = typeInfo.Category.ToString();
                if (!string.IsNullOrEmpty(categoryFilter) &&
                    !category.Contains(categoryFilter, StringComparison.OrdinalIgnoreCase))
                    continue;

                types.Add(new Dictionary<string, object>
                {
                    ["name"]          = debugName,
                    ["category"]      = category,
                    ["type_index"]    = i,
                    ["size_bytes"]    = typeInfo.SizeInChunk,
                    ["is_zero_sized"] = typeInfo.IsZeroSized,
                    ["is_buffer"]     = typeInfo.Category == TypeManager.TypeCategory.BufferData,
                    ["is_shared"]     = typeInfo.Category == TypeManager.TypeCategory.ISharedComponentData,
                    ["is_enableable"] = typeInfo.EnableableType
                });

                if (types.Count >= pageSize)
                    break;
            }

            return new SuccessResponse(
                $"Found {types.Count} component type(s) (of {typeCount - 1} total).", new Dictionary<string, object>
                {
                    ["total_registered"] = typeCount - 1,
                    ["returned"]         = types.Count,
                    ["types"]            = types
                });
        }

        #endregion

        #region Entity CRUD

        private static object CreateEntity(ToolParams p)
        {
            var world = ResolveWorld(p);
            if (world == null)
                return new ErrorResponse("World not found. Use list_worlds to see available worlds.");

            string componentTypesStr = p.Get("component_types");
            var em = world.EntityManager;
            Entity entity;

            if (!string.IsNullOrEmpty(componentTypesStr))
            {
                string[] typeNames = componentTypesStr.Split(',')
                    .Select(t => t.Trim())
                    .Where(t => !string.IsNullOrEmpty(t))
                    .ToArray();

                var componentTypes = new List<ComponentType>();
                foreach (string typeName in typeNames)
                {
                    var resolvedType = ResolveComponentType(typeName);
                    if (resolvedType == null)
                        return new ErrorResponse($"Component type '{typeName}' not found.");
                    componentTypes.Add(resolvedType.Value);
                }

                var archetype = em.CreateArchetype(componentTypes.ToArray());
                entity = em.CreateEntity(archetype);
            }
            else
            {
                entity = em.CreateEntity();
            }

            return new SuccessResponse(
                $"Created entity (Index={entity.Index}, Version={entity.Version}) in world '{world.Name}'.",
                SerializeEntityBrief(em, entity));
        }

        private static object DestroyEntity(ToolParams p)
        {
            var world = ResolveWorld(p);
            if (world == null)
                return new ErrorResponse("World not found. Use list_worlds to see available worlds.");

            int? entityIndex = p.GetInt("entity_index");
            int? entityVersion = p.GetInt("entity_version");
            if (entityIndex == null)
                return new ErrorResponse("'entity_index' parameter is required.");

            var entity = new Entity { Index = entityIndex.Value, Version = entityVersion ?? 1 };
            var em = world.EntityManager;

            if (!em.Exists(entity))
                return new ErrorResponse($"Entity (Index={entityIndex}, Version={entityVersion ?? 1}) does not exist.");

            em.DestroyEntity(entity);
            return new SuccessResponse($"Destroyed entity (Index={entityIndex}, Version={entityVersion ?? 1}) in world '{world.Name}'.");
        }

        private static object SetComponent(ToolParams p)
        {
            var world = ResolveWorld(p);
            if (world == null)
                return new ErrorResponse("World not found.");

            int? entityIndex = p.GetInt("entity_index");
            int? entityVersion = p.GetInt("entity_version");
            if (entityIndex == null)
                return new ErrorResponse("'entity_index' parameter is required.");

            string componentName = p.Get("component_name");
            if (string.IsNullOrEmpty(componentName))
                return new ErrorResponse("'component_name' parameter is required.");

            string fieldName = p.Get("field_name");
            if (string.IsNullOrEmpty(fieldName))
                return new ErrorResponse("'field_name' parameter is required.");

            string fieldValue = p.Get("field_value");
            if (fieldValue == null)
                return new ErrorResponse("'field_value' parameter is required.");

            var entity = new Entity { Index = entityIndex.Value, Version = entityVersion ?? 1 };
            var em = world.EntityManager;

            if (!em.Exists(entity))
                return new ErrorResponse($"Entity (Index={entityIndex}, Version={entityVersion ?? 1}) does not exist.");

            var ct = ResolveComponentType(componentName);
            if (ct == null)
                return new ErrorResponse($"Component type '{componentName}' not found.");

            if (!em.HasComponent(entity, ct.Value))
                return new ErrorResponse($"Entity does not have component '{componentName}'.");

            try
            {
                var type = ct.Value.GetManagedType();
                if (type == null)
                    return new ErrorResponse($"Cannot resolve managed type for '{componentName}'.");

                var obj = em.Debug.GetComponentBoxed(entity, ct.Value);
                if (obj == null)
                    return new ErrorResponse($"Cannot read component '{componentName}'.");

                var field = type.GetField(fieldName, BindingFlags.Public | BindingFlags.Instance);
                if (field == null)
                    return new ErrorResponse($"Field '{fieldName}' not found on component '{componentName}'.");

                // Parse the value to the correct type
                object parsedValue = Convert.ChangeType(fieldValue, field.FieldType, System.Globalization.CultureInfo.InvariantCulture);
                field.SetValue(obj, parsedValue);

                // SetComponentBoxed not available in public API — use SetComponentObject via reflection
                var setMethod = typeof(EntityManager).GetMethod("SetComponentObject",
                    BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance,
                    null, new[] { typeof(Entity), typeof(ComponentType), typeof(object) }, null);
                if (setMethod != null)
                {
                    setMethod.Invoke(em, new object[] { entity, ct.Value, obj });
                }
                else
                {
                    return new ErrorResponse("SetComponent is not supported in this version of Unity Entities.");
                }

                return new SuccessResponse(
                    $"Set {componentName}.{fieldName} = {fieldValue} on entity (Index={entityIndex}, Version={entityVersion ?? 1}).",
                    new Dictionary<string, object>
                    {
                        ["entity_index"] = entityIndex.Value,
                        ["component"]    = componentName,
                        ["field"]        = fieldName,
                        ["value"]        = fieldValue
                    });
            }
            catch (Exception e)
            {
                return new ErrorResponse($"Failed to set field: {e.Message}");
            }
        }

        private static object AddComponent(ToolParams p)
        {
            var world = ResolveWorld(p);
            if (world == null)
                return new ErrorResponse("World not found.");

            int? entityIndex = p.GetInt("entity_index");
            int? entityVersion = p.GetInt("entity_version");
            if (entityIndex == null)
                return new ErrorResponse("'entity_index' parameter is required.");

            string componentName = p.Get("component_name");
            if (string.IsNullOrEmpty(componentName))
                return new ErrorResponse("'component_name' parameter is required.");

            var entity = new Entity { Index = entityIndex.Value, Version = entityVersion ?? 1 };
            var em = world.EntityManager;

            if (!em.Exists(entity))
                return new ErrorResponse($"Entity (Index={entityIndex}, Version={entityVersion ?? 1}) does not exist.");

            var ct = ResolveComponentType(componentName);
            if (ct == null)
                return new ErrorResponse($"Component type '{componentName}' not found.");

            if (em.HasComponent(entity, ct.Value))
                return new ErrorResponse($"Entity already has component '{componentName}'.");

            em.AddComponent(entity, ct.Value);
            return new SuccessResponse(
                $"Added '{componentName}' to entity (Index={entityIndex}, Version={entityVersion ?? 1}).",
                SerializeEntityBrief(em, entity));
        }

        private static object RemoveComponent(ToolParams p)
        {
            var world = ResolveWorld(p);
            if (world == null)
                return new ErrorResponse("World not found.");

            int? entityIndex = p.GetInt("entity_index");
            int? entityVersion = p.GetInt("entity_version");
            if (entityIndex == null)
                return new ErrorResponse("'entity_index' parameter is required.");

            string componentName = p.Get("component_name");
            if (string.IsNullOrEmpty(componentName))
                return new ErrorResponse("'component_name' parameter is required.");

            var entity = new Entity { Index = entityIndex.Value, Version = entityVersion ?? 1 };
            var em = world.EntityManager;

            if (!em.Exists(entity))
                return new ErrorResponse($"Entity (Index={entityIndex}, Version={entityVersion ?? 1}) does not exist.");

            var ct = ResolveComponentType(componentName);
            if (ct == null)
                return new ErrorResponse($"Component type '{componentName}' not found.");

            if (!em.HasComponent(entity, ct.Value))
                return new ErrorResponse($"Entity does not have component '{componentName}'.");

            em.RemoveComponent(entity, ct.Value);
            return new SuccessResponse(
                $"Removed '{componentName}' from entity (Index={entityIndex}, Version={entityVersion ?? 1}).",
                SerializeEntityBrief(em, entity));
        }

        private static object QueryCount(ToolParams p)
        {
            var world = ResolveWorld(p);
            if (world == null)
                return new ErrorResponse("World not found.");

            string componentTypesStr = p.Get("component_types");
            if (string.IsNullOrEmpty(componentTypesStr))
                return new ErrorResponse("'component_types' parameter is required.");

            string[] typeNames = componentTypesStr.Split(',')
                .Select(t => t.Trim())
                .Where(t => !string.IsNullOrEmpty(t))
                .ToArray();

            var componentTypes = new List<ComponentType>();
            foreach (string typeName in typeNames)
            {
                var resolvedType = ResolveComponentType(typeName);
                if (resolvedType == null)
                    return new ErrorResponse($"Component type '{typeName}' not found.");
                componentTypes.Add(resolvedType.Value);
            }

            var em = world.EntityManager;
            using var query = em.CreateEntityQuery(componentTypes.ToArray());
            int count = query.CalculateEntityCount();

            return new SuccessResponse(
                $"{count} entities match [{string.Join(", ", typeNames)}] in world '{world.Name}'.",
                new Dictionary<string, object>
                {
                    ["count"]           = count,
                    ["component_types"] = typeNames,
                    ["world"]           = world.Name
                });
        }

        #endregion

        #region Performance

        private static object PerformanceSnapshot(ToolParams p)
        {
            var world = ResolveWorld(p);
            if (world == null)
                return new ErrorResponse("World not found. Use list_worlds to see available worlds.");

            var em = world.EntityManager;

            // Archetype stats
            var archetypes = new NativeList<EntityArchetype>(Allocator.Temp);
            em.GetAllArchetypes(archetypes);
            int totalChunks = 0;
            int totalEntities = 0;
            int emptyChunks = 0;
            var archetypeStats = new List<object>();

            for (int i = 0; i < archetypes.Length; i++)
            {
                var archetype = archetypes[i];
                int chunkCount = archetype.ChunkCount;
                int chunkCapacity = archetype.ChunkCapacity;

                // Use a query to count entities for this archetype
                int entityCount = 0;
                if (chunkCount > 0 && chunkCapacity > 0)
                {
                    // Estimate: chunkCount * average fill. For exact count, use query.
                    // ChunkCapacity is per-chunk max; actual count needs CalculateEntityCount.
                    // For perf snapshot, we use the universal query total and archetype breakdown.
                    entityCount = chunkCount > 0 ? EstimateArchetypeEntityCount(archetype) : 0;
                }

                int capacity = chunkCapacity * chunkCount;
                float utilization = capacity > 0 ? (float)entityCount / capacity * 100f : 0f;

                totalChunks += chunkCount;
                totalEntities += entityCount;
                if (entityCount == 0 && chunkCount > 0) emptyChunks += chunkCount;

                if (chunkCount > 0)
                {
                    var componentNames = new List<string>();
                    var types = archetype.GetComponentTypes(Allocator.Temp);
                    for (int t = 0; t < types.Length; t++)
                    {
                        var info = TypeManager.GetTypeInfo(types[t].TypeIndex);
                        componentNames.Add(info.DebugTypeName.ToString());
                    }
                    types.Dispose();

                    archetypeStats.Add(new Dictionary<string, object>
                    {
                        ["components"]      = componentNames,
                        ["chunk_count"]     = chunkCount,
                        ["entity_count"]    = entityCount,
                        ["chunk_capacity"]  = chunkCapacity,
                        ["utilization_pct"] = Math.Round(utilization, 1)
                    });
                }
            }

            // Sort by entity count descending
            archetypeStats.Sort((a, b) =>
            {
                var aCount = (int)((Dictionary<string, object>)a)["entity_count"];
                var bCount = (int)((Dictionary<string, object>)b)["entity_count"];
                return bCount.CompareTo(aCount);
            });

            int limit = p.GetInt("limit") ?? 20;
            archetypes.Dispose();

            if (archetypeStats.Count > limit)
                archetypeStats = archetypeStats.Take(limit).ToList();

            return new SuccessResponse($"Performance snapshot for world '{world.Name}'.", new Dictionary<string, object>
            {
                ["world"]               = world.Name,
                ["total_entities"]      = world.EntityManager.UniversalQuery.CalculateEntityCount(),
                ["total_archetypes"]    = archetypes.Length,
                ["total_chunks"]        = totalChunks,
                ["empty_chunks"]        = emptyChunks,
                ["system_count"]        = world.Systems.Count,
                ["top_archetypes"]      = archetypeStats
            });
        }

        /// <summary>
        /// Estimates entity count for an archetype using unsafe access.
        /// Falls back to chunk_count * chunk_capacity as upper bound.
        /// </summary>
        private static int EstimateArchetypeEntityCount(EntityArchetype archetype)
        {
            // EntityArchetype doesn't expose EntityCount directly in public API.
            // Use reflection to access internal Archetype->EntityCount if available,
            // otherwise return capacity as upper bound estimate.
            try
            {
                // Try the internal StableHash-based approach
                var archetypeField = typeof(EntityArchetype).GetField("Archetype",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                if (archetypeField != null)
                {
                    // Box the struct to access the field
                    object boxed = archetype;
                    var ptr = archetypeField.GetValue(boxed);
                    if (ptr != null)
                    {
                        // Archetype* has EntityCount property
                        var entityCountProp = ptr.GetType().GetProperty("EntityCount");
                        if (entityCountProp != null)
                            return (int)entityCountProp.GetValue(ptr);
                    }
                }
            }
            catch
            {
                // Reflection failed, fall back to estimate
            }

            // Upper bound: all chunks fully utilized
            return archetype.ChunkCapacity * archetype.ChunkCount;
        }

        #endregion

        #region Helpers

        private static World ResolveWorld(ToolParams p)
        {
            string worldName = p.Get("world");
            if (string.IsNullOrEmpty(worldName))
                return World.DefaultGameObjectInjectionWorld;

            foreach (var w in World.All)
            {
                if (string.Equals(w.Name, worldName, StringComparison.OrdinalIgnoreCase))
                    return w;
            }
            return null;
        }

        /// <summary>
        /// Resolves a component type name to a ComponentType by searching all loaded assemblies.
        /// Supports short names ("LocalTransform") and full names ("Unity.Transforms.LocalTransform").
        /// </summary>
        private static ComponentType? ResolveComponentType(string typeName)
        {
            // First, try iterating all registered ECS types via TypeManager
            int typeCount = TypeManager.GetTypeCount();
            for (int i = 1; i < typeCount; i++) // Start at 1; index 0 is Entity itself
            {
                var typeInfo = TypeManager.GetTypeInfo(i);
                string debugName = typeInfo.DebugTypeName.ToString();

                // Match by short name or full name
                if (string.Equals(debugName, typeName, StringComparison.OrdinalIgnoreCase) ||
                    debugName.EndsWith("." + typeName, StringComparison.OrdinalIgnoreCase))
                {
                    return ComponentType.FromTypeIndex(i);
                }
            }
            return null;
        }

        private static ComponentSystemBase FindSystem(World world, string systemName)
        {
            foreach (var sys in world.Systems)
            {
                var sysType = sys.GetType();
                if (sysType.Name == systemName || sysType.FullName == systemName)
                    return sys;
            }
            return null;
        }

        private static Dictionary<string, object> SerializeEntityBrief(EntityManager em, Entity entity)
        {
            var componentTypes = em.GetComponentTypes(entity, Allocator.Temp);
            var names = new List<string>();
            for (int i = 0; i < componentTypes.Length; i++)
            {
                var info = TypeManager.GetTypeInfo(componentTypes[i].TypeIndex);
                names.Add(info.DebugTypeName.ToString());
            }
            componentTypes.Dispose();

            return new Dictionary<string, object>
            {
                ["index"]      = entity.Index,
                ["version"]    = entity.Version,
                ["components"] = names
            };
        }

        private static Dictionary<string, object> SerializeEntityFull(EntityManager em, Entity entity)
        {
            var componentTypes = em.GetComponentTypes(entity, Allocator.Temp);
            var components = new List<object>();

            for (int i = 0; i < componentTypes.Length; i++)
            {
                var typeInfo = TypeManager.GetTypeInfo(componentTypes[i].TypeIndex);
                string typeName = typeInfo.DebugTypeName.ToString();
                var componentData = new Dictionary<string, object>
                {
                    ["name"]          = typeName,
                    ["category"]      = typeInfo.Category.ToString(),
                    ["size_bytes"]    = typeInfo.SizeInChunk,
                    ["is_zero_sized"] = typeInfo.IsZeroSized
                };

                // Check enableable component state
                if (typeInfo.EnableableType)
                {
                    try
                    {
                        componentData["is_enabled"] = em.IsComponentEnabled(entity, componentTypes[i]);
                    }
                    catch
                    {
                        componentData["is_enabled"] = "<unknown>";
                    }
                }

                // Read field values for IComponentData
                if (!typeInfo.IsZeroSized && typeInfo.Category == TypeManager.TypeCategory.ComponentData)
                {
                    ReadComponentFields(em, entity, componentTypes[i], componentData);
                }
                // Read shared component data
                else if (typeInfo.Category == TypeManager.TypeCategory.ISharedComponentData)
                {
                    ReadSharedComponentFields(em, entity, componentTypes[i], componentData);
                }
                // Read buffer element data
                else if (typeInfo.Category == TypeManager.TypeCategory.BufferData)
                {
                    ReadBufferElements(em, entity, componentTypes[i], typeInfo, componentData);
                }

                components.Add(componentData);
            }
            componentTypes.Dispose();

            return new Dictionary<string, object>
            {
                ["index"]           = entity.Index,
                ["version"]         = entity.Version,
                ["component_count"] = components.Count,
                ["components"]      = components
            };
        }

        private static void ReadComponentFields(EntityManager em, Entity entity, ComponentType ct, Dictionary<string, object> data)
        {
            try
            {
                var type = ct.GetManagedType();
                if (type == null) return;

                var obj = em.Debug.GetComponentBoxed(entity, ct);
                if (obj == null) return;

                var fields = new Dictionary<string, object>();
                foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
                {
                    try
                    {
                        fields[field.Name] = field.GetValue(obj)?.ToString() ?? "null";
                    }
                    catch
                    {
                        fields[field.Name] = "<unreadable>";
                    }
                }
                data["fields"] = fields;
            }
            catch
            {
                // Some components can't be boxed safely
            }
        }

        private static void ReadSharedComponentFields(EntityManager em, Entity entity, ComponentType ct, Dictionary<string, object> data)
        {
            try
            {
                var type = ct.GetManagedType();
                if (type == null) return;

                var obj = em.Debug.GetComponentBoxed(entity, ct);
                if (obj == null) return;

                var fields = new Dictionary<string, object>();
                foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
                {
                    try
                    {
                        fields[field.Name] = field.GetValue(obj)?.ToString() ?? "null";
                    }
                    catch
                    {
                        fields[field.Name] = "<unreadable>";
                    }
                }
                data["fields"] = fields;
            }
            catch
            {
                // Shared components may not be readable
            }
        }

        private static void ReadBufferElements(EntityManager em, Entity entity, ComponentType ct, TypeManager.TypeInfo typeInfo, Dictionary<string, object> data)
        {
            try
            {
                var type = ct.GetManagedType();
                if (type == null) return;

                // Use reflection to call EntityManager.GetBuffer<T>(entity)
                var getBufferMethod = typeof(EntityManager).GetMethod("GetBuffer",
                    new[] { typeof(Entity), typeof(bool) });
                if (getBufferMethod == null) return;

                var genericMethod = getBufferMethod.MakeGenericMethod(type);
                var buffer = genericMethod.Invoke(em, new object[] { entity, true }); // readOnly=true
                if (buffer == null) return;

                // Get Length property
                var lengthProp = buffer.GetType().GetProperty("Length");
                int length = lengthProp != null ? (int)lengthProp.GetValue(buffer) : 0;
                data["buffer_length"] = length;

                // Read up to 10 elements
                int sampleCount = Math.Min(length, 10);
                var elements = new List<object>();
                var indexer = buffer.GetType().GetProperty("Item");
                if (indexer != null)
                {
                    for (int e = 0; e < sampleCount; e++)
                    {
                        try
                        {
                            var elem = indexer.GetValue(buffer, new object[] { e });
                            var fields = new Dictionary<string, object>();
                            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
                            {
                                try
                                {
                                    fields[field.Name] = field.GetValue(elem)?.ToString() ?? "null";
                                }
                                catch
                                {
                                    fields[field.Name] = "<unreadable>";
                                }
                            }
                            elements.Add(fields);
                        }
                        catch { break; }
                    }
                }
                data["elements"] = elements;
            }
            catch
            {
                data["buffer_length"] = "<unreadable>";
            }
        }

        private static string GetSystemGroupName(Type systemType)
        {
            var attrs = systemType.GetCustomAttributes(typeof(UpdateInGroupAttribute), true);
            if (attrs.Length > 0)
            {
                var groupAttr = (UpdateInGroupAttribute)attrs[0];
                return groupAttr.GroupType.Name;
            }
            return "Default";
        }

        private static List<string> GetQueryComponentTypeNames(EntityQuery query)
        {
            var names = new List<string>();
            try
            {
                // GetQueryTypes is internal — use reflection
                var method = typeof(EntityQuery).GetMethod("GetQueryTypes",
                    BindingFlags.NonPublic | BindingFlags.Instance, null, Type.EmptyTypes, null);
                if (method != null)
                {
                    var types = (ComponentType[])method.Invoke(query, null);
                    foreach (var ct in types)
                    {
                        var info = TypeManager.GetTypeInfo(ct.TypeIndex);
                        names.Add(info.DebugTypeName.ToString());
                    }
                }
                else
                {
                    names.Add("<query types unavailable>");
                }
            }
            catch
            {
                names.Add("<unable to read query types>");
            }
            return names;
        }

        #endregion
    }
}
#endif
