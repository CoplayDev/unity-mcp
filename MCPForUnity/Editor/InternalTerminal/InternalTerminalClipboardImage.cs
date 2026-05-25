using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace WTL.InternalTerminal.Editor
{
    internal static class InternalTerminalClipboardImage
    {
        public static bool TrySavePng(out string imagePath)
        {
            imagePath = string.Empty;

            if (Application.platform != RuntimePlatform.WindowsEditor)
            {
                return false;
            }

            var directory = Path.Combine(GetProjectTempDirectory(), "WTLInternalTerminal", "ClipboardImages");
            Directory.CreateDirectory(directory);
            var candidate = Path.Combine(directory, "clipboard-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".png");

            if (!TryRunWindowsClipboardExport(candidate, out var savedPath))
            {
                return false;
            }

            imagePath = savedPath;
            return true;
        }

        public static string FormatPasteText(string imagePath)
        {
            return imagePath + Environment.NewLine;
        }

        private static string GetProjectTempDirectory()
        {
            var assetsDirectory = new DirectoryInfo(Application.dataPath);
            var projectDirectory = assetsDirectory.Parent != null
                ? assetsDirectory.Parent.FullName
                : Directory.GetCurrentDirectory();
            return Path.Combine(projectDirectory, "Temp");
        }

        private static bool TryRunWindowsClipboardExport(string imagePath, out string savedPath)
        {
            savedPath = string.Empty;

            var script = @"
$ProgressPreference = 'SilentlyContinue'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
$path = [System.Environment]::GetEnvironmentVariable('WTL_INTERNAL_TERMINAL_CLIPBOARD_IMAGE_PATH', 'Process')
if ([string]::IsNullOrWhiteSpace($path)) { exit 2 }
if (-not [System.Windows.Forms.Clipboard]::ContainsImage()) { exit 3 }
$image = [System.Windows.Forms.Clipboard]::GetImage()
if ($null -eq $image) { exit 4 }
$directory = [System.IO.Path]::GetDirectoryName($path)
[System.IO.Directory]::CreateDirectory($directory) | Out-Null
$image.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
$image.Dispose()
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
[Console]::Write($path)
";
            var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));

            foreach (var executable in new[] { "pwsh.exe", "powershell.exe" })
            {
                if (TryRunPowerShellClipboardExport(executable, encoded, imagePath, out savedPath))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryRunPowerShellClipboardExport(string executable, string encodedCommand, string imagePath, out string savedPath)
        {
            savedPath = string.Empty;

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = executable,
                    Arguments = "-NoLogo -NoProfile -Sta -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + encodedCommand,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };
                startInfo.EnvironmentVariables["WTL_INTERNAL_TERMINAL_CLIPBOARD_IMAGE_PATH"] = imagePath;

                using (var process = Process.Start(startInfo))
                {
                    if (process == null)
                    {
                        return false;
                    }

                    process.WaitForExit(2000);

                    if (!process.HasExited)
                    {
                        try
                        {
                            process.Kill();
                        }
                        catch
                        {
                            // Best effort cleanup.
                        }

                        return false;
                    }

                    var output = process.StandardOutput.ReadToEnd();
                    var error = process.StandardError.ReadToEnd();

                    if (process.ExitCode != 0)
                    {
                        if (!string.IsNullOrWhiteSpace(error) && process.ExitCode != 3)
                        {
                            Debug.LogWarning("WTL Internal Terminal clipboard image export failed: " + error.Trim());
                        }

                        return false;
                    }

                    var trimmed = output.Trim();
                    if (!File.Exists(trimmed))
                    {
                        return false;
                    }

                    savedPath = trimmed;
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }
    }
}
