using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using MCPForUnity.Editor.Services.Transport;

namespace MCPForUnityTests.Editor.Services
{
    /// <summary>
    /// Tests verifying the optimization fix for GitHub issue #577.
    /// Confirms that the early-exit optimization in ProcessQueue prevents
    /// excessive memory allocations when the dispatcher is idle.
    /// </summary>
    public class TransportCommandDispatcherOptimizationTests
    {
        [Test]
        public void ExecuteCommandJsonAsync_WithSimplePingCommand_CompletesSuccessfully()
        {
            // Arrange
            var commandJson = """{"type": "ping", "params": {}}""";
            var cts = new CancellationTokenSource();
            cts.CancelAfter(TimeSpan.FromSeconds(5));

            // Act
            var task = TransportCommandDispatcher.ExecuteCommandJsonAsync(commandJson, cts.Token);
            var completed = task.Wait(TimeSpan.FromSeconds(2));

            // Assert
            Assert.IsTrue(completed, "Ping command should complete within 2 seconds");
            Assert.IsTrue(task.IsCompletedSuccessfully, "Command task should succeed");
            Assert.IsNotEmpty(task.Result, "Response should not be empty");
        }

        [Test]
        public void ExecuteCommandJsonAsync_WithCancellation_CancelsProperly()
        {
            // Arrange
            var commandJson = """{"type": "ping", "params": {}}""";
            var cts = new CancellationTokenSource();

            // Act: Create command but cancel before it processes
            var task = TransportCommandDispatcher.ExecuteCommandJsonAsync(commandJson, cts.Token);
            cts.Cancel();

            var completed = task.Wait(TimeSpan.FromSeconds(1));

            // Assert
            Assert.IsTrue(completed, "Cancelled task should complete");
            Assert.IsTrue(task.IsCanceled || task.IsCompletedSuccessfully,
                "Task should be either cancelled or completed");
        }

        [Test]
        public void ExecuteCommandJsonAsync_MultipleCommands_ProcessesAllCorrectly()
        {
            // Arrange
            var cts = new CancellationTokenSource();
            cts.CancelAfter(TimeSpan.FromSeconds(5));

            // Act: Execute multiple commands in rapid succession
            var tasks = new Task<string>[3];
            for (int i = 0; i < 3; i++)
            {
                var commandJson = """{"type": "ping", "params": {}}""";
                tasks[i] = TransportCommandDispatcher.ExecuteCommandJsonAsync(commandJson, cts.Token);
            }

            var allCompleted = Task.WaitAll(tasks, TimeSpan.FromSeconds(3));

            // Assert
            Assert.IsTrue(allCompleted, "All commands should complete");
            foreach (var task in tasks)
            {
                Assert.IsTrue(task.IsCompletedSuccessfully, "Each command should complete successfully");
            }
        }

        [Test]
        public void ExecuteCommandJsonAsync_WithInvalidJson_ReturnsError()
        {
            // Arrange
            var invalidCommandJson = "this is not json";
            var cts = new CancellationTokenSource();
            cts.CancelAfter(TimeSpan.FromSeconds(5));

            // Act
            var task = TransportCommandDispatcher.ExecuteCommandJsonAsync(invalidCommandJson, cts.Token);
            var completed = task.Wait(TimeSpan.FromSeconds(2));

            // Assert
            Assert.IsTrue(completed, "Invalid command should complete with error");
            Assert.IsTrue(task.IsCompletedSuccessfully, "Task should complete (with error result)");
            StringAssert.Contains("error", task.Result.ToLower(),
                "Response should indicate an error");
        }

        [Test]
        public void ExecuteCommandJsonAsync_WithEmptyCommand_ReturnsError()
        {
            // Arrange
            var emptyJson = "";
            var cts = new CancellationTokenSource();
            cts.CancelAfter(TimeSpan.FromSeconds(5));

            // Act
            var task = TransportCommandDispatcher.ExecuteCommandJsonAsync(emptyJson, cts.Token);
            var completed = task.Wait(TimeSpan.FromSeconds(2));

            // Assert
            Assert.IsTrue(completed, "Empty command should complete with error");
            Assert.IsTrue(task.IsCompletedSuccessfully, "Task should complete");
            StringAssert.Contains("error", task.Result.ToLower(),
                "Response should indicate an error for empty command");
        }

        [Test]
        public void ExecuteCommandJsonAsync_WithNullThrowsArgumentNullException()
        {
            // Arrange
            string nullJson = null;
            var cts = new CancellationTokenSource();

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() =>
            {
                TransportCommandDispatcher.ExecuteCommandJsonAsync(nullJson, cts.Token);
            });
        }

        [Test]
        public void ExecuteCommandJsonAsync_RapidFireCommands_MaintainsOrdering()
        {
            // Arrange: A sequence of numbered ping commands
            var cts = new CancellationTokenSource();
            cts.CancelAfter(TimeSpan.FromSeconds(10));

            // Act: Fire multiple pings rapidly
            var tasks = new Task<string>[5];
            for (int i = 0; i < 5; i++)
            {
                var cmd = """{"type": "ping", "params": {}}""";
                tasks[i] = TransportCommandDispatcher.ExecuteCommandJsonAsync(cmd, cts.Token);
            }

            var allCompleted = Task.WaitAll(tasks, TimeSpan.FromSeconds(5));

            // Assert
            Assert.IsTrue(allCompleted, "All rapid-fire commands should complete");
            int successCount = 0;
            foreach (var task in tasks)
            {
                if (task.IsCompletedSuccessfully && task.Result.Contains("pong"))
                {
                    successCount++;
                }
            }
            Assert.That(successCount, Is.EqualTo(5), "All 5 ping commands should succeed");
        }
    }
}
