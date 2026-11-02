using System;
using System.Collections.Generic;
using System.IO;
using MCPForUnity.Editor.Constants;
using MCPForUnity.Editor.Models;
using MCPForUnity.Editor.Services;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace MCPForUnity.Editor.Helpers
{
    /// <summary>Editor-owned inputs captured before filesystem and executable discovery.</summary>
    internal sealed class WindowRefreshContext
    {
        internal bool UseHttp, Remote, ForceRefresh, ProjectScopedTools, LocalLaunchAllowed;
        internal string LocalUrl, LocalRpcUrl, RemoteRpcUrl, RpcUrl, ApiKey, SystemRoot;
        internal string SourceOverride, PackageSource, PackageRoot, PackageVersion, ProjectDirOverride, ProjectDir;
        internal string DeploySource, BackupPath, DeployTarget, LocalLaunchError;
        internal RuntimePlatform Platform;
        internal WindowPathResolution Paths;
        internal string DevFlags;
        internal IReadOnlyList<string> DevFlagList;
        internal bool HasProjectDirOverride, HasBackup;
        private Func<WindowPathResolution> resolvePaths;
        private static readonly PathResolverService DefaultWindowPaths = new PathResolverService();
        internal string Key { get; private set; }
        internal ConfiguredTransport Transport => !UseHttp ? ConfiguredTransport.Stdio :
            Remote ? ConfiguredTransport.HttpRemote : ConfiguredTransport.Http;
        internal IList<string> FromArgs => AssetPathUtility.GetBetaServerFromArgsList(
            string.IsNullOrEmpty(SourceOverride) ? string.Empty : PackageSource, PackageSource);
        internal string QuotedFromArgs => AssetPathUtility.GetBetaServerFromArgs(
            string.IsNullOrEmpty(SourceOverride) ? string.Empty : PackageSource, PackageSource, true);

        internal static WindowRefreshContext Capture()
        {
            var package = PackageInfo.FindForAssembly(typeof(WindowRefreshContext).Assembly);
            var context = new WindowRefreshContext
            {
                UseHttp = EditorConfigurationCache.Instance.UseHttpTransport,
                Remote = HttpEndpointUtility.IsRemoteScope(),
                LocalUrl = HttpEndpointUtility.GetLocalBaseUrl(),
                LocalRpcUrl = HttpEndpointUtility.GetLocalMcpRpcUrl(),
                RemoteRpcUrl = HttpEndpointUtility.GetRemoteMcpRpcUrl(),
                RpcUrl = HttpEndpointUtility.GetMcpRpcUrl(),
                ApiKey = EditorPrefs.GetString(EditorPrefKeys.ApiKey, string.Empty),
                SourceOverride = EditorPrefs.GetString(EditorPrefKeys.GitUrlOverride, string.Empty),
                ProjectDirOverride = EditorPrefs.GetString(EditorPrefKeys.ClientProjectDirOverride, string.Empty),
                ProjectDir = Path.GetDirectoryName(Application.dataPath),
                Platform = Application.platform,
                SystemRoot = MCPServiceLocator.Platform.GetSystemRoot(),
                ForceRefresh = EditorPrefs.GetBool(EditorPrefKeys.DevModeForceServerRefresh, false),
                ProjectScopedTools = EditorPrefs.GetBool(EditorPrefKeys.ProjectScopedToolsLocalHttp, true),
                DeploySource = EditorPrefs.GetString(EditorPrefKeys.PackageDeploySourcePath, string.Empty),
                BackupPath = EditorPrefs.GetString(EditorPrefKeys.PackageDeployLastBackupPath, string.Empty),
                PackageVersion = package?.version,
                PackageRoot = package?.resolvedPath
            };
            if (string.IsNullOrEmpty(context.PackageRoot))
            {
                string assetPath = AssetPathUtility.GetMcpPackageRootPath();
                context.PackageRoot = string.IsNullOrEmpty(assetPath) ? null :
                    Path.GetFullPath(Path.Combine(context.ProjectDir, assetPath));
            }
            context.LocalLaunchAllowed = HttpEndpointUtility.IsHttpLocalUrlAllowedForLaunch(
                context.LocalUrl, out context.LocalLaunchError);
            // Legacy implementations have no worker-safety contract. Display captured
            // preference/system paths without calling their synchronous Unity APIs.
            var windowPaths = MCPServiceLocator.Paths as IWindowPathResolver ?? DefaultWindowPaths;
            context.resolvePaths = windowPaths.CaptureWindowResolution();
            // Keys stay in memory. They can contain credentials and must never be logged.
            context.Key = string.Join("\n", context.UseHttp, context.Remote, context.LocalUrl,
                context.RemoteRpcUrl, context.ApiKey, context.SourceOverride, context.ProjectDirOverride,
                context.ProjectDir, context.PackageVersion, context.PackageRoot, context.ForceRefresh,
                context.ProjectScopedTools, context.DeploySource, context.BackupPath, context.LocalLaunchAllowed,
                EditorPrefs.GetBool(EditorPrefKeys.AllowLanHttpBind, false),
                EditorPrefs.GetBool(EditorPrefKeys.AllowInsecureRemoteHttp, false),
                EditorPrefs.GetString(EditorPrefKeys.UvxPathOverride, string.Empty),
                EditorPrefs.GetString(EditorPrefKeys.ClaudeCliPathOverride, string.Empty),
                Environment.GetEnvironmentVariable("PATH"), Environment.GetEnvironmentVariable("CLAUDE_CLI"));
            return context;
        }

        /// <summary>Worker-only: never reads EditorPrefs, Unity objects or package APIs.</summary>
        internal WindowRefreshContext Resolve()
        {
            Paths = resolvePaths();
            if (string.IsNullOrEmpty(PackageVersion))
            {
                string jsonPath = Path.Combine(PackageRoot ?? string.Empty, "package.json");
                PackageVersion = File.Exists(jsonPath) ? JObject.Parse(File.ReadAllText(jsonPath))["version"]?.ToString() : "unknown";
            }
            PackageSource = AssetPathUtility.ResolveMcpServerPackageSource(SourceOverride, PackageVersion);
            HasProjectDirOverride = !string.IsNullOrEmpty(ProjectDirOverride) && Directory.Exists(ProjectDirOverride);
            if (HasProjectDirOverride) ProjectDir = ProjectDirOverride;
            HasBackup = !string.IsNullOrEmpty(BackupPath) && Directory.Exists(BackupPath);
            DeployTarget = !string.IsNullOrEmpty(PackageRoot) && Directory.Exists(PackageRoot)
                ? PackageRoot.Replace('\\', '/') : "Not found (check Packages/manifest.json)";
            bool localSource = PackageSource.StartsWith("file://", StringComparison.OrdinalIgnoreCase) || Path.IsPathRooted(PackageSource);
            bool force = ForceRefresh || localSource;
            bool offline = !force && ProbeOfflineCache(Paths.UvxPath, QuotedFromArgs);
            DevFlags = AssetPathUtility.GetUvxDevFlags(force, offline);
            DevFlagList = DevFlags.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            return this;
        }

        private static readonly object OfflineLock = new object();
        private static string offlineKey;
        private static DateTime offlineChecked;
        private static bool offlineResult;
        private static bool ProbeOfflineCache(string uvx, string fromArgs)
        {
            if (string.IsNullOrEmpty(uvx)) return false;
            // Shared by the three sections; the lock is only acquired on workers.
            lock (OfflineLock)
            {
                string key = uvx + "\n" + fromArgs;
                if (key == offlineKey && DateTime.UtcNow - offlineChecked < TimeSpan.FromSeconds(30)) return offlineResult;
                offlineResult = ExecPath.TryRun(uvx, $"--offline {fromArgs} mcp-for-unity --help", null,
                    out _, out _, 3000);
                offlineKey = key;
                offlineChecked = DateTime.UtcNow;
                return offlineResult;
            }
        }
    }
}
