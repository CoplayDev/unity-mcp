using System;
using System.Reflection;
using MCPForUnity.Editor.Clients;
using MCPForUnity.Editor.Constants;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Models;
using UnityEditor;

namespace MCPForUnity.Editor.Services
{
    /// <summary>
    /// Once per Editor session, sweeps registered configurators and re-runs CheckStatus(attemptAutoRewrite: true)
    /// for any installed client that already has a config on disk. Catches the case where the user updated the
    /// MCP for Unity package while the Editor was closed — without this sweep, stale package versions in client
    /// configs would persist until the user opens the MCP window.
    /// </summary>
    [InitializeOnLoad]
    public static class StartupConfigRewrite
    {
        public const string SESSION_GUARD_KEY = "MCPForUnity.StartupConfigRewrite.Ran";

        static StartupConfigRewrite()
        {
            if (UnityEditorInternal.InternalEditorUtility.inBatchMode) return;
            // AssetImportWorker subprocesses share [InitializeOnLoad] but don't host MCP and
            // shouldn't be writing client configs from a half-loaded domain (same surface as
            // issue #1134 in CommandRegistry).
            if (IsRunningInAssetImportWorker()) return;
            if (SessionState.GetBool(SESSION_GUARD_KEY, false)) return;
            EditorApplication.delayCall += RunOnce;
        }

        private static void RunOnce()
        {
            if (SessionState.GetBool(SESSION_GUARD_KEY, false)) return;
            SessionState.SetBool(SESSION_GUARD_KEY, true);

            if (!EditorPrefs.GetBool(EditorPrefKeys.AutoRegisterEnabled, true)) return;

            int rewrote = 0;
            foreach (var c in McpClientRegistry.All)
            {
                try
                {
                    if (!c.IsInstalled) continue;
                    // Always let CheckStatus read the current state from disk before deciding —
                    // the in-memory Status can be NotConfigured on a fresh editor load even
                    // though the file already has a valid config.
                    var before = c.Status;
                    var after = c.CheckStatus(attemptAutoRewrite: true);
                    if (before != after && after == McpStatus.Configured) rewrote++;
                }
                catch (System.Exception ex)
                {
                    McpLog.Warn($"[StartupConfigRewrite] {c.DisplayName} failed: {ex.Message}");
                }
            }
            if (rewrote > 0)
                McpLog.Info($"[StartupConfigRewrite] refreshed {rewrote} client config(s).");
        }

        private static bool? _cachedIsAssetImportWorker;

        private static bool IsRunningInAssetImportWorker()
        {
            if (_cachedIsAssetImportWorker.HasValue)
                return _cachedIsAssetImportWorker.Value;

            bool result = false;
            try
            {
                var method = typeof(UnityEditor.AssetDatabase).GetMethod(
                    "IsAssetImportWorkerProcess",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (method != null && method.GetParameters().Length == 0)
                    result = method.Invoke(null, null) is bool b && b;
            }
            catch { }

            if (!result)
            {
                try
                {
                    string cmd = Environment.CommandLine ?? string.Empty;
                    if (cmd.IndexOf("-importWorker", StringComparison.OrdinalIgnoreCase) >= 0
                        || cmd.IndexOf("AssetImportWorker", StringComparison.OrdinalIgnoreCase) >= 0)
                        result = true;
                }
                catch { }
            }

            _cachedIsAssetImportWorker = result;
            return result;
        }
    }
}
