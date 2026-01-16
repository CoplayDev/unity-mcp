using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace MCPForUnity.Editor.ActionTrace.Capture
{
    /// <summary>
    /// First line of defense: Capture-layer blacklist to filter out system junk.
    ///
    /// Philosophy: Blacklist at capture layer = "Record everything EXCEPT known garbage"
    /// - Preserves serendipity: AI can see unexpected but important changes
    /// - Protects memory: Prevents EventStore from filling with 29000 junk entries
    ///
    /// Filtered patterns:
    /// - Python cache: __pycache__, *.pyc
    /// - Unity internals: Library/, Temp/, obj/, .csproj files
    /// - Temporary files: *.tmp, ~$*, .DS_Store
    /// - Build artifacts: *.meta (for non-essential assets)
    ///
    /// Usage: Call EventFilter.IsJunkPath(path) before recording events.
    /// </summary>
    public static class EventFilter
    {
        // ========== Blacklist Patterns ==========

        /// <summary>
        /// Directory prefixes that are always filtered (fast path check).
        /// Checked before regex for performance.
        /// </summary>
        private static readonly HashSet<string> JunkDirectoryPrefixes = new(StringComparer.OrdinalIgnoreCase)
        {
            "Library/",
            "Temp/",
            "obj/",
            "Logs/",
            "UserSettings/",
            "__pycache__/",
            ".git/",
            ".vs/",
            "bin/",
            "debug/"
        };

        /// <summary>
        /// File extension blacklist (fast path check).
        /// Extensions with leading dot (e.g., ".pyc")
        /// </summary>
        private static readonly HashSet<string> JunkExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            // Python cache
            ".pyc",
            ".pyo",
            ".pyd",

            // Temporary files
            ".tmp",
            ".temp",
            ".cache",
            ".bak",
            ".swp",
            "~$",

            // OS-specific
            ".DS_Store",
            "Thumbs.db",
            "desktop.ini",

            // Build artifacts (Unity-specific)
            ".csproj",
            ".sln",
            ".suo",
            ".user",
            ".pidb",
            ".booproj"
        };

        /// <summary>
        /// Regex patterns for complex junk path matching.
        /// Used when prefix/extension checks aren't enough.
        /// </summary>
        private static readonly List<Regex> JunkPatterns = new()
        {
            // Python cache directories (any nested __pycache__)
            new Regex(@"__pycache__", RegexOptions.Compiled),

            // IDE temp files (editor recovery files)
            new Regex(@"~\$.*", RegexOptions.Compiled),

            // Unity Library subdirectories (deep nested junk)
            new Regex(@"Library/[^/]+/.*", RegexOptions.Compiled),

            // Build artifacts with specific patterns
            new Regex(@".*\.Assembly-CSharp[^/]*\.dll$", RegexOptions.Compiled),
            new Regex(@".*\.Unity\.Editor\.dll$", RegexOptions.Compiled),

            // Temp directories anywhere in path
            new Regex(@".*/Temp/.*", RegexOptions.Compiled | RegexOptions.IgnoreCase)
        };

        // ========== Public API ==========

        /// <summary>
        /// Determines if a given path should be filtered as junk.
        ///
        /// Uses a tiered checking strategy for performance:
        /// 1. Fast prefix check (string StartsWith)
        /// 2. Fast extension check (string EndsWith)
        /// 3. Regex pattern match (only if needed)
        ///
        /// Returns: true if the path should be filtered out, false otherwise.
        /// </summary>
        public static bool IsJunkPath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return false;

            // Fast path 1: Directory prefix check
            foreach (var prefix in JunkDirectoryPrefixes)
            {
                if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            // Fast path 2: File extension check
            foreach (var ext in JunkExtensions)
            {
                if (path.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            // Slow path: Regex pattern match
            foreach (var pattern in JunkPatterns)
            {
                if (pattern.IsMatch(path))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Checks if an asset path should generate an event.
        /// This is a wrapper around IsJunkPath with additional logic for assets.
        ///
        /// Asset-specific filtering:
        /// - .meta files are filtered UNLESS they're for prefabs/scenes (important assets)
        /// - Resources folder assets are never filtered
        /// </summary>
        public static bool ShouldTrackAsset(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
                return true; // Default to tracking for safety

            // Check base junk filter
            if (IsJunkPath(assetPath))
                return false;

            // Special handling for .meta files
            if (assetPath.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
            {
                // Track .meta for important asset types
                string basePath = assetPath.Substring(0, assetPath.Length - 5); // Remove ".meta"

                // Track if it's a prefab or scene
                if (basePath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase) ||
                    basePath.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                // Skip .meta for everything else
                return false;
            }

            // Never filter assets in Resources folder (user-accessible at runtime)
            if (assetPath.Contains("/Resources/", StringComparison.OrdinalIgnoreCase))
                return true;

            return true; // Default: track the asset
        }

        /// <summary>
        /// Checks if a GameObject name should be filtered.
        /// Unity creates many internal GameObjects that are rarely interesting.
        ///
        /// Filtered patterns:
        /// - "Collider" + number (generated colliders)
        /// - "GameObject" + number (unnamed objects)
        /// - Unity editor internals
        /// </summary>
        public static bool IsJunkGameObject(string name)
        {
            if (string.IsNullOrEmpty(name))
                return false;

            // Unity-generated colliders
            if (Regex.IsMatch(name, @"^Collider\d+$", RegexOptions.IgnoreCase))
                return true;

            // Unnamed GameObjects
            if (Regex.IsMatch(name, @"^GameObject\d+$", RegexOptions.IgnoreCase))
                return true;

            // Editor-only objects
            if (name.StartsWith("EditorOnly", StringComparison.OrdinalIgnoreCase))
                return true;

            return false;
        }

        // ========== Runtime Configuration ==========

        /// <summary>
        /// Adds a custom junk directory prefix at runtime.
        /// Useful for project-specific filtering rules.
        /// </summary>
        public static void AddJunkDirectoryPrefix(string prefix)
        {
            if (!string.IsNullOrEmpty(prefix))
                JunkDirectoryPrefixes.Add(prefix);
        }

        /// <summary>
        /// Adds a custom junk file extension at runtime.
        /// </summary>
        public static void AddJunkExtension(string extension)
        {
            if (!string.IsNullOrEmpty(extension))
            {
                string ext = extension.StartsWith(".") ? extension : $".{extension}";
                JunkExtensions.Add(ext);
            }
        }

        /// <summary>
        /// Adds a custom regex pattern for junk path matching.
        /// </summary>
        public static void AddJunkPattern(string regexPattern)
        {
            if (!string.IsNullOrEmpty(regexPattern))
            {
                JunkPatterns.Add(new Regex(regexPattern, RegexOptions.Compiled));
            }
        }

        // ========== Diagnostic Info ==========

        /// <summary>
        /// Gets diagnostic information about the filter configuration.
        /// Useful for debugging and ensuring filters are working as expected.
        /// </summary>
        public static string GetDiagnosticInfo()
        {
            return $"EventFilter Configuration:\n" +
                   $"  - Directory Prefixes: {JunkDirectoryPrefixes.Count}\n" +
                   $"  - Junk Extensions: {JunkExtensions.Count}\n" +
                   $"  - Regex Patterns: {JunkPatterns.Count}\n" +
                   $"  - Total Checks: 3-tier (prefix → extension → regex)";
        }
    }
}
