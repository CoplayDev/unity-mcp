using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEditor;
using MCPForUnity.Editor.Helpers;

namespace MCPForUnity.Editor.Tools.MenuItems
{
    /// <summary>
    /// Executes Unity Editor menu items by path with safety checks.
    /// </summary>
    public static class MenuItemExecutor
    {
        // Basic blacklist to prevent execution of disruptive menu items.
        private static readonly HashSet<string> _menuPathBlacklist = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase)
        {
            "File/Quit",
        };

        /// <summary>
        /// Execute a specific menu item. Expects 'menu_path' or 'menuPath' in params.
        /// </summary>
        public static object Execute(JObject @params)
        {
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
