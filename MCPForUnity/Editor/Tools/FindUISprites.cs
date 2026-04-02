using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using MCPForUnity.Editor.Helpers;

namespace MCPForUnity.Editor.Tools
{
    [McpForUnityTool("find_ui_sprites", AutoRegister = false)]
    public static class FindUISprites
    {
        public static object HandleCommand(JObject @params)
        {
            try
            {
                string searchPattern = @params["searchPattern"]?.ToString() ?? "";
                string pathScope = @params["path"]?.ToString() ?? "Assets";
                bool only9Slice = @params["only9Slice"]?.ToObject<bool>() ?? false;
                int limit = @params["limit"]?.ToObject<int>() ?? 20;

                string filter = "t:Sprite";
                if (!string.IsNullOrEmpty(searchPattern))
                {
                    filter += " " + searchPattern;
                }

                string[] folderScope = new string[] { AssetPathUtility.SanitizeAssetPath(pathScope) };
                string[] guids = AssetDatabase.FindAssets(filter, folderScope);

                var results = new List<object>();
                int count = 0;

                foreach (var guid in guids)
                {
                    if (count >= limit) break;

                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);

                    if (sprite == null) continue;

                    // Check for 9-slice (has borders)
                    bool hasBorder = sprite.border != Vector4.zero;

                    if (only9Slice && !hasBorder) continue;

                    results.Add(new
                    {
                        name = sprite.name,
                        path = path,
                        guid = guid,
                        width = sprite.rect.width,
                        height = sprite.rect.height,
                        border = new { left = sprite.border.x, bottom = sprite.border.y, right = sprite.border.z, top = sprite.border.w },
                        hasBorder = hasBorder,
                        pixelsPerUnit = sprite.pixelsPerUnit
                    });

                    count++;
                }

                return new SuccessResponse($"Found {results.Count} UI sprites.", new
                {
                    sprites = results
                });
            }
            catch (Exception e)
            {
                return new ErrorResponse($"Failed to search UI sprites: {e.Message}");
            }
        }
    }
}
