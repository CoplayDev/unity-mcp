using System;
using System.Collections.Generic;
using MCPForUnity.Editor.Constants;
using MCPForUnity.Editor.Security;
using UnityEditor;

namespace MCPForUnity.Editor.Helpers
{
    /// <summary>
    /// Per-user configuration for <c>execute_code</c>'s delete allowlist.
    ///
    /// Stored in EditorPrefs (not in the tool arguments) on purpose: an agent must not
    /// be able to grant itself permission to delete arbitrary paths. A present-but-empty
    /// value means "deny every delete", which lets a user lock the feature down without
    /// disabling safety checks entirely.
    /// </summary>
    internal static class ExecuteCodePrefs
    {
        private static readonly char[] Separators = { ';', '\n', '\r' };

        /// <summary>
        /// Project-relative roots that <c>File.Delete</c> / <c>Directory.Delete</c> may
        /// target. Falls back to <see cref="DeletePathPolicy.DefaultAllowedRoots"/> when
        /// the user has never configured a value.
        /// </summary>
        internal static IReadOnlyList<string> AllowedDeleteRoots
        {
            get
            {
                if (!EditorPrefs.HasKey(EditorPrefKeys.ExecuteCodeDeleteRoots))
                    return DeletePathPolicy.DefaultAllowedRoots;

                string raw = EditorPrefs.GetString(EditorPrefKeys.ExecuteCodeDeleteRoots, string.Empty);
                return DeletePathPolicy.NormalizeAllowedRoots(
                    raw.Split(Separators, StringSplitOptions.RemoveEmptyEntries));
            }
        }

        internal static bool HasCustomAllowedDeleteRoots =>
            EditorPrefs.HasKey(EditorPrefKeys.ExecuteCodeDeleteRoots);

        internal static void SetAllowedDeleteRoots(IEnumerable<string> roots)
        {
            var normalized = DeletePathPolicy.NormalizeAllowedRoots(roots);
            EditorPrefs.SetString(EditorPrefKeys.ExecuteCodeDeleteRoots, string.Join(";", normalized));
        }

        internal static void ResetAllowedDeleteRoots()
        {
            EditorPrefs.DeleteKey(EditorPrefKeys.ExecuteCodeDeleteRoots);
        }
    }
}
