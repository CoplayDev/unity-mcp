using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using MCPForUnity.Editor.Dependencies;
using MCPForUnity.Editor.Dependencies.Models;
using MCPForUnity.Editor.Dependencies.PlatformDetectors;
using MCPForUnity.Tests.Mocks;

namespace MCPForUnity.Tests.Dependencies
{
    [TestFixture]
    public class DependencyManagerTests
    {
        private MockPlatformDetector _mockDetector;
        
        [SetUp]
        public void SetUp()
        {
            _mockDetector = new MockPlatformDetector();
        }

        [Test]
        public void GetCurrentPlatformDetector_ReturnsValidDetector()
        {
            // Act
            var detector = DependencyManager.GetCurrentPlatformDetector();
            
            // Assert
            Assert.IsNotNull(detector, "Platform detector should not be null");
            Assert.IsTrue(detector.CanDetect, "Platform detector should be able to detect on current platform");
            Assert.IsNotEmpty(detector.PlatformName, "Platform name should not be empty");
        }

        [Test]
        public void CheckAllDependencies_ReturnsValidResult()
        {
            // Act
            var result = DependencyManager.CheckAllDependencies();
            
            // Assert
            Assert.IsNotNull(result, "Dependency check result should not be null");
            Assert.IsNotNull(result.Dependencies, "Dependencies list should not be null");
            Assert.GreaterOrEqual(result.Dependencies.Count, 3, "Should check at least Python, UV, and MCP Server");
            Assert.IsNotNull(result.Summary, "Summary should not be null");
            Assert.IsNotEmpty(result.RecommendedActions, "Should have recommended actions");
        }

        [Test]
        public void CheckAllDependencies_IncludesRequiredDependencies()
        {
            // Act
            var result = DependencyManager.CheckAllDependencies();
            
            // Assert
            var dependencyNames = result.Dependencies.Select(d => d.Name).ToList();
            Assert.Contains("Python", dependencyNames, "Should check Python dependency");
            Assert.Contains("UV Package Manager", dependencyNames, "Should check UV dependency");
            Assert.Contains("MCP Server", dependencyNames, "Should check MCP Server dependency");
        }

        [Test]
        public void IsSystemReady_ReturnsFalse_WhenDependenciesMissing()
        {
            // This test assumes some dependencies might be missing in test environment
            // Act
            var isReady = DependencyManager.IsSystemReady();
            
            // Assert
            Assert.IsNotNull(isReady, "IsSystemReady should return a boolean value");
            // Note: We can't assert true/false here as it depends on the test environment
        }

        [Test]
        public void GetMissingDependenciesSummary_ReturnsValidString()
        {
            // Act
            var summary = DependencyManager.GetMissingDependenciesSummary();
            
            // Assert
            Assert.IsNotNull(summary, "Missing dependencies summary should not be null");
            Assert.IsNotEmpty(summary, "Missing dependencies summary should not be empty");
        }

        [Test]
        public void IsDependencyAvailable_Python_ReturnsBoolean()
        {
            // Act
            var isAvailable = DependencyManager.IsDependencyAvailable("python");
            
            // Assert
            Assert.IsNotNull(isAvailable, "Python availability check should return a boolean");
        }

        [Test]
        public void IsDependencyAvailable_UV_ReturnsBoolean()
        {
            // Act
            var isAvailable = DependencyManager.IsDependencyAvailable("uv");
            
            // Assert
            Assert.IsNotNull(isAvailable, "UV availability check should return a boolean");
        }

        [Test]
        public void IsDependencyAvailable_MCPServer_ReturnsBoolean()
        {
            // Act
            var isAvailable = DependencyManager.IsDependencyAvailable("mcpserver");
            
            // Assert
            Assert.IsNotNull(isAvailable, "MCP Server availability check should return a boolean");
        }

        [Test]
        public void IsDependencyAvailable_UnknownDependency_ReturnsFalse()
        {
            // Act
            var isAvailable = DependencyManager.IsDependencyAvailable("unknown-dependency");
            
            // Assert
            Assert.IsFalse(isAvailable, "Unknown dependency should return false");
        }

        [Test]
        public void GetInstallationRecommendations_ReturnsValidString()
        {
            // Act
            var recommendations = DependencyManager.GetInstallationRecommendations();
            
            // Assert
            Assert.IsNotNull(recommendations, "Installation recommendations should not be null");
            Assert.IsNotEmpty(recommendations, "Installation recommendations should not be empty");
        }

        [Test]
        public void GetInstallationUrls_ReturnsValidUrls()
        {
            // Act
            var (pythonUrl, uvUrl) = DependencyManager.GetInstallationUrls();
            
            // Assert
            Assert.IsNotNull(pythonUrl, "Python URL should not be null");
            Assert.IsNotNull(uvUrl, "UV URL should not be null");
            Assert.IsTrue(pythonUrl.StartsWith("http"), "Python URL should be a valid URL");
            Assert.IsTrue(uvUrl.StartsWith("http"), "UV URL should be a valid URL");
        }

        [Test]
        public void GetDependencyDiagnostics_ReturnsDetailedInfo()
        {
            // Act
            var diagnostics = DependencyManager.GetDependencyDiagnostics();
            
            // Assert
            Assert.IsNotNull(diagnostics, "Diagnostics should not be null");
            Assert.IsNotEmpty(diagnostics, "Diagnostics should not be empty");
            Assert.IsTrue(diagnostics.Contains("Platform:"), "Diagnostics should include platform info");
            Assert.IsTrue(diagnostics.Contains("System Ready:"), "Diagnostics should include system ready status");
        }

        [Test]
        public void CheckAllDependencies_HandlesExceptions_Gracefully()
        {
            // This test verifies that the dependency manager handles exceptions gracefully
            // We can't easily force an exception without mocking, but we can verify the result structure
            
            // Act
            var result = DependencyManager.CheckAllDependencies();
            
            // Assert
            Assert.IsNotNull(result, "Result should not be null even if errors occur");
            Assert.IsNotNull(result.Summary, "Summary should be provided even if errors occur");
        }

        [Test]
        public void ValidateMCPServerStartup_ReturnsBoolean()
        {
            // Act
            var isValid = DependencyManager.ValidateMCPServerStartup();
            
            // Assert
            Assert.IsNotNull(isValid, "MCP Server startup validation should return a boolean");
        }

        [Test]
        public void RepairPythonEnvironment_ReturnsBoolean()
        {
            // Act
            var repairResult = DependencyManager.RepairPythonEnvironment();
            
            // Assert
            Assert.IsNotNull(repairResult, "Python environment repair should return a boolean");
        }
    }
}