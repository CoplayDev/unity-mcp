using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MCPForUnity.Editor.Dependencies.Models;
using MCPForUnity.Editor.Helpers;
using UnityEngine;

namespace MCPForUnity.Editor.Installation
{
    /// <summary>
    /// Orchestrates the installation of missing dependencies
    /// </summary>
    public class InstallationOrchestrator
    {
        public event Action<string> OnProgressUpdate;
        public event Action<bool, string> OnInstallationComplete;

        private bool _isInstalling = false;

        /// <summary>
        /// Start installation of missing dependencies
        /// </summary>
        public async void StartInstallation(List<DependencyStatus> missingDependencies)
        {
            if (_isInstalling)
            {
                McpLog.Warn("Installation already in progress");
                return;
            }

            _isInstalling = true;

            try
            {
                OnProgressUpdate?.Invoke("Starting installation process...");
                
                bool allSuccessful = true;
                string finalMessage = "";

                foreach (var dependency in missingDependencies)
                {
                    OnProgressUpdate?.Invoke($"Installing {dependency.Name}...");
                    
                    bool success = await InstallDependency(dependency);
                    if (!success)
                    {
                        allSuccessful = false;
                        finalMessage += $"Failed to install {dependency.Name}. ";
                    }
                    else
                    {
                        finalMessage += $"Successfully installed {dependency.Name}. ";
                    }
                }

                if (allSuccessful)
                {
                    OnProgressUpdate?.Invoke("Installation completed successfully!");
                    OnInstallationComplete?.Invoke(true, "All dependencies installed successfully.");
                }
                else
                {
                    OnProgressUpdate?.Invoke("Installation completed with errors.");
                    OnInstallationComplete?.Invoke(false, finalMessage);
                }
            }
            catch (Exception ex)
            {
                McpLog.Error($"Installation failed: {ex.Message}");
                OnInstallationComplete?.Invoke(false, $"Installation failed: {ex.Message}");
            }
            finally
            {
                _isInstalling = false;
            }
        }

        /// <summary>
        /// Install a specific dependency
        /// </summary>
        private async Task<bool> InstallDependency(DependencyStatus dependency)
        {
            try
            {
                switch (dependency.Name)
                {
                    case "Python":
                        return await InstallPython();
                    
                    case "UV Package Manager":
                        return await InstallUV();
                    
                    case "MCP Server":
                        return await InstallMCPServer();
                    
                    default:
                        McpLog.Warn($"Unknown dependency: {dependency.Name}");
                        return false;
                }
            }
            catch (Exception ex)
            {
                McpLog.Error($"Error installing {dependency.Name}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Attempt to install Python (limited automatic options)
        /// </summary>
        private async Task<bool> InstallPython()
        {
            OnProgressUpdate?.Invoke("Python installation requires manual intervention...");
            
            // For Asset Store compliance, we cannot automatically install Python
            // We can only guide the user to install it manually
            await Task.Delay(1000); // Simulate some work
            
            OnProgressUpdate?.Invoke("Python must be installed manually. Please visit the installation URL provided.");
            return false; // Always return false since we can't auto-install
        }

        /// <summary>
        /// Attempt to install UV package manager
        /// </summary>
        private async Task<bool> InstallUV()
        {
            OnProgressUpdate?.Invoke("UV installation requires manual intervention...");
            
            // For Asset Store compliance, we cannot automatically install UV
            // We can only guide the user to install it manually
            await Task.Delay(1000); // Simulate some work
            
            OnProgressUpdate?.Invoke("UV must be installed manually. Please visit the installation URL provided.");
            return false; // Always return false since we can't auto-install
        }

        /// <summary>
        /// Install MCP Server (this we can do automatically)
        /// </summary>
        private async Task<bool> InstallMCPServer()
        {
            try
            {
                OnProgressUpdate?.Invoke("Installing MCP Server...");
                
                // Run server installation on a background thread
                bool success = await Task.Run(() =>
                {
                    try
                    {
                        ServerInstaller.EnsureServerInstalled();
                        return true;
                    }
                    catch (Exception ex)
                    {
                        McpLog.Error($"Server installation failed: {ex.Message}");
                        return false;
                    }
                });

                if (success)
                {
                    OnProgressUpdate?.Invoke("MCP Server installed successfully.");
                    return true;
                }
                else
                {
                    OnProgressUpdate?.Invoke("MCP Server installation failed.");
                    return false;
                }
            }
            catch (Exception ex)
            {
                McpLog.Error($"Error during MCP Server installation: {ex.Message}");
                OnProgressUpdate?.Invoke($"MCP Server installation error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Check if installation is currently in progress
        /// </summary>
        public bool IsInstalling => _isInstalling;

        /// <summary>
        /// Cancel ongoing installation (if possible)
        /// </summary>
        public void CancelInstallation()
        {
            if (_isInstalling)
            {
                OnProgressUpdate?.Invoke("Cancelling installation...");
                _isInstalling = false;
                OnInstallationComplete?.Invoke(false, "Installation cancelled by user.");
            }
        }
    }
}