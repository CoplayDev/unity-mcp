using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using MCPForUnity.Editor.Tools.MenuItems;

namespace MCPForUnity.Editor.Tools
{
    /// <summary>
    /// Registry for all MCP command handlers (Refactored Version)
    /// </summary>
    public static class CommandRegistry
    {
        // Maps command names (matching those called from Python via ctx.bridge.unity_editor.HandlerName)
        // to the corresponding static HandleCommand method in the appropriate tool class.
        private static readonly Dictionary<string, Func<JObject, object>> _handlers = new()
        {
            { "HandleManageScript", ManageScript.HandleCommand },
            { "HandleManageScene", ManageScene.HandleCommand },
            { "HandleManageEditor", ManageEditor.HandleCommand },
            { "HandleManageGameObject", ManageGameObject.HandleCommand },
            { "HandleManageAsset", ManageAsset.HandleCommand },
            { "HandleReadConsole", ReadConsole.HandleCommand },
            { "HandleManageMenuItem", ManageMenuItem.HandleCommand },
            { "HandleManageShader", ManageShader.HandleCommand},
        };

        /// <summary>
        /// Gets a command handler by name.
        /// </summary>
        /// <param name="commandName">Name of the command handler (e.g., "HandleManageAsset").</param>
        /// <returns>The command handler function if found, null otherwise.</returns>
        public static Func<JObject, object> GetHandler(string commandName)
        {
            // Use case-insensitive comparison for flexibility, although Python side should be consistent
            return _handlers.TryGetValue(commandName, out var handler) ? handler : null;
            // Consider adding logging here if a handler is not found
            /*
            if (_handlers.TryGetValue(commandName, out var handler)) {
                return handler;
            } else {
                UnityEngine.Debug.LogError($\"[CommandRegistry] No handler found for command: {commandName}\");
                return null;
            }
            */
        }
    }
}

