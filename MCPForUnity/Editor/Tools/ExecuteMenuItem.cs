using System;
using System.Collections.Generic;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEditor;

namespace MCPForUnity.Editor.Tools
{
    [McpForUnityTool("execute_menu_item", AutoRegister = false)]
    /// <summary>
    /// Tool to execute a Unity Editor menu item by its path.
    /// </summary>
    public static class ExecuteMenuItem
    {
        // Allow-list policy (harden/security, R6). The previous one-entry deny-list allowed
        // almost everything — builds, asset deletion, package ops, third-party plugin menus.
        // We instead permit only menu paths under these prefixes and reject everything else.
        // This intentionally covers the scene/object/asset authoring workflows while blocking
        // build, delete, package, project-settings and quit operations. Extend deliberately.
        private static readonly string[] _menuPathAllowPrefixes =
        {
            "GameObject/",
            "Component/",
            "Assets/Create/",
            "Window/",
            "Edit/Undo",
            "Edit/Redo",
            "Edit/Duplicate",
            "Edit/Frame Selected",
            "Edit/Select All",
            "File/Save",
        };

        private static bool IsAllowed(string menuPath)
        {
            foreach (var prefix in _menuPathAllowPrefixes)
            {
                if (menuPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        public static object HandleCommand(JObject @params)
        {
            McpLog.Info("[ExecuteMenuItem] Handling menu item command");
            string menuPath = @params["menu_path"]?.ToString() ?? @params["menuPath"]?.ToString();
            if (string.IsNullOrWhiteSpace(menuPath))
            {
                return new ErrorResponse("Required parameter 'menu_path' or 'menuPath' is missing or empty.");
            }

            if (!IsAllowed(menuPath))
            {
                return new ErrorResponse(
                    $"Menu item '{menuPath}' is not on the allow-list and was blocked for safety. " +
                    "Permitted prefixes: GameObject/, Component/, Assets/Create/, Window/, " +
                    "Edit/Undo|Redo|Duplicate|Frame Selected|Select All, File/Save.");
            }

            try
            {
                bool executed = EditorApplication.ExecuteMenuItem(menuPath);
                if (!executed)
                {
                    McpLog.Error($"[MenuItemExecutor] Failed to execute menu item '{menuPath}'. It might be invalid, disabled, or context-dependent.");
                    return new ErrorResponse($"Failed to execute menu item '{menuPath}'. It might be invalid, disabled, or context-dependent.");
                }
                return new SuccessResponse($"Attempted to execute menu item: '{menuPath}'. Check Unity logs for confirmation or errors.");
            }
            catch (Exception e)
            {
                McpLog.Error($"[MenuItemExecutor] Failed to setup execution for '{menuPath}': {e}");
                return new ErrorResponse($"Error setting up execution for menu item '{menuPath}': {e.Message}");
            }
        }
    }
}
