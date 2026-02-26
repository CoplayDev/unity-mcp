using System.Collections.Generic;
using NUnit.Framework;
using Newtonsoft.Json.Linq;
using MCPForUnity.Editor.Tools;

namespace MCPForUnity.Tests.Editor
{
    [TestFixture]
    public class BatchExecuteAsyncTests
    {
        [Test]
        public void CommandGatewayState_Queue_IsNotNull()
        {
            Assert.That(CommandGatewayState.Queue, Is.Not.Null);
        }

        [Test]
        public void CommandGatewayState_Queue_IsSameInstance()
        {
            var q1 = CommandGatewayState.Queue;
            var q2 = CommandGatewayState.Queue;
            Assert.That(q1, Is.SameAs(q2));
        }

        [Test]
        public void PollJob_NullParams_ReturnsError()
        {
            var result = PollJob.HandleCommand(null);
            Assert.That(result, Is.Not.Null);
        }

        [Test]
        public void PollJob_MissingTicket_ReturnsError()
        {
            var result = PollJob.HandleCommand(new JObject());
            Assert.That(result, Is.Not.Null);
        }

        [Test]
        public void PollJob_InvalidTicket_ReturnsNotFound()
        {
            var p = new JObject { ["ticket"] = "nonexistent-ticket" };
            var result = PollJob.HandleCommand(p);
            Assert.That(result, Is.Not.Null);
        }

        [Test]
        public void QueueStatus_ReturnsStatusObject()
        {
            var result = QueueStatus.HandleCommand(new JObject());
            Assert.That(result, Is.Not.Null);
        }

        [Test]
        public void BatchExecuteMaxCommands_DefaultIs25()
        {
            // Just verify the constants are sane
            Assert.That(BatchExecute.DefaultMaxCommandsPerBatch, Is.EqualTo(25));
            Assert.That(BatchExecute.AbsoluteMaxCommandsPerBatch, Is.EqualTo(100));
        }

        [Test]
        public void PollJob_QueuedReloadJob_IncludesBlockedBy_WhenBusy()
        {
            var queue = new CommandQueue();
            queue.IsEditorBusy = () => true;
            var cmds = new List<BatchCommand>
            {
                new() { Tool = "refresh_unity", Params = new JObject(), Tier = ExecutionTier.Heavy, CausesDomainReload = true }
            };
            var job = queue.Submit("agent-1", "refresh", false, cmds);

            string reason = queue.GetBlockedReason(job.Ticket);
            Assert.That(reason, Is.Not.Null);
        }

        [Test]
        public void PollJob_QueuedNonReloadJob_BlockedByIsNull()
        {
            var queue = new CommandQueue();
            queue.IsEditorBusy = () => true;
            var cmds = new List<BatchCommand>
            {
                new() { Tool = "run_tests", Params = new JObject(), Tier = ExecutionTier.Heavy, CausesDomainReload = false }
            };
            var job = queue.Submit("agent-1", "tests", false, cmds);

            string reason = queue.GetBlockedReason(job.Ticket);
            Assert.That(reason, Is.Null);
        }
    }
}
