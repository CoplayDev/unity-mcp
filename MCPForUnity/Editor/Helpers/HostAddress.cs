using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;

namespace MCPForUnity.Editor.Helpers
{
    /// <summary>
    /// Centralized host address handling for IPv4/IPv6 connections.
    /// Provides platform-aware defaults and consistent address normalization.
    /// </summary>
    public static class HostAddress
    {
        // Platform-specific default host
#if UNITY_EDITOR_WIN
        public const string DefaultHost = "127.0.0.1";
#else
        public const string DefaultHost = "localhost";
#endif

        /// <summary>
        /// Returns the platform-appropriate default host for connections.
        /// </summary>
        public static string GetDefaultHost() => DefaultHost;

        /// <summary>
        /// Normalizes a host address for client connections.
        /// - Explicit IPv4/IPv6 addresses are preserved
        /// - Wildcard addresses (0.0.0.0, ::) are converted to platform-aware default
        /// - localhost is kept as-is (DNS resolution will handle it)
        /// </summary>
        public static string NormalizeForClient(string host)
        {
            if (string.IsNullOrWhiteSpace(host))
                return DefaultHost;

            // Bind-only wildcards - use platform-aware default (check before explicit IP checks)
            // Note: 0.0.0.0 is valid IPv4 format, so we must check for wildcards first
            if (host == "0.0.0.0" || host == "::")
                return DefaultHost;

            // Explicit IPv6 - respect user choice
            if (IsExplicitIPv6(host))
                return host;

            // Explicit IPv4 - respect user choice (but exclude 0.0.0.0 which is already handled)
            if (IsExplicitIPv4(host))
                return host;

            // localhost and other hostnames - keep as-is (DNS resolution will handle it)
            return host;
        }

        /// <summary>
        /// Builds an ordered list of hosts to try connecting to.
        /// Returns IPv4 first for wildcard addresses on all platforms.
        /// </summary>
        public static List<string> BuildConnectionList(string host, bool enableIPv6Fallback = false)
        {
            var result = new List<string>();

            if (string.IsNullOrWhiteSpace(host))
                host = DefaultHost;

            // Bind-only wildcards - IPv4 first, optional IPv6 fallback
            // Note: 0.0.0.0 is valid IPv4 format, so check for wildcards before explicit IPv4
            if (host == "0.0.0.0" || host == "::")
            {
                result.Add("127.0.0.1");  // Always try IPv4 first
                if (enableIPv6Fallback)
                    result.Add("::1");
                return result;
            }

            // Explicit IPv6 - single attempt
            if (IsExplicitIPv6(host))
            {
                result.Add(host);
                return result;
            }

            // Explicit IPv4 - single attempt
            if (IsExplicitIPv4(host))
            {
                result.Add(host);
                return result;
            }

            // localhost and other hostnames
            if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
            {
                result.Add("127.0.0.1");  // Always try IPv4 first
                if (enableIPv6Fallback)
                    result.Add("::1");
            }
            else
            {
                // Other hostnames - single attempt
                result.Add(host);
            }

            return result;
        }

        /// <summary>
        /// Checks if the host is an explicit IPv4 address (x.x.x.x format).
        /// </summary>
        public static bool IsExplicitIPv4(string host)
        {
            if (string.IsNullOrWhiteSpace(host)) return false;
            string[] parts = host.Split('.');
            return parts.Length == 4 &&
                   parts.All(p => int.TryParse(p, out int n) && n >= 0 && n <= 255);
        }

        /// <summary>
        /// Checks if the host is an explicit IPv6 address.
        /// Handles IPv4-mapped addresses (e.g., ::ffff:127.0.0.1) and zone IDs (e.g., fe80::1%eth0).
        /// </summary>
        public static bool IsExplicitIPv6(string host)
        {
            if (string.IsNullOrWhiteSpace(host)) return false;

            // Handle zone ID (e.g., fe80::1%eth0) - strip the zone part before parsing
            var addressPart = host.Contains('%') ? host.Split('%')[0] : host;

            // Use IPAddress.Parse for reliable IPv6 detection
            if (IPAddress.TryParse(addressPart, out var ipAddress))
            {
                return ipAddress.AddressFamily == AddressFamily.InterNetworkV6;
            }

            return false;
        }

        /// <summary>
        /// Checks if the host is a bind-only wildcard address.
        /// </summary>
        public static bool IsBindOnlyAddress(string host)
        {
            return host == "0.0.0.0" || host == "::";
        }
    }
}
