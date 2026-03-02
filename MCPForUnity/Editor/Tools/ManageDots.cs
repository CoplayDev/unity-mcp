#if UNITY_ENTITIES
using System;
using System.Collections.Generic;
using System.Linq;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using Unity.Collections;
using Unity.Entities;

namespace MCPForUnity.Editor.Tools
{
    /// <summary>
    /// MCP tool for Unity DOTS ECS debugging, inspection, and performance monitoring.
    /// Actions: list_worlds, query_entities, get_entity, list_systems, get_system,
    ///          performance_snapshot, toggle_system
    /// Requires com.unity.entities package.
    /// </summary>
    [McpForUnityTool("manage_dots")]
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
                    "list_worlds"          => ListWorlds(p),
                    "query_entities"       => QueryEntities(p),
                    "get_entity"           => GetEntity(p),
                    "list_systems"         => ListSystems(p),
                    "get_system"           => GetSystem(p),
                    "performance_snapshot" => PerformanceSnapshot(p),
                    "toggle_system"        => ToggleSystem(p),
                    _ => new ErrorResponse(
                        $"Unknown action: '{action}'. Supported: list_worlds, query_entities, get_entity, " +
                        "list_systems, get_system, performance_snapshot, toggle_system")
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
                var systems = world.Systems;
                worlds.Add(new Dictionary<string, object>
                {
                    ["name"]         = world.Name,
                    ["is_created"]   = world.IsCreated,
                    ["system_count"] = systems.Count,
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
                int typeIndex = TypeManager.GetTypeIndexByName(typeName);
                if (typeIndex == -1)
                    return new ErrorResponse($"Component type '{typeName}' not found. Check spelling or ensure the assembly is loaded.");
                componentTypes.Add(ComponentType.FromTypeIndex(typeIndex));
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
                var managedType = sys.GetManagedType();
                if (managedType == null) continue;

                string groupName = GetSystemGroupName(sys);
                if (!string.IsNullOrEmpty(groupFilter) &&
                    !groupName.Contains(groupFilter, StringComparison.OrdinalIgnoreCase))
                    continue;

                systems.Add(new Dictionary<string, object>
                {
                    ["name"]    = managedType.Name,
                    ["type"]    = managedType.FullName,
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

            ComponentSystemBase system = null;
            foreach (var sys in world.Systems)
            {
                var mt = sys.GetManagedType();
                if (mt != null && (mt.Name == systemName || mt.FullName == systemName))
                {
                    system = sys;
                    break;
                }
            }

            if (system == null)
                return new ErrorResponse($"System '{systemName}' not found in world '{world.Name}'.");

            var managedType = system.GetManagedType();
            var queries = new List<object>();
            foreach (var q in system.EntityQueries)
            {
                queries.Add(new Dictionary<string, object>
                {
                    ["entity_count"]    = q.CalculateEntityCount(),
                    ["component_types"] = GetQueryComponentTypes(q)
                });
            }

            return new SuccessResponse($"System '{systemName}' details.", new Dictionary<string, object>
            {
                ["name"]           = managedType.Name,
                ["full_name"]      = managedType.FullName,
                ["group"]          = GetSystemGroupName(system),
                ["enabled"]        = system.Enabled,
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

            foreach (var sys in world.Systems)
            {
                var mt = sys.GetManagedType();
                if (mt != null && (mt.Name == systemName || mt.FullName == systemName))
                {
                    sys.Enabled = enabled.Value;
                    return new SuccessResponse(
                        $"System '{systemName}' {(enabled.Value ? "enabled" : "disabled")} in world '{world.Name}'.");
                }
            }

            return new ErrorResponse($"System '{systemName}' not found in world '{world.Name}'.");
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
            using var archetypes = em.GetAllArchetypes(Allocator.Temp);
            int totalChunks = 0;
            int totalEntities = 0;
            int emptyChunks = 0;
            var archetypeStats = new List<object>();

            for (int i = 0; i < archetypes.Length; i++)
            {
                var archetype = archetypes[i];
                if (archetype.Archetype == null) continue;

                int chunkCount = archetype.ChunkCount;
                int entityCount = archetype.ChunkCapacity > 0 ? chunkCount > 0 ? archetype.EntityCount : 0 : 0;
                int capacity = archetype.ChunkCapacity * chunkCount;
                float utilization = capacity > 0 ? (float)entityCount / capacity * 100f : 0f;

                totalChunks += chunkCount;
                totalEntities += entityCount;
                if (entityCount == 0 && chunkCount > 0) emptyChunks += chunkCount;

                if (chunkCount > 0) // Only report non-empty archetypes
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
                        ["chunk_capacity"]  = archetype.ChunkCapacity,
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

            // Limit to top archetypes
            int limit = p.GetInt("limit") ?? 20;
            if (archetypeStats.Count > limit)
                archetypeStats = archetypeStats.Take(limit).ToList();

            return new SuccessResponse($"Performance snapshot for world '{world.Name}'.", new Dictionary<string, object>
            {
                ["world"]               = world.Name,
                ["total_entities"]      = totalEntities,
                ["total_archetypes"]    = archetypes.Length,
                ["total_chunks"]        = totalChunks,
                ["empty_chunks"]        = emptyChunks,
                ["system_count"]        = world.Systems.Count,
                ["top_archetypes"]      = archetypeStats
            });
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

                // Try to read field values for IComponentData
                if (!typeInfo.IsZeroSized && typeInfo.Category == TypeManager.TypeCategory.ComponentData)
                {
                    try
                    {
                        var type = componentTypes[i].GetManagedType();
                        if (type != null)
                        {
                            var obj = em.Debug.GetComponentBoxed(entity, componentTypes[i]);
                            if (obj != null)
                            {
                                var fields = new Dictionary<string, object>();
                                foreach (var field in type.GetFields(
                                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
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
                                componentData["fields"] = fields;
                            }
                        }
                    }
                    catch
                    {
                        // Some components can't be boxed safely
                    }
                }

                components.Add(componentData);
            }
            componentTypes.Dispose();

            var archetype = em.GetChunk(entity).Archetype;
            return new Dictionary<string, object>
            {
                ["index"]           = entity.Index,
                ["version"]         = entity.Version,
                ["archetype_hash"]  = archetype.GetHashCode(),
                ["chunk_capacity"]  = archetype.ChunkCapacity,
                ["component_count"] = components.Count,
                ["components"]      = components
            };
        }

        private static string GetSystemGroupName(ComponentSystemBase system)
        {
            var type = system.GetManagedType();
            if (type == null) return "Unknown";

            var attr = type.GetCustomAttributes(typeof(UpdateInGroupAttribute), true);
            if (attr.Length > 0)
            {
                var groupAttr = (UpdateInGroupAttribute)attr[0];
                return groupAttr.GroupType.Name;
            }
            return "Default";
        }

        private static List<string> GetQueryComponentTypes(EntityQuery query)
        {
            var desc = query.GetEntityQueryDesc();
            var names = new List<string>();
            if (desc.All != null)
            {
                foreach (var ct in desc.All)
                {
                    var info = TypeManager.GetTypeInfo(ct.TypeIndex);
                    names.Add(info.DebugTypeName.ToString());
                }
            }
            return names;
        }

        #endregion
    }
}
#endif
