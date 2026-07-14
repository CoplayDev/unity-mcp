using System;
using System.Runtime.InteropServices;
using MCPForUnity.Editor.Helpers;

namespace MCPForUnity.Editor.Dependencies
{
    /// <summary>
    /// Runs the official uv installer for the current platform. UI-agnostic: build the command,
    /// run it (off the main thread), and report the outcome. The caller owns confirmation and
    /// re-checking dependencies afterwards.
    /// Installer reference: https://docs.astral.sh/uv/getting-started/installation/
    /// </summary>
    public static class UvInstaller
    {
        public readonly struct UvInstallResult
        {
            public readonly bool Success;
            public readonly string Output;

            public UvInstallResult(bool success, string output)
            {
                Success = success;
                Output = output ?? string.Empty;
            }
        }

        /// <summary>One-click install is available on Windows, macOS and Linux.</summary>
        public static bool IsSupported =>
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ||
            RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ||
            RuntimeInformation.IsOSPlatform(OSPlatform.Linux);

        /// <summary>
        /// Build the installer invocation for the current platform. Pure and testable — the
        /// returned command is exactly what <see cref="Run"/> executes and what the confirm
        /// dialog shows the user.
        /// </summary>
        public static (string file, string arguments) BuildInstallCommand()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return ("powershell",
                    "-NoProfile -ExecutionPolicy ByPass -c \"irm https://astral.sh/uv/install.ps1 | iex\"");
            }

            // macOS and Linux share the POSIX shell installer.
            return ("/bin/sh", "-c \"curl -LsSf https://astral.sh/uv/install.sh | sh\"");
        }

        /// <summary>Human-readable one-line description of the command that will run.</summary>
        public static string DescribeCommand()
        {
            var (file, arguments) = BuildInstallCommand();
            return $"{file} {arguments}";
        }

        /// <summary>
        /// Run the installer and return the outcome. Blocks until the installer exits or the
        /// timeout elapses, so call it off the main thread (e.g. via Task.Run).
        /// </summary>
        public static UvInstallResult Run(int timeoutMs = 180000)
        {
            try
            {
                var (file, arguments) = BuildInstallCommand();
                bool ok = ExecPath.TryRun(file, arguments, null, out string stdout, out string stderr, timeoutMs);
                return new UvInstallResult(ok, Combine(stdout, stderr));
            }
            catch (Exception ex)
            {
                return new UvInstallResult(false, ex.Message);
            }
        }

        private static string Combine(string stdout, string stderr)
        {
            string outText = (stdout ?? string.Empty).Trim();
            string errText = (stderr ?? string.Empty).Trim();
            string combined =
                string.IsNullOrEmpty(errText) ? outText :
                string.IsNullOrEmpty(outText) ? errText :
                outText + "\n" + errText;

            // Keep dialogs readable — echo only the tail of long installer output.
            const int max = 1500;
            if (combined.Length > max)
            {
                combined = "…" + combined.Substring(combined.Length - max);
            }
            return combined;
        }
    }
}
