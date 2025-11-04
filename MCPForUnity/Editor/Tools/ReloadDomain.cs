using System;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEditor;

namespace MCPForUnity.Editor.Tools
{
    /// <summary>
    /// Handles domain reload operations to refresh Unity's script assemblies.
    /// This is essential after creating or modifying scripts to make new types available.
    /// </summary>
    [McpForUnityTool("reload_domain")]
    public static class ReloadDomain
    {
        /// <summary>
        /// Main handler for domain reload command.
        /// Triggers Unity to reload all script assemblies, which is necessary after
        /// script changes before new components can be used.
        /// </summary>
        public static object HandleCommand(JObject @params)
        {
            try
            {
                McpLog.Info("[ReloadDomain] Requesting domain reload");
                EditorUtility.RequestScriptReload();
                return Response.Success("Domain reload requested. Unity will recompile scripts and refresh assemblies.");
            }
            catch (Exception ex)
            {
                McpLog.Error($"[ReloadDomain] Error requesting domain reload: {ex.Message}");
                return Response.Error($"Failed to request domain reload: {ex.Message}");
            }
        }
    }
}
