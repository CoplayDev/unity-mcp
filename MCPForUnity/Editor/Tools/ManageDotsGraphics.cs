#if UNITY_ENTITIES_GRAPHICS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using Unity.Collections;
using Unity.Entities;
using Unity.Rendering;

namespace MCPForUnity.Editor.Tools
{
    /// <summary>
    /// MCP tool for Unity DOTS Entities Graphics debugging.
    /// Actions: get_render_stats, list_rendered_entities, get_entity_rendering,
    ///          list_registered_materials, list_registered_meshes
    /// Requires com.unity.entities.graphics package.
    /// </summary>
    [McpForUnityTool("manage_dots_graphics", AutoRegister = false)]
    public static class ManageDotsGraphics
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
                    "get_render_stats"           => GetRenderStats(p),
                    "list_rendered_entities"      => ListRenderedEntities(p),
                    "get_entity_rendering"        => GetEntityRendering(p),
                    "list_registered_materials"   => ListRegisteredMaterials(p),
                    "list_registered_meshes"      => ListRegisteredMeshes(p),
                    _ => new ErrorResponse(
                        $"Unknown action: '{action}'. Supported: get_render_stats, " +
                        "list_rendered_entities, get_entity_rendering, " +
                        "list_registered_materials, list_registered_meshes")
                };
            }
            catch (Exception e)
            {
                McpLog.Error($"[ManageDotsGraphics] Action '{action}' failed: {e}");
                return new ErrorResponse($"Internal error: {e.Message}");
            }
        }

        #region Render Stats

        private static object GetRenderStats(ToolParams p)
        {
            var world = ResolveWorld(p);
            if (world == null)
                return new ErrorResponse("World not found.");

            var em = world.EntityManager;

            // Count entities with rendering components
            int materialMeshInfoCount;
            int renderBoundsCount;
            int renderMeshArrayCount;
            int lodGroupCount;

            using (var q1 = em.CreateEntityQuery(typeof(MaterialMeshInfo)))
                materialMeshInfoCount = q1.CalculateEntityCount();
            using (var q2 = em.CreateEntityQuery(typeof(RenderBounds)))
                renderBoundsCount = q2.CalculateEntityCount();
            using (var q3 = em.CreateEntityQuery(typeof(RenderMeshArray)))
                renderMeshArrayCount = q3.CalculateEntityCount();

            // LODGroupEntity may not exist in all versions
            lodGroupCount = 0;
            try
            {
                var lodType = typeof(Unity.Rendering.LODGroupWorldReferencePoint);
                if (lodType != null)
                {
                    using var qLod = em.CreateEntityQuery(ComponentType.ReadOnly(lodType));
                    lodGroupCount = qLod.CalculateEntityCount();
                }
            }
            catch { /* LOD type not available */ }

            return new SuccessResponse("Entities Graphics stats.", new Dictionary<string, object>
            {
                ["world"]                      = world.Name,
                ["entities_with_material_mesh"] = materialMeshInfoCount,
                ["entities_with_render_bounds"] = renderBoundsCount,
                ["entities_with_render_mesh_array"] = renderMeshArrayCount,
                ["entities_with_lod"]          = lodGroupCount,
            });
        }

        #endregion

        #region List Rendered Entities

        private static object ListRenderedEntities(ToolParams p)
        {
            var world = ResolveWorld(p);
            if (world == null)
                return new ErrorResponse("World not found.");

            var em = world.EntityManager;
            int pageSize = p.GetInt("page_size") ?? 20;
            pageSize = Math.Clamp(pageSize, 1, 100);

            using var query = em.CreateEntityQuery(typeof(MaterialMeshInfo));
            int totalCount = query.CalculateEntityCount();

            var entities = query.ToEntityArray(Allocator.Temp);
            int sampleCount = Math.Min(pageSize, entities.Length);

            var results = new List<object>();
            for (int i = 0; i < sampleCount; i++)
            {
                var entity = entities[i];
                var data = new Dictionary<string, object>
                {
                    ["entity_index"]   = entity.Index,
                    ["entity_version"] = entity.Version,
                };

                try
                {
                    var mmi = em.GetComponentData<MaterialMeshInfo>(entity);
                    data["material_id"] = mmi.MaterialID.GetHashCode();
                    data["mesh_id"]     = mmi.MeshID.GetHashCode();
                }
                catch
                {
                    data["material_mesh_info"] = "<unreadable>";
                }

                if (em.HasComponent<RenderBounds>(entity))
                {
                    try
                    {
                        var bounds = em.GetComponentData<RenderBounds>(entity);
                        data["render_bounds_center"] = $"{bounds.Value.Center.x:F2},{bounds.Value.Center.y:F2},{bounds.Value.Center.z:F2}";
                        data["render_bounds_extents"] = $"{bounds.Value.Extents.x:F2},{bounds.Value.Extents.y:F2},{bounds.Value.Extents.z:F2}";
                    }
                    catch { /* bounds unreadable */ }
                }

                results.Add(data);
            }
            entities.Dispose();

            return new SuccessResponse(
                $"Found {totalCount} rendered entities in '{world.Name}'.",
                new Dictionary<string, object>
                {
                    ["total_count"] = totalCount,
                    ["returned"]    = results.Count,
                    ["entities"]    = results
                });
        }

        #endregion

        #region Entity Rendering Detail

        private static object GetEntityRendering(ToolParams p)
        {
            var world = ResolveWorld(p);
            if (world == null)
                return new ErrorResponse("World not found.");

            int? entityIndex = p.GetInt("entity_index");
            if (entityIndex == null)
                return new ErrorResponse("'entity_index' parameter is required.");

            var em = world.EntityManager;
            Entity entity;
            try
            {
                var allEntities = em.GetAllEntities(Allocator.Temp);
                entity = Entity.Null;
                for (int i = 0; i < allEntities.Length; i++)
                {
                    if (allEntities[i].Index == entityIndex.Value)
                    {
                        entity = allEntities[i];
                        break;
                    }
                }
                allEntities.Dispose();

                if (entity == Entity.Null)
                    return new ErrorResponse($"Entity with index {entityIndex} not found.");
            }
            catch (Exception e)
            {
                return new ErrorResponse($"Failed to find entity: {e.Message}");
            }

            var result = new Dictionary<string, object>
            {
                ["entity_index"]   = entity.Index,
                ["entity_version"] = entity.Version,
            };

            // MaterialMeshInfo
            if (em.HasComponent<MaterialMeshInfo>(entity))
            {
                try
                {
                    var mmi = em.GetComponentData<MaterialMeshInfo>(entity);
                    result["material_id"] = mmi.MaterialID.GetHashCode();
                    result["mesh_id"]     = mmi.MeshID.GetHashCode();
                }
                catch { result["material_mesh_info"] = "<unreadable>"; }
            }
            else
            {
                result["material_mesh_info"] = "none";
            }

            // RenderBounds
            if (em.HasComponent<RenderBounds>(entity))
            {
                try
                {
                    var bounds = em.GetComponentData<RenderBounds>(entity);
                    result["render_bounds"] = new Dictionary<string, object>
                    {
                        ["center"]  = $"{bounds.Value.Center.x:F3},{bounds.Value.Center.y:F3},{bounds.Value.Center.z:F3}",
                        ["extents"] = $"{bounds.Value.Extents.x:F3},{bounds.Value.Extents.y:F3},{bounds.Value.Extents.z:F3}"
                    };
                }
                catch { result["render_bounds"] = "<unreadable>"; }
            }

            // RenderFilterSettings
            if (em.HasComponent<RenderFilterSettings>(entity))
            {
                try
                {
                    var rfs = em.GetSharedComponentManaged<RenderFilterSettings>(entity);
                    result["render_filter"] = new Dictionary<string, object>
                    {
                        ["layer"]               = rfs.Layer,
                        ["shadow_casting"]      = rfs.ShadowCastingMode.ToString(),
                        ["receive_shadows"]     = rfs.ReceiveShadows,
                        ["static_shadow_caster"] = rfs.StaticShadowCaster,
                    };
                }
                catch { result["render_filter"] = "<unreadable>"; }
            }

            // Check for WorldRenderBounds (if available)
            if (em.HasComponent<WorldRenderBounds>(entity))
            {
                try
                {
                    var wrb = em.GetComponentData<WorldRenderBounds>(entity);
                    result["world_render_bounds"] = new Dictionary<string, object>
                    {
                        ["center"]  = $"{wrb.Value.Center.x:F3},{wrb.Value.Center.y:F3},{wrb.Value.Center.z:F3}",
                        ["extents"] = $"{wrb.Value.Extents.x:F3},{wrb.Value.Extents.y:F3},{wrb.Value.Extents.z:F3}"
                    };
                }
                catch { result["world_render_bounds"] = "<unreadable>"; }
            }

            return new SuccessResponse($"Rendering info for entity {entity.Index}.", result);
        }

        #endregion

        #region Registered Materials & Meshes

        private static object ListRegisteredMaterials(ToolParams p)
        {
            var world = ResolveWorld(p);
            if (world == null)
                return new ErrorResponse("World not found.");

            var em = world.EntityManager;
            int pageSize = p.GetInt("page_size") ?? 20;
            pageSize = Math.Clamp(pageSize, 1, 100);

            // RenderMeshArray is a shared component containing materials + meshes
            using var query = em.CreateEntityQuery(typeof(RenderMeshArray));
            int totalArrays = query.CalculateEntityCount();

            // Collect unique materials from RenderMeshArray shared components
            var materialSet = new HashSet<string>();
            var materialList = new List<object>();

            var entities = query.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < entities.Length && materialList.Count < pageSize; i++)
            {
                try
                {
                    var rma = em.GetSharedComponentManaged<RenderMeshArray>(entities[i]);
                    if (rma.Materials != null)
                    {
                        foreach (var mat in rma.Materials)
                        {
                            if (mat != null && materialSet.Add(mat.name))
                            {
                                materialList.Add(new Dictionary<string, object>
                                {
                                    ["name"]       = mat.name,
                                    ["shader"]     = mat.shader?.name ?? "<none>",
                                    ["render_queue"] = mat.renderQueue,
                                });
                                if (materialList.Count >= pageSize) break;
                            }
                        }
                    }
                }
                catch { /* skip unreadable */ }
            }
            entities.Dispose();

            return new SuccessResponse(
                $"Found {materialSet.Count} unique material(s) across {totalArrays} RenderMeshArray(s).",
                new Dictionary<string, object>
                {
                    ["total_render_mesh_arrays"] = totalArrays,
                    ["unique_materials"]         = materialSet.Count,
                    ["returned"]                 = materialList.Count,
                    ["materials"]                = materialList
                });
        }

        private static object ListRegisteredMeshes(ToolParams p)
        {
            var world = ResolveWorld(p);
            if (world == null)
                return new ErrorResponse("World not found.");

            var em = world.EntityManager;
            int pageSize = p.GetInt("page_size") ?? 20;
            pageSize = Math.Clamp(pageSize, 1, 100);

            using var query = em.CreateEntityQuery(typeof(RenderMeshArray));
            int totalArrays = query.CalculateEntityCount();

            var meshSet = new HashSet<string>();
            var meshList = new List<object>();

            var entities = query.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < entities.Length && meshList.Count < pageSize; i++)
            {
                try
                {
                    var rma = em.GetSharedComponentManaged<RenderMeshArray>(entities[i]);
                    if (rma.Meshes != null)
                    {
                        foreach (var mesh in rma.Meshes)
                        {
                            if (mesh != null && meshSet.Add(mesh.name))
                            {
                                meshList.Add(new Dictionary<string, object>
                                {
                                    ["name"]          = mesh.name,
                                    ["vertex_count"]  = mesh.vertexCount,
                                    ["sub_mesh_count"] = mesh.subMeshCount,
                                    ["bounds_center"]  = $"{mesh.bounds.center.x:F2},{mesh.bounds.center.y:F2},{mesh.bounds.center.z:F2}",
                                    ["bounds_size"]    = $"{mesh.bounds.size.x:F2},{mesh.bounds.size.y:F2},{mesh.bounds.size.z:F2}",
                                });
                                if (meshList.Count >= pageSize) break;
                            }
                        }
                    }
                }
                catch { /* skip unreadable */ }
            }
            entities.Dispose();

            return new SuccessResponse(
                $"Found {meshSet.Count} unique mesh(es) across {totalArrays} RenderMeshArray(s).",
                new Dictionary<string, object>
                {
                    ["total_render_mesh_arrays"] = totalArrays,
                    ["unique_meshes"]            = meshSet.Count,
                    ["returned"]                 = meshList.Count,
                    ["meshes"]                   = meshList
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

        #endregion
    }
}
#endif
