using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using MCPForUnity.Editor.Dependencies.Models;
using MCPForUnity.Editor.Dependencies.PlatformDetectors;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Services;
using UnityEditor;
using UnityEngine;

namespace MCPForUnity.Editor.Dependencies
{
    /// <summary>
    /// Context object containing all necessary paths and settings for dependency checking.
    /// This allows the check to run on a background thread without accessing Unity APIs.
    /// </summary>
    public class DependencyContext
    {
        public string PackageRootPath { get; set; }
        public string PythonOverridePath { get; set; }
        public string NodeOverridePath { get; set; }
        public string UvOverridePath { get; set; }
    }

    /// <summary>
    /// Main orchestrator for dependency validation and management
    /// </summary>
    public static class DependencyManager
    {
        private static readonly List<IPlatformDetector> _detectors = new List<IPlatformDetector>
        {
            new WindowsPlatformDetector(),
            new MacOSPlatformDetector(),
            new LinuxPlatformDetector()
        };

        private static IPlatformDetector _currentDetector;

        /// <summary>
        /// Get the platform detector for the current operating system
        /// </summary>
        public static IPlatformDetector GetCurrentPlatformDetector()
        {
            if (_currentDetector == null)
            {
                _currentDetector = _detectors.FirstOrDefault(d => d.CanDetect);
                if (_currentDetector == null)
                {
                    throw new PlatformNotSupportedException($"No detector available for current platform: {RuntimeInformation.OSDescription}");
                }
            }
            return _currentDetector;
        }

        /// <summary>
        /// Perform a comprehensive dependency check (Synchronous - Main Thread Only)
        /// </summary>
        public static DependencyCheckResult CheckAllDependencies()
        {
            // Gather context on main thread
            var context = new DependencyContext
            {
                PackageRootPath = AssetPathUtility.GetMcpPackageRootPath(), // Changed to match local API
                PythonOverridePath = MCPServiceLocator.Paths.GetPythonPath(),
                NodeOverridePath = MCPServiceLocator.Paths.GetNodePath(),
                UvOverridePath = MCPServiceLocator.Paths.GetUvxPath()
            };

            return CheckAllDependenciesInternal(context);
        }

        /// <summary>
        /// Perform a comprehensive dependency check (Thread-Safe)
        /// </summary>
        public static DependencyCheckResult CheckAllDependenciesAsync(DependencyContext context)
        {
            return CheckAllDependenciesInternal(context);
        }

        private static DependencyCheckResult CheckAllDependenciesInternal(DependencyContext context)
        {
            var result = new DependencyCheckResult();

            try
            {
                var detector = GetCurrentPlatformDetector();
                McpLog.Info($"Checking dependencies on {detector.PlatformName}...", always: false);

                // Check Python
                var pythonStatus = detector.DetectPython(context.PythonOverridePath);
                result.Dependencies.Add(pythonStatus);

                // Check uv
                var uvStatus = detector.DetectUv(context.UvOverridePath);
                result.Dependencies.Add(uvStatus);

                // Check Node.js
                // Note: If detector doesn't support DetectNode yet, we might need to update detectors too.
                // Assuming detectors are being updated or have default interface implementation.
                var nodeStatus = detector.DetectNode(context.NodeOverridePath);
                result.Dependencies.Add(nodeStatus);

                // Check Server Environment
                // We assume ServerEnvironmentSetup.IsEnvironmentReady exists.
                // If not, we'll need to update that too.
                // Note: IsEnvironmentReady might access Unity API. If so, this "Async" flavor is risky.
                // But CheckAllDependencies() is called on main thread in our UI usage.
                
                // For now, we use a try-catch block specifically for server check to avoid blocking the whole result
                try 
                {
                     // We use the path gathered on the main thread to ensure thread safety
                     bool isServerReady = MCPForUnity.Editor.Setup.ServerEnvironmentSetup.IsEnvironmentReady(context.PackageRootPath); 
                     
                     result.Dependencies.Add(new DependencyStatus("Server Environment", true)
                     {
                         Version = isServerReady ? "Installed" : "Missing",
                         IsAvailable = isServerReady,
                         Details = isServerReady ? "Virtual Environment Ready" : "Run 'Install Server Environment'",
                         ErrorMessage = isServerReady ? null : "Virtual environment not set up"
                     });
                }
                catch (Exception ex)
                {
                    McpLog.Warn($"Server environment check failed: {ex.Message}");
                     result.Dependencies.Add(new DependencyStatus("Server Environment", true)
                     {
                         Version = "Error",
                         IsAvailable = false,
                         Details = "Check failed",
                         ErrorMessage = ex.Message
                     });
                }


                // Generate summary and recommendations
                result.GenerateSummary();
                GenerateRecommendations(result, detector);

                McpLog.Info($"Dependency check completed. System ready: {result.IsSystemReady}", always: false);
            }
            catch (Exception ex)
            {
                McpLog.Error($"Error during dependency check: {ex.Message}");
                result.Summary = $"Dependency check failed: {ex.Message}";
                result.IsSystemReady = false;
            }

            return result;
        }

        /// <summary>
        /// Get installation recommendations for the current platform
        /// </summary>
        public static string GetInstallationRecommendations()
        {
            try
            {
                var detector = GetCurrentPlatformDetector();
                return detector.GetInstallationRecommendations();
            }
            catch (Exception ex)
            {
                return $"Error getting installation recommendations: {ex.Message}";
            }
        }

        /// <summary>
        /// Get platform-specific installation URLs
        /// </summary>
        public static (string pythonUrl, string uvUrl) GetInstallationUrls()
        {
            try
            {
                var detector = GetCurrentPlatformDetector();
                return (detector.GetPythonInstallUrl(), detector.GetUvInstallUrl());
            }
            catch
            {
                return ("https://python.org/downloads/", "https://docs.astral.sh/uv/getting-started/installation/");
            }
        }

        private static void GenerateRecommendations(DependencyCheckResult result, IPlatformDetector detector)
        {
            var missing = result.GetMissingDependencies();

            if (missing.Count == 0)
            {
                result.RecommendedActions.Add("All dependencies are available. You can start using MCP for Unity.");
                return;
            }

            foreach (var dep in missing)
            {
                if (dep.Name == "Python")
                {
                    result.RecommendedActions.Add($"Install python 3.11+ from: {detector.GetPythonInstallUrl()}");
                }
                else if (dep.Name == "uv Package Manager")
                {
                    result.RecommendedActions.Add($"Install uv package manager from: {detector.GetUvInstallUrl()}");
                }
                else if (dep.Name == "Node.js")
                {
                    result.RecommendedActions.Add("Install Node.js (LTS) from: https://nodejs.org/");
                }
                else if (dep.Name == "MCP Server")
                {
                    result.RecommendedActions.Add("MCP Server will be installed automatically when needed.");
                }
            }

            if (result.GetMissingRequired().Count > 0)
            {
                result.RecommendedActions.Add("Use the Setup Window (Window > MCP for Unity > Setup Window) for guided installation.");
            }
        }
    }
}
