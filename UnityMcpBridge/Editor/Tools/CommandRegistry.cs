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
            { "manage_script", ManageScript.HandleCommand },
            { "manage_scene", ManageScene.HandleCommand },
            { "manage_editor", ManageEditor.HandleCommand },
            { "manage_gameobject", ManageGameObject.HandleCommand },
            { "manage_asset", ManageAsset.HandleCommand },
            { "read_console", ReadConsole.HandleCommand },
            { "manage_menu_item", ManageMenuItem.HandleCommand },
            { "manage_shader", ManageShader.HandleCommand},
        };

        /// <summary>
        /// Gets a command handler by name.
        /// </summary>
        /// <param name="commandName">Name of the command handler (e.g., "HandleManageAsset").</param>
        /// <returns>The command handler function if found, null otherwise.</returns>
        public static Func<JObject, object> GetHandler(string commandName)
        {
            if (!_handlers.TryGetValue(commandName, out var handler))
            {
                throw new InvalidOperationException(
                    $"Unknown or unsupported command type: {commandName}");
            }

            return handler;
        }

        public static void Add(string commandName, Func<JObject, object> handler)
        {
            _handlers.Add(commandName, handler);
        }
    }
}

