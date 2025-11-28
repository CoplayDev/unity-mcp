using System;
using UnityEngine;

namespace MCPForUnity.Editor.Config
{
    /// <summary>
    /// Distribution controls so we can ship different defaults (Asset Store vs. git) without forking code.
    /// </summary>
    [CreateAssetMenu(menuName = "MCP/Distribution Settings", fileName = "McpDistributionSettings")]
    public class McpDistributionSettings : ScriptableObject
    {
        [SerializeField] internal string defaultHttpBaseUrl = "http://localhost:8080";
        [SerializeField] internal bool skipSetupWindowWhenRemoteDefault = false;

        internal bool IsRemoteDefault =>
            !string.IsNullOrWhiteSpace(defaultHttpBaseUrl)
            && !defaultHttpBaseUrl.Contains("localhost", StringComparison.OrdinalIgnoreCase)
            && !defaultHttpBaseUrl.Contains("127.0.0.1", StringComparison.OrdinalIgnoreCase);
    }

    internal static class McpDistribution
    {
        private const string ResourcePath = "McpDistributionSettings";
        private static McpDistributionSettings _cached;

        internal static McpDistributionSettings Settings
        {
            get
            {
                if (_cached != null)
                {
                    return _cached;
                }

                _cached = UnityEngine.Resources.Load<McpDistributionSettings>(ResourcePath);
                if (_cached != null)
                {
                    return _cached;
                }

                // No asset present (git/dev installs) - fall back to baked-in defaults.
                _cached = ScriptableObject.CreateInstance<McpDistributionSettings>();
                _cached.name = "McpDistributionSettings (Runtime Defaults)";
                return _cached;
            }
        }
    }
}
