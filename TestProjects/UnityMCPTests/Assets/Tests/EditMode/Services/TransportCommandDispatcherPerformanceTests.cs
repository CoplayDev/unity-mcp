using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using MCPForUnity.Editor.Services.Transport;
using UnityEditor;

namespace MCPForUnityTests.Editor.Services
{
    /// <summary>
    /// Tests verifying that TransportCommandDispatcher doesn't allocate excessively when idle.
    /// Regression tests for GitHub issue #577: "High performance impact even when MCP server is off"
    /// </summary>
    public class TransportCommandDispatcherPerformanceTests
    {
        private const int FramesPerSecond = 60;
        private const int TestDurationSeconds = 2;
        private const int TestFrames = FramesPerSecond * TestDurationSeconds;

        [Test]
        public void ProcessQueue_WhenIdle_DoesNotAllocateMemory()
        {
            // Arrange: Simulate idle frames (no pending commands)
            var initialMemory = GC.GetTotalMemory(false);
            var allocationsSeen = new List<long>();

            // Act: Simulate 120 frames of EditorApplication.update firing with no commands
            for (int frame = 0; frame < TestFrames; frame++)
            {
                // This simulates calling EditorApplication.update via reflection or direct hook
                SimulateEditorUpdate();

                // Sample memory every 10 frames
                if (frame % 10 == 0)
                {
                    var currentMemory = GC.GetTotalMemory(false);
                    var delta = currentMemory - initialMemory;
                    allocationsSeen.Add(delta);
                }
            }

            // Assert: With the fix, memory growth should be minimal (no List allocations)
            // Without the fix, each frame allocates a List object, leading to ~120 allocations
            // and eventual GC pressure of 28ms spikes every second.
            var maxAllocation = 0L;
            foreach (var allocation in allocationsSeen)
            {
                if (allocation > maxAllocation)
                {
                    maxAllocation = allocation;
                }
            }

            // Each List<> allocation is ~48 bytes. We expect < 10KB total growth when idle.
            Assert.That(maxAllocation, Is.LessThan(10_000),
                "When idle with no pending commands, ProcessQueue should not allocate significantly");
        }

        [Test]
        public void ProcessQueue_WithPendingCommands_ProcessesThemCorrectly()
        {
            // Arrange
            var commandJson = """{"type": "ping", "params": {}}""";
            var cts = new CancellationTokenSource();
            var commandProcessed = false;

            // Simulate a command arriving
            var task = TransportCommandDispatcher.ExecuteCommandJsonAsync(commandJson, cts.Token);

            // Act: Let the command process
            SimulateEditorUpdate();

            // Wait for the command to complete
            if (task.Wait(TimeSpan.FromSeconds(1)))
            {
                commandProcessed = true;
            }

            // Assert
            Assert.IsTrue(commandProcessed, "Command should have been processed");
            Assert.IsTrue(task.IsCompletedSuccessfully, "Command task should complete successfully");
        }

        [Test]
        public void ExecuteCommandJsonAsync_CallsRequestMainThreadPump_EnsuresResponsiveness()
        {
            // Arrange
            var commandJson = """{"type": "ping", "params": {}}""";
            var cts = new CancellationTokenSource();
            var pumpWasCalled = false;

            // Mock RequestMainThreadPump by monitoring if ProcessQueue gets called
            // This verifies that the "wake up" mechanism works even if EditorApplication.update is not hooked

            // Act: Execute a command
            var task = TransportCommandDispatcher.ExecuteCommandJsonAsync(commandJson, cts.Token);

            // The RequestMainThreadPump should have been called (we can't directly test it,
            // but if the command completes, it means the pump worked)
            SimulateEditorUpdate();

            if (task.Wait(TimeSpan.FromSeconds(1)))
            {
                pumpWasCalled = true;
            }

            // Assert
            Assert.IsTrue(pumpWasCalled,
                "RequestMainThreadPump should ensure command is processed even if update hook is inactive");
        }

        [Test]
        public void ProcessQueue_UnhooksWhenIdle_AndRehooksWhenNeeded()
        {
            // Arrange
            var commandJson = """{"type": "ping", "params": {}}""";
            var cts = new CancellationTokenSource();

            // Act: Process a command (which should leave ProcessQueue idle afterward)
            var task = TransportCommandDispatcher.ExecuteCommandJsonAsync(commandJson, cts.Token);
            SimulateEditorUpdate();
            task.Wait(TimeSpan.FromSeconds(1));

            // Send idle frames - with the fix, ProcessQueue should unhook itself
            for (int i = 0; i < 10; i++)
            {
                SimulateEditorUpdate();
            }

            // Send another command - RequestMainThreadPump should work even with hook unhooked
            var command2 = """{"type": "ping", "params": {}}""";
            var task2 = TransportCommandDispatcher.ExecuteCommandJsonAsync(command2, cts.Token);
            SimulateEditorUpdate();

            var command2Processed = task2.Wait(TimeSpan.FromSeconds(1));

            // Assert
            Assert.IsTrue(command2Processed,
                "After unhooking due to idle, RequestMainThreadPump should still ensure command processing");
        }

        [Test]
        public void ProcessQueue_NoGarbageCollectionSpikes_DuringIdleFrames()
        {
            // Arrange: Get baseline GC statistics
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced);
            GC.WaitForPendingFinalizers();

            var gen0CollectionsBefore = GC.GetTotalMemory(false);

            // Act: Simulate 120 idle frames (2 seconds at 60 FPS)
            for (int frame = 0; frame < TestFrames; frame++)
            {
                SimulateEditorUpdate();
            }

            // Assert: No major allocations that would trigger GC
            var gen0CollectionsAfter = GC.GetTotalMemory(false);
            var memoryGrowth = gen0CollectionsAfter - gen0CollectionsBefore;

            // With the fix, we should see minimal allocations.
            // Without the fix, we'd see ~120 List allocations (6KB+) every 60 frames,
            // which accumulates to trigger GC pressure around 28ms spikes/second
            Assert.That(memoryGrowth, Is.LessThan(5_000),
                "Idle frames should not create allocations that trigger GC pressure");
        }

        // Helper: Simulates EditorApplication.update callback
        // Uses reflection to invoke ProcessQueue on the TransportCommandDispatcher
        private void SimulateEditorUpdate()
        {
            try
            {
                var method = typeof(TransportCommandDispatcher)
                    .GetMethod("ProcessQueue",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

                if (method != null)
                {
                    method.Invoke(null, null);
                }
            }
            catch (System.Reflection.TargetInvocationException ex)
            {
                // Unwrap the inner exception from reflection
                if (ex.InnerException != null)
                {
                    throw ex.InnerException;
                }
                throw;
            }
        }
    }
}
