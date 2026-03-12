#if UNITY_TILEMAP
using System;
using System.Collections.Generic;
using System.Globalization;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace MCPForUnity.Editor.Tools
{
    [McpForUnityTool("manage_tilemap", AutoRegister = true)]
    public static class ManageTilemap
    {
        public static object HandleCommand(JObject @params)
        {
            if (@params == null) return new ErrorResponse("Parameters cannot be null.");
            var p = new ToolParams(@params);
            var actionResult = p.GetRequired("action");
            if (!actionResult.IsSuccess) return new ErrorResponse(actionResult.ErrorMessage);
            string action = actionResult.Value.ToLowerInvariant();

            try
            {
                return action switch
                {
                    "list_tilemaps" => ListTilemaps(p),
                    "get_info"      => GetInfo(p),
                    "get_tile"      => GetTile(p),
                    "set_tile"      => SetTile(p),
                    "clear_tile"    => ClearTile(p),
                    "clear_all"     => ClearAll(p),
                    "get_bounds"    => GetBounds(p),
                    "fill_area"     => FillArea(p),
                    _ => new ErrorResponse($"Unknown action: '{action}'.")
                };
            }
            catch (Exception e)
            {
                McpLog.Error($"[ManageTilemap] Action '{action}' failed: {e}");
                return new ErrorResponse($"Internal error: {e.Message}");
            }
        }

        private static object ListTilemaps(ToolParams p)
        {
            var tilemaps = UnityEngine.Object.FindObjectsByType<Tilemap>(FindObjectsSortMode.InstanceID);
            var pagination = PaginationRequest.FromParams(
                new JObject { ["page_size"] = p.GetInt("page_size"), ["cursor"] = p.GetInt("cursor") });

            var allItems = new List<Dictionary<string, object>>();
            foreach (var tm in tilemaps)
            {
                var bounds = tm.cellBounds;
                allItems.Add(new Dictionary<string, object>
                {
                    ["name"] = tm.gameObject.name,
                    ["instance_id"] = tm.gameObject.GetInstanceID(),
                    ["cell_layout"] = tm.layoutGrid != null ? tm.layoutGrid.cellLayout.ToString() : "Unknown",
                    ["size"] = $"{bounds.size.x},{bounds.size.y},{bounds.size.z}",
                });
            }

            var page = PaginationResponse<Dictionary<string, object>>.Create(allItems, pagination);
            return new SuccessResponse($"Found {tilemaps.Length} Tilemap(s).", page);
        }

        private static object GetInfo(ToolParams p)
        {
            var tm = ResolveTilemap(p);
            if (tm == null) return new ErrorResponse("Tilemap not found. Provide 'target' param.");

            var bounds = tm.cellBounds;
            int tileCount = 0;
            foreach (var pos in bounds.allPositionsWithin)
            {
                if (tm.HasTile(pos)) tileCount++;
            }

            return new SuccessResponse($"Tilemap '{tm.gameObject.name}'.", new Dictionary<string, object>
            {
                ["name"] = tm.gameObject.name,
                ["cell_layout"] = tm.layoutGrid != null ? tm.layoutGrid.cellLayout.ToString() : "Unknown",
                ["orientation"] = tm.orientation.ToString(),
                ["size"] = $"{bounds.size.x},{bounds.size.y},{bounds.size.z}",
                ["origin"] = $"{bounds.position.x},{bounds.position.y},{bounds.position.z}",
                ["tile_count"] = tileCount,
                ["tile_anchor"] = $"{tm.tileAnchor.x:F3},{tm.tileAnchor.y:F3},{tm.tileAnchor.z:F3}",
            });
        }

        private static object GetTile(ToolParams p)
        {
            var tm = ResolveTilemap(p);
            if (tm == null) return new ErrorResponse("Tilemap not found.");

            if (!TryParseVector3Int(p.Get("position"), out var pos))
                return new ErrorResponse("'position' parameter is required (e.g. '0,0,0').");

            var tile = tm.GetTile(pos);
            if (tile == null)
                return new SuccessResponse($"No tile at {pos}.", new Dictionary<string, object>
                {
                    ["has_tile"] = false, ["position"] = $"{pos.x},{pos.y},{pos.z}",
                });

            var sprite = tm.GetSprite(pos);
            var color = tm.GetColor(pos);
            var flags = tm.GetTileFlags(pos);

            return new SuccessResponse($"Tile at {pos}.", new Dictionary<string, object>
            {
                ["has_tile"] = true,
                ["position"] = $"{pos.x},{pos.y},{pos.z}",
                ["tile_type"] = tile.GetType().Name,
                ["tile_name"] = tile.name,
                ["sprite"] = sprite != null ? sprite.name : null,
                ["color"] = $"{color.r:F3},{color.g:F3},{color.b:F3},{color.a:F3}",
                ["flags"] = flags.ToString(),
            });
        }

        private static object SetTile(ToolParams p)
        {
            var tm = ResolveTilemap(p);
            if (tm == null) return new ErrorResponse("Tilemap not found.");

            if (!TryParseVector3Int(p.Get("position"), out var pos))
                return new ErrorResponse("'position' parameter is required (e.g. '0,0,0').");

            var assetPath = p.Get("tile_asset");
            if (string.IsNullOrEmpty(assetPath))
                return new ErrorResponse("'tile_asset' parameter is required (asset path).");

            var tile = AssetDatabase.LoadAssetAtPath<TileBase>(assetPath);
            if (tile == null) return new ErrorResponse($"TileBase asset not found at '{assetPath}'.");

            Undo.RecordObject(tm, "MCP SetTile");
            tm.SetTile(pos, tile);
            EditorUtility.SetDirty(tm);

            return new SuccessResponse($"Tile set at {pos}.");
        }

        private static object ClearTile(ToolParams p)
        {
            var tm = ResolveTilemap(p);
            if (tm == null) return new ErrorResponse("Tilemap not found.");

            if (!TryParseVector3Int(p.Get("position"), out var pos))
                return new ErrorResponse("'position' parameter is required (e.g. '0,0,0').");

            Undo.RecordObject(tm, "MCP ClearTile");
            tm.SetTile(pos, null);
            EditorUtility.SetDirty(tm);

            return new SuccessResponse($"Tile cleared at {pos}.");
        }

        private static object ClearAll(ToolParams p)
        {
            var tm = ResolveTilemap(p);
            if (tm == null) return new ErrorResponse("Tilemap not found.");

            Undo.RecordObject(tm, "MCP ClearAll");
            tm.ClearAllTiles();
            EditorUtility.SetDirty(tm);

            return new SuccessResponse($"All tiles cleared on '{tm.gameObject.name}'.");
        }

        private static object GetBounds(ToolParams p)
        {
            var tm = ResolveTilemap(p);
            if (tm == null) return new ErrorResponse("Tilemap not found.");

            var bounds = tm.cellBounds;
            return new SuccessResponse($"Bounds of '{tm.gameObject.name}'.", new Dictionary<string, object>
            {
                ["min"] = $"{bounds.min.x},{bounds.min.y},{bounds.min.z}",
                ["max"] = $"{bounds.max.x},{bounds.max.y},{bounds.max.z}",
                ["size"] = $"{bounds.size.x},{bounds.size.y},{bounds.size.z}",
            });
        }

        private static object FillArea(ToolParams p)
        {
            var tm = ResolveTilemap(p);
            if (tm == null) return new ErrorResponse("Tilemap not found.");

            if (!TryParseVector3Int(p.Get("min"), out var min))
                return new ErrorResponse("'min' parameter is required (e.g. '0,0,0').");
            if (!TryParseVector3Int(p.Get("max"), out var max))
                return new ErrorResponse("'max' parameter is required (e.g. '10,10,0').");

            var assetPath = p.Get("tile_asset");
            if (string.IsNullOrEmpty(assetPath))
                return new ErrorResponse("'tile_asset' parameter is required.");

            var tile = AssetDatabase.LoadAssetAtPath<TileBase>(assetPath);
            if (tile == null) return new ErrorResponse($"TileBase asset not found at '{assetPath}'.");

            Undo.RecordObject(tm, "MCP FillArea");
            int count = 0;
            for (int x = min.x; x <= max.x; x++)
            for (int y = min.y; y <= max.y; y++)
            for (int z = min.z; z <= max.z; z++)
            {
                tm.SetTile(new Vector3Int(x, y, z), tile);
                count++;
            }
            EditorUtility.SetDirty(tm);

            return new SuccessResponse($"Filled {count} cells on '{tm.gameObject.name}'.");
        }

        #region Helpers

        private static Tilemap ResolveTilemap(ToolParams p)
        {
            string target = p.Get("target");
            if (string.IsNullOrEmpty(target)) return null;
            var go = ObjectResolver.ResolveGameObject(new JValue(target));
            return go != null ? go.GetComponent<Tilemap>() : null;
        }

        private static bool TryParseVector3Int(string str, out Vector3Int result)
        {
            result = Vector3Int.zero;
            if (string.IsNullOrEmpty(str)) return false;
            var parts = str.Split(',');
            if (parts.Length < 2) return false;
            if (int.TryParse(parts[0].Trim(), out int x) &&
                int.TryParse(parts[1].Trim(), out int y))
            {
                int z = 0;
                if (parts.Length >= 3) int.TryParse(parts[2].Trim(), out z);
                result = new Vector3Int(x, y, z);
                return true;
            }
            return false;
        }

        #endregion
    }
}
#endif
