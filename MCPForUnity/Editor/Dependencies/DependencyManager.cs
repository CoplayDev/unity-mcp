using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using MCPForUnity.Editor.Dependencies.Models;
using MCPForUnity.Editor.Dependencies.PlatformDetectors;
using MCPForUnity.Editor.Helpers;
using UnityEditor;
using UnityEngine;

namespace MCPForUnity.Editor.Dependencies
{
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
        /// Perform a comprehensive dependency check
        /// </summary>
        public static DependencyCheckResult CheckAllDependencies()
        {
            var result = new DependencyCheckResult();

            try
            {
                var detector = GetCurrentPlatformDetector();
                McpLog.Info($"Checking dependencies on {detector.PlatformName}...", always: false);

                // Check Python
                var pythonStatus = detector.DetectPython();
                result.Dependencies.Add(pythonStatus);

                // Check uv
                var uvStatus = detector.DetectUv();
                result.Dependencies.Add(uvStatus);

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
                    result.RecommendedActions.Add($"Install Python 3.10+ from: {detector.GetPythonInstallUrl()}");
                }
                else if (dep.Name == "uv Package Manager")
                {
                    result.RecommendedActions.Add($"Install uv package manager from: {detector.GetUvInstallUrl()}");
                }
                else if (dep.Name == "MCP Server")
                {
                    result.RecommendedActions.Add("MCP Server will be installed automatically when needed.");
                }
            }

            if (result.GetMissingRequired().Count > 0)
            {
                result.RecommendedActions.Add("Use the Setup Window (Window > MCP for Unity > Local Setup Window) for guided installation.");
            }
        }
    }
}
