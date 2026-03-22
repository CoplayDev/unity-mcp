#if UNITY_ENTITIES
using System;
using System.Collections.Generic;
using System.Linq;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEditor;
using UnityEngine;

namespace MCPForUnity.Editor.Tools
{
    /// <summary>
    /// Aggregates all runtime validation data into a single response.
    /// Replaces 15-20 individual MCP calls with 1-2 calls (capture + compare).
    /// Actions: capture, compare.
    /// </summary>
    [McpForUnityTool("validation_snapshot", AutoRegister = true)]
    public static class ValidationSnapshot
    {
        private const int DefaultSampleSize = 20;
        private const int MaxSampleSize = 100;
        private const float MovementThreshold = 0.1f;

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
                    "capture" => Capture(p),
                    "compare" => Compare(p),
                    _ => new ErrorResponse($"Unknown action: '{action}'. Valid: capture, compare.")
                };
            }
            catch (Exception ex)
            {
                McpLog.Error($"[ValidationSnapshot] Action '{action}' failed: {ex}");
                return new ErrorResponse($"Error in {action}: {ex.Message}");
            }
        }

        private static object Capture(ToolParams p)
        {
            bool isPlaying = EditorApplication.isPlaying;
            int sampleSize = Math.Clamp(p.GetInt("sample_size") ?? DefaultSampleSize, 1, MaxSampleSize);

            // Console errors
            int consoleErrorCount = GetConsoleErrorCount();

            // Editor state
            var editorState = new Dictionary<string, object>
            {
                ["is_playing"] = isPlaying,
                ["is_paused"] = EditorApplication.isPaused
            };

            if (!isPlaying)
            {
                return new SuccessResponse("Snapshot captured (not playing).", new Dictionary<string, object>
                {
                    ["editor"] = editorState,
                    ["console_errors"] = consoleErrorCount,
                    ["note"] = "Not in Play mode. Entity and rendering data unavailable."
                });
            }

            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
                return new ErrorResponse("No active ECS World found.");

            var em = world.EntityManager;
            em.CompleteAllTrackedJobs();

            // Entity counts
            var entityCounts = CollectEntityCounts(em);

            // Health distribution
            var healthStats = CollectHealthStats(em);

            // Position samples
            var positionSamples = CollectPositionSamples(em, sampleSize);

            // NaN bounds check
            var nanBoundsResult = CheckNaNBounds(em);

            // Rendering stats
            var rendering = CollectRenderingStats();

            // Battle state
            var battleState = CollectBattleState(em);

            return new SuccessResponse("Validation snapshot captured.", new Dictionary<string, object>
            {
                ["editor"] = editorState,
                ["console_errors"] = consoleErrorCount,
                ["entities"] = entityCounts,
                ["health"] = healthStats,
                ["positions"] = positionSamples,
                ["nan_bounds"] = nanBoundsResult,
                ["rendering"] = rendering,
                ["battle"] = battleState
            });
        }

        private static object Compare(ToolParams p)
        {
            var snapshotAToken = p.GetRaw("snapshot_a");
            var snapshotBToken = p.GetRaw("snapshot_b");

            if (snapshotAToken == null || snapshotBToken == null)
                return new ErrorResponse("Both 'snapshot_a' and 'snapshot_b' are required for compare.");

            JObject a, b;
            try
            {
                a = snapshotAToken is JObject aObj ? aObj : JObject.Parse(snapshotAToken.ToString());
                b = snapshotBToken is JObject bObj ? bObj : JObject.Parse(snapshotBToken.ToString());
            }
            catch (Exception ex)
            {
                return new ErrorResponse($"Failed to parse snapshots: {ex.Message}");
            }

            var anomalies = new List<string>();

            // Entity deltas
            int totalA = GetNestedInt(a, "entities", "total") ?? 0;
            int totalB = GetNestedInt(b, "entities", "total") ?? 0;
            int aliveA = GetNestedInt(a, "entities", "alive") ?? 0;
            int aliveB = GetNestedInt(b, "entities", "alive") ?? 0;
            int deadA = GetNestedInt(a, "entities", "dead") ?? 0;
            int deadB = GetNestedInt(b, "entities", "dead") ?? 0;

            // HP mean delta
            float hpMeanA = GetNestedFloat(a, "health", "mean") ?? 0f;
            float hpMeanB = GetNestedFloat(b, "health", "mean") ?? 0f;

            // Position deltas and movement ratio
            var positionsA = a["positions"]?["samples"] as JArray;
            var positionsB = b["positions"]?["samples"] as JArray;

            var positionDeltas = new List<object>();
            int movedCount = 0;
            int matchedCount = 0;

            if (positionsA != null && positionsB != null)
            {
                // Build lookup by entity index for snapshot B
                var bLookup = new Dictionary<int, JObject>();
                foreach (var item in positionsB)
                {
                    if (item is JObject obj && obj["entity_index"] != null)
                        bLookup[(int)obj["entity_index"]] = obj;
                }

                foreach (var itemA in positionsA)
                {
                    if (itemA is not JObject objA || objA["entity_index"] == null)
                        continue;

                    int entityIndex = (int)objA["entity_index"];
                    if (!bLookup.TryGetValue(entityIndex, out var objB))
                        continue;

                    matchedCount++;
                    float ax = (float)(objA["x"] ?? 0), ay = (float)(objA["y"] ?? 0), az = (float)(objA["z"] ?? 0);
                    float bx = (float)(objB["x"] ?? 0), by = (float)(objB["y"] ?? 0), bz = (float)(objB["z"] ?? 0);
                    float dist = math.sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay) + (bz - az) * (bz - az));

                    if (dist > MovementThreshold) movedCount++;

                    positionDeltas.Add(new Dictionary<string, object>
                    {
                        ["entity_index"] = entityIndex,
                        ["distance"] = Math.Round(dist, 3)
                    });

                    // Check for NaN positions
                    if (float.IsNaN(bx) || float.IsNaN(by) || float.IsNaN(bz))
                        anomalies.Add($"Entity {entityIndex} has NaN position in snapshot_b.");
                }
            }

            float movementRatio = matchedCount > 0 ? (float)movedCount / matchedCount : 0f;

            // Rendering deltas
            int drawCallsA = GetNestedInt(a, "rendering", "draw_calls") ?? 0;
            int drawCallsB = GetNestedInt(b, "rendering", "draw_calls") ?? 0;
            float fpsA = GetNestedFloat(a, "rendering", "fps") ?? 0f;
            float fpsB = GetNestedFloat(b, "rendering", "fps") ?? 0f;

            // Anomaly detection
            int consoleErrorsB = b["console_errors"]?.Value<int>() ?? 0;
            if (consoleErrorsB > 0)
                anomalies.Add($"{consoleErrorsB} console error(s) in snapshot_b.");

            if (aliveB == 0 && deadB == 0)
                anomalies.Add("No alive or dead entities in snapshot_b (no units spawned?).");
            else if (movementRatio < 0.01f && aliveB > 0)
                anomalies.Add("Zero movement detected among alive entities.");

            var nanBoundsB = b["nan_bounds"]?["nan_count"]?.Value<int>() ?? 0;
            if (nanBoundsB > 0)
                anomalies.Add($"{nanBoundsB} chunk(s) with NaN bounds in snapshot_b.");

            return new SuccessResponse("Snapshot comparison complete.", new Dictionary<string, object>
            {
                ["entity_delta"] = totalB - totalA,
                ["dead_delta"] = deadB - deadA,
                ["hp_mean_delta"] = Math.Round(hpMeanB - hpMeanA, 2),
                ["movement_ratio"] = Math.Round(movementRatio, 3),
                ["position_deltas"] = positionDeltas,
                ["rendering_delta"] = new Dictionary<string, object>
                {
                    ["draw_calls"] = drawCallsB - drawCallsA,
                    ["fps"] = Math.Round(fpsB - fpsA, 1)
                },
                ["anomalies"] = anomalies
            });
        }

        #region Data Collection

        private static int GetConsoleErrorCount()
        {
            try
            {
                var logEntriesType = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.LogEntries");
                if (logEntriesType == null) return -1;

                var getCountMethod = logEntriesType.GetMethod("GetCountsByType",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                if (getCountMethod != null)
                {
                    // GetCountsByType(out int error, out int warning, out int log)
                    var parameters = new object[] { 0, 0, 0 };
                    getCountMethod.Invoke(null, parameters);
                    return (int)parameters[0]; // error count
                }

                // Fallback: try StartGettingEntries + GetEntryInternal
                var startMethod = logEntriesType.GetMethod("StartGettingEntries",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                var countProp = logEntriesType.GetMethod("GetCount",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                var endMethod = logEntriesType.GetMethod("EndGettingEntries",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);

                if (startMethod == null || countProp == null) return -1;

                startMethod.Invoke(null, null);
                int total = (int)countProp.Invoke(null, null);
                int errorCount = 0;

                var getEntryMethod = logEntriesType.GetMethod("GetEntryInternal",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);

                if (getEntryMethod != null)
                {
                    // Count via mode flags: error = 1
                    var entryType = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.LogEntry");
                    if (entryType != null)
                    {
                        var entry = Activator.CreateInstance(entryType);
                        var modeField = entryType.GetField("mode");
                        for (int i = 0; i < total && i < 1000; i++)
                        {
                            getEntryMethod.Invoke(null, new object[] { i, entry });
                            if (modeField != null)
                            {
                                int mode = (int)modeField.GetValue(entry);
                                // Error modes: bits for Error, Exception, Assert
                                if ((mode & (1 << 0 | 1 << 2 | 1 << 4)) != 0) errorCount++;
                            }
                        }
                    }
                }

                endMethod?.Invoke(null, null);
                return errorCount;
            }
            catch
            {
                return -1;
            }
        }

        private static Dictionary<string, object> CollectEntityCounts(EntityManager em)
        {
            // Total with Health
            var healthType = ResolveComponentType("DOTSCombat.Health");
            var deadTagType = ResolveComponentType("DOTSCore.DeadTag");
            var teamIdType = ResolveComponentType("DOTSCore.TeamId");

            int totalWithHealth = 0;
            int aliveCount = 0;
            int deadCount = 0;
            var teamCounts = new Dictionary<byte, int>();

            if (healthType != null && deadTagType != null)
            {
                // Query all entities with Health + DeadTag, ignoring enabled state
                using var query = em.CreateEntityQuery(new EntityQueryDesc
                {
                    All = new[] { healthType.Value, deadTagType.Value },
                    Options = EntityQueryOptions.IgnoreComponentEnabledState
                });
                var entities = query.ToEntityArray(Allocator.Temp);
                totalWithHealth = entities.Length;

                for (int i = 0; i < entities.Length; i++)
                {
                    bool isDead = em.IsComponentEnabled(entities[i], deadTagType.Value);
                    if (isDead) deadCount++;
                    else aliveCount++;

                    if (teamIdType != null && em.HasComponent(entities[i], teamIdType.Value))
                    {
                        var teamObj = em.Debug.GetComponentBoxed(entities[i], teamIdType.Value);
                        if (teamObj != null)
                        {
                            var valueField = teamObj.GetType().GetField("Value");
                            if (valueField != null)
                            {
                                byte team = (byte)valueField.GetValue(teamObj);
                                teamCounts.TryGetValue(team, out int count);
                                teamCounts[team] = count + 1;
                            }
                        }
                    }
                }
                entities.Dispose();
            }

            var result = new Dictionary<string, object>
            {
                ["total"] = totalWithHealth,
                ["alive"] = aliveCount,
                ["dead"] = deadCount,
            };

            if (teamCounts.Count > 0)
            {
                var teams = new Dictionary<string, int>();
                foreach (var kvp in teamCounts)
                    teams[$"team_{kvp.Key}"] = kvp.Value;
                result["by_team"] = teams;
            }

            return result;
        }

        private static Dictionary<string, object> CollectHealthStats(EntityManager em)
        {
            var healthType = ResolveComponentType("DOTSCombat.Health");
            var deadTagType = ResolveComponentType("DOTSCore.DeadTag");

            if (healthType == null)
                return new Dictionary<string, object> { ["error"] = "Health component not found." };

            // Only alive entities
            using var query = em.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[] { healthType.Value },
                None = deadTagType != null ? new[] { deadTagType.Value } : Array.Empty<ComponentType>()
            });

            var entities = query.ToEntityArray(Allocator.Temp);
            if (entities.Length == 0)
            {
                entities.Dispose();
                return new Dictionary<string, object> { ["count"] = 0 };
            }

            float min = float.MaxValue, max = float.MinValue, sum = 0f;
            for (int i = 0; i < entities.Length; i++)
            {
                var obj = em.Debug.GetComponentBoxed(entities[i], healthType.Value);
                if (obj == null) continue;
                var currentField = obj.GetType().GetField("Current");
                if (currentField == null) continue;

                float hp = (float)currentField.GetValue(obj);
                if (hp < min) min = hp;
                if (hp > max) max = hp;
                sum += hp;
            }

            int aliveCount = entities.Length;
            float mean = sum / aliveCount;
            entities.Dispose();

            return new Dictionary<string, object>
            {
                ["count"] = aliveCount,
                ["min"] = Math.Round(min, 1),
                ["max"] = Math.Round(max, 1),
                ["mean"] = Math.Round(mean, 1)
            };
        }

        private static Dictionary<string, object> CollectPositionSamples(EntityManager em, int sampleSize)
        {
            var healthType = ResolveComponentType("DOTSCombat.Health");
            var transformType = ResolveComponentType("Unity.Transforms.LocalTransform");

            if (healthType == null || transformType == null)
                return new Dictionary<string, object> { ["error"] = "Required components not found." };

            using var query = em.CreateEntityQuery(healthType.Value, transformType.Value);
            var entities = query.ToEntityArray(Allocator.Temp);

            int count = Math.Min(sampleSize, entities.Length);
            var samples = new List<object>(count);

            for (int i = 0; i < count; i++)
            {
                var obj = em.Debug.GetComponentBoxed(entities[i], transformType.Value);
                if (obj == null) continue;

                var posField = obj.GetType().GetField("Position");
                if (posField == null) continue;

                var pos = (float3)posField.GetValue(obj);
                samples.Add(new Dictionary<string, object>
                {
                    ["entity_index"] = entities[i].Index,
                    ["x"] = Math.Round(pos.x, 2),
                    ["y"] = Math.Round(pos.y, 2),
                    ["z"] = Math.Round(pos.z, 2)
                });
            }
            entities.Dispose();

            return new Dictionary<string, object>
            {
                ["count"] = samples.Count,
                ["samples"] = samples
            };
        }

        private static Dictionary<string, object> CheckNaNBounds(EntityManager em)
        {
            var boundsType = ResolveComponentType("Unity.Rendering.ChunkWorldRenderBounds");
            if (boundsType == null)
            {
                // Try alternate name
                boundsType = ResolveComponentType("ChunkWorldRenderBounds");
            }

            if (boundsType == null)
                return new Dictionary<string, object> { ["nan_count"] = 0, ["note"] = "ChunkWorldRenderBounds type not found." };

            // ChunkWorldRenderBounds is a chunk component, so we inspect via archetype iteration
            var archetypes = new NativeList<EntityArchetype>(Allocator.Temp);
            em.GetAllArchetypes(archetypes);

            int nanChunkCount = 0;
            int totalChunks = 0;

            // Check via entities that have WorldRenderBounds (per-entity bounds)
            var entityBoundsType = ResolveComponentType("Unity.Rendering.WorldRenderBounds");
            if (entityBoundsType == null)
                entityBoundsType = ResolveComponentType("WorldRenderBounds");

            if (entityBoundsType != null)
            {
                using var query = em.CreateEntityQuery(entityBoundsType.Value);
                var entities = query.ToEntityArray(Allocator.Temp);
                totalChunks = entities.Length;

                for (int i = 0; i < Math.Min(entities.Length, 500); i++)
                {
                    var obj = em.Debug.GetComponentBoxed(entities[i], entityBoundsType.Value);
                    if (obj == null) continue;

                    // WorldRenderBounds has an AABB Value field
                    var valueField = obj.GetType().GetField("Value");
                    if (valueField == null) continue;

                    var aabb = valueField.GetValue(obj);
                    var centerField = aabb.GetType().GetField("Center");
                    var extentsField = aabb.GetType().GetField("Extents");
                    if (centerField == null || extentsField == null) continue;

                    var center = (float3)centerField.GetValue(aabb);
                    var extents = (float3)extentsField.GetValue(aabb);

                    if (math.any(math.isnan(center)) || math.any(math.isnan(extents)) ||
                        math.any(math.isinf(center)) || math.any(math.isinf(extents)))
                    {
                        nanChunkCount++;
                    }
                }
                entities.Dispose();
            }

            archetypes.Dispose();

            return new Dictionary<string, object>
            {
                ["nan_count"] = nanChunkCount,
                ["entities_checked"] = Math.Min(totalChunks, 500)
            };
        }

        private static Dictionary<string, object> CollectRenderingStats()
        {
            double cpuMainMs = 0;
            double renderThreadMs = 0;
            FrameTimingManager.CaptureFrameTimings();
            var timings = new FrameTiming[1];
            uint timingCount = FrameTimingManager.GetLatestTimings(1, timings);
            if (timingCount > 0)
            {
                cpuMainMs = timings[0].cpuFrameTime;
                renderThreadMs = timings[0].cpuRenderThreadFrameTime;
            }

            return new Dictionary<string, object>
            {
                ["fps"] = EditorApplication.isPlaying ? Math.Round(1.0f / Time.unscaledDeltaTime, 1) : 0,
                ["draw_calls"] = UnityStats.drawCalls,
                ["batches"] = UnityStats.batches,
                ["triangles"] = UnityStats.triangles,
                ["shadow_casters"] = UnityStats.shadowCasters,
                ["cpu_main_ms"] = Math.Round(cpuMainMs, 2),
                ["render_thread_ms"] = Math.Round(renderThreadMs, 2)
            };
        }

        private static Dictionary<string, object> CollectBattleState(EntityManager em)
        {
            var battleStateType = ResolveComponentType("DOTSCombat.BattleState");
            if (battleStateType == null)
                return new Dictionary<string, object> { ["found"] = false };

            using var query = em.CreateEntityQuery(battleStateType.Value);
            if (query.CalculateEntityCount() == 0)
                return new Dictionary<string, object> { ["found"] = false, ["note"] = "No BattleState singleton." };

            var entity = query.GetSingletonEntity();
            var obj = em.Debug.GetComponentBoxed(entity, battleStateType.Value);
            if (obj == null)
                return new Dictionary<string, object> { ["found"] = false };

            var type = obj.GetType();
            bool battleOver = (bool)(type.GetField("BattleOver")?.GetValue(obj) ?? false);
            byte winnerTeam = (byte)(type.GetField("WinnerTeam")?.GetValue(obj) ?? (byte)0);
            bool hasStarted = (bool)(type.GetField("HasStarted")?.GetValue(obj) ?? false);

            return new Dictionary<string, object>
            {
                ["found"] = true,
                ["battle_over"] = battleOver,
                ["winner_team"] = winnerTeam,
                ["has_started"] = hasStarted
            };
        }

        #endregion

        #region Helpers

        private static ComponentType? ResolveComponentType(string typeName)
        {
            int typeCount = TypeManager.GetTypeCount();
            for (int i = 1; i < typeCount; i++)
            {
                var typeInfo = TypeManager.GetTypeInfo(i);
                string debugName = typeInfo.DebugTypeName.ToString();

                if (string.Equals(debugName, typeName, StringComparison.OrdinalIgnoreCase) ||
                    debugName.EndsWith("." + typeName, StringComparison.OrdinalIgnoreCase))
                {
                    return ComponentType.FromTypeIndex(typeInfo.TypeIndex);
                }
            }
            return null;
        }

        private static int? GetNestedInt(JObject root, string section, string key)
        {
            return root[section]?[key]?.Value<int>();
        }

        private static float? GetNestedFloat(JObject root, string section, string key)
        {
            return root[section]?[key]?.Value<float>();
        }

        #endregion
    }
}
#endif
