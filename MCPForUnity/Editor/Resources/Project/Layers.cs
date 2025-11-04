using System;
using System.Collections.Generic;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace MCPForUnity.Editor.Resources.Project
{
    /// <summary>
    /// Provides dictionary of layer indices to layer names.
    /// </summary>
    [McpForUnityResource("get_layers")]
    public static class Layers
    {
        private const int TotalLayerCount = 32;

        public static object HandleCommand(JObject @params)
        {
            try
            {
                var layers = new Dictionary<int, string>();
                for (int i = 0; i < TotalLayerCount; i++)
                {
                    string layerName = LayerMask.LayerToName(i);
                    if (!string.IsNullOrEmpty(layerName))
                    {
                        layers.Add(i, layerName);
                    }
                }
                
                return Response.Success("Retrieved current named layers.", layers);
            }
            catch (Exception e)
            {
                return Response.Error($"Failed to retrieve layers: {e.Message}");
            }
        }
    }
}
