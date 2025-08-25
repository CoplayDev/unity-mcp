using UnityEditor;
using UnityEngine;

namespace MCPForUnity.Editor.Helpers
{
    /// <summary>
    /// Auto-runs legacy/older install detection on package load/update (log-only).
    /// Runs once per embedded server version using an EditorPrefs version-scoped key.
    /// </summary>
    [InitializeOnLoad]
    public static class PackageDetector
    {
        private const string DetectOnceFlagKeyPrefix = "MCPForUnity.LegacyDetectLogged:";

        static PackageDetector()
        {
            try
            {
                string pkgVer = ReadPackageVersionOrFallback();
                string key = DetectOnceFlagKeyPrefix + pkgVer;

                // Always force-run if legacy roots exist or canonical install is missing
                bool legacyPresent = LegacyRootsExist();
                bool canonicalMissing = !System.IO.File.Exists(System.IO.Path.Combine(ServerInstaller.GetServerPath(), "server.py"));

                if (!EditorPrefs.GetBool(key, false) || legacyPresent || canonicalMissing)
                {
                    EditorApplication.delayCall += () =>
                    {
                        try
                        {
                            ServerInstaller.EnsureServerInstalled();
                        }
                        catch (System.Exception ex)
                        {
                            Debug.LogWarning("MCP for Unity: Auto-detect on load failed: " + ex.Message);
                        }
                        finally
                        {
                            EditorPrefs.SetBool(key, true);
                        }
                    };
                }
            }
            catch { /* ignore */ }
        }

        private static string ReadEmbeddedVersionOrFallback()
        {
            try
            {
                if (ServerPathResolver.TryFindEmbeddedServerSource(out var embeddedSrc))
                {
                    var p = System.IO.Path.Combine(embeddedSrc, "server_version.txt");
                    if (System.IO.File.Exists(p))
                        return (System.IO.File.ReadAllText(p)?.Trim() ?? "unknown");
                }
            }
            catch { }
            return "unknown";
        }

        private static string ReadPackageVersionOrFallback()
        {
            try
            {
                var info = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(PackageDetector).Assembly);
                if (info != null && !string.IsNullOrEmpty(info.version)) return info.version;
            }
            catch { }
            // Fallback to embedded server version if package info unavailable
            return ReadEmbeddedVersionOrFallback();
        }

        private static bool LegacyRootsExist()
        {
            try
            {
                string home = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile) ?? string.Empty;
                string[] roots =
                {
                    System.IO.Path.Combine(home, ".config", "UnityMCP", "UnityMcpServer", "src"),
                    System.IO.Path.Combine(home, ".local", "share", "UnityMCP", "UnityMcpServer", "src")
                };
                foreach (var r in roots)
                {
                    try { if (System.IO.File.Exists(System.IO.Path.Combine(r, "server.py"))) return true; } catch { }
                }
            }
            catch { }
            return false;
        }
    }
}


