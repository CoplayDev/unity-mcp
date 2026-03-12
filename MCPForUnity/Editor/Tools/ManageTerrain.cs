using System;
using System.Collections.Generic;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace MCPForUnity.Editor.Tools
{
    /// <summary>
    /// MCP tool for Unity Terrain inspection and modification.
    /// Actions: get_info, get_height, set_heights, flatten,
    ///          get_splat_weights, paint_texture, get_heightmap_sample
    /// </summary>
    [McpForUnityTool("manage_terrain", AutoRegister = true)]
    public static class ManageTerrain
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
                    "get_info"              => GetInfo(p),
                    "get_height"            => GetHeight(p),
                    "set_heights"           => SetHeights(p),
                    "flatten"               => Flatten(p),
                    "get_splat_weights"     => GetSplatWeights(p),
                    "paint_texture"         => PaintTexture(p),
                    "get_heightmap_sample"  => GetHeightmapSample(p),
                    _ => new ErrorResponse(
                        $"Unknown action: '{action}'. Supported: get_info, get_height, set_heights, " +
                        "flatten, get_splat_weights, paint_texture, get_heightmap_sample")
                };
            }
            catch (Exception e)
            {
                McpLog.Error($"[ManageTerrain] Action '{action}' failed: {e}");
                return new ErrorResponse($"Internal error processing action '{action}': {e.Message}");
            }
        }

        #region Terrain Actions

        private static object GetInfo(ToolParams p)
        {
            var terrain = ResolveTerrain(p);
            if (terrain == null)
                return new ErrorResponse("No Terrain found. Ensure a Terrain exists in the scene.");

            var td = terrain.terrainData;
            return new SuccessResponse($"Terrain '{terrain.name}' info.", new Dictionary<string, object>
            {
                ["name"]                  = terrain.name,
                ["heightmap_resolution"]  = td.heightmapResolution,
                ["size_x"]                = td.size.x,
                ["size_y"]                = td.size.y,
                ["size_z"]                = td.size.z,
                ["splat_prototype_count"] = td.terrainLayers.Length,
                ["tree_instance_count"]   = td.treeInstanceCount,
                ["detail_resolution"]     = td.detailResolution,
                ["alphamap_resolution"]   = td.alphamapResolution
            });
        }

        private static object GetHeight(ToolParams p)
        {
            var terrain = ResolveTerrain(p);
            if (terrain == null)
                return new ErrorResponse("No Terrain found.");

            float? worldX = p.GetFloat("x");
            float? worldZ = p.GetFloat("z");
            if (worldX == null) return new ErrorResponse("'x' parameter is required.");
            if (worldZ == null) return new ErrorResponse("'z' parameter is required.");

            float height = terrain.SampleHeight(new Vector3(worldX.Value, 0f, worldZ.Value));
            return new SuccessResponse(
                $"Height at ({worldX.Value:F2}, {worldZ.Value:F2}) = {height:F4}.",
                new Dictionary<string, object>
                {
                    ["world_x"] = worldX.Value,
                    ["world_z"] = worldZ.Value,
                    ["height"]  = height
                });
        }

        private static object SetHeights(ToolParams p)
        {
            var terrain = ResolveTerrain(p);
            if (terrain == null)
                return new ErrorResponse("No Terrain found.");

            float? worldX  = p.GetFloat("x");
            float? worldZ  = p.GetFloat("z");
            float? radius  = p.GetFloat("radius");
            float? height  = p.GetFloat("height");
            string mode    = (p.Get("mode") ?? "set").ToLowerInvariant();

            if (worldX == null)  return new ErrorResponse("'x' parameter is required.");
            if (worldZ == null)  return new ErrorResponse("'z' parameter is required.");
            if (radius == null)  return new ErrorResponse("'radius' parameter is required.");
            if (height == null)  return new ErrorResponse("'height' parameter is required.");

            float clampedHeight = Mathf.Clamp01(height.Value);

            var td = terrain.terrainData;
            int res = td.heightmapResolution;
            Vector3 tPos = terrain.transform.position;

            // Center pixel in heightmap space
            int cX = WorldToHeightmapX(worldX.Value, tPos.x, td.size.x, res);
            int cZ = WorldToHeightmapZ(worldZ.Value, tPos.z, td.size.z, res);

            // Pixel radius
            float pixelsPerMeterX = (res - 1) / td.size.x;
            float pixelsPerMeterZ = (res - 1) / td.size.z;
            int pRadX = Mathf.Max(1, Mathf.CeilToInt(radius.Value * pixelsPerMeterX));
            int pRadZ = Mathf.Max(1, Mathf.CeilToInt(radius.Value * pixelsPerMeterZ));

            // Patch bounds (clamped to terrain)
            int minX = Mathf.Clamp(cX - pRadX, 0, res - 1);
            int minZ = Mathf.Clamp(cZ - pRadZ, 0, res - 1);
            int maxX = Mathf.Clamp(cX + pRadX, 0, res - 1);
            int maxZ = Mathf.Clamp(cZ + pRadZ, 0, res - 1);
            int patchW = maxX - minX + 1;
            int patchH = maxZ - minZ + 1;

            // Read existing heights for the patch
            float[,] existing = td.GetHeights(minX, minZ, patchW, patchH);
            float[,] patch = new float[patchH, patchW];

            for (int iz = 0; iz < patchH; iz++)
            {
                for (int ix = 0; ix < patchW; ix++)
                {
                    float dx = (minX + ix - cX) / (float)pRadX;
                    float dz = (minZ + iz - cZ) / (float)pRadZ;
                    float dist = Mathf.Sqrt(dx * dx + dz * dz);
                    float falloff = Mathf.Clamp01(1f - dist);

                    float current = existing[iz, ix];
                    patch[iz, ix] = mode switch
                    {
                        "raise"  => current + (clampedHeight * falloff),
                        "lower"  => current - (clampedHeight * falloff),
                        "smooth" => Mathf.Lerp(current, clampedHeight, falloff * 0.5f),
                        _        => Mathf.Lerp(current, clampedHeight, falloff) // "set"
                    };
                    patch[iz, ix] = Mathf.Clamp01(patch[iz, ix]);
                }
            }

            td.SetHeights(minX, minZ, patch);
            terrain.Flush();

            return new SuccessResponse(
                $"Applied '{mode}' heights at ({worldX.Value:F2}, {worldZ.Value:F2}) radius={radius.Value:F2}.",
                new Dictionary<string, object>
                {
                    ["world_x"]    = worldX.Value,
                    ["world_z"]    = worldZ.Value,
                    ["radius"]     = radius.Value,
                    ["height"]     = clampedHeight,
                    ["mode"]       = mode,
                    ["patch_w"]    = patchW,
                    ["patch_h"]    = patchH
                });
        }

        private static object Flatten(ToolParams p)
        {
            var terrain = ResolveTerrain(p);
            if (terrain == null)
                return new ErrorResponse("No Terrain found.");

            float? height = p.GetFloat("height");
            if (height == null) return new ErrorResponse("'height' parameter is required (normalized 0-1).");

            float clampedHeight = Mathf.Clamp01(height.Value);
            var td = terrain.terrainData;
            int res = td.heightmapResolution;

            float[,] heights = new float[res, res];
            for (int z = 0; z < res; z++)
                for (int x = 0; x < res; x++)
                    heights[z, x] = clampedHeight;

            td.SetHeights(0, 0, heights);
            terrain.Flush();

            return new SuccessResponse(
                $"Flattened terrain '{terrain.name}' to height {clampedHeight:F4} (normalized).",
                new Dictionary<string, object>
                {
                    ["terrain"] = terrain.name,
                    ["height"]  = clampedHeight,
                    ["resolution"] = res
                });
        }

        private static object GetSplatWeights(ToolParams p)
        {
            var terrain = ResolveTerrain(p);
            if (terrain == null)
                return new ErrorResponse("No Terrain found.");

            float? worldX = p.GetFloat("x");
            float? worldZ = p.GetFloat("z");
            if (worldX == null) return new ErrorResponse("'x' parameter is required.");
            if (worldZ == null) return new ErrorResponse("'z' parameter is required.");

            var td = terrain.terrainData;
            Vector3 tPos = terrain.transform.position;

            // Convert world to alphamap coords
            int aRes = td.alphamapResolution;
            int ax = Mathf.Clamp(Mathf.RoundToInt((worldX.Value - tPos.x) / td.size.x * (aRes - 1)), 0, aRes - 1);
            int az = Mathf.Clamp(Mathf.RoundToInt((worldZ.Value - tPos.z) / td.size.z * (aRes - 1)), 0, aRes - 1);

            float[,,] alphas = td.GetAlphamaps(ax, az, 1, 1);
            int layerCount = td.terrainLayers.Length;

            var weights = new List<object>();
            for (int i = 0; i < layerCount; i++)
            {
                string layerName = td.terrainLayers[i] != null ? td.terrainLayers[i].name : $"Layer{i}";
                weights.Add(new Dictionary<string, object>
                {
                    ["layer_index"] = i,
                    ["name"]        = layerName,
                    ["weight"]      = alphas[0, 0, i]
                });
            }

            return new SuccessResponse(
                $"Splat weights at ({worldX.Value:F2}, {worldZ.Value:F2}).",
                new Dictionary<string, object>
                {
                    ["world_x"]     = worldX.Value,
                    ["world_z"]     = worldZ.Value,
                    ["layer_count"] = layerCount,
                    ["weights"]     = weights
                });
        }

        private static object PaintTexture(ToolParams p)
        {
            var terrain = ResolveTerrain(p);
            if (terrain == null)
                return new ErrorResponse("No Terrain found.");

            float? worldX       = p.GetFloat("x");
            float? worldZ       = p.GetFloat("z");
            float? radius       = p.GetFloat("radius");
            int?   layerIndex   = p.GetInt("layer_index");
            float? strength     = p.GetFloat("strength");

            if (worldX == null)     return new ErrorResponse("'x' parameter is required.");
            if (worldZ == null)     return new ErrorResponse("'z' parameter is required.");
            if (radius == null)     return new ErrorResponse("'radius' parameter is required.");
            if (layerIndex == null) return new ErrorResponse("'layer_index' parameter is required.");
            if (strength == null)   return new ErrorResponse("'strength' parameter is required.");

            var td = terrain.terrainData;
            int layerCount = td.terrainLayers.Length;

            if (layerIndex.Value < 0 || layerIndex.Value >= layerCount)
                return new ErrorResponse($"'layer_index' {layerIndex.Value} out of range (0-{layerCount - 1}).");

            float clampedStrength = Mathf.Clamp01(strength.Value);
            Vector3 tPos = terrain.transform.position;

            int aRes = td.alphamapResolution;
            int cX = Mathf.Clamp(Mathf.RoundToInt((worldX.Value - tPos.x) / td.size.x * (aRes - 1)), 0, aRes - 1);
            int cZ = Mathf.Clamp(Mathf.RoundToInt((worldZ.Value - tPos.z) / td.size.z * (aRes - 1)), 0, aRes - 1);

            float pixelsPerMeterX = (aRes - 1) / td.size.x;
            float pixelsPerMeterZ = (aRes - 1) / td.size.z;
            int pRadX = Mathf.Max(1, Mathf.CeilToInt(radius.Value * pixelsPerMeterX));
            int pRadZ = Mathf.Max(1, Mathf.CeilToInt(radius.Value * pixelsPerMeterZ));

            int minX = Mathf.Clamp(cX - pRadX, 0, aRes - 1);
            int minZ = Mathf.Clamp(cZ - pRadZ, 0, aRes - 1);
            int maxX = Mathf.Clamp(cX + pRadX, 0, aRes - 1);
            int maxZ = Mathf.Clamp(cZ + pRadZ, 0, aRes - 1);
            int patchW = maxX - minX + 1;
            int patchH = maxZ - minZ + 1;

            float[,,] alphas = td.GetAlphamaps(minX, minZ, patchW, patchH);

            for (int iz = 0; iz < patchH; iz++)
            {
                for (int ix = 0; ix < patchW; ix++)
                {
                    float dx = (minX + ix - cX) / (float)pRadX;
                    float dz = (minZ + iz - cZ) / (float)pRadZ;
                    float dist = Mathf.Sqrt(dx * dx + dz * dz);
                    float falloff = Mathf.Clamp01(1f - dist);
                    float blendAmount = clampedStrength * falloff;

                    // Blend target layer weight up, redistribute remaining layers
                    float currentTarget = alphas[iz, ix, layerIndex.Value];
                    float newTarget = Mathf.Clamp01(currentTarget + blendAmount * (1f - currentTarget));
                    float delta = newTarget - currentTarget;

                    // Reduce other layers proportionally
                    float otherSum = 1f - currentTarget;
                    alphas[iz, ix, layerIndex.Value] = newTarget;
                    if (otherSum > 0f)
                    {
                        for (int l = 0; l < layerCount; l++)
                        {
                            if (l == layerIndex.Value) continue;
                            alphas[iz, ix, l] = Mathf.Clamp01(alphas[iz, ix, l] - delta * (alphas[iz, ix, l] / otherSum));
                        }
                    }
                }
            }

            td.SetAlphamaps(minX, minZ, alphas);

            return new SuccessResponse(
                $"Painted layer {layerIndex.Value} at ({worldX.Value:F2}, {worldZ.Value:F2}) strength={clampedStrength:F2} radius={radius.Value:F2}.",
                new Dictionary<string, object>
                {
                    ["world_x"]     = worldX.Value,
                    ["world_z"]     = worldZ.Value,
                    ["radius"]      = radius.Value,
                    ["layer_index"] = layerIndex.Value,
                    ["strength"]    = clampedStrength
                });
        }

        private static object GetHeightmapSample(ToolParams p)
        {
            var terrain = ResolveTerrain(p);
            if (terrain == null)
                return new ErrorResponse("No Terrain found.");

            float? worldX = p.GetFloat("x");
            float? worldZ = p.GetFloat("z");
            int?   size   = p.GetInt("size");

            if (worldX == null) return new ErrorResponse("'x' parameter is required.");
            if (worldZ == null) return new ErrorResponse("'z' parameter is required.");
            if (size == null)   return new ErrorResponse("'size' parameter is required.");

            int clampedSize = Mathf.Clamp(size.Value, 1, 64);

            var td = terrain.terrainData;
            int res = td.heightmapResolution;
            Vector3 tPos = terrain.transform.position;

            int cX = WorldToHeightmapX(worldX.Value, tPos.x, td.size.x, res);
            int cZ = WorldToHeightmapZ(worldZ.Value, tPos.z, td.size.z, res);

            int half = clampedSize / 2;
            int startX = Mathf.Clamp(cX - half, 0, res - 1);
            int startZ = Mathf.Clamp(cZ - half, 0, res - 1);
            int actualW = Mathf.Min(clampedSize, res - startX);
            int actualH = Mathf.Min(clampedSize, res - startZ);

            float[,] patch = td.GetHeights(startX, startZ, actualW, actualH);

            // Flatten to rows for JSON serialization
            var rows = new List<List<float>>();
            for (int iz = 0; iz < actualH; iz++)
            {
                var row = new List<float>();
                for (int ix = 0; ix < actualW; ix++)
                    row.Add(patch[iz, ix]);
                rows.Add(row);
            }

            return new SuccessResponse(
                $"Heightmap sample ({actualW}x{actualH}) around ({worldX.Value:F2}, {worldZ.Value:F2}).",
                new Dictionary<string, object>
                {
                    ["world_x"]  = worldX.Value,
                    ["world_z"]  = worldZ.Value,
                    ["start_hm_x"] = startX,
                    ["start_hm_z"] = startZ,
                    ["width"]    = actualW,
                    ["height"]   = actualH,
                    ["rows"]     = rows
                });
        }

        #endregion

        #region Helpers

        private static Terrain ResolveTerrain(ToolParams p)
        {
            string target = p.Get("target", null);
            if (!string.IsNullOrEmpty(target))
            {
                GameObject go = null;
                if (int.TryParse(target, out int id))
                    go = UnityEditor.EditorUtility.EntityIdToObject(id) as GameObject;
                if (go == null)
                    go = GameObject.Find(target);
                if (go != null)
                    return go.GetComponent<Terrain>();
            }
            return Terrain.activeTerrain;
        }

        private static int WorldToHeightmapX(float worldX, float terrainX, float sizeX, int resolution)
        {
            return Mathf.Clamp(
                Mathf.RoundToInt((worldX - terrainX) / sizeX * (resolution - 1)),
                0, resolution - 1);
        }

        private static int WorldToHeightmapZ(float worldZ, float terrainZ, float sizeZ, int resolution)
        {
            return Mathf.Clamp(
                Mathf.RoundToInt((worldZ - terrainZ) / sizeZ * (resolution - 1)),
                0, resolution - 1);
        }

        #endregion
    }
}
