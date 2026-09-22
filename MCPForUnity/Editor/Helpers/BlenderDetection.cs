using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace MCPForUnity.Editor.Helpers
{
    /// <summary>
    /// Best-effort detection of a locally installed Blender application and of Blender's per-user
    /// addons folders, for the Asset Gen tab's Blender Bridge. This finds the Blender APP and its
    /// config folders only — whether the BlenderMCP addon is running is a socket question answered
    /// by the bridge itself.
    /// </summary>
    internal static class BlenderDetection
    {
        /// <summary>True if a Blender executable is found in a well-known location or on PATH.</summary>
        public static bool IsInstalled()
        {
            try { return DetectIn(CandidatePaths(), File.Exists); }
            catch { return false; }
        }

        /// <summary>Pure core: true if <paramref name="exists"/> reports any candidate present. Testable.</summary>
        internal static bool DetectIn(IEnumerable<string> candidates, Func<string, bool> exists)
        {
            if (candidates == null || exists == null) return false;
            foreach (string c in candidates)
                if (!string.IsNullOrEmpty(c) && exists(c)) return true;
            return false;
        }

        /// <summary>Well-known Blender executable paths for the current platform, plus PATH entries.</summary>
        internal static IEnumerable<string> CandidatePaths()
        {
            var list = new List<string>();
            bool win = Application.platform == RuntimePlatform.WindowsEditor;
            string exeName = win ? "blender.exe" : "blender";

            // PATH entries: <dir>/blender(.exe)
            string pathVar = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            foreach (string dir in pathVar.Split(win ? ';' : ':'))
                if (!string.IsNullOrWhiteSpace(dir)) list.Add(Path.Combine(dir.Trim(), exeName));

            switch (Application.platform)
            {
                case RuntimePlatform.OSXEditor:
                    list.Add("/Applications/Blender.app/Contents/MacOS/Blender");
                    string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                    if (!string.IsNullOrEmpty(home))
                        list.Add(Path.Combine(home, "Applications/Blender.app/Contents/MacOS/Blender"));
                    break;
                case RuntimePlatform.WindowsEditor:
                    foreach (string pf in new[]
                             {
                                 Environment.GetEnvironmentVariable("ProgramFiles"),
                                 Environment.GetEnvironmentVariable("ProgramFiles(x86)")
                             })
                    {
                        if (string.IsNullOrEmpty(pf)) continue;
                        string foundation = Path.Combine(pf, "Blender Foundation");
                        // Blender installs under a version subdir (Blender X.Y); enumerate them.
                        try
                        {
                            if (Directory.Exists(foundation))
                                foreach (string d in Directory.GetDirectories(foundation))
                                    list.Add(Path.Combine(d, "blender.exe"));
                        }
                        catch { /* unreadable dir; ignore */ }
                    }
                    break;
                case RuntimePlatform.LinuxEditor:
                    list.Add("/usr/bin/blender");
                    list.Add("/usr/local/bin/blender");
                    list.Add("/snap/bin/blender");
                    list.Add("/var/lib/flatpak/exports/bin/org.blender.Blender");
                    break;
            }
            return list;
        }

        /// <summary>
        /// Blender's per-user config roots (the folder that holds one subfolder per Blender version):
        /// %APPDATA%/Blender Foundation/Blender on Windows, ~/Library/Application Support/Blender on
        /// macOS, $XDG_CONFIG_HOME/blender or ~/.config/blender on Linux.
        /// </summary>
        internal static IEnumerable<string> UserConfigRoots()
        {
            var list = new List<string>();
            switch (Application.platform)
            {
                case RuntimePlatform.WindowsEditor:
                    string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                    if (!string.IsNullOrEmpty(appData))
                        list.Add(Path.Combine(appData, "Blender Foundation", "Blender"));
                    break;
                case RuntimePlatform.OSXEditor:
                    string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                    if (!string.IsNullOrEmpty(home))
                        list.Add(Path.Combine(home, "Library", "Application Support", "Blender"));
                    break;
                case RuntimePlatform.LinuxEditor:
                    string xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
                    string linuxHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                    if (!string.IsNullOrEmpty(xdg)) list.Add(Path.Combine(xdg, "blender"));
                    else if (!string.IsNullOrEmpty(linuxHome)) list.Add(Path.Combine(linuxHome, ".config", "blender"));
                    break;
            }
            return list;
        }

        /// <summary>&lt;root&gt;/&lt;X.Y&gt;/scripts/addons for every versioned config folder, newest version first.</summary>
        internal static IEnumerable<string> UserAddonsDirs()
        {
            var found = new List<(Version Ver, string Dir)>();
            foreach (string root in UserConfigRoots())
            {
                try
                {
                    if (!Directory.Exists(root)) continue;
                    foreach (string d in Directory.GetDirectories(root))
                    {
                        Version v = ParseVersion(Path.GetFileName(d));
                        if (v != null) found.Add((v, Path.Combine(d, "scripts", "addons")));
                    }
                }
                catch { /* unreadable dir; ignore */ }
            }
            return found.OrderByDescending(x => x.Ver).Select(x => x.Dir.Replace('\\', '/')).ToList();
        }

        /// <summary>Newest user addons dir that already contains <paramref name="fileName"/>, else the newest one, else null.</summary>
        internal static string FindUserAddonsDir(string fileName)
        {
            try { return PickAddonsDir(UserAddonsDirs(), File.Exists, fileName); }
            catch { return null; }
        }

        /// <summary>Pure core of <see cref="FindUserAddonsDir"/>. Testable.</summary>
        internal static string PickAddonsDir(IEnumerable<string> dirsNewestFirst, Func<string, bool> fileExists, string fileName)
        {
            if (dirsNewestFirst == null) return null;
            string first = null;
            foreach (string d in dirsNewestFirst)
            {
                if (string.IsNullOrEmpty(d)) continue;
                first ??= d;
                if (!string.IsNullOrEmpty(fileName) && fileExists != null && fileExists(d.TrimEnd('/') + "/" + fileName))
                    return d;
            }
            return first;
        }

        /// <summary>Parses a Blender version folder name ("4.2", "5.2") into a comparable Version; null if it is not one.</summary>
        internal static Version ParseVersion(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            return Version.TryParse(name.Contains('.') ? name : name + ".0", out Version v) ? v : null;
        }
    }
}
