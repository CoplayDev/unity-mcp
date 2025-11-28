using System;
using System.Net;
using System.Net.Sockets;
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
            && !IsLocalAddress(defaultHttpBaseUrl);

        private static bool IsLocalAddress(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return true;
            }

            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                return false;
            }

            string host = uri.Host;

            if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (IPAddress.TryParse(host, out var ip))
            {
                if (IPAddress.IsLoopback(ip))
                {
                    return true;
                }

                if (ip.AddressFamily == AddressFamily.InterNetwork)
                {
                    var bytes = ip.GetAddressBytes();
                    // 10.0.0.0/8, 172.16.0.0/12, 192.168.0.0/16, 169.254.0.0/16
                    if (bytes[0] == 10) return true;
                    if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) return true;
                    if (bytes[0] == 192 && bytes[1] == 168) return true;
                    if (bytes[0] == 169 && bytes[1] == 254) return true;
                }
                else if (ip.AddressFamily == AddressFamily.InterNetworkV6)
                {
                    // ::1 loopback or fe80::/10 link-local
                    if (ip.IsIPv6LinkLocal || ip.Equals(IPAddress.IPv6Loopback))
                    {
                        return true;
                    }
                }

                return false;
            }

            // Hostname: treat *.local as local network; otherwise assume remote.
            if (host.EndsWith(".local", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }
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
