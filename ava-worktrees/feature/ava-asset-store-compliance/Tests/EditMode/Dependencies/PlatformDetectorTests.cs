using System;
using NUnit.Framework;
using MCPForUnity.Editor.Dependencies.PlatformDetectors;
using MCPForUnity.Tests.Mocks;

namespace MCPForUnity.Tests.Dependencies
{
    [TestFixture]
    public class PlatformDetectorTests
    {
        [Test]
        public void WindowsPlatformDetector_CanDetect_OnWindows()
        {
            // Arrange
            var detector = new WindowsPlatformDetector();
            
            // Act & Assert
            if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
            {
                Assert.IsTrue(detector.CanDetect, "Windows detector should detect on Windows platform");
                Assert.AreEqual("Windows", detector.PlatformName, "Platform name should be Windows");
            }
            else
            {
                Assert.IsFalse(detector.CanDetect, "Windows detector should not detect on non-Windows platform");
            }
        }

        [Test]
        public void MacOSPlatformDetector_CanDetect_OnMacOS()
        {
            // Arrange
            var detector = new MacOSPlatformDetector();
            
            // Act & Assert
            if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX))
            {
                Assert.IsTrue(detector.CanDetect, "macOS detector should detect on macOS platform");
                Assert.AreEqual("macOS", detector.PlatformName, "Platform name should be macOS");
            }
            else
            {
                Assert.IsFalse(detector.CanDetect, "macOS detector should not detect on non-macOS platform");
            }
        }

        [Test]
        public void LinuxPlatformDetector_CanDetect_OnLinux()
        {
            // Arrange
            var detector = new LinuxPlatformDetector();
            
            // Act & Assert
            if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Linux))
            {
                Assert.IsTrue(detector.CanDetect, "Linux detector should detect on Linux platform");
                Assert.AreEqual("Linux", detector.PlatformName, "Platform name should be Linux");
            }
            else
            {
                Assert.IsFalse(detector.CanDetect, "Linux detector should not detect on non-Linux platform");
            }
        }

        [Test]
        public void PlatformDetector_DetectPython_ReturnsValidStatus()
        {
            // Arrange
            var detector = GetCurrentPlatformDetector();
            
            // Act
            var pythonStatus = detector.DetectPython();
            
            // Assert
            Assert.IsNotNull(pythonStatus, "Python status should not be null");
            Assert.AreEqual("Python", pythonStatus.Name, "Dependency name should be Python");
            Assert.IsTrue(pythonStatus.IsRequired, "Python should be marked as required");
        }

        [Test]
        public void PlatformDetector_DetectUV_ReturnsValidStatus()
        {
            // Arrange
            var detector = GetCurrentPlatformDetector();
            
            // Act
            var uvStatus = detector.DetectUV();
            
            // Assert
            Assert.IsNotNull(uvStatus, "UV status should not be null");
            Assert.AreEqual("UV Package Manager", uvStatus.Name, "Dependency name should be UV Package Manager");
            Assert.IsTrue(uvStatus.IsRequired, "UV should be marked as required");
        }

        [Test]
        public void PlatformDetector_DetectMCPServer_ReturnsValidStatus()
        {
            // Arrange
            var detector = GetCurrentPlatformDetector();
            
            // Act
            var serverStatus = detector.DetectMCPServer();
            
            // Assert
            Assert.IsNotNull(serverStatus, "MCP Server status should not be null");
            Assert.AreEqual("MCP Server", serverStatus.Name, "Dependency name should be MCP Server");
            Assert.IsFalse(serverStatus.IsRequired, "MCP Server should not be marked as required (auto-installable)");
        }

        [Test]
        public void PlatformDetector_GetInstallationRecommendations_ReturnsValidString()
        {
            // Arrange
            var detector = GetCurrentPlatformDetector();
            
            // Act
            var recommendations = detector.GetInstallationRecommendations();
            
            // Assert
            Assert.IsNotNull(recommendations, "Installation recommendations should not be null");
            Assert.IsNotEmpty(recommendations, "Installation recommendations should not be empty");
        }

        [Test]
        public void PlatformDetector_GetPythonInstallUrl_ReturnsValidUrl()
        {
            // Arrange
            var detector = GetCurrentPlatformDetector();
            
            // Act
            var url = detector.GetPythonInstallUrl();
            
            // Assert
            Assert.IsNotNull(url, "Python install URL should not be null");
            Assert.IsTrue(url.StartsWith("http"), "Python install URL should be a valid URL");
        }

        [Test]
        public void PlatformDetector_GetUVInstallUrl_ReturnsValidUrl()
        {
            // Arrange
            var detector = GetCurrentPlatformDetector();
            
            // Act
            var url = detector.GetUVInstallUrl();
            
            // Assert
            Assert.IsNotNull(url, "UV install URL should not be null");
            Assert.IsTrue(url.StartsWith("http"), "UV install URL should be a valid URL");
        }

        [Test]
        public void MockPlatformDetector_WorksCorrectly()
        {
            // Arrange
            var mockDetector = new MockPlatformDetector();
            mockDetector.SetPythonAvailable(true, "3.11.0", "/usr/bin/python3");
            mockDetector.SetUVAvailable(false);
            mockDetector.SetMCPServerAvailable(true);
            
            // Act
            var pythonStatus = mockDetector.DetectPython();
            var uvStatus = mockDetector.DetectUV();
            var serverStatus = mockDetector.DetectMCPServer();
            
            // Assert
            Assert.IsTrue(pythonStatus.IsAvailable, "Mock Python should be available");
            Assert.AreEqual("3.11.0", pythonStatus.Version, "Mock Python version should match");
            Assert.AreEqual("/usr/bin/python3", pythonStatus.Path, "Mock Python path should match");
            
            Assert.IsFalse(uvStatus.IsAvailable, "Mock UV should not be available");
            Assert.IsTrue(serverStatus.IsAvailable, "Mock MCP Server should be available");
        }

        private IPlatformDetector GetCurrentPlatformDetector()
        {
            if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
                return new WindowsPlatformDetector();
            if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX))
                return new MacOSPlatformDetector();
            if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Linux))
                return new LinuxPlatformDetector();
            
            throw new PlatformNotSupportedException("Current platform not supported for testing");
        }
    }
}