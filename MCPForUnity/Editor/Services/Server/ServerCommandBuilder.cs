using System;
using System.IO;
using System.Linq;
using MCPForUnity.Editor.Constants;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Services;
using UnityEditor;
using UnityEngine;

namespace MCPForUnity.Editor.Services.Server
{
    /// <summary>
    /// Builds uvx/server command strings for starting the MCP HTTP server.
    /// Handles platform-specific command construction.
    /// </summary>
    public class ServerCommandBuilder : IServerCommandBuilder
    {
        /// <inheritdoc/>
        public bool TryBuildCommand(out string fileName, out string arguments, out string displayCommand, out string error)
        {
            fileName = null;
            arguments = null;
            displayCommand = null;
            error = null;

            bool useHttpTransport = EditorConfigurationCache.Instance.UseHttpTransport;
            if (!useHttpTransport)
            {
                error = "HTTP transport is disabled. Enable it in the MCP For Unity window first.";
                return false;
            }

            string httpUrl = HttpEndpointUtility.GetLocalBaseUrl();
            if (!IsLocalUrl(httpUrl))
            {
                error = $"The configured URL ({httpUrl}) is not a local address. Local server launch only works for localhost.";
                return false;
            }

            var (uvxPath, fromUrl, packageName) = AssetPathUtility.GetUvxCommandParts();
            if (string.IsNullOrEmpty(uvxPath))
            {
                error = "uv is not installed or found in PATH. Install it or set an override in Advanced Settings.";
                return false;
            }

            // Use central helper that checks both DevModeForceServerRefresh AND local path detection.
            // Note: --reinstall is not supported by uvx, use --no-cache --refresh instead
            string devFlags = AssetPathUtility.ShouldForceUvxRefresh() ? "--no-cache --refresh " : string.Empty;
            bool projectScopedTools = EditorPrefs.GetBool(
                EditorPrefKeys.ProjectScopedToolsLocalHttp,
                true
            );
            string scopedFlag = projectScopedTools ? " --project-scoped-tools" : string.Empty;

            // Use centralized helper for beta server / prerelease args
            string fromArgs = AssetPathUtility.GetBetaServerFromArgs(quoteFromPath: true);

            string args = string.IsNullOrEmpty(fromArgs)
                ? $"{devFlags}{packageName} --transport http --http-url {httpUrl}{scopedFlag}"
                : $"{devFlags}{fromArgs} {packageName} --transport http --http-url {httpUrl}{scopedFlag}";

            fileName = uvxPath;
            arguments = args;
            displayCommand = $"{QuoteIfNeeded(uvxPath)} {args}";
            return true;
        }

        /// <inheritdoc/>
        public string BuildUvPathFromUvx(string uvxPath)
        {
            if (string.IsNullOrWhiteSpace(uvxPath))
            {
                return uvxPath;
            }

            string directory = Path.GetDirectoryName(uvxPath);
            string extension = Path.GetExtension(uvxPath);
            string uvFileName = "uv" + extension;

            return string.IsNullOrEmpty(directory)
                ? uvFileName
                : Path.Combine(directory, uvFileName);
        }

        /// <inheritdoc/>
        public string GetPlatformSpecificPathPrepend()
        {
            if (Application.platform == RuntimePlatform.OSXEditor)
            {
                return string.Join(Path.PathSeparator.ToString(), new[]
                {
                    "/opt/homebrew/bin",
                    "/usr/local/bin",
                    "/usr/bin",
                    "/bin"
                });
            }

            if (Application.platform == RuntimePlatform.LinuxEditor)
            {
                return string.Join(Path.PathSeparator.ToString(), new[]
                {
                    "/usr/local/bin",
                    "/usr/bin",
                    "/bin"
                });
            }

            if (Application.platform == RuntimePlatform.WindowsEditor)
            {
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

                return string.Join(Path.PathSeparator.ToString(), new[]
                {
                    !string.IsNullOrEmpty(localAppData) ? Path.Combine(localAppData, "Programs", "uv") : null,
                    !string.IsNullOrEmpty(programFiles) ? Path.Combine(programFiles, "uv") : null
                }.Where(p => !string.IsNullOrEmpty(p)).ToArray());
            }

            return null;
        }

        /// <inheritdoc/>
        public string QuoteIfNeeded(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;
            return input.IndexOf(' ') >= 0 ? $"\"{input}\"" : input;
        }

        /// <summary>
        /// Check if a URL is local (localhost, 127.0.0.1, 0.0.0.0, ::1)
        /// </summary>
        private static bool IsLocalUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return false;

            try
            {
                var uri = new Uri(url);
                string host = uri.Host.ToLower();
                return host == "localhost" || host == "127.0.0.1" || host == "0.0.0.0" || host == "::1";
            }
            catch
            {
                return false;
            }
        }
    }
}
