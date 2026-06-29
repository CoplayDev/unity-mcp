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
            string dataPath = Application.dataPath.Replace('\\', '/');
            string projectRoot = dataPath.Substring(0, dataPath.Length - "Assets".Length);
            return Path.Combine(projectRoot, projectRelative).Replace('\\', '/');
        }

        /// <summary>Convert an absolute (or already-relative) path to a project-relative ("Assets/...") path.</summary>
        public static string ToProjectRelative(string path)
        {
            string p = path.Replace('\\', '/');
            if (p.StartsWith("Assets")) return p;
            string dataPath = Application.dataPath.Replace('\\', '/');
            if (p.StartsWith(dataPath)) return "Assets" + p.Substring(dataPath.Length);
            return p;
        }
    }
}
