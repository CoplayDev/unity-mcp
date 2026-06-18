using MCPForUnity.Runtime.Helpers;
using UnityEditor;

namespace MCPForUnity.Editor.Helpers
{
    /// <summary>
    /// Per-user EditorPrefs override for the default screenshot output folder.
    /// Resolution priority used by callers:
    ///   1. Per-call <c>output_folder</c> tool parameter
    ///   2. <see cref="DefaultFolder"/> (this preference)
    ///   3. <see cref="ScreenshotUtility.DefaultFolder"/> built-in fallback
    /// </summary>
    public static class ScreenshotPreferences
    {
        public const string EditorPrefsKey = "MCPForUnity_ScreenshotsFolder";

        /// <summary>
        /// User-configured default folder, or empty string when unset.
        /// Stored as a project-relative path (e.g. "Assets/Screenshots", "Captures").
        /// </summary>
        public static string DefaultFolder
        {
            get => EditorPrefs.GetString(EditorPrefsKey, string.Empty);
            set
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    EditorPrefs.DeleteKey(EditorPrefsKey);
                }
                else
                {
                    EditorPrefs.SetString(EditorPrefsKey, value.Trim());
                }
            }
        }

        /// <summary>
        /// Resolves the effective folder: caller override → user pref → built-in default.
        /// Returns a project-relative path string suitable for <see cref="ScreenshotUtility.ResolveFolderAbsolute"/>.
        /// </summary>
        public static string Resolve(string callerOverride)
        {
            if (!string.IsNullOrWhiteSpace(callerOverride)) return callerOverride.Trim();
            string pref = DefaultFolder;
            if (!string.IsNullOrWhiteSpace(pref)) return pref;
            return ScreenshotUtility.DefaultFolder;
        }
    }
}
