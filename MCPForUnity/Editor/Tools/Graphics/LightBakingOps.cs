using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace MCPForUnity.Editor.Tools.Graphics
{
    internal static class LightBakingOps
    {
        // === bake_start ===
        // Params: async (bool, default true)
        internal static object StartBake(JObject @params)
        {
            if (Application.isPlaying)
                return new ErrorResponse("Light baking requires Edit mode.");

            var p = new ToolParams(@params);
            bool async_ = p.GetBool("async", true);

            if (async_)
            {
                Lightmapping.BakeAsync();
                return new PendingResponse(
                    "Light bake started (async). Use bake_get_status to check progress.",
                    pollIntervalSeconds: 2.0,
                    data: new { mode = "async" }
                );
            }

            Lightmapping.Bake();
            return new
            {
                success = true,
                message = "Light bake completed (synchronous).",
                data = new
                {
                    mode = "sync",
                    lightmapCount = LightmapSettings.lightmaps.Length
                }
            };
        }

        // === bake_cancel ===
        internal static object CancelBake(JObject @params)
        {
            Lightmapping.Cancel();
            return new
            {
                success = true,
                message = "Light bake cancelled."
            };
        }

        // === bake_get_status ===
        internal static object GetStatus(JObject @params)
        {
            return new
            {
                success = true,
                message = Lightmapping.isRunning ? "Light bake in progress." : "No bake running.",
                data = new
                {
                    isRunning = Lightmapping.isRunning,
                    bakedGI = Lightmapping.bakedGI,
                    realtimeGI = Lightmapping.realtimeGI,
                    lightmapCount = LightmapSettings.lightmaps.Length
                }
            };
        }

        // === bake_clear ===
        internal static object ClearBake(JObject @params)
        {
            Lightmapping.Clear();
            Lightmapping.ClearLightingDataAsset();
            return new
            {
                success = true,
                message = "Cleared all baked lighting data and lighting data asset."
            };
        }

        // === bake_reflection_probe ===
        // Params: target (name or instanceID of GameObject with ReflectionProbe)
        internal static object BakeReflectionProbe(JObject @params)
        {
            if (Application.isPlaying)
                return new ErrorResponse("Reflection probe baking requires Edit mode.");

            var p = new ToolParams(@params);
            string target = p.Get("target");
            if (string.IsNullOrEmpty(target))
                return new ErrorResponse("'target' parameter is required (name or instanceID of a GameObject with ReflectionProbe).");

            var go = FindGameObject(target);
            if (go == null)
                return new ErrorResponse($"GameObject '{target}' not found.");

            var probe = go.GetComponent<ReflectionProbe>();
            if (probe == null)
                return new ErrorResponse($"GameObject '{go.name}' does not have a ReflectionProbe component.");

            string dir = "Assets/Lightmaps";
            if (!AssetDatabase.IsValidFolder(dir))
                AssetDatabase.CreateFolder("Assets", "Lightmaps");

            string outputPath = $"{dir}/{probe.name}_ReflectionProbe.exr";

            bool result = Lightmapping.BakeReflectionProbe(probe, outputPath);
            if (!result)
                return new ErrorResponse($"Failed to bake reflection probe '{probe.name}'.");

            return new
            {
                success = true,
                message = $"Baked reflection probe '{probe.name}' to '{outputPath}'.",
                data = new
                {
                    probeName = probe.name,
                    outputPath,
                    instanceID = go.GetInstanceID()
                }
            };
        }

        // === bake_get_settings ===
        internal static object GetSettings(JObject @params)
        {
            var settings = Lightmapping.lightingSettings;
            if (settings == null)
                return new ErrorResponse(
                    "No LightingSettings asset found. Create one in Window > Rendering > Lighting.");

            var data = new Dictionary<string, object>
            {
                ["name"] = settings.name,
                ["path"] = AssetDatabase.GetAssetPath(settings),
                ["bakedGI"] = settings.bakedGI,
                ["realtimeGI"] = settings.realtimeGI,
                ["lightmapper"] = settings.lightmapper.ToString(),
                ["lightmapResolution"] = settings.lightmapResolution,
                ["lightmapMaxSize"] = settings.lightmapMaxSize,
                ["directSampleCount"] = settings.directSampleCount,
                ["indirectSampleCount"] = settings.indirectSampleCount,
                ["environmentSampleCount"] = settings.environmentSampleCount,
                ["mixedBakeMode"] = settings.mixedBakeMode.ToString(),
                ["compressLightmaps"] = settings.compressLightmaps,
                ["ao"] = settings.ao,
                ["aoMaxDistance"] = settings.aoMaxDistance
            };

            // bounceCount vs maxBounces — name varies by Unity version
            ReadBounceCount(settings, data);

            return new
            {
                success = true,
                message = $"Lighting settings: {settings.lightmapper}, resolution {settings.lightmapResolution}.",
                data
            };
        }

        // === bake_set_settings ===
        // Params: settings (dict of property name -> value)
        internal static object SetSettings(JObject @params)
        {
            var p = new ToolParams(@params);
            var settingsToken = p.GetRaw("settings") as JObject;
            if (settingsToken == null || !settingsToken.HasValues)
                return new ErrorResponse("'settings' parameter is required (dict of property name to value).");

            var lightingSettings = Lightmapping.lightingSettings;
            if (lightingSettings == null)
                return new ErrorResponse(
                    "No LightingSettings asset found. Create one in Window > Rendering > Lighting.");

            Undo.RecordObject(lightingSettings, "Modify Lighting Settings");

            var changed = new List<string>();
            var failed = new List<string>();

            foreach (var prop in settingsToken.Properties())
            {
                string name = prop.Name;
                JToken value = prop.Value;

                try
                {
                    if (TrySetLightingSetting(lightingSettings, name, value))
                        changed.Add(name);
                    else
                        failed.Add(name);
                }
                catch (Exception ex)
                {
                    McpLog.Warn($"[LightBakingOps] Failed to set '{name}': {ex.Message}");
                    failed.Add(name);
                }
            }

            if (changed.Count == 0 && failed.Count > 0)
                return new ErrorResponse($"Failed to set any settings. Invalid properties: {string.Join(", ", failed)}");

            EditorUtility.SetDirty(lightingSettings);

            var msg = $"Updated {changed.Count} lighting setting(s)";
            if (failed.Count > 0)
                msg += $". Failed: {string.Join(", ", failed)}";

            return new
            {
                success = true,
                message = msg,
                data = new { changed, failed }
            };
        }

        // === bake_create_light_probe_group ===
        // Params: name, position, grid_size, spacing
        internal static object CreateLightProbeGroup(JObject @params)
        {
            var p = new ToolParams(@params);
            string name = p.Get("name") ?? "Light Probes";
            float spacing = p.GetFloat("spacing") ?? 2.0f;

            var posToken = p.GetRaw("position") as JArray;
            Vector3 position = posToken != null && posToken.Count >= 3
                ? new Vector3(posToken[0].Value<float>(), posToken[1].Value<float>(), posToken[2].Value<float>())
                : Vector3.zero;

            var gridToken = p.GetRaw("grid_size") as JArray;
            int gridX = gridToken != null && gridToken.Count >= 1 ? gridToken[0].Value<int>() : 3;
            int gridY = gridToken != null && gridToken.Count >= 2 ? gridToken[1].Value<int>() : 2;
            int gridZ = gridToken != null && gridToken.Count >= 3 ? gridToken[2].Value<int>() : 3;

            var go = new GameObject(name);
            go.transform.position = position;
            Undo.RegisterCreatedObjectUndo(go, $"Create Light Probe Group '{name}'");

            var probeGroup = go.AddComponent<LightProbeGroup>();

            var positions = new List<Vector3>();
            float halfX = (gridX - 1) * spacing * 0.5f;
            float halfY = (gridY - 1) * spacing * 0.5f;
            float halfZ = (gridZ - 1) * spacing * 0.5f;

            for (int x = 0; x < gridX; x++)
            {
                for (int y = 0; y < gridY; y++)
                {
                    for (int z = 0; z < gridZ; z++)
                    {
                        positions.Add(new Vector3(
                            x * spacing - halfX,
                            y * spacing - halfY,
                            z * spacing - halfZ
                        ));
                    }
                }
            }

            probeGroup.probePositions = positions.ToArray();
            GraphicsHelpers.MarkDirty(probeGroup);

            return new
            {
                success = true,
                message = $"Created Light Probe Group '{name}' with {positions.Count} probes ({gridX}x{gridY}x{gridZ} grid, spacing {spacing}).",
                data = new
                {
                    instanceID = go.GetInstanceID(),
                    probeCount = positions.Count,
                    gridSize = new[] { gridX, gridY, gridZ },
                    spacing,
                    position = new[] { position.x, position.y, position.z }
                }
            };
        }

        // === bake_create_reflection_probe ===
        // Params: name, position, size, resolution, mode, hdr, box_projection
        internal static object CreateReflectionProbe(JObject @params)
        {
            var p = new ToolParams(@params);
            string name = p.Get("name") ?? "Reflection Probe";
            int resolution = p.GetInt("resolution") ?? 256;
            bool hdr = p.GetBool("hdr", true);
            bool boxProjection = p.GetBool("box_projection", false);
            string modeStr = p.Get("mode") ?? "Baked";

            var posToken = p.GetRaw("position") as JArray;
            Vector3 position = posToken != null && posToken.Count >= 3
                ? new Vector3(posToken[0].Value<float>(), posToken[1].Value<float>(), posToken[2].Value<float>())
                : Vector3.zero;

            var sizeToken = p.GetRaw("size") as JArray;
            Vector3 size = sizeToken != null && sizeToken.Count >= 3
                ? new Vector3(sizeToken[0].Value<float>(), sizeToken[1].Value<float>(), sizeToken[2].Value<float>())
                : new Vector3(10f, 10f, 10f);

            if (!Enum.TryParse<ReflectionProbeMode>(modeStr, true, out var mode))
                return new ErrorResponse(
                    $"Invalid mode '{modeStr}'. Valid values: Baked, Realtime, Custom.");

            var go = new GameObject(name);
            go.transform.position = position;
            Undo.RegisterCreatedObjectUndo(go, $"Create Reflection Probe '{name}'");

            var probe = go.AddComponent<ReflectionProbe>();
            probe.size = size;
            probe.resolution = resolution;
            probe.mode = mode;
            probe.hdr = hdr;
            probe.boxProjection = boxProjection;

            GraphicsHelpers.MarkDirty(probe);

            return new
            {
                success = true,
                message = $"Created Reflection Probe '{name}' (mode: {mode}, resolution: {resolution}, HDR: {hdr}).",
                data = new
                {
                    instanceID = go.GetInstanceID(),
                    mode = mode.ToString(),
                    resolution,
                    hdr,
                    boxProjection,
                    size = new[] { size.x, size.y, size.z },
                    position = new[] { position.x, position.y, position.z }
                }
            };
        }

        // === bake_set_probe_positions ===
        // Params: target (name/instanceID), positions (array of [x,y,z])
        internal static object SetProbePositions(JObject @params)
        {
            var p = new ToolParams(@params);
            string target = p.Get("target");
            if (string.IsNullOrEmpty(target))
                return new ErrorResponse("'target' parameter is required (name or instanceID of a GameObject with LightProbeGroup).");

            var go = FindGameObject(target);
            if (go == null)
                return new ErrorResponse($"GameObject '{target}' not found.");

            var probeGroup = go.GetComponent<LightProbeGroup>();
            if (probeGroup == null)
                return new ErrorResponse($"GameObject '{go.name}' does not have a LightProbeGroup component.");

            var positionsToken = p.GetRaw("positions") as JArray;
            if (positionsToken == null || positionsToken.Count == 0)
                return new ErrorResponse("'positions' parameter is required (array of [x,y,z] arrays).");

            Undo.RecordObject(probeGroup, "Set Light Probe Positions");

            var positions = new Vector3[positionsToken.Count];
            for (int i = 0; i < positionsToken.Count; i++)
            {
                var arr = positionsToken[i] as JArray;
                if (arr == null || arr.Count < 3)
                    return new ErrorResponse($"Position at index {i} must be an array of [x, y, z].");
                positions[i] = new Vector3(
                    arr[0].Value<float>(),
                    arr[1].Value<float>(),
                    arr[2].Value<float>()
                );
            }

            probeGroup.probePositions = positions;
            GraphicsHelpers.MarkDirty(probeGroup);

            return new
            {
                success = true,
                message = $"Set {positions.Length} probe positions on '{go.name}'.",
                data = new
                {
                    instanceID = go.GetInstanceID(),
                    probeCount = positions.Length
                }
            };
        }

        // --- Helper: Find a GameObject by name or instanceID ---
        private static GameObject FindGameObject(string target)
        {
            if (string.IsNullOrEmpty(target))
                return null;

            if (int.TryParse(target, out int instanceId))
            {
                var byId = EditorUtility.InstanceIDToObject(instanceId) as GameObject;
                if (byId != null) return byId;
            }

            return GameObject.Find(target);
        }

        // --- Helper: Read bounceCount with version fallback ---
        private static void ReadBounceCount(LightingSettings settings, Dictionary<string, object> data)
        {
            var type = typeof(LightingSettings);

            // Try bounceCount first (Unity 2022+)
            var prop = type.GetProperty("bounceCount", BindingFlags.Public | BindingFlags.Instance);
            if (prop != null)
            {
                data["bounceCount"] = prop.GetValue(settings);
                return;
            }

            // Fallback to maxBounces (older Unity versions)
            prop = type.GetProperty("maxBounces", BindingFlags.Public | BindingFlags.Instance);
            if (prop != null)
                data["maxBounces"] = prop.GetValue(settings);
        }

        // --- Helper: Set a single lighting setting by name ---
        private static bool TrySetLightingSetting(LightingSettings settings, string name, JToken value)
        {
            switch (name.ToLowerInvariant())
            {
                case "bakedgi":
                case "baked_gi":
                    settings.bakedGI = ParamCoercion.CoerceBool(value, settings.bakedGI);
                    return true;

                case "realtimegi":
                case "realtime_gi":
                    settings.realtimeGI = ParamCoercion.CoerceBool(value, settings.realtimeGI);
                    return true;

                case "lightmapper":
                    if (TryParseEnum<LightingSettings.Lightmapper>(value, out var lm))
                    {
                        settings.lightmapper = lm;
                        return true;
                    }
                    return false;

                case "lightmapresolution":
                case "lightmap_resolution":
                    settings.lightmapResolution = ParamCoercion.CoerceFloat(value, settings.lightmapResolution);
                    return true;

                case "lightmapmaxsize":
                case "lightmap_max_size":
                    settings.lightmapMaxSize = ParamCoercion.CoerceInt(value, settings.lightmapMaxSize);
                    return true;

                case "directsamplecount":
                case "direct_sample_count":
                    settings.directSampleCount = ParamCoercion.CoerceInt(value, settings.directSampleCount);
                    return true;

                case "indirectsamplecount":
                case "indirect_sample_count":
                    settings.indirectSampleCount = ParamCoercion.CoerceInt(value, settings.indirectSampleCount);
                    return true;

                case "environmentsamplecount":
                case "environment_sample_count":
                    settings.environmentSampleCount = ParamCoercion.CoerceInt(value, settings.environmentSampleCount);
                    return true;

                case "bouncecount":
                case "bounce_count":
                case "maxbounces":
                case "max_bounces":
                    return TrySetBounceCount(settings, ParamCoercion.CoerceInt(value, 2));

                case "mixedbakemode":
                case "mixed_bake_mode":
                    if (TryParseEnum<MixedLightingMode>(value, out var mlm))
                    {
                        settings.mixedBakeMode = mlm;
                        return true;
                    }
                    return false;

                case "compresslightmaps":
                case "compress_lightmaps":
                    settings.compressLightmaps = ParamCoercion.CoerceBool(value, settings.compressLightmaps);
                    return true;

                case "ao":
                    settings.ao = ParamCoercion.CoerceBool(value, settings.ao);
                    return true;

                case "aomaxdistance":
                case "ao_max_distance":
                    settings.aoMaxDistance = ParamCoercion.CoerceFloat(value, settings.aoMaxDistance);
                    return true;

                default:
                    return false;
            }
        }

        // --- Helper: Set bounceCount with version fallback ---
        private static bool TrySetBounceCount(LightingSettings settings, int value)
        {
            var type = typeof(LightingSettings);

            // Try bounceCount first (Unity 2022+)
            var prop = type.GetProperty("bounceCount", BindingFlags.Public | BindingFlags.Instance);
            if (prop != null && prop.CanWrite)
            {
                prop.SetValue(settings, value);
                return true;
            }

            // Fallback to maxBounces (older Unity versions)
            prop = type.GetProperty("maxBounces", BindingFlags.Public | BindingFlags.Instance);
            if (prop != null && prop.CanWrite)
            {
                prop.SetValue(settings, value);
                return true;
            }

            return false;
        }

        // --- Helper: Parse enum from JToken (string name or int value) ---
        private static bool TryParseEnum<T>(JToken value, out T result) where T : struct, Enum
        {
            result = default;
            if (value == null || value.Type == JTokenType.Null) return false;

            string str = value.ToString();

            // Try parse by name
            if (Enum.TryParse(str, true, out result))
                return true;

            // Try parse by int value
            if (int.TryParse(str, out int intVal))
            {
                result = (T)Enum.ToObject(typeof(T), intVal);
                return true;
            }

            return false;
        }
    }
}
