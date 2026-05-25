using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace WTL.InternalTerminal.Editor
{
    internal static class InternalTerminalFontResolver
    {
        private const string RegularFontFileName = "SarasaMonoSC-Regular.ttf";
        private const string BoldFontFileName = "SarasaMonoSC-Bold.ttf";
        private const string RegularFontGuid = "564c9baa7e9d8b84a8a2cf3912d3c458";
        private const string BoldFontGuid = "ac2919343ce06c24daa7ca004019f289";

        private static readonly string[] FallbackFonts =
        {
            "Cascadia Mono",
            "Cascadia Code",
            "Consolas",
            "Courier New"
        };

        public struct ResolvedFont
        {
            public ResolvedFont(string displayName, Font regular, Font bold, string source)
            {
                DisplayName = displayName;
                Regular = regular;
                Bold = bold;
                Source = source;
            }

            public string DisplayName { get; }
            public Font Regular { get; }
            public Font Bold { get; }
            public string Source { get; }
        }

        public static ResolvedFont Resolve(string configuredFontName)
        {
            var trimmed = string.IsNullOrWhiteSpace(configuredFontName) ? "auto" : configuredFontName.Trim();
            if (trimmed.StartsWith("os:", StringComparison.OrdinalIgnoreCase))
            {
                var osFontName = trimmed.Substring(3).Trim();
                if (!string.IsNullOrWhiteSpace(osFontName))
                {
                    var osFont = TryCreateOsFont(osFontName);
                    if (osFont.Regular != null)
                    {
                        return osFont;
                    }
                }
            }

            var bundledFont = TryLoadBundled();
            if (bundledFont.Regular != null)
            {
                return bundledFont;
            }

            var installed = new HashSet<string>(Font.GetOSInstalledFontNames(), StringComparer.OrdinalIgnoreCase);
            var terminalFont = TryReadWindowsTerminalFont(installed);
            if (!string.IsNullOrEmpty(terminalFont))
            {
                return TryCreateOsFont(terminalFont);
            }

            foreach (var fallback in FallbackFonts)
            {
                if (installed.Contains(fallback))
                {
                    return TryCreateOsFont(fallback);
                }
            }

            var fonts = Font.GetOSInstalledFontNames();
            return TryCreateOsFont(fonts.Length > 0 ? fonts[0] : "Courier New");
        }

        private static ResolvedFont TryLoadBundled()
        {
            var regular = LoadBundledFont(RegularFontGuid, RegularFontFileName, out var regularSource);
            var bold = LoadBundledFont(BoldFontGuid, BoldFontFileName, out var boldSource);
            var source = string.IsNullOrEmpty(regularSource) ? boldSource : regularSource;
            return new ResolvedFont("Bundled Sarasa Mono SC", regular, bold, source);
        }

        public static string BuildDiagnosticReport(string configuredFontName)
        {
            var lines = new List<string>();
            lines.Add("WTL Internal Terminal font diagnostics");
            lines.Add("Configured Font: " + (string.IsNullOrWhiteSpace(configuredFontName) ? "auto" : configuredFontName));
            lines.Add("PackageRoot: " + InternalTerminalPaths.PackageRoot);
            lines.Add("Regular GUID path: " + AssetDatabase.GUIDToAssetPath(RegularFontGuid));
            lines.Add("Bold GUID path: " + AssetDatabase.GUIDToAssetPath(BoldFontGuid));
            lines.Add("Regular package path exists: " + File.Exists(ToAbsoluteFilePath(PackageFontAssetPath(RegularFontFileName))));
            lines.Add("Bold package path exists: " + File.Exists(ToAbsoluteFilePath(PackageFontAssetPath(BoldFontFileName))));
            lines.Add("FindAssets Regular: " + string.Join(", ", FindFontAssetPaths(RegularFontFileName)));
            lines.Add("FindAssets Bold: " + string.Join(", ", FindFontAssetPaths(BoldFontFileName)));

            var resolved = Resolve(configuredFontName);
            lines.Add("Resolved DisplayName: " + resolved.DisplayName);
            lines.Add("Resolved Source: " + (string.IsNullOrEmpty(resolved.Source) ? "<none>" : resolved.Source));
            lines.Add("Regular loaded: " + (resolved.Regular != null ? resolved.Regular.name : "<null>"));
            lines.Add("Bold loaded: " + (resolved.Bold != null ? resolved.Bold.name : "<null>"));
            return string.Join("\n", lines);
        }

        private static Font LoadBundledFont(string guid, string fileName, out string source)
        {
            source = string.Empty;

            var guidPath = AssetDatabase.GUIDToAssetPath(guid);
            var font = LoadFontAsset(guidPath, out source);
            if (font != null)
            {
                return font;
            }

            foreach (var path in CandidateAssetPaths(fileName))
            {
                font = LoadFontAsset(path, out source);
                if (font != null)
                {
                    return font;
                }
            }

            foreach (var path in FindFontAssetPaths(fileName))
            {
                font = LoadFontAsset(path, out source);
                if (font != null)
                {
                    return font;
                }
            }

            source = "not found: " + fileName;
            return null;
        }

        private static Font LoadFontAsset(string assetPath, out string source)
        {
            source = string.Empty;
            if (string.IsNullOrEmpty(assetPath))
            {
                return null;
            }

            var font = AssetDatabase.LoadAssetAtPath<Font>(assetPath);
            if (font != null)
            {
                source = assetPath;
                return font;
            }

            if (File.Exists(ToAbsoluteFilePath(assetPath)))
            {
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
                font = AssetDatabase.LoadAssetAtPath<Font>(assetPath);
                if (font != null)
                {
                    source = assetPath + " (imported)";
                    return font;
                }
            }

            return null;
        }

        private static IEnumerable<string> CandidateAssetPaths(string fileName)
        {
            yield return PackageFontAssetPath(fileName);
        }

        private static string PackageFontAssetPath(string fileName)
        {
            return InternalTerminalPaths.PackageAssetRoot + "/Fonts/" + fileName;
        }

        private static IEnumerable<string> FindFontAssetPaths(string fileName)
        {
            return AssetDatabase.FindAssets(Path.GetFileNameWithoutExtension(fileName) + " t:Font")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => string.Equals(Path.GetFileName(path), fileName, StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => path.StartsWith(InternalTerminalPaths.PackageAssetRoot + "/", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(path => path, StringComparer.OrdinalIgnoreCase);
        }

        private static string ToAbsoluteFilePath(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
            {
                return string.Empty;
            }

            if (assetPath.StartsWith(InternalTerminalPaths.PackageAssetRoot + "/", StringComparison.OrdinalIgnoreCase))
            {
                var relative = assetPath.Substring((InternalTerminalPaths.PackageAssetRoot + "/").Length).Replace('/', Path.DirectorySeparatorChar);
                return Path.Combine(InternalTerminalPaths.PackageRoot, "Editor", "InternalTerminal", relative);
            }

            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            return string.IsNullOrEmpty(projectRoot)
                ? string.Empty
                : Path.GetFullPath(Path.Combine(projectRoot, assetPath.Replace('/', Path.DirectorySeparatorChar)));
        }

        private static ResolvedFont TryCreateOsFont(string fontName)
        {
            var regular = Font.CreateDynamicFontFromOSFont(fontName, 13);
            var bold = regular;
            return new ResolvedFont(fontName, regular, bold, "OS font: " + fontName);
        }

        private static string TryReadWindowsTerminalFont(HashSet<string> installed)
        {
            if (Application.platform != RuntimePlatform.WindowsEditor)
            {
                return string.Empty;
            }

            foreach (var path in WindowsTerminalSettingsPaths())
            {
                if (!File.Exists(path))
                {
                    continue;
                }

                try
                {
                    var json = File.ReadAllText(path);
                    var font = FirstInstalledFont(ParseFontFaces(json), installed);
                    if (!string.IsNullOrEmpty(font))
                    {
                        return font;
                    }
                }
                catch
                {
                    // Ignore malformed or inaccessible terminal settings.
                }
            }

            return string.Empty;
        }

        private static IEnumerable<string> WindowsTerminalSettingsPaths()
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrEmpty(localAppData))
            {
                yield break;
            }

            yield return Path.Combine(localAppData, "Packages", "Microsoft.WindowsTerminal_8wekyb3d8bbwe", "LocalState", "settings.json");
            yield return Path.Combine(localAppData, "Packages", "Microsoft.WindowsTerminalPreview_8wekyb3d8bbwe", "LocalState", "settings.json");
            yield return Path.Combine(localAppData, "Microsoft", "Windows Terminal", "settings.json");
        }

        private static IEnumerable<string> ParseFontFaces(string json)
        {
            foreach (Match match in Regex.Matches(json, "\"face\"\\s*:\\s*\"(?<font>(?:\\\\.|[^\"])*)\""))
            {
                var font = Regex.Unescape(match.Groups["font"].Value);
                if (!string.IsNullOrWhiteSpace(font))
                {
                    yield return font.Trim();
                }
            }
        }

        private static string FirstInstalledFont(IEnumerable<string> fonts, HashSet<string> installed)
        {
            foreach (var font in fonts)
            {
                if (installed.Contains(font))
                {
                    return font;
                }
            }

            return string.Empty;
        }
    }
}
