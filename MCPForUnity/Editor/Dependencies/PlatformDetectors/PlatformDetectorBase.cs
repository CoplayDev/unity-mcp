using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using MCPForUnity.Editor.Constants;
using MCPForUnity.Editor.Dependencies.Models;
using UnityEditor;

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
            var status = new DependencyStatus("uv Package Manager", isRequired: true)
            {
                InstallationHint = GetUvInstallUrl()
            };

            try
            {
                // 0. Check Override
                if (overridePath == null)
                {
                    overridePath = GetEditorPrefsSafely(EditorPrefKeys.UvPathOverride);
                }
                
                if (!string.IsNullOrEmpty(overridePath) && File.Exists(overridePath))
                {
                     // Validate version of the override executable
                     if (TryExecuteProcess(overridePath, "--version", 3000, out string output) && output.StartsWith("uv "))
                     {
                         status.IsAvailable = true;
                         status.Version = output.Substring(3).Trim();
                         status.Path = overridePath;
                         status.Details = $"Using custom uv path: {overridePath}";
                         return status;
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
                
                // Assume python is in PATH. 
                // Note: This might pick up a different python than what the user configured if they have strict overrides,
                // but this is a fallback mechanism.
                if (TryExecuteProcess("python", $"-c \"{script}\"", 3000, out string path))
                {
                    if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    {
                        // Found the binary, now check version
                        if (TryExecuteProcess(path, "--version", 3000, out string output) && output.StartsWith("uv "))
                        {
                            version = output.Substring(3).Trim();
                            uvPath = path;
                            return true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"TryFindUvViaPython failed: {ex.Message}");
            }
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
                    overridePath = GetEditorPrefsSafely(EditorPrefKeys.NodePathOverride);
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
            if (TryExecuteProcess(nodePath, "--version", 5000, out string output) && output.StartsWith("v"))
            {
                version = output.Substring(1).Trim();
                return true;
            }
            return false;
        }

        protected bool TryFindNodeInPath(out string nodePath, out string version)
        {
            nodePath = null;
            version = null;

            if (TryExecuteProcess("node", "--version", 5000, out string output) && output.StartsWith("v"))
            {
                version = output.Substring(1).Trim();
                nodePath = "node";
                return true;
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
                if (TryExecuteProcess(cmd, "--version", 5000, out string output) && output.StartsWith("uv "))
                {
                    version = output.Substring(3).Trim();
                    uvPath = cmd;
                    return true;
                }
            }
            return false;
        }

        // --- Helpers ---

        protected string GetEditorPrefsSafely(string key, string defaultValue = "")
        {
            try 
            { 
                return EditorPrefs.GetString(key, defaultValue);
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"Failed to read EditorPrefs key '{key}': {ex.Message}");
                return defaultValue;
            }
        }

        protected bool TryExecuteProcess(string fileName, string arguments, int timeoutMs, out string output)
        {
            output = string.Empty;
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
                var outputBuilder = new StringBuilder();
                
                process.OutputDataReceived += (sender, e) => 
                { 
                    if (!string.IsNullOrEmpty(e.Data)) 
                    {
                        outputBuilder.AppendLine(e.Data); 
                    }
                };

                if (!process.Start()) return false;

                process.BeginOutputReadLine();

                if (!process.WaitForExit(timeoutMs))
                {
                    try { process.Kill(); } catch { }
                    return false;
                }

                output = outputBuilder.ToString().Trim();
                return process.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }
        protected bool TryParseVersion(string versionString, out int major, out int minor)
        {
            major = 0;
            minor = 0;
            if (string.IsNullOrEmpty(versionString)) return false;

            try
            {
                var parts = versionString.Split('.');
                if (parts.Length >= 2)
                {
                    if (int.TryParse(parts[0], out major) && int.TryParse(parts[1], out minor))
                    {
                        return true;
                    }
                }
                else if (parts.Length == 1)
                {
                     if (int.TryParse(parts[0], out major))
                     {
                         minor = 0;
                         return true;
                     }
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
