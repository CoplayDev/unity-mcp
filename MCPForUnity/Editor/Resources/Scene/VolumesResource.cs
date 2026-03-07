using System;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Tools.Graphics;
using Newtonsoft.Json.Linq;

namespace MCPForUnity.Editor.Resources.Scene
{
    [McpForUnityResource("get_volumes")]
    public static class VolumesResource
    {
        public static object HandleCommand(JObject @params)
        {
            try
            {
                return VolumeOps.ListVolumes(@params ?? new JObject());
            }
            catch (Exception e)
            {
                McpLog.Error($"[VolumesResource] Error listing volumes: {e}");
                return new ErrorResponse($"Error listing volumes: {e.Message}");
            }
        }
    }
}
