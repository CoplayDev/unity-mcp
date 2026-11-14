using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace MCPForUnity.Editor.Dependencies
{
    /// <summary>Builds exact optional-package requests while preserving installed defaults.</summary>
    internal sealed class PackageVersionSelection
    {
        private static readonly Regex ExactVersion = new Regex(
            @"\A(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-(?:0|[1-9][0-9]*|[0-9]*[a-zA-Z-][0-9a-zA-Z-]*)(?:\.(?:0|[1-9][0-9]*|[0-9]*[a-zA-Z-][0-9a-zA-Z-]*))*)?(?:\+[0-9a-zA-Z-]+(?:\.[0-9a-zA-Z-]+)*)?\z",
            RegexOptions.CultureInvariant);

        internal string PackageId { get; }
        internal string InstalledVersion { get; }
        internal bool IsInstalled { get; }
        internal string Version { get; set; }
        internal string SessionKey => "MCPForUnity.OptionalPackageVersion." + PackageId;

        internal PackageVersionSelection(string packageId, string installedVersion, bool isInstalled)
        {
            PackageId = packageId;
            InstalledVersion = installedVersion;
            IsInstalled = isInstalled;
            Version = installedVersion ?? string.Empty;
        }

        internal bool TryGetRequest(out string request, out string error)
        {
            request = null;
            error = null;
            string version = Version?.Trim() ?? string.Empty;
            // No request means even Git/local packages keep their original source.
            if (IsInstalled && (InstalledVersion == null || version.Length == 0 || version == InstalledVersion)) return true;
            if (version.Length > 0 && !ExactVersion.IsMatch(version))
            {
                error = "Use an exact package version, such as 2.4.0 or 3.0.0-pre.1, or leave it empty.";
                return false;
            }
            request = version.Length == 0 ? PackageId : PackageId + "@" + version;
            return true;
        }

        internal static bool TryCollectRequests(IEnumerable<PackageVersionSelection> selections,
            out string[] requests, out string error)
        {
            var result = new List<string>();
            foreach (var selection in selections)
            {
                if (!selection.TryGetRequest(out string request, out error))
                {
                    requests = Array.Empty<string>();
                    error = selection.PackageId + ": " + error;
                    return false;
                }
                if (request != null) result.Add(request);
            }
            requests = result.ToArray();
            error = null;
            return true;
        }
    }
}
