using System;
using System.Collections.Generic;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace MCPForUnity.Editor.Resources.Editor
{
    /// <summary>
    /// Provides list of all open editor windows.
    /// </summary>
    [McpForUnityResource("get_windows")]
    public static class Windows
    {
        public static object HandleCommand(JObject @params)
        {
            try
            {
                EditorWindow[] allWindows = UnityEngine.Resources.FindObjectsOfTypeAll<EditorWindow>();
                var openWindows = new List<object>();

                foreach (EditorWindow window in allWindows)
                {
                    if (window == null)
                        continue;

                    try
                    {
                        openWindows.Add(new
                        {
                            title = window.titleContent.text,
                            typeName = window.GetType().FullName,
                            isFocused = EditorWindow.focusedWindow == window,
                            position = new
                            {
                                x = window.position.x,
                                y = window.position.y,
                                width = window.position.width,
                                height = window.position.height
                            },
                            instanceID = window.GetInstanceID()
                        });
                    }
                    catch (Exception ex)
                    {
                        McpLog.Warn($"Could not get info for window {window.GetType().Name}: {ex.Message}");
                    }
                }

                return new SuccessResponse("Retrieved list of open editor windows.", openWindows);
            }
            catch (Exception e)
            {
                return new ErrorResponse($"Error getting editor windows: {e.Message}");
            }
        }
    }
}
