using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using Newtonsoft.Json.Linq;
using MCPForUnity.Editor.Tools;

namespace MCPForUnity.Tests.Editor
{
    [TestFixture]
    public class CommandQueueTests
    {
        CommandQueue _queue;

        static Task<object> DummyExecutor(string tool, JObject p)
            => Task.FromResult<object>(new { success = true });

        static Task<object> SlowExecutor(string tool, JObject p)
            => Task.FromResult<object>(new { success = true });

        [SetUp]
        public void SetUp() => _queue = new CommandQueue();

        [Test]
        public void Submit_ReturnsJobWithTicket()
        {
            var cmds = new List<BatchCommand>
            {
                new() { Tool = "find_gameobjects", Params = new JObject(), Tier = ExecutionTier.Instant }
            };
            var job = _queue.Submit("agent-1", "test", false, cmds);
            Assert.That(job, Is.Not.Null);
            Assert.That(job.Ticket, Does.StartWith("t-"));
        }

        [Test]
        public void Submit_HeavyJob_IncreasesQueueDepth()
        {
            var cmds = new List<BatchCommand>
            {
                new() { Tool = "refresh_unity", Params = new JObject(), Tier = ExecutionTier.Heavy }
            };
            _queue.Submit("agent-1", "heavy", false, cmds);
            Assert.That(_queue.QueueDepth, Is.GreaterThanOrEqualTo(1));
        }

        [Test]
        public void Poll_ExistingTicket_ReturnsJob()
        {
            var cmds = new List<BatchCommand>
            {
                new() { Tool = "find_gameobjects", Params = new JObject(), Tier = ExecutionTier.Instant }
            };
            var job = _queue.Submit("agent-1", "test", false, cmds);
            var polled = _queue.Poll(job.Ticket);
            Assert.That(polled, Is.Not.Null);
            Assert.That(polled.Ticket, Is.EqualTo(job.Ticket));
        }

        [Test]
        public void Poll_InvalidTicket_ReturnsNull()
        {
            Assert.That(_queue.Poll("nonexistent"), Is.Null);
        }

        [Test]
        public void Cancel_QueuedJob_Succeeds()
        {
            var cmds = new List<BatchCommand>
            {
                new() { Tool = "refresh_unity", Params = new JObject(), Tier = ExecutionTier.Heavy }
            };
            var job = _queue.Submit("agent-1", "heavy", false, cmds);
            bool cancelled = _queue.Cancel(job.Ticket, "agent-1");
            Assert.That(cancelled, Is.True);
            Assert.That(job.Status, Is.EqualTo(JobStatus.Cancelled));
        }

        [Test]
        public void Cancel_WrongAgent_Fails()
        {
            var cmds = new List<BatchCommand>
            {
                new() { Tool = "refresh_unity", Params = new JObject(), Tier = ExecutionTier.Heavy }
            };
            var job = _queue.Submit("agent-1", "heavy", false, cmds);
            bool cancelled = _queue.Cancel(job.Ticket, "agent-2");
            Assert.That(cancelled, Is.False);
            Assert.That(job.Status, Is.EqualTo(JobStatus.Queued));
        }

        [Test]
        public void GetAheadOf_ReturnsJobsBeforeTicket()
        {
            var heavyCmds = new List<BatchCommand>
            {
                new() { Tool = "refresh_unity", Params = new JObject(), Tier = ExecutionTier.Heavy }
            };
            var j1 = _queue.Submit("a", "first", false, heavyCmds);
            var j2 = _queue.Submit("b", "second", false, heavyCmds);

            var ahead = _queue.GetAheadOf(j2.Ticket);
            Assert.That(ahead.Count, Is.GreaterThanOrEqualTo(1));
            Assert.That(ahead[0].Ticket, Is.EqualTo(j1.Ticket));
        }

        [Test]
        public void HasActiveHeavy_InitiallyFalse()
        {
            Assert.That(_queue.HasActiveHeavy, Is.False);
        }

        [Test]
        public void GetStatus_ReturnsQueueInfo()
        {
            var cmds = new List<BatchCommand>
            {
                new() { Tool = "find_gameobjects", Params = new JObject(), Tier = ExecutionTier.Instant }
            };
            _queue.Submit("agent-1", "test", false, cmds);
            var status = _queue.GetStatus();
            Assert.That(status, Is.Not.Null);
        }

        [Test]
        public void Submit_MultipleHeavyJobs_AllQueued()
        {
            var heavyCmds = new List<BatchCommand>
            {
                new() { Tool = "refresh_unity", Params = new JObject(), Tier = ExecutionTier.Heavy }
            };
            _queue.Submit("a", "h1", false, heavyCmds);
            _queue.Submit("b", "h2", false, heavyCmds);
            _queue.Submit("c", "h3", false, heavyCmds);

            // All three should be queued
            Assert.That(_queue.QueueDepth, Is.GreaterThanOrEqualTo(3));
        }

        [Test]
        public void BatchCommand_CausesDomainReload_DefaultsFalse()
        {
            var cmd = new BatchCommand { Tool = "find_gameobjects", Params = new JObject(), Tier = ExecutionTier.Instant };
            Assert.That(cmd.CausesDomainReload, Is.False);
        }

        [Test]
        public void BatchJob_CausesDomainReload_DefaultsFalse()
        {
            var job = new BatchJob();
            Assert.That(job.CausesDomainReload, Is.False);
        }

        [Test]
        public void Submit_WithReloadCommand_SetsJobCausesDomainReload()
        {
            var cmds = new List<BatchCommand>
            {
                new() { Tool = "find_gameobjects", Params = new JObject(), Tier = ExecutionTier.Instant, CausesDomainReload = false },
                new() { Tool = "refresh_unity", Params = new JObject(), Tier = ExecutionTier.Heavy, CausesDomainReload = true }
            };
            var job = _queue.Submit("agent-1", "test", false, cmds);
            Assert.That(job.CausesDomainReload, Is.True);
        }

        [Test]
        public void Submit_WithoutReloadCommand_JobCausesDomainReloadFalse()
        {
            var cmds = new List<BatchCommand>
            {
                new() { Tool = "run_tests", Params = new JObject(), Tier = ExecutionTier.Heavy, CausesDomainReload = false }
            };
            var job = _queue.Submit("agent-1", "test", false, cmds);
            Assert.That(job.CausesDomainReload, Is.False);
        }

        [Test]
        public void ProcessTick_ReloadJob_SkippedWhenEditorBusy()
        {
            _queue.IsEditorBusy = () => true;
            var cmds = new List<BatchCommand>
            {
                new() { Tool = "refresh_unity", Params = new JObject(), Tier = ExecutionTier.Heavy, CausesDomainReload = true }
            };
            var job = _queue.Submit("agent-1", "refresh", false, cmds);

            // Tick should NOT start the job because editor is busy
            _queue.ProcessTick(DummyExecutor);
            Assert.That(job.Status, Is.EqualTo(JobStatus.Queued));
            Assert.That(_queue.HasActiveHeavy, Is.False);
        }

        [Test]
        public void ProcessTick_ReloadJob_ProceedsWhenEditorNotBusy()
        {
            _queue.IsEditorBusy = () => false;
            var cmds = new List<BatchCommand>
            {
                new() { Tool = "refresh_unity", Params = new JObject(), Tier = ExecutionTier.Heavy, CausesDomainReload = true }
            };
            var job = _queue.Submit("agent-1", "refresh", false, cmds);

            _queue.ProcessTick(DummyExecutor);
            Assert.That(job.Status, Is.Not.EqualTo(JobStatus.Queued));
        }

        [Test]
        public void ProcessTick_NonReloadHeavyJob_ProceedsEvenWhenBusy()
        {
            _queue.IsEditorBusy = () => true;
            var cmds = new List<BatchCommand>
            {
                new() { Tool = "run_tests", Params = new JObject(), Tier = ExecutionTier.Heavy, CausesDomainReload = false }
            };
            var job = _queue.Submit("agent-1", "tests", false, cmds);

            _queue.ProcessTick(DummyExecutor);
            Assert.That(job.Status, Is.Not.EqualTo(JobStatus.Queued));
        }

        [Test]
        public void ProcessTick_ReloadJobBehindNonReload_NonReloadProceeds()
        {
            _queue.IsEditorBusy = () => true;
            var testCmds = new List<BatchCommand>
            {
                new() { Tool = "run_tests", Params = new JObject(), Tier = ExecutionTier.Heavy, CausesDomainReload = false }
            };
            var refreshCmds = new List<BatchCommand>
            {
                new() { Tool = "refresh_unity", Params = new JObject(), Tier = ExecutionTier.Heavy, CausesDomainReload = true }
            };
            var testJob = _queue.Submit("agent-1", "tests", false, testCmds);
            var refreshJob = _queue.Submit("agent-2", "refresh", false, refreshCmds);

            _queue.ProcessTick(DummyExecutor);
            // test job should start (non-reload), refresh should stay queued
            Assert.That(testJob.Status, Is.Not.EqualTo(JobStatus.Queued));
            Assert.That(refreshJob.Status, Is.EqualTo(JobStatus.Queued));
        }
    }
}
