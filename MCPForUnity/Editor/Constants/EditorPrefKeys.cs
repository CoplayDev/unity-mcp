namespace MCPForUnity.Editor.Constants
{
    /// <summary>
    /// Centralized list of EditorPrefs keys used by the MCP for Unity package.
    /// Keeping them in one place avoids typos and simplifies migrations.
    /// </summary>
    internal static class EditorPrefKeys
    {
        internal const string UseHttpTransport = "MCPForUnity.UseHttpTransport";
        internal const string HttpTransportScope = "MCPForUnity.HttpTransportScope"; // "local" | "remote"
        internal const string LastLocalHttpServerPid = "MCPForUnity.LocalHttpServer.LastPid";
        internal const string LastLocalHttpServerPort = "MCPForUnity.LocalHttpServer.LastPort";
        internal const string LastLocalHttpServerStartedUtc = "MCPForUnity.LocalHttpServer.LastStartedUtc";
        internal const string LastLocalHttpServerPidArgsHash = "MCPForUnity.LocalHttpServer.LastPidArgsHash";
        internal const string LastLocalHttpServerPidFilePath = "MCPForUnity.LocalHttpServer.LastPidFilePath";
        internal const string LastLocalHttpServerInstanceToken = "MCPForUnity.LocalHttpServer.LastInstanceToken";
        internal const string DebugLogs = "MCPForUnity.DebugLogs";
        internal const string ValidationLevel = "MCPForUnity.ValidationLevel";
        internal const string UnitySocketPort = "MCPForUnity.UnitySocketPort";
        internal const string ResumeHttpAfterReload = "MCPForUnity.ResumeHttpAfterReload";
        internal const string ResumeStdioAfterReload = "MCPForUnity.ResumeStdioAfterReload";

        internal const string UvxPathOverride = "MCPForUnity.UvxPath";
        internal const string ClaudeCliPathOverride = "MCPForUnity.ClaudeCliPath";

        internal const string HttpBaseUrl = "MCPForUnity.HttpUrl";
        internal const string HttpRemoteBaseUrl = "MCPForUnity.HttpRemoteUrl";
        internal const string SessionId = "MCPForUnity.SessionId";
        internal const string WebSocketUrlOverride = "MCPForUnity.WebSocketUrl";
        internal const string GitUrlOverride = "MCPForUnity.GitUrlOverride";
        internal const string DevModeForceServerRefresh = "MCPForUnity.DevModeForceServerRefresh";
        internal const string UseBetaServer = "MCPForUnity.UseBetaServer";
        internal const string ProjectScopedToolsLocalHttp = "MCPForUnity.ProjectScopedTools.LocalHttp";

        internal const string PackageDeploySourcePath = "MCPForUnity.PackageDeploy.SourcePath";
        internal const string PackageDeployLastBackupPath = "MCPForUnity.PackageDeploy.LastBackupPath";
        internal const string PackageDeployLastTargetPath = "MCPForUnity.PackageDeploy.LastTargetPath";
        internal const string PackageDeployLastSourcePath = "MCPForUnity.PackageDeploy.LastSourcePath";

        internal const string ServerSrc = "MCPForUnity.ServerSrc";
        internal const string UseEmbeddedServer = "MCPForUnity.UseEmbeddedServer";
        internal const string LockCursorConfig = "MCPForUnity.LockCursorConfig";
        internal const string AutoRegisterEnabled = "MCPForUnity.AutoRegisterEnabled";
        internal const string ToolEnabledPrefix = "MCPForUnity.ToolEnabled.";
        internal const string ToolFoldoutStatePrefix = "MCPForUnity.ToolFoldout.";
        internal const string ResourceEnabledPrefix = "MCPForUnity.ResourceEnabled.";
        internal const string ResourceFoldoutStatePrefix = "MCPForUnity.ResourceFoldout.";
        internal const string EditorWindowActivePanel = "MCPForUnity.EditorWindow.ActivePanel";

        internal const string SetupCompleted = "MCPForUnity.SetupCompleted";
        internal const string SetupDismissed = "MCPForUnity.SetupDismissed";

        internal const string CustomToolRegistrationEnabled = "MCPForUnity.CustomToolRegistrationEnabled";

        internal const string LastUpdateCheck = "MCPForUnity.LastUpdateCheck";
        internal const string LatestKnownVersion = "MCPForUnity.LatestKnownVersion";
        internal const string LastAssetStoreUpdateCheck = "MCPForUnity.LastAssetStoreUpdateCheck";
        internal const string LatestKnownAssetStoreVersion = "MCPForUnity.LatestKnownAssetStoreVersion";
        internal const string LastStdIoUpgradeVersion = "MCPForUnity.LastStdIoUpgradeVersion";

        internal const string TelemetryDisabled = "MCPForUnity.TelemetryDisabled";
        internal const string CustomerUuid = "MCPForUnity.CustomerUUID";

        internal const string ApiKey = "MCPForUnity.ApiKey";
    }
}
