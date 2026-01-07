using System;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Services;
using Newtonsoft.Json.Linq;

namespace MCPForUnity.Editor.Resources.Editor
{
    /// <summary>
    /// Provides dynamic editor state information that changes frequently.
    /// </summary>
    [McpForUnityResource("get_editor_state")]
    public static class EditorState
    {
        public static object HandleCommand(JObject @params)
        {
            try
            {
                var snapshot = EditorStateCache.GetSnapshot();
                return new SuccessResponse("Retrieved editor state.", snapshot);
            }
            catch (Exception e)
            {
                return new ErrorResponse($"Error getting editor state: {e.Message}");
            }
        }
    }
}
