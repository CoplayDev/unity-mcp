using System;
using Newtonsoft.Json.Linq;
using MCPForUnity.Editor.Helpers;

namespace MCPForUnity.Editor.Tools.MenuItems
{
    public static class ManageMenuItem
    {
        /// <summary>
        /// Routes actions: execute, list, exists, refresh
        /// </summary>
        public static object HandleCommand(JObject @params)
        {
            string action = @params["action"]?.ToString()?.ToLowerInvariant();
            if (string.IsNullOrEmpty(action))
            {
                return Response.Error("Action parameter is required. Valid actions are: execute, list, exists, refresh.");
            }

            try
            {
                switch (action)
                {
                    case "execute":
                        return MenuItemExecutor.Execute(@params);
                    case "list":
                        return MenuItemsReader.List(@params);
                    case "exists":
                        return MenuItemsReader.Exists(@params);
                    default:
                        return Response.Error($"Unknown action: '{action}'. Valid actions are: execute, list, exists, refresh.");
                }
            }
            catch (Exception e)
            {
                McpLog.Error($"[ManageMenuItem] Action '{action}' failed: {e}");
                return Response.Error($"Internal error: {e.Message}");
            }
        }
    }
}
