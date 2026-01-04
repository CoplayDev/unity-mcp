using System;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Services;
using Newtonsoft.Json.Linq;

namespace MCPForUnity.Editor.Resources.Editor
{
    /// <summary>
    /// Provides a cached, v2 readiness snapshot. This is designed to remain responsive even when Unity is busy.
    /// </summary>
    [McpForUnityResource("get_editor_state_v2")]
    public static class EditorStateV2
    {
        public static object HandleCommand(JObject @params)
        {
            try
            {
                var snapshot = EditorStateCache.GetSnapshot();
                return new SuccessResponse("Retrieved editor state (v2).", snapshot);
            }
            catch (Exception e)
            {
                return new ErrorResponse($"Error getting editor state (v2): {e.Message}");
            }
        }
    }
}


