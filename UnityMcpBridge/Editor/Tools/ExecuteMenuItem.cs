using System;
using System.Collections.Generic; // Added for HashSet
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using MCPForUnity.Editor.Helpers; // For Response class

namespace MCPForUnity.Editor.Tools
{
    /// <summary>
    /// Handles executing Unity Editor menu items by path.
    /// </summary>
    public static class ExecuteMenuItem
    {
        // Basic blacklist to prevent accidental execution of potentially disruptive menu items.
        // This can be expanded based on needs.
        private static readonly HashSet<string> _menuPathBlacklist = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase
        )
        {
            "File/Quit",
            // Add other potentially dangerous items like "Edit/Preferences...", "File/Build Settings..." if needed
        };

        /// <summary>
        /// Main handler for executing menu items or getting available ones.
        /// </summary>
        public static object HandleCommand(JObject @params)
        {
            string action = (@params["action"]?.ToString())?.ToLowerInvariant() ?? "execute"; // Default action

            try
            {
                switch (action)
                {
                    case "execute":
                        return ExecuteItem(@params);
                    case "get_available_menus":
                        // Getting a comprehensive list of *all* menu items dynamically is very difficult
                        // and often requires complex reflection or maintaining a manual list.
                        // Returning a placeholder/acknowledgement for now.
                        Debug.LogWarning(
                            "[ExecuteMenuItem] 'get_available_menus' action is not fully implemented. Dynamically listing all menu items is complex."
                        );
                        // Returning an empty list as per the refactor plan's requirements.
                        return Response.Success(
                            "'get_available_menus' action is not fully implemented. Returning empty list.",
                            new List<string>()
                        );
                    // TODO: Consider implementing a basic list of common/known menu items or exploring reflection techniques if this feature becomes critical.
                    default:
                        return Response.Error(
                            $"Unknown action: '{action}'. Valid actions are 'execute', 'get_available_menus'."
                        );
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[ExecuteMenuItem] Action '{action}' failed: {e}");
                return Response.Error($"Internal error processing action '{action}': {e.Message}");
            }
        }

        /// <summary>
        /// Executes a specific menu item.
        /// </summary>
        private static object ExecuteItem(JObject @params)
        {
            // Try both naming conventions: snake_case and camelCase
            string menuPath = @params["menu_path"]?.ToString() ?? @params["menuPath"]?.ToString();
            // Optional future param retained for API compatibility; not used in synchronous mode
            // int timeoutMs = Math.Max(0, (@params["timeout_ms"]?.ToObject<int>() ?? 2000));

            // string alias = @params["alias"]?.ToString(); // TODO: Implement alias mapping based on refactor plan requirements.
            // JObject parameters = @params["parameters"] as JObject; // TODO: Investigate parameter passing (often not directly supported by ExecuteMenuItem).

            if (string.IsNullOrWhiteSpace(menuPath))
            {
                return Response.Error("Required parameter 'menu_path' or 'menuPath' is missing or empty.");
            }

            // Validate against blacklist
            if (_menuPathBlacklist.Contains(menuPath))
            {
                return Response.Error(
                    $"Execution of menu item '{menuPath}' is blocked for safety reasons."
                );
            }

            // TODO: Implement alias lookup here if needed (Map alias to actual menuPath).
            // if (!string.IsNullOrEmpty(alias)) { menuPath = LookupAlias(alias); if(menuPath == null) return Response.Error(...); }

            // TODO: Handle parameters ('parameters' object) if a viable method is found.
            // This is complex as EditorApplication.ExecuteMenuItem doesn't take arguments directly.
            // It might require finding the underlying EditorWindow or command if parameters are needed.

            try
            {
                // Trace incoming execute requests (debug-gated)
                McpLog.Info($"[ExecuteMenuItem] Request to execute menu: '{menuPath}'", always: false);

                // Execute synchronously. This code runs on the Editor main thread in our bridge path.
                bool executed = EditorApplication.ExecuteMenuItem(menuPath);
                if (executed)
                {
                    // Success trace (debug-gated)
                    McpLog.Info($"[ExecuteMenuItem] Executed successfully: '{menuPath}'", always: false);
                    return Response.Success(
                        $"Executed menu item: '{menuPath}'",
                        new { executed = true, menuPath }
                    );
                }
                Debug.LogWarning($"[ExecuteMenuItem] Failed (not found/disabled): '{menuPath}'");
                return Response.Error(
                    $"Failed to execute menu item (not found or disabled): '{menuPath}'",
                    new { executed = false, menuPath }
                );
            }
            catch (Exception e)
            {
                Debug.LogError($"[ExecuteMenuItem] Error executing '{menuPath}': {e}");
                return Response.Error($"Error executing menu item '{menuPath}': {e.Message}");
            }
        }

        // TODO: Add helper for alias lookup if implementing aliases.
        // private static string LookupAlias(string alias) { ... return actualMenuPath or null ... }
    }
}

