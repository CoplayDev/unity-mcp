using System;
using System.IO;
using UnityEngine;

namespace MCPForUnity.Editor.Helpers
{
    /// <summary>
    /// Project-path conversions shared by the asset-gen import/write code: project-relative
    /// ("Assets/...") ↔ absolute on-disk paths, with forward-slash normalization for
    /// cross-platform consistency.
    /// </summary>
    public static class AssetGenPaths
    {
        /// <summary>Resolve a project-relative ("Assets/...") path to an absolute, forward-slashed path.</summary>
        public static string ToAbsolute(string projectRelative)
        {
            if (string.IsNullOrWhiteSpace(projectRelative)) return projectRelative;
            string p = projectRelative.Replace('\\', '/');
            string abs = Path.IsPathRooted(p) ? p : Path.Combine(ProjectRoot(), p);
            return Path.GetFullPath(abs).Replace('\\', '/');
        }

        /// <summary>Convert an absolute (or already-relative) path to a project-relative ("Assets/...") path.</summary>
        public static string ToProjectRelative(string path)
        {
            if (TryGetAssetsRelativePath(path, out string rel)) return rel;
            return path?.Replace('\\', '/');
        }

        /// <summary>
        /// Normalize an absolute or "Assets/..." path and verify it resolves inside the project's
        /// Assets directory. Rejects traversal such as "Assets/../ProjectSettings".
        /// </summary>
        public static bool TryGetAssetsRelativePath(string path, out string projectRelative)
        {
            projectRelative = null;
            if (string.IsNullOrWhiteSpace(path)) return false;

            try
            {
                string p = path.Replace('\\', '/');
                string abs;
                if (p == "Assets" || p.StartsWith("Assets/", StringComparison.Ordinal))
                {
                    abs = Path.Combine(ProjectRoot(), p);
                }
                else if (Path.IsPathRooted(p))
                {
                    abs = p;
                }
                else
                {
                    return false;
                }

                string full = Path.GetFullPath(abs).Replace('\\', '/');
                string dataPath = Path.GetFullPath(Application.dataPath).Replace('\\', '/').TrimEnd('/');
                if (string.Equals(full, dataPath, StringComparison.Ordinal))
                {
                    projectRelative = "Assets";
                    return true;
                }

                string prefix = dataPath + "/";
                if (!full.StartsWith(prefix, StringComparison.Ordinal)) return false;

                projectRelative = "Assets/" + full.Substring(prefix.Length);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Normalize an Assets folder path, trimming trailing slashes.</summary>
        public static bool TryGetAssetsFolder(string path, out string projectRelative)
        {
            if (!TryGetAssetsRelativePath(path, out projectRelative)) return false;
            projectRelative = projectRelative.TrimEnd('/');
            return true;
        }

        private static string ProjectRoot()
        {
            string dataPath = Path.GetFullPath(Application.dataPath).Replace('\\', '/');
            return dataPath.Substring(0, dataPath.Length - "Assets".Length);
        }
    }
}
