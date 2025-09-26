using System;
using System.Collections.Generic;
using System.Diagnostics;
using NUnit.Framework;
using MCPForUnity.Editor.Dependencies;
using MCPForUnity.Editor.Setup;
using MCPForUnity.Editor.Installation;
using MCPForUnity.Editor.Dependencies.Models;

namespace MCPForUnity.Tests
{
    [TestFixture]
    public class PerformanceTests
    {
        private const int PERFORMANCE_THRESHOLD_MS = 1000; // 1 second threshold for most operations
        private const int STARTUP_THRESHOLD_MS = 100; // 100ms threshold for startup operations

        [Test]
        public void DependencyManager_CheckAllDependencies_PerformanceTest()
        {
            // Test that dependency checking completes within reasonable time
            
            var stopwatch = Stopwatch.StartNew();
            
            // Act
            var result = DependencyManager.CheckAllDependencies();
            
            stopwatch.Stop();
            
            // Assert
            Assert.IsNotNull(result, "Should return valid result");
            Assert.Less(stopwatch.ElapsedMilliseconds, PERFORMANCE_THRESHOLD_MS, 
                $"Dependency check should complete within {PERFORMANCE_THRESHOLD_MS}ms, took {stopwatch.ElapsedMilliseconds}ms");
            
            UnityEngine.Debug.Log($"DependencyManager.CheckAllDependencies took {stopwatch.ElapsedMilliseconds}ms");
        }

        [Test]
        public void DependencyManager_IsSystemReady_PerformanceTest()
        {
            // Test that system ready check is fast (should be cached or optimized)
            
            var stopwatch = Stopwatch.StartNew();
            
            // Act
            var isReady = DependencyManager.IsSystemReady();
            
            stopwatch.Stop();
            
            // Assert
            Assert.Less(stopwatch.ElapsedMilliseconds, PERFORMANCE_THRESHOLD_MS, 
                $"System ready check should complete within {PERFORMANCE_THRESHOLD_MS}ms, took {stopwatch.ElapsedMilliseconds}ms");
            
            UnityEngine.Debug.Log($"DependencyManager.IsSystemReady took {stopwatch.ElapsedMilliseconds}ms");
        }

        [Test]
        public void DependencyManager_GetCurrentPlatformDetector_PerformanceTest()
        {
            // Test that platform detector retrieval is fast (startup critical)
            
            var stopwatch = Stopwatch.StartNew();
            
            // Act
            var detector = DependencyManager.GetCurrentPlatformDetector();
            
            stopwatch.Stop();
            
            // Assert
            Assert.IsNotNull(detector, "Should return valid detector");
            Assert.Less(stopwatch.ElapsedMilliseconds, STARTUP_THRESHOLD_MS, 
                $"Platform detector retrieval should complete within {STARTUP_THRESHOLD_MS}ms, took {stopwatch.ElapsedMilliseconds}ms");
            
            UnityEngine.Debug.Log($"DependencyManager.GetCurrentPlatformDetector took {stopwatch.ElapsedMilliseconds}ms");
        }

        [Test]
        public void SetupWizard_GetSetupState_PerformanceTest()
        {
            // Test that setup state retrieval is fast (startup critical)
            
            var stopwatch = Stopwatch.StartNew();
            
            // Act
            var state = SetupWizard.GetSetupState();
            
            stopwatch.Stop();
            
            // Assert
            Assert.IsNotNull(state, "Should return valid state");
            Assert.Less(stopwatch.ElapsedMilliseconds, STARTUP_THRESHOLD_MS, 
                $"Setup state retrieval should complete within {STARTUP_THRESHOLD_MS}ms, took {stopwatch.ElapsedMilliseconds}ms");
            
            UnityEngine.Debug.Log($"SetupWizard.GetSetupState took {stopwatch.ElapsedMilliseconds}ms");
        }

        [Test]
        public void SetupWizard_SaveSetupState_PerformanceTest()
        {
            // Test that setup state saving is reasonably fast
            
            var stopwatch = Stopwatch.StartNew();
            
            // Act
            SetupWizard.SaveSetupState();
            
            stopwatch.Stop();
            
            // Assert
            Assert.Less(stopwatch.ElapsedMilliseconds, STARTUP_THRESHOLD_MS, 
                $"Setup state saving should complete within {STARTUP_THRESHOLD_MS}ms, took {stopwatch.ElapsedMilliseconds}ms");
            
            UnityEngine.Debug.Log($"SetupWizard.SaveSetupState took {stopwatch.ElapsedMilliseconds}ms");
        }

        [Test]
        public void DependencyManager_RepeatedCalls_PerformanceTest()
        {
            // Test performance of repeated dependency checks (should be optimized/cached)
            
            const int iterations = 10;
            var times = new List<long>();
            
            for (int i = 0; i < iterations; i++)
            {
                var stopwatch = Stopwatch.StartNew();
                DependencyManager.IsSystemReady();
                stopwatch.Stop();
                times.Add(stopwatch.ElapsedMilliseconds);
            }
            
            // Calculate average
            long totalTime = 0;
            foreach (var time in times)
            {
                totalTime += time;
            }
            var averageTime = totalTime / iterations;
            
            // Assert
            Assert.Less(averageTime, PERFORMANCE_THRESHOLD_MS, 
                $"Average repeated dependency check should complete within {PERFORMANCE_THRESHOLD_MS}ms, average was {averageTime}ms");
            
            UnityEngine.Debug.Log($"Average time for {iterations} dependency checks: {averageTime}ms");
        }

        [Test]
        public void InstallationOrchestrator_Creation_PerformanceTest()
        {
            // Test that installation orchestrator creation is fast
            
            var stopwatch = Stopwatch.StartNew();
            
            // Act
            var orchestrator = new InstallationOrchestrator();
            
            stopwatch.Stop();
            
            // Assert
            Assert.IsNotNull(orchestrator, "Should create valid orchestrator");
            Assert.Less(stopwatch.ElapsedMilliseconds, STARTUP_THRESHOLD_MS, 
                $"Installation orchestrator creation should complete within {STARTUP_THRESHOLD_MS}ms, took {stopwatch.ElapsedMilliseconds}ms");
            
            UnityEngine.Debug.Log($"InstallationOrchestrator creation took {stopwatch.ElapsedMilliseconds}ms");
        }

        [Test]
        public void DependencyCheckResult_LargeDataSet_PerformanceTest()
        {
            // Test performance with large number of dependencies
            
            var result = new DependencyCheckResult();
            
            // Add many dependencies
            for (int i = 0; i < 1000; i++)
            {
                result.Dependencies.Add(new DependencyStatus
                {
                    Name = $"Dependency {i}",
                    IsAvailable = i % 2 == 0,
                    IsRequired = i % 3 == 0,
                    Version = $"1.{i}.0",
                    Path = $"/path/to/dependency{i}",
                    Details = $"Details for dependency {i}"
                });
            }
            
            var stopwatch = Stopwatch.StartNew();
            
            // Act
            result.GenerateSummary();
            var missing = result.GetMissingDependencies();
            var missingRequired = result.GetMissingRequired();
            
            stopwatch.Stop();
            
            // Assert
            Assert.Less(stopwatch.ElapsedMilliseconds, PERFORMANCE_THRESHOLD_MS, 
                $"Large dataset processing should complete within {PERFORMANCE_THRESHOLD_MS}ms, took {stopwatch.ElapsedMilliseconds}ms");
            
            UnityEngine.Debug.Log($"Processing 1000 dependencies took {stopwatch.ElapsedMilliseconds}ms");
        }

        [Test]
        public void SetupState_RepeatedOperations_PerformanceTest()
        {
            // Test performance of repeated setup state operations
            
            const int iterations = 100;
            var stopwatch = Stopwatch.StartNew();
            
            for (int i = 0; i < iterations; i++)
            {
                var state = SetupWizard.GetSetupState();
                state.RecordSetupAttempt($"Attempt {i}");
                state.ShouldShowSetup($"Version {i}");
                SetupWizard.SaveSetupState();
            }
            
            stopwatch.Stop();
            
            var averageTime = stopwatch.ElapsedMilliseconds / iterations;
            
            // Assert
            Assert.Less(averageTime, 10, // 10ms per operation
                $"Average setup state operation should complete within 10ms, average was {averageTime}ms");
            
            UnityEngine.Debug.Log($"Average time for {iterations} setup state operations: {averageTime}ms");
        }

        [Test]
        public void DependencyManager_ConcurrentAccess_PerformanceTest()
        {
            // Test performance under concurrent access
            
            const int threadCount = 10;
            const int operationsPerThread = 10;
            
            var tasks = new List<System.Threading.Tasks.Task>();
            var stopwatch = Stopwatch.StartNew();
            
            for (int i = 0; i < threadCount; i++)
            {
                tasks.Add(System.Threading.Tasks.Task.Run(() =>
                {
                    for (int j = 0; j < operationsPerThread; j++)
                    {
                        DependencyManager.IsSystemReady();
                        DependencyManager.IsDependencyAvailable("python");
                        DependencyManager.GetMissingDependenciesSummary();
                    }
                }));
            }
            
            System.Threading.Tasks.Task.WaitAll(tasks.ToArray());
            stopwatch.Stop();
            
            var totalOperations = threadCount * operationsPerThread * 3; // 3 operations per iteration
            var averageTime = (double)stopwatch.ElapsedMilliseconds / totalOperations;
            
            // Assert
            Assert.Less(averageTime, 100, // 100ms per operation under load
                $"Average concurrent operation should complete within 100ms, average was {averageTime:F2}ms");
            
            UnityEngine.Debug.Log($"Concurrent access: {totalOperations} operations in {stopwatch.ElapsedMilliseconds}ms, average {averageTime:F2}ms per operation");
        }

        [Test]
        public void MemoryUsage_DependencyOperations_Test()
        {
            // Test memory usage of dependency operations
            
            var initialMemory = GC.GetTotalMemory(true);
            
            // Perform many operations
            for (int i = 0; i < 100; i++)
            {
                var result = DependencyManager.CheckAllDependencies();
                var diagnostics = DependencyManager.GetDependencyDiagnostics();
                var summary = DependencyManager.GetMissingDependenciesSummary();
                
                // Force garbage collection periodically
                if (i % 10 == 0)
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                }
            }
            
            GC.Collect();
            GC.WaitForPendingFinalizers();
            var finalMemory = GC.GetTotalMemory(false);
            
            var memoryIncrease = finalMemory - initialMemory;
            var memoryIncreaseMB = memoryIncrease / (1024.0 * 1024.0);
            
            // Assert reasonable memory usage (less than 10MB increase)
            Assert.Less(memoryIncreaseMB, 10.0, 
                $"Memory usage should not increase significantly, increased by {memoryIncreaseMB:F2}MB");
            
            UnityEngine.Debug.Log($"Memory usage increased by {memoryIncreaseMB:F2}MB after 100 dependency operations");
        }

        [Test]
        public void StartupImpact_SimulatedUnityStartup_PerformanceTest()
        {
            // Simulate Unity startup scenario to measure impact
            
            var stopwatch = Stopwatch.StartNew();
            
            // Simulate what happens during Unity startup
            var detector = DependencyManager.GetCurrentPlatformDetector();
            var state = SetupWizard.GetSetupState();
            var shouldShow = state.ShouldShowSetup("3.4.0");
            
            stopwatch.Stop();
            
            // Assert minimal startup impact
            Assert.Less(stopwatch.ElapsedMilliseconds, 200, // 200ms threshold for startup
                $"Startup operations should complete within 200ms, took {stopwatch.ElapsedMilliseconds}ms");
            
            UnityEngine.Debug.Log($"Simulated Unity startup impact: {stopwatch.ElapsedMilliseconds}ms");
        }
    }
}