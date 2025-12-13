using System;
using System.Diagnostics;
using System.IO;
using MCPForUnity.Editor.Constants;
using MCPForUnity.Editor.Dependencies.Models;

namespace MCPForUnity.Editor.Dependencies.PlatformDetectors
{
    /// <summary>
    /// Base class for platform-specific dependency detection
    /// </summary>
    public abstract class PlatformDetectorBase : IPlatformDetector
    {
        public abstract string PlatformName { get; }
        public abstract bool CanDetect { get; }

        public abstract DependencyStatus DetectPython(string overridePath = null);
        public abstract string GetPythonInstallUrl();
        public abstract string GetUvInstallUrl();
        public abstract string GetInstallationRecommendations();

        public virtual DependencyStatus DetectUv(string overridePath = null)
        {
            var status = new DependencyStatus("uv Package Manager", isRequired: false)
            {
                InstallationHint = GetUvInstallUrl()
            };

            try
            {
                // 0. Check Override
                if (overridePath == null)
                {
                    try { overridePath = UnityEditor.EditorPrefs.GetString(EditorPrefKeys.UvPathOverride, ""); } catch { }
                }
                if (!string.IsNullOrEmpty(overridePath) && File.Exists(overridePath))
                {
                     // Validate version of the override executable
                     var verPsi = new ProcessStartInfo
                     {
                         FileName = overridePath,
                         Arguments = "--version",
                         UseShellExecute = false,
                         RedirectStandardOutput = true,
                         RedirectStandardError = true,
                         CreateNoWindow = true
                     };
                     
                     using var verProcess = Process.Start(verPsi);
                     if (verProcess != null)
                     {
                         string output = verProcess.StandardOutput.ReadToEnd().Trim();
                         verProcess.WaitForExit(3000);
                         if (verProcess.ExitCode == 0 && output.StartsWith("uv "))
                         {
                             status.IsAvailable = true;
                             status.Version = output.Substring(3).Trim();
                             status.Path = overridePath;
                             status.Details = $"Using custom uv path: {overridePath}";
                             return status;
                         }
                     }
                }

                // 1. Try to find uv/uvx in PATH
                if (TryFindUvInPath(out string uvPath, out string version))
                {
                    status.IsAvailable = true;
                    status.Version = version;
                    status.Path = uvPath;
                    status.Details = $"Found uv {version} in PATH";
                    return status;
                }

                // 2. Fallback: Try to find uv in Python Scripts (if installed via pip but not in PATH)
                if (TryFindUvViaPython(out uvPath, out version))
                {
                    status.IsAvailable = true;
                    status.Version = version;
                    status.Path = uvPath;
                    status.Details = $"Found uv {version} via Python";
                    return status;
                }

                status.ErrorMessage = "uv not found in PATH";
                status.Details = "Install uv package manager and ensure it's added to PATH.";
            }
            catch (Exception ex)
            {
                status.ErrorMessage = $"Error detecting uv: {ex.Message}";
            }

            return status;
        }

        protected virtual bool TryFindUvViaPython(out string uvPath, out string version)
        {
            uvPath = null;
            version = null;
            try
            {
                // Ask Python where the Scripts folder is and check for uv
                string script = "import sys, os; print(os.path.join(sys.prefix, 'Scripts' if os.name == 'nt' else 'bin', 'uv' + ('.exe' if os.name == 'nt' else '')))";
                
                var psi = new ProcessStartInfo
                {
                    FileName = "python", // Assume python is in PATH
                    Arguments = $"-c \"{script}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process != null)
                {
                    string path = process.StandardOutput.ReadToEnd().Trim();
                    process.WaitForExit(3000);

                    if (process.ExitCode == 0 && !string.IsNullOrEmpty(path) && File.Exists(path))
                    {
                        // Found the binary, now check version
                        var verPsi = new ProcessStartInfo
                        {
                            FileName = path,
                            Arguments = "--version",
                            UseShellExecute = false,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            CreateNoWindow = true
                        };
                        
                        using var verProcess = Process.Start(verPsi);
                        if (verProcess != null)
                        {
                            string output = verProcess.StandardOutput.ReadToEnd().Trim();
                            verProcess.WaitForExit(3000);
                            if (verProcess.ExitCode == 0 && output.StartsWith("uv "))
                            {
                                version = output.Substring(3).Trim();
                                uvPath = path;
                                return true;
                            }
                        }
                    }
                }
            }
            catch { }
            return false;
        }

        public virtual DependencyStatus DetectNode(string overridePath = null)
        {
            var status = new DependencyStatus("Node.js", isRequired: true)
            {
                InstallationHint = "https://nodejs.org/"
            };

            try
            {
                // 1. Check Override
                if (overridePath == null)
                {
                    try { overridePath = UnityEditor.EditorPrefs.GetString(EditorPrefKeys.NodePathOverride, ""); } catch { }
                }
                if (!string.IsNullOrEmpty(overridePath) && File.Exists(overridePath))
                {
                    if (TryValidateNode(overridePath, out string version))
                    {
                        status.IsAvailable = true;
                        status.Version = version;
                        status.Path = overridePath;
                        status.Details = $"Using custom Node.js path: {overridePath}";
                        return status;
                    }
                }

                // 2. Try to find node in PATH
                if (TryFindNodeInPath(out string nodePath, out string nodeVersion))
                {
                    status.IsAvailable = true;
                    status.Version = nodeVersion;
                    status.Path = nodePath;
                    status.Details = $"Found Node.js {nodeVersion} in PATH";
                    return status;
                }

                status.ErrorMessage = "Node.js not found in PATH";
                status.Details = "Install Node.js (LTS recommended) and ensure it's added to PATH.";
            }
            catch (Exception ex)
            {
                status.ErrorMessage = $"Error detecting Node.js: {ex.Message}";
            }

            return status;
        }

        protected bool TryValidateNode(string nodePath, out string version)
        {
            version = null;
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = nodePath,
                    Arguments = "--version",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process != null)
                {
                    string output = process.StandardOutput.ReadToEnd().Trim();
                    process.WaitForExit(5000);

                    if (process.ExitCode == 0 && output.StartsWith("v"))
                    {
                        version = output.Substring(1).Trim();
                        return true;
                    }
                }
            }
            catch { }
            return false;
        }

        protected bool TryFindNodeInPath(out string nodePath, out string version)
        {
            nodePath = null;
            version = null;

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "node",
                    Arguments = "--version",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process != null)
                {
                    string output = process.StandardOutput.ReadToEnd().Trim();
                    process.WaitForExit(5000);

                    if (process.ExitCode == 0 && output.StartsWith("v"))
                    {
                        version = output.Substring(1).Trim(); // Remove 'v' prefix
                        nodePath = "node";
                        return true;
                    }
                }
            }
            catch
            {
                // Ignore
            }

            return false;
        }

        protected bool TryFindUvInPath(out string uvPath, out string version)
        {
            uvPath = null;
            version = null;

            // Try common uv command names
            var commands = new[] { "uvx", "uv" };

            foreach (var cmd in commands)
            {
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = cmd,
                        Arguments = "--version",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    };

                    using var process = Process.Start(psi);
                    if (process == null) continue;

                    string output = process.StandardOutput.ReadToEnd().Trim();
                    process.WaitForExit(5000);

                    if (process.ExitCode == 0 && output.StartsWith("uv "))
                    {
                        version = output.Substring(3).Trim();
                        uvPath = cmd;
                        return true;
                    }
                }
                catch
                {
                    // Try next command
                }
            }

            return false;
        }

        protected bool TryParseVersion(string version, out int major, out int minor)
        {
            major = 0;
            minor = 0;

            try
            {
                var parts = version.Split('.');
                if (parts.Length >= 2)
                {
                    return int.TryParse(parts[0], out major) && int.TryParse(parts[1], out minor);
                }
            }
            catch
            {
                // Ignore parsing errors
            }

            return false;
        }
    }
}
