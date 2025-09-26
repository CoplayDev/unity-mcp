using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using MCPForUnity.Editor.Installation;
using MCPForUnity.Editor.Dependencies.Models;

namespace MCPForUnity.Tests.Installation
{
    [TestFixture]
    public class InstallationOrchestratorTests
    {
        private InstallationOrchestrator _orchestrator;
        private List<string> _progressUpdates;
        private bool? _lastInstallationResult;
        private string _lastInstallationMessage;

        [SetUp]
        public void SetUp()
        {
            _orchestrator = new InstallationOrchestrator();
            _progressUpdates = new List<string>();
            _lastInstallationResult = null;
            _lastInstallationMessage = null;

            // Subscribe to events
            _orchestrator.OnProgressUpdate += OnProgressUpdate;
            _orchestrator.OnInstallationComplete += OnInstallationComplete;
        }

        [TearDown]
        public void TearDown()
        {
            // Unsubscribe from events
            _orchestrator.OnProgressUpdate -= OnProgressUpdate;
            _orchestrator.OnInstallationComplete -= OnInstallationComplete;
        }

        private void OnProgressUpdate(string message)
        {
            _progressUpdates.Add(message);
        }

        private void OnInstallationComplete(bool success, string message)
        {
            _lastInstallationResult = success;
            _lastInstallationMessage = message;
        }

        [Test]
        public void InstallationOrchestrator_DefaultState()
        {
            // Assert
            Assert.IsFalse(_orchestrator.IsInstalling, "Should not be installing by default");
        }

        [Test]
        public void StartInstallation_EmptyList_CompletesSuccessfully()
        {
            // Arrange
            var emptyDependencies = new List<DependencyStatus>();

            // Act
            _orchestrator.StartInstallation(emptyDependencies);

            // Wait a bit for async operation
            System.Threading.Thread.Sleep(100);

            // Assert
            Assert.IsTrue(_lastInstallationResult.HasValue, "Installation should complete");
            Assert.IsTrue(_lastInstallationResult.Value, "Empty installation should succeed");
            Assert.IsNotNull(_lastInstallationMessage, "Should have completion message");
        }

        [Test]
        public void StartInstallation_PythonDependency_FailsAsExpected()
        {
            // Arrange
            var dependencies = new List<DependencyStatus>
            {
                new DependencyStatus
                {
                    Name = "Python",
                    IsRequired = true,
                    IsAvailable = false
                }
            };

            // Act
            _orchestrator.StartInstallation(dependencies);

            // Wait for async operation
            System.Threading.Thread.Sleep(2000);

            // Assert
            Assert.IsTrue(_lastInstallationResult.HasValue, "Installation should complete");
            Assert.IsFalse(_lastInstallationResult.Value, "Python installation should fail (Asset Store compliance)");
            Assert.IsTrue(_progressUpdates.Count > 0, "Should have progress updates");
            Assert.IsTrue(_progressUpdates.Exists(p => p.Contains("Python")), "Should mention Python in progress");
        }

        [Test]
        public void StartInstallation_UVDependency_FailsAsExpected()
        {
            // Arrange
            var dependencies = new List<DependencyStatus>
            {
                new DependencyStatus
                {
                    Name = "UV Package Manager",
                    IsRequired = true,
                    IsAvailable = false
                }
            };

            // Act
            _orchestrator.StartInstallation(dependencies);

            // Wait for async operation
            System.Threading.Thread.Sleep(2000);

            // Assert
            Assert.IsTrue(_lastInstallationResult.HasValue, "Installation should complete");
            Assert.IsFalse(_lastInstallationResult.Value, "UV installation should fail (Asset Store compliance)");
            Assert.IsTrue(_progressUpdates.Count > 0, "Should have progress updates");
            Assert.IsTrue(_progressUpdates.Exists(p => p.Contains("UV")), "Should mention UV in progress");
        }

        [Test]
        public void StartInstallation_MCPServerDependency_AttemptsInstallation()
        {
            // Arrange
            var dependencies = new List<DependencyStatus>
            {
                new DependencyStatus
                {
                    Name = "MCP Server",
                    IsRequired = false,
                    IsAvailable = false
                }
            };

            // Act
            _orchestrator.StartInstallation(dependencies);

            // Wait for async operation
            System.Threading.Thread.Sleep(3000);

            // Assert
            Assert.IsTrue(_lastInstallationResult.HasValue, "Installation should complete");
            // Result depends on whether ServerInstaller.EnsureServerInstalled() succeeds
            Assert.IsTrue(_progressUpdates.Count > 0, "Should have progress updates");
            Assert.IsTrue(_progressUpdates.Exists(p => p.Contains("MCP Server")), "Should mention MCP Server in progress");
        }

        [Test]
        public void StartInstallation_MultipleDependencies_ProcessesAll()
        {
            // Arrange
            var dependencies = new List<DependencyStatus>
            {
                new DependencyStatus { Name = "Python", IsRequired = true, IsAvailable = false },
                new DependencyStatus { Name = "UV Package Manager", IsRequired = true, IsAvailable = false },
                new DependencyStatus { Name = "MCP Server", IsRequired = false, IsAvailable = false }
            };

            // Act
            _orchestrator.StartInstallation(dependencies);

            // Wait for async operation
            System.Threading.Thread.Sleep(5000);

            // Assert
            Assert.IsTrue(_lastInstallationResult.HasValue, "Installation should complete");
            Assert.IsFalse(_lastInstallationResult.Value, "Should fail due to Python/UV compliance restrictions");
            
            // Check that all dependencies were processed
            Assert.IsTrue(_progressUpdates.Exists(p => p.Contains("Python")), "Should process Python");
            Assert.IsTrue(_progressUpdates.Exists(p => p.Contains("UV")), "Should process UV");
            Assert.IsTrue(_progressUpdates.Exists(p => p.Contains("MCP Server")), "Should process MCP Server");
        }

        [Test]
        public void StartInstallation_UnknownDependency_HandlesGracefully()
        {
            // Arrange
            var dependencies = new List<DependencyStatus>
            {
                new DependencyStatus
                {
                    Name = "Unknown Dependency",
                    IsRequired = true,
                    IsAvailable = false
                }
            };

            // Act
            _orchestrator.StartInstallation(dependencies);

            // Wait for async operation
            System.Threading.Thread.Sleep(2000);

            // Assert
            Assert.IsTrue(_lastInstallationResult.HasValue, "Installation should complete");
            Assert.IsFalse(_lastInstallationResult.Value, "Unknown dependency installation should fail");
            Assert.IsTrue(_progressUpdates.Count > 0, "Should have progress updates");
        }

        [Test]
        public void StartInstallation_AlreadyInstalling_IgnoresSecondCall()
        {
            // Arrange
            var dependencies = new List<DependencyStatus>
            {
                new DependencyStatus { Name = "Python", IsRequired = true, IsAvailable = false }
            };

            // Act
            _orchestrator.StartInstallation(dependencies);
            Assert.IsTrue(_orchestrator.IsInstalling, "Should be installing after first call");

            var initialProgressCount = _progressUpdates.Count;
            _orchestrator.StartInstallation(dependencies); // Second call should be ignored

            // Assert
            // The second call should be ignored, so progress count shouldn't change significantly
            System.Threading.Thread.Sleep(100);
            var progressCountAfterSecondCall = _progressUpdates.Count;
            
            // We expect minimal change in progress updates from the second call
            Assert.IsTrue(progressCountAfterSecondCall - initialProgressCount <= 1, 
                "Second installation call should be ignored or have minimal impact");
        }

        [Test]
        public void CancelInstallation_StopsInstallation()
        {
            // Arrange
            var dependencies = new List<DependencyStatus>
            {
                new DependencyStatus { Name = "Python", IsRequired = true, IsAvailable = false }
            };

            // Act
            _orchestrator.StartInstallation(dependencies);
            Assert.IsTrue(_orchestrator.IsInstalling, "Should be installing");

            _orchestrator.CancelInstallation();

            // Wait a bit
            System.Threading.Thread.Sleep(100);

            // Assert
            Assert.IsFalse(_orchestrator.IsInstalling, "Should not be installing after cancellation");
            Assert.IsTrue(_lastInstallationResult.HasValue, "Should have completion result");
            Assert.IsFalse(_lastInstallationResult.Value, "Cancelled installation should be marked as failed");
            Assert.IsTrue(_lastInstallationMessage.Contains("cancelled"), "Message should indicate cancellation");
        }

        [Test]
        public void CancelInstallation_WhenNotInstalling_DoesNothing()
        {
            // Act
            _orchestrator.CancelInstallation();

            // Assert
            Assert.IsFalse(_orchestrator.IsInstalling, "Should not be installing");
            Assert.IsFalse(_lastInstallationResult.HasValue, "Should not have completion result");
        }

        [Test]
        public void InstallationOrchestrator_EventHandling()
        {
            // Test that events are properly fired
            var progressUpdateReceived = false;
            var installationCompleteReceived = false;

            var testOrchestrator = new InstallationOrchestrator();
            testOrchestrator.OnProgressUpdate += (message) => progressUpdateReceived = true;
            testOrchestrator.OnInstallationComplete += (success, message) => installationCompleteReceived = true;

            // Act
            var dependencies = new List<DependencyStatus>
            {
                new DependencyStatus { Name = "Python", IsRequired = true, IsAvailable = false }
            };
            testOrchestrator.StartInstallation(dependencies);

            // Wait for async operation
            System.Threading.Thread.Sleep(2000);

            // Assert
            Assert.IsTrue(progressUpdateReceived, "Progress update event should be fired");
            Assert.IsTrue(installationCompleteReceived, "Installation complete event should be fired");
        }

        [Test]
        public void InstallationOrchestrator_AssetStoreCompliance()
        {
            // This test verifies Asset Store compliance by ensuring that
            // Python and UV installations always fail (no automatic downloads)
            
            var dependencies = new List<DependencyStatus>
            {
                new DependencyStatus { Name = "Python", IsRequired = true, IsAvailable = false },
                new DependencyStatus { Name = "UV Package Manager", IsRequired = true, IsAvailable = false }
            };

            // Act
            _orchestrator.StartInstallation(dependencies);

            // Wait for async operation
            System.Threading.Thread.Sleep(3000);

            // Assert
            Assert.IsTrue(_lastInstallationResult.HasValue, "Installation should complete");
            Assert.IsFalse(_lastInstallationResult.Value, "Installation should fail for Asset Store compliance");
            
            // Verify that the failure messages indicate manual installation is required
            Assert.IsTrue(_lastInstallationMessage.Contains("Failed"), "Should indicate failure");
            Assert.IsTrue(_progressUpdates.Exists(p => p.Contains("manual")), 
                "Should indicate manual installation is required");
        }
    }
}