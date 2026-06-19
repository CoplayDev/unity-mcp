using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace MCPForUnity.Editor.Helpers
{
    /// <summary>
    /// Shared-secret authentication for the local bridge (harden/security, R4/R5).
    ///
    /// The local stdio TCP bridge and the HTTP-local control plane were unauthenticated:
    /// any local process could connect and drive the full toolset, and a new stdio
    /// connection could displace the live one. A shared token narrows the trust boundary
    /// from "any process on the machine" to "any process that knows my token."
    ///
    /// The token is resolved identically by the Unity Editor (this helper) and the Python
    /// server: the UNITY_MCP_BRIDGE_TOKEN environment variable wins; otherwise a 0600 file
    /// at ~/.unity-mcp/bridge-token is used. The Editor is the single writer — it generates
    /// the file on first use so the Python client (which only reads) never races to create it.
    /// </summary>
    internal static class BridgeAuth
    {
        internal const string EnvVarName = "UNITY_MCP_BRIDGE_TOKEN";
        private const string TokenFileName = "bridge-token";

        private static readonly object _lock = new object();
        private static string _cached;

        /// <summary>
        /// Returns the active bridge token, generating and persisting one if necessary.
        /// Never returns null/empty under normal conditions.
        /// </summary>
        internal static string GetToken()
        {
            if (!string.IsNullOrEmpty(_cached))
                return _cached;

            lock (_lock)
            {
                if (!string.IsNullOrEmpty(_cached))
                    return _cached;

                string fromEnv = Environment.GetEnvironmentVariable(EnvVarName);
                if (!string.IsNullOrWhiteSpace(fromEnv))
                {
                    _cached = fromEnv.Trim();
                    return _cached;
                }

                _cached = ReadOrCreateTokenFile();
                return _cached;
            }
        }

        /// <summary>
        /// Constant-time comparison of a presented token against the active token.
        /// </summary>
        internal static bool IsValid(string presented)
        {
            if (string.IsNullOrEmpty(presented))
                return false;
            string expected = GetToken();
            if (string.IsNullOrEmpty(expected))
                return false;

            byte[] a = Encoding.UTF8.GetBytes(presented);
            byte[] b = Encoding.UTF8.GetBytes(expected);
            // FixedTimeEquals short-circuits only on length; pad to equal length so the
            // length itself isn't leaked by timing.
            if (a.Length != b.Length)
            {
                // Still run a fixed-time compare against expected to avoid an early-out.
                CryptographicOperations.FixedTimeEquals(b, b);
                return false;
            }
            return CryptographicOperations.FixedTimeEquals(a, b);
        }

        internal static string TokenFilePath()
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".unity-mcp");
            return Path.Combine(dir, TokenFileName);
        }

        private static string ReadOrCreateTokenFile()
        {
            string path = TokenFilePath();
            try
            {
                if (File.Exists(path))
                {
                    string existing = File.ReadAllText(path).Trim();
                    if (!string.IsNullOrEmpty(existing))
                        return existing;
                }
            }
            catch (Exception ex)
            {
                McpLog.Warn($"[BridgeAuth] Could not read token file: {ex.Message}");
            }

            string token = GenerateToken();
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, token);
                TryRestrictPermissions(path);
            }
            catch (Exception ex)
            {
                McpLog.Warn($"[BridgeAuth] Could not write token file: {ex.Message}");
            }
            return token;
        }

        private static string GenerateToken()
        {
            byte[] buf = new byte[32];
            using (var rng = RandomNumberGenerator.Create())
                rng.GetBytes(buf);
            // URL-safe, no padding — keeps it shell/JSON friendly.
            return Convert.ToBase64String(buf)
                .Replace('+', '-').Replace('/', '_').TrimEnd('=');
        }

        /// <summary>
        /// Best-effort 0600 on POSIX. Uses File.SetUnixFileMode via reflection so we don't
        /// take a hard dependency on a .NET 6 API across the supported Unity version range.
        /// </summary>
        private static void TryRestrictPermissions(string path)
        {
            try
            {
                if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                    return;
                var mi = typeof(File).GetMethod("SetUnixFileMode", new[] { typeof(string), Type.GetType("System.IO.UnixFileMode") });
                var modeEnum = Type.GetType("System.IO.UnixFileMode");
                if (mi != null && modeEnum != null)
                {
                    // UserRead (0x100) | UserWrite (0x80) == 0600
                    object mode = Enum.ToObject(modeEnum, 0x100 | 0x80);
                    mi.Invoke(null, new object[] { path, mode });
                }
            }
            catch
            {
                // Best effort only; on platforms/runtimes without the API the file keeps
                // its default (umask) permissions.
            }
        }
    }
}
