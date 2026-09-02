using System.IO;
using MCPForUnity.Editor.Constants;
using MCPForUnity.Editor.Services.Blender;
using UnityEditor;

namespace MCPForUnity.Editor.Helpers
{
    /// <summary>
    /// Per-user, NON-SECRET configuration for the Blender Bridge (Asset Gen tab): where the
    /// BlenderMCP addon socket listens, where the user's blender-mcp checkout lives, and where
    /// Blender keeps its user addons. Nothing here is required for the bridge to talk to Blender;
    /// the checkout path only unlocks addon sync and update checks. EditorPrefs is main-thread
    /// only, so callers capture what they need (e.g. <see cref="Endpoint"/>) before going async.
    /// </summary>
    public static class BlenderBridgePrefs
    {
        public const string DefaultHost = "127.0.0.1";
        public const int DefaultPort = 9876;
        public const string AddonFileName = "addon.py";

        public static string Host
        {
            get => EditorPrefs.GetString(EditorPrefKeys.BlenderHost, DefaultHost);
            set => SetOrDelete(EditorPrefKeys.BlenderHost, value);
        }

        public static int Port
        {
            get => EditorPrefs.GetInt(EditorPrefKeys.BlenderPort, DefaultPort);
            set => EditorPrefs.SetInt(EditorPrefKeys.BlenderPort, value > 0 && value <= 65535 ? value : DefaultPort);
        }

        /// <summary>Host and port as one value, safe to hand to a background thread.</summary>
        public static BlenderEndpoint Endpoint => new BlenderEndpoint(Host, Port);

        /// <summary>Local checkout of the blender-mcp repository (contains addon.py). Empty = not configured.</summary>
        public static string ForkPath
        {
            get => NormalizePath(EditorPrefs.GetString(EditorPrefKeys.BlenderForkPath, string.Empty));
            set => SetOrDelete(EditorPrefKeys.BlenderForkPath, NormalizePath(value));
        }

        /// <summary>
        /// Blender's user addons directory. Empty = auto-detect the newest
        /// &lt;user config&gt;/Blender/&lt;version&gt;/scripts/addons that already has the addon installed.
        /// </summary>
        public static string AddonsDirOverride
        {
            get => NormalizePath(EditorPrefs.GetString(EditorPrefKeys.BlenderAddonsDir, string.Empty));
            set => SetOrDelete(EditorPrefKeys.BlenderAddonsDir, NormalizePath(value));
        }

        public static bool IsForkConfigured => !string.IsNullOrEmpty(ForkPath);

        public static string ForkAddonPath => IsForkConfigured ? ForkPath + "/" + AddonFileName : null;

        /// <summary>The override when set, otherwise the newest detected user addons folder; null if none.</summary>
        public static string ResolveAddonsDir()
        {
            string o = AddonsDirOverride;
            return !string.IsNullOrEmpty(o) ? o : BlenderDetection.FindUserAddonsDir(AddonFileName);
        }

        public static string InstalledAddonPath
        {
            get
            {
                string dir = ResolveAddonsDir();
                return string.IsNullOrEmpty(dir) ? null : dir + "/" + AddonFileName;
            }
        }

        /// <summary>True when the checkout path points at a folder that actually contains addon.py.</summary>
        public static bool IsValidForkPath(string path)
        {
            string p = NormalizePath(path);
            return !string.IsNullOrEmpty(p) && File.Exists(Path.Combine(p, AddonFileName));
        }

        /// <summary>Trims, converts backslashes to slashes and drops a trailing slash.</summary>
        internal static string NormalizePath(string value)
        {
            return (value ?? string.Empty).Trim().Replace('\\', '/').TrimEnd('/');
        }

        /// <summary>Stores a string pref, deleting the key when the value is blank so defaults apply.</summary>
        private static void SetOrDelete(string key, string value)
        {
            if (string.IsNullOrWhiteSpace(value)) EditorPrefs.DeleteKey(key);
            else EditorPrefs.SetString(key, value.Trim());
        }
    }
}
