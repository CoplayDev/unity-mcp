using System.Collections.Generic;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEditor;

namespace MCPForUnity.Editor.Tools
{
    /// <summary>
    /// Forces Unity to reserialize assets, updating them to the current serialization format.
    /// Useful after Unity version upgrades, script changes that affect serialized data,
    /// or when assets need their .meta files regenerated.
    /// </summary>
    [McpForUnityTool("reserialize_assets", AutoRegister = false)]
    public static class ReserializeAssets
    {
        /// <summary>
        /// Main handler for asset reserialization.
        /// </summary>
        public static object HandleCommand(JObject @params)
        {
            if (@params == null)
            {
                return new ErrorResponse("Parameters cannot be null.");
            }

            var p = new ToolParams(@params);

            // Support both single path and array of paths
            string singlePath = p.Get("path");
            JToken pathsToken = @params["paths"];

            var paths = new List<string>();

            if (pathsToken != null && pathsToken.Type == JTokenType.Array)
            {
                foreach (var item in pathsToken)
                {
                    string val = item.ToString();
                    if (!string.IsNullOrEmpty(val))
                    {
                        paths.Add(val);
                    }
                }
            }
            else if (!string.IsNullOrEmpty(singlePath))
            {
                paths.Add(singlePath);
            }

            if (paths.Count == 0)
            {
                return new ErrorResponse("'path' (string) or 'paths' (array of strings) parameter required.");
            }

            AssetDatabase.ForceReserializeAssets(paths);

            return new SuccessResponse(
                $"Reserialized {paths.Count} asset(s).",
                new { paths = paths.ToArray() }
            );
        }
    }
}
