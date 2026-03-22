using System;
using System.Collections.Generic;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEditor;

namespace MCPForUnity.Editor.Tools
{
    /// <summary>
    /// Reimports specific assets by path without showing confirmation dialogs.
    /// Unlike "Reimport All", this is granular, fast, and AI-friendly.
    /// </summary>
    [McpForUnityTool("reimport_assets", AutoRegister = false)]
    public static class ReimportAssets
    {
        public static object HandleCommand(JObject @params)
        {
            var pathsToken = @params?["paths"];
            if (pathsToken == null || pathsToken.Type == JTokenType.Null)
                return new ErrorResponse("'paths' parameter is required (array of asset paths).");

            var paths = new List<string>();
            if (pathsToken.Type == JTokenType.Array)
            {
                foreach (var item in (JArray)pathsToken)
                    paths.Add(item.ToString());
            }
            else if (pathsToken.Type == JTokenType.String)
            {
                paths.Add(pathsToken.ToString());
            }
            else
            {
                return new ErrorResponse("'paths' must be a string or array of strings.");
            }

            if (paths.Count == 0)
                return new ErrorResponse("'paths' array is empty.");

            bool force = ParamCoercion.CoerceBool(@params?["force"], true);
            bool recursive = ParamCoercion.CoerceBool(@params?["recursive"], true);

            var options = force
                ? ImportAssetOptions.ForceUpdate
                : ImportAssetOptions.Default;

            int reimported = 0;
            var errors = new List<string>();

            foreach (var path in paths)
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    errors.Add("Empty path skipped.");
                    continue;
                }

                try
                {
                    if (AssetDatabase.IsValidFolder(path) && recursive)
                    {
                        var guids = AssetDatabase.FindAssets("", new[] { path });
                        foreach (var guid in guids)
                        {
                            var assetPath = AssetDatabase.GUIDToAssetPath(guid);
                            if (!AssetDatabase.IsValidFolder(assetPath))
                            {
                                AssetDatabase.ImportAsset(assetPath, options);
                                reimported++;
                            }
                        }
                    }
                    else
                    {
                        AssetDatabase.ImportAsset(path, options);
                        reimported++;
                    }
                }
                catch (Exception ex)
                {
                    errors.Add($"{path}: {ex.Message}");
                }
            }

            return new SuccessResponse($"Reimported {reimported} asset(s).", new
            {
                reimported_count = reimported,
                requested_paths = paths.Count,
                force,
                recursive,
                errors = errors.Count > 0 ? errors : null
            });
        }
    }
}
