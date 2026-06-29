using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace MCPForUnity.Editor.Helpers
{
    /// <summary>
    /// Best-effort detection of a locally installed Blender application, for the Asset Gen tab's
    /// "Blender → Unity handoff" hint. This finds the Blender APP only — it cannot tell whether the
    /// BlenderMCP server is configured in the user's AI client (that lives outside Unity).
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
    }
}
