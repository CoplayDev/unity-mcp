using System;
using System.IO;

namespace MCPForUnity.Editor.Helpers
{
    /// <summary>
    /// Writes and removes a configuration-only DeepSeek Harness (DSH) bundle that mounts
    /// the Unity MCP server. The bundle is a small directory under
    /// <c>$DSH_HOME/bundles/&lt;dir&gt;</c> containing a <c>package.json</c> manifest and
    /// the <c>cordis.patch.yml</c> patch block (produced by
    /// <see cref="DshConfigHelper.BuildUnityMcpBlock"/>). It is installed into a specific
    /// DSH profile through the DSH plugin manager
    /// (<c>dsh plugin --profile &lt;name&gt; add &lt;bundle-dir&gt;</c>), making the row
    /// profile-scoped and removable as a unit — unlike the root home patch, which applies
    /// to every profile and is not uninstallable through the plugin manager.
    /// </summary>
    public static class DshBundleWriter
    {
        /// <summary>Bundle package name recorded in the manifest and used for remove.</summary>
        public const string BundleName = "@local/unity-mcp-dsh";

        /// <summary>Directory name under <c>$DSH_HOME/bundles/</c>.</summary>
        public const string BundleDirName = "unity-mcp";

        private const string ManifestFileName = "package.json";
        private const string PatchFileName = "cordis.patch.yml";

        /// <summary>
        /// The exact manifest content proven to install via DSH's plugin manager.
        /// Must stay byte-identical to what the <c>dsh plugin add</c> flow expects.
        /// </summary>
        internal const string ManifestJson =
            "{\n" +
            "  \"name\": \"@local/unity-mcp-dsh\",\n" +
            "  \"version\": \"1.0.0\",\n" +
            "  \"private\": true,\n" +
            "  \"type\": \"module\",\n" +
            "  \"dsh\": { \"bundle\": { \"patch\": \"./cordis.patch.yml\" } }\n" +
            "}\n";

        /// <summary>
        /// Writes (or overwrites) the bundle directory containing the manifest and the
        /// given patch block. The bundle directory is entirely managed by us, so no marker
        /// surgery is needed inside it — files are overwritten wholesale. Returns the
        /// bundle directory path. The path is profile-qualified because
        /// <c>dsh plugin add</c> symlinks this directory into the profile: sharing one
        /// directory across profiles would let one profile's Unregister dangle another's
        /// link.
        /// </summary>
        /// <param name="dshHome">DSH home directory (<c>$DSH_HOME</c> or <c>~/.dsh</c>).</param>
        /// <param name="profile">DSH profile the bundle is installed into.</param>
        /// <param name="patchBlock">Full YAML block from <see cref="DshConfigHelper.BuildUnityMcpBlock"/>.</param>
        public static string WriteBundle(string dshHome, string profile, string patchBlock)
        {
            if (string.IsNullOrEmpty(dshHome))
                throw new ArgumentException("dshHome is required", nameof(dshHome));
            if (patchBlock == null)
                throw new ArgumentNullException(nameof(patchBlock));

            string dir = GetBundleDir(dshHome, profile);
            Directory.CreateDirectory(dir);

            File.WriteAllText(Path.Combine(dir, ManifestFileName), ManifestJson);
            File.WriteAllText(Path.Combine(dir, PatchFileName), patchBlock);

            return dir;
        }

        /// <summary>
        /// Removes the bundle directory recursively. Returns true when the directory
        /// existed and was deleted; false when it was already absent. Only the calling
        /// profile's bundle is removed — other profiles keep their own directories.
        /// </summary>
        public static bool TryDeleteBundle(string dshHome, string profile)
        {
            if (string.IsNullOrEmpty(dshHome)) return false;
            string dir = GetBundleDir(dshHome, profile);
            if (!Directory.Exists(dir)) return false;
            Directory.Delete(dir, true);
            return true;
        }

        /// <summary>
        /// Path to the profile-qualified bundle directory that holds the manifest and patch.
        /// Profile names are validated because the name flows into filesystem paths and into
        /// the <c>cmd.exe</c> command line: '%' is rejected (cmd expands %NAME% patterns even
        /// inside double quotes) and '"' breaks argument quoting. cmd.exe metacharacters such
        /// as '&amp;' are allowed because arguments are always double-quoted.
        /// </summary>
        public static string GetBundleDir(string dshHome, string profile)
        {
            if (string.IsNullOrWhiteSpace(profile))
                throw new ArgumentException("profile is required", nameof(profile));
            if (profile == "." || profile == ".."
                || profile.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
                || profile.IndexOfAny(new[] { '/', '\\', '%', '"' }) >= 0)
            {
                throw new ArgumentException($"profile is not a safe directory name: '{profile}'", nameof(profile));
            }
            return Path.Combine(dshHome, "bundles", profile, BundleDirName);
        }
    }
}
