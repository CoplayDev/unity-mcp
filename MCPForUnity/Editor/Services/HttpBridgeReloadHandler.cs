using System;
using System.Threading.Tasks;
using MCPForUnity.Editor.Constants;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Services.Transport;
using MCPForUnity.Editor.Windows;
using UnityEditor;

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
                var transport = MCPServiceLocator.TransportManager;
                bool shouldResume = transport.IsRunning(TransportMode.Http);

                if (shouldResume)
                {
                    EditorPrefs.SetBool(EditorPrefKeys.ResumeHttpAfterReload, true);
                }
                else
                {
                    EditorPrefs.DeleteKey(EditorPrefKeys.ResumeHttpAfterReload);
                }

                if (shouldResume)
                {
                    var stopTask = transport.StopAsync(TransportMode.Http);
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
                // Only resume HTTP if it is still the selected transport.
                bool useHttp = EditorConfigurationCache.Instance.UseHttpTransport;
                resume = useHttp && EditorPrefs.GetBool(EditorPrefKeys.ResumeHttpAfterReload, false);
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

            // If the editor is not compiling, attempt an immediate restart without relying on editor focus.
            bool isCompiling = EditorApplication.isCompiling;
            try
            {
                var pipeline = Type.GetType("UnityEditor.Compilation.CompilationPipeline, UnityEditor");
                var prop = pipeline?.GetProperty("isCompiling", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                if (prop != null) isCompiling |= (bool)prop.GetValue(null);
            }
            catch { }

            if (!isCompiling)
            {
                try
                {
                    var startTask = MCPServiceLocator.TransportManager.StartAsync(TransportMode.Http);
                    startTask.ContinueWith(t =>
                    {
                        if (t.IsFaulted)
                        {
                            var baseEx = t.Exception?.GetBaseException();
                            McpLog.Warn($"Failed to resume HTTP MCP bridge after domain reload: {baseEx?.Message}");
                            return;
                        }
                        bool started = t.Result;
                        if (!started)
                        {
                            McpLog.Warn("Failed to resume HTTP MCP bridge after domain reload");
                        }
                        else
                        {
                            MCPForUnityEditorWindow.RequestHealthVerification();
                        }
                    }, TaskScheduler.Default);
                    return;
                }
                catch (Exception ex)
                {
                    McpLog.Error($"Error resuming HTTP MCP bridge: {ex.Message}");
                    return;
                }
            }

            // Fallback when compiling: schedule on the editor loop
            EditorApplication.delayCall += async () =>
            {
                try
                {
                    bool started = await MCPServiceLocator.TransportManager.StartAsync(TransportMode.Http);
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
