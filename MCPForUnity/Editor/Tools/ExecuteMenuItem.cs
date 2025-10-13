using System;
using System.Collections.Generic;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEditor;

namespace MCPForUnity.Editor.Tools
{
    [McpForUnityTool("execute_menu_item")]
    public static class ExecuteMenuItem
    {
        // Basic blacklist to prevent execution of disruptive menu items.
        private static readonly HashSet<string> _menuPathBlacklist = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase)
        {
            "File/Quit",
        };

        /// <summary>
        /// Routes actions: execute, list, exists, refresh
        /// </summary>
        public static object HandleCommand(JObject @params)
        {
            McpLog.Info("[ExecuteMenuItem] Handling menu item command");
            string menuPath = @params["menu_path"]?.ToString() ?? @params["menuPath"]?.ToString();
            if (string.IsNullOrWhiteSpace(menuPath))
            {
                return Response.Error("Required parameter 'menu_path' or 'menuPath' is missing or empty.");
            }

            if (_menuPathBlacklist.Contains(menuPath))
            {
                return Response.Error($"Execution of menu item '{menuPath}' is blocked for safety reasons.");
            }

            try
            {
                bool executed = EditorApplication.ExecuteMenuItem(menuPath);
                if (!executed)
                {
                    McpLog.Error($"[MenuItemExecutor] Failed to execute menu item '{menuPath}'. It might be invalid, disabled, or context-dependent.");
                    return Response.Error($"Failed to execute menu item '{menuPath}'. It might be invalid, disabled, or context-dependent.");
                }
                return Response.Success($"Attempted to execute menu item: '{menuPath}'. Check Unity logs for confirmation or errors.");
            }
            catch (Exception e)
            {
                McpLog.Error($"[MenuItemExecutor] Failed to setup execution for '{menuPath}': {e}");
                return Response.Error($"Error setting up execution for menu item '{menuPath}': {e.Message}");
            }
        }
    }
}
