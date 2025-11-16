using System;
using System.Threading.Tasks;
using UnityEditor;
using MCPForUnity.Editor.Constants;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Services.Transport;
using MCPForUnity.Editor.Windows;

namespace MCPForUnity.Editor.Services
{
    /// <summary>
    /// Ensures HTTP transports resume after domain reloads similar to the legacy stdio bridge.
    /// </summary>
    [InitializeOnLoad]
    internal static class HttpBridgeReloadHandler
    {
        static HttpBridgeReloadHandler()
        {
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
            AssemblyReloadEvents.afterAssemblyReload += OnAfterAssemblyReload;
        }

        private static void OnBeforeAssemblyReload()
        {
            try
            {
                var bridge = MCPServiceLocator.Bridge;
                bool shouldResume = bridge.IsRunning && bridge.ActiveMode == TransportMode.Http;

                if (shouldResume)
                {
                    EditorPrefs.SetBool(EditorPrefKeys.ResumeHttpAfterReload, true);
                }
                else
                {
                    EditorPrefs.DeleteKey(EditorPrefKeys.ResumeHttpAfterReload);
                }

                if (bridge.IsRunning)
                {
                    var stopTask = bridge.StopAsync();
                    stopTask.ContinueWith(t =>
                    {
                        if (t.IsFaulted && t.Exception != null)
                        {
                            McpLog.Warn($"Error stopping MCP bridge before reload: {t.Exception.GetBaseException().Message}");
                        }
                    }, TaskScheduler.Default);
                }
            }
            catch (Exception ex)
            {
                McpLog.Warn($"Failed to evaluate HTTP bridge reload state: {ex.Message}");
            }
        }

        private static void OnAfterAssemblyReload()
        {
            bool resume = false;
            try
            {
                resume = EditorPrefs.GetBool(EditorPrefKeys.ResumeHttpAfterReload, false);
                if (resume)
                {
                    EditorPrefs.DeleteKey(EditorPrefKeys.ResumeHttpAfterReload);
                }
            }
            catch (Exception ex)
            {
                McpLog.Warn($"Failed to read HTTP bridge reload flag: {ex.Message}");
                resume = false;
            }

            if (!resume)
            {
                return;
            }

            EditorApplication.delayCall += async () =>
            {
                try
                {
                    bool started = await MCPServiceLocator.Bridge.StartAsync();
                    if (!started)
                    {
                        McpLog.Warn("Failed to resume HTTP MCP bridge after domain reload");
                    }
                    else
                    {
                        MCPForUnityEditorWindow.RequestHealthVerification();
                    }
                }
                catch (Exception ex)
                {
                    McpLog.Error($"Error resuming HTTP MCP bridge: {ex.Message}");
                }
            };
        }
    }
}
