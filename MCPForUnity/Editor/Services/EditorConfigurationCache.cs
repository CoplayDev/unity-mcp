using System;
using MCPForUnity.Editor.Constants;
using UnityEditor;

namespace MCPForUnity.Editor.Services
{
    /// <summary>
    /// Centralized cache for frequently-read EditorPrefs values.
    /// Reduces scattered EditorPrefs.Get* calls and provides change notification.
    ///
    /// Usage:
    ///   var config = EditorConfigurationCache.Instance;
    ///   if (config.UseHttpTransport) { ... }
    ///   config.OnConfigurationChanged += (key) => { /* refresh UI */ };
    /// </summary>
    public class EditorConfigurationCache
    {
        private static EditorConfigurationCache _instance;
        private static readonly object _lock = new object();

        /// <summary>
        /// Singleton instance. Thread-safe lazy initialization.
        /// </summary>
        public static EditorConfigurationCache Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                        {
                            _instance = new EditorConfigurationCache();
                        }
                    }
                }
                return _instance;
            }
        }

        /// <summary>
        /// Event fired when any cached configuration value changes.
        /// The string parameter is the EditorPrefKeys constant name that changed.
        /// </summary>
        public event Action<string> OnConfigurationChanged;

        // Cached values - most frequently read
        private bool _useHttpTransport;
        private bool _debugLogs;
        private bool _useBetaServer;
        private bool _devModeForceServerRefresh;
        private string _uvxPathOverride;
        private string _gitUrlOverride;
        private string _httpBaseUrl;
        private string _httpRemoteBaseUrl;
        private string _claudeCliPathOverride;
        private string _httpTransportScope;
        private int _unitySocketPort;

        /// <summary>
        /// Whether to use HTTP transport (true) or Stdio transport (false).
        /// Default: true
        /// </summary>
        public bool UseHttpTransport => _useHttpTransport;

        /// <summary>
        /// Whether debug logging is enabled.
        /// Default: false
        /// </summary>
        public bool DebugLogs => _debugLogs;

        /// <summary>
        /// Whether to use the beta server channel.
        /// Default: true
        /// </summary>
        public bool UseBetaServer => _useBetaServer;

        /// <summary>
        /// Whether to force server refresh in dev mode (--no-cache --refresh).
        /// Default: false
        /// </summary>
        public bool DevModeForceServerRefresh => _devModeForceServerRefresh;

        /// <summary>
        /// Custom path override for uvx executable.
        /// Default: empty string (auto-detect)
        /// </summary>
        public string UvxPathOverride => _uvxPathOverride;

        /// <summary>
        /// Custom Git URL override for server installation.
        /// Default: empty string (use default)
        /// </summary>
        public string GitUrlOverride => _gitUrlOverride;

        /// <summary>
        /// HTTP base URL for the local MCP server.
        /// Default: empty string
        /// </summary>
        public string HttpBaseUrl => _httpBaseUrl;

        /// <summary>
        /// HTTP base URL for the remote-hosted MCP server.
        /// Default: empty string
        /// </summary>
        public string HttpRemoteBaseUrl => _httpRemoteBaseUrl;

        /// <summary>
        /// Custom path override for Claude CLI executable.
        /// Default: empty string (auto-detect)
        /// </summary>
        public string ClaudeCliPathOverride => _claudeCliPathOverride;

        /// <summary>
        /// HTTP transport scope: "local" or "remote".
        /// Default: empty string
        /// </summary>
        public string HttpTransportScope => _httpTransportScope;

        /// <summary>
        /// Unity socket port for Stdio transport.
        /// Default: 0 (auto-assign)
        /// </summary>
        public int UnitySocketPort => _unitySocketPort;

        private EditorConfigurationCache()
        {
            Refresh();
        }

        /// <summary>
        /// Refresh all cached values from EditorPrefs.
        /// Call this after bulk EditorPrefs changes or domain reload.
        /// </summary>
        public void Refresh()
        {
            _useHttpTransport = EditorPrefs.GetBool(EditorPrefKeys.UseHttpTransport, true);
            _debugLogs = EditorPrefs.GetBool(EditorPrefKeys.DebugLogs, false);
            _useBetaServer = EditorPrefs.GetBool(EditorPrefKeys.UseBetaServer, true);
            _devModeForceServerRefresh = EditorPrefs.GetBool(EditorPrefKeys.DevModeForceServerRefresh, false);
            _uvxPathOverride = EditorPrefs.GetString(EditorPrefKeys.UvxPathOverride, string.Empty);
            _gitUrlOverride = EditorPrefs.GetString(EditorPrefKeys.GitUrlOverride, string.Empty);
            _httpBaseUrl = EditorPrefs.GetString(EditorPrefKeys.HttpBaseUrl, string.Empty);
            _httpRemoteBaseUrl = EditorPrefs.GetString(EditorPrefKeys.HttpRemoteBaseUrl, string.Empty);
            _claudeCliPathOverride = EditorPrefs.GetString(EditorPrefKeys.ClaudeCliPathOverride, string.Empty);
            _httpTransportScope = EditorPrefs.GetString(EditorPrefKeys.HttpTransportScope, string.Empty);
            _unitySocketPort = EditorPrefs.GetInt(EditorPrefKeys.UnitySocketPort, 0);
        }

        /// <summary>
        /// Set UseHttpTransport and update cache + EditorPrefs atomically.
        /// </summary>
        public void SetUseHttpTransport(bool value)
        {
            if (_useHttpTransport != value)
            {
                _useHttpTransport = value;
                EditorPrefs.SetBool(EditorPrefKeys.UseHttpTransport, value);
                OnConfigurationChanged?.Invoke(nameof(UseHttpTransport));
            }
        }

        /// <summary>
        /// Set DebugLogs and update cache + EditorPrefs atomically.
        /// </summary>
        public void SetDebugLogs(bool value)
        {
            if (_debugLogs != value)
            {
                _debugLogs = value;
                EditorPrefs.SetBool(EditorPrefKeys.DebugLogs, value);
                OnConfigurationChanged?.Invoke(nameof(DebugLogs));
            }
        }

        /// <summary>
        /// Set UseBetaServer and update cache + EditorPrefs atomically.
        /// </summary>
        public void SetUseBetaServer(bool value)
        {
            if (_useBetaServer != value)
            {
                _useBetaServer = value;
                EditorPrefs.SetBool(EditorPrefKeys.UseBetaServer, value);
                OnConfigurationChanged?.Invoke(nameof(UseBetaServer));
            }
        }

        /// <summary>
        /// Set DevModeForceServerRefresh and update cache + EditorPrefs atomically.
        /// </summary>
        public void SetDevModeForceServerRefresh(bool value)
        {
            if (_devModeForceServerRefresh != value)
            {
                _devModeForceServerRefresh = value;
                EditorPrefs.SetBool(EditorPrefKeys.DevModeForceServerRefresh, value);
                OnConfigurationChanged?.Invoke(nameof(DevModeForceServerRefresh));
            }
        }

        /// <summary>
        /// Set UvxPathOverride and update cache + EditorPrefs atomically.
        /// </summary>
        public void SetUvxPathOverride(string value)
        {
            value = value ?? string.Empty;
            if (_uvxPathOverride != value)
            {
                _uvxPathOverride = value;
                EditorPrefs.SetString(EditorPrefKeys.UvxPathOverride, value);
                OnConfigurationChanged?.Invoke(nameof(UvxPathOverride));
            }
        }

        /// <summary>
        /// Set GitUrlOverride and update cache + EditorPrefs atomically.
        /// </summary>
        public void SetGitUrlOverride(string value)
        {
            value = value ?? string.Empty;
            if (_gitUrlOverride != value)
            {
                _gitUrlOverride = value;
                EditorPrefs.SetString(EditorPrefKeys.GitUrlOverride, value);
                OnConfigurationChanged?.Invoke(nameof(GitUrlOverride));
            }
        }

        /// <summary>
        /// Set HttpBaseUrl and update cache + EditorPrefs atomically.
        /// </summary>
        public void SetHttpBaseUrl(string value)
        {
            value = value ?? string.Empty;
            if (_httpBaseUrl != value)
            {
                _httpBaseUrl = value;
                EditorPrefs.SetString(EditorPrefKeys.HttpBaseUrl, value);
                OnConfigurationChanged?.Invoke(nameof(HttpBaseUrl));
            }
        }

        /// <summary>
        /// Set HttpRemoteBaseUrl and update cache + EditorPrefs atomically.
        /// </summary>
        public void SetHttpRemoteBaseUrl(string value)
        {
            value = value ?? string.Empty;
            if (_httpRemoteBaseUrl != value)
            {
                _httpRemoteBaseUrl = value;
                EditorPrefs.SetString(EditorPrefKeys.HttpRemoteBaseUrl, value);
                OnConfigurationChanged?.Invoke(nameof(HttpRemoteBaseUrl));
            }
        }

        /// <summary>
        /// Set ClaudeCliPathOverride and update cache + EditorPrefs atomically.
        /// </summary>
        public void SetClaudeCliPathOverride(string value)
        {
            value = value ?? string.Empty;
            if (_claudeCliPathOverride != value)
            {
                _claudeCliPathOverride = value;
                EditorPrefs.SetString(EditorPrefKeys.ClaudeCliPathOverride, value);
                OnConfigurationChanged?.Invoke(nameof(ClaudeCliPathOverride));
            }
        }

        /// <summary>
        /// Set HttpTransportScope and update cache + EditorPrefs atomically.
        /// </summary>
        public void SetHttpTransportScope(string value)
        {
            value = value ?? string.Empty;
            if (_httpTransportScope != value)
            {
                _httpTransportScope = value;
                EditorPrefs.SetString(EditorPrefKeys.HttpTransportScope, value);
                OnConfigurationChanged?.Invoke(nameof(HttpTransportScope));
            }
        }

        /// <summary>
        /// Set UnitySocketPort and update cache + EditorPrefs atomically.
        /// </summary>
        public void SetUnitySocketPort(int value)
        {
            if (_unitySocketPort != value)
            {
                _unitySocketPort = value;
                EditorPrefs.SetInt(EditorPrefKeys.UnitySocketPort, value);
                OnConfigurationChanged?.Invoke(nameof(UnitySocketPort));
            }
        }

        /// <summary>
        /// Force refresh of a single cached value from EditorPrefs.
        /// Useful when external code modifies EditorPrefs directly.
        /// </summary>
        public void InvalidateKey(string keyName)
        {
            switch (keyName)
            {
                case nameof(UseHttpTransport):
                    _useHttpTransport = EditorPrefs.GetBool(EditorPrefKeys.UseHttpTransport, true);
                    break;
                case nameof(DebugLogs):
                    _debugLogs = EditorPrefs.GetBool(EditorPrefKeys.DebugLogs, false);
                    break;
                case nameof(UseBetaServer):
                    _useBetaServer = EditorPrefs.GetBool(EditorPrefKeys.UseBetaServer, true);
                    break;
                case nameof(DevModeForceServerRefresh):
                    _devModeForceServerRefresh = EditorPrefs.GetBool(EditorPrefKeys.DevModeForceServerRefresh, false);
                    break;
                case nameof(UvxPathOverride):
                    _uvxPathOverride = EditorPrefs.GetString(EditorPrefKeys.UvxPathOverride, string.Empty);
                    break;
                case nameof(GitUrlOverride):
                    _gitUrlOverride = EditorPrefs.GetString(EditorPrefKeys.GitUrlOverride, string.Empty);
                    break;
                case nameof(HttpBaseUrl):
                    _httpBaseUrl = EditorPrefs.GetString(EditorPrefKeys.HttpBaseUrl, string.Empty);
                    break;
                case nameof(HttpRemoteBaseUrl):
                    _httpRemoteBaseUrl = EditorPrefs.GetString(EditorPrefKeys.HttpRemoteBaseUrl, string.Empty);
                    break;
                case nameof(ClaudeCliPathOverride):
                    _claudeCliPathOverride = EditorPrefs.GetString(EditorPrefKeys.ClaudeCliPathOverride, string.Empty);
                    break;
                case nameof(HttpTransportScope):
                    _httpTransportScope = EditorPrefs.GetString(EditorPrefKeys.HttpTransportScope, string.Empty);
                    break;
                case nameof(UnitySocketPort):
                    _unitySocketPort = EditorPrefs.GetInt(EditorPrefKeys.UnitySocketPort, 0);
                    break;
            }
            OnConfigurationChanged?.Invoke(keyName);
        }
    }
}
