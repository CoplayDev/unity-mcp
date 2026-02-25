using NUnit.Framework;
using MCPForUnity.Editor.Tools;

namespace MCPForUnity.Tests.Editor
{
    [TestFixture]
    public class TicketStoreTests
    {
        TicketStore _store;

        [SetUp]
        public void SetUp() => _store = new TicketStore();

        [Test]
        public void CreateJob_ReturnsUniqueTicket()
        {
            var job1 = _store.CreateJob("agent-1", "label-1", true, ExecutionTier.Heavy);
            var job2 = _store.CreateJob("agent-2", "label-2", false, ExecutionTier.Smooth);
            Assert.That(job1.Ticket, Is.Not.EqualTo(job2.Ticket));
        }

        [Test]
        public void CreateJob_DefaultStatus_IsQueued()
        {
            var job = _store.CreateJob("agent-1", "test", false, ExecutionTier.Smooth);
            Assert.That(job.Status, Is.EqualTo(JobStatus.Queued));
        }

        [Test]
        public void CreateJob_StoresAgent()
        {
            var job = _store.CreateJob("my-agent", "test", false, ExecutionTier.Smooth);
            Assert.That(job.Agent, Is.EqualTo("my-agent"));
        }

        [Test]
        public void CreateJob_NullAgent_DefaultsToAnonymous()
        {
            var job = _store.CreateJob(null, "test", false, ExecutionTier.Smooth);
            Assert.That(job.Agent, Is.EqualTo("anonymous"));
        }

        [Test]
        public void GetJob_ValidTicket_ReturnsJob()
        {
            var job = _store.CreateJob("agent-1", "test", false, ExecutionTier.Smooth);
            var found = _store.GetJob(job.Ticket);
            Assert.That(found, Is.Not.Null);
            Assert.That(found.Ticket, Is.EqualTo(job.Ticket));
        }

        [Test]
        public void GetJob_InvalidTicket_ReturnsNull()
        {
            Assert.That(_store.GetJob("nonexistent"), Is.Null);
        }

        [Test]
        public void CleanExpired_RemovesDoneJobsOlderThanTimeout()
        {
            var job = _store.CreateJob("agent-1", "test", false, ExecutionTier.Smooth);
            job.Status = JobStatus.Done;
            job.CompletedAt = System.DateTime.UtcNow.AddMinutes(-6);
            _store.CleanExpired(System.TimeSpan.FromMinutes(5));
            Assert.That(_store.GetJob(job.Ticket), Is.Null);
        }

        [Test]
        public void CleanExpired_KeepsRecentDoneJobs()
        {
            var job = _store.CreateJob("agent-1", "test", false, ExecutionTier.Smooth);
            job.Status = JobStatus.Done;
            job.CompletedAt = System.DateTime.UtcNow;
            _store.CleanExpired(System.TimeSpan.FromMinutes(5));
            Assert.That(_store.GetJob(job.Ticket), Is.Not.Null);
        }

        [Test]
        public void CleanExpired_KeepsQueuedJobs()
        {
            var job = _store.CreateJob("agent-1", "test", false, ExecutionTier.Smooth);
            // Queued jobs have no CompletedAt — should not be cleaned
            _store.CleanExpired(System.TimeSpan.FromMinutes(5));
            Assert.That(_store.GetJob(job.Ticket), Is.Not.Null);
        }

        [Test]
        public void GetAgentStats_ReturnsCorrectCounts()
        {
            _store.CreateJob("agent-1", "a", false, ExecutionTier.Smooth);
            var done = _store.CreateJob("agent-1", "b", false, ExecutionTier.Smooth);
            done.Status = JobStatus.Done;
            _store.CreateJob("agent-2", "c", false, ExecutionTier.Heavy);

            var stats = _store.GetAgentStats();
            Assert.That(stats["agent-1"].Queued, Is.EqualTo(1));
            Assert.That(stats["agent-1"].Completed, Is.EqualTo(1));
            Assert.That(stats["agent-2"].Queued, Is.EqualTo(1));
        }

        [Test]
        public void QueueDepth_ReflectsQueuedJobsOnly()
        {
            _store.CreateJob("a", "j1", false, ExecutionTier.Smooth);
            _store.CreateJob("b", "j2", false, ExecutionTier.Heavy);
            var done = _store.CreateJob("c", "j3", false, ExecutionTier.Instant);
            done.Status = JobStatus.Done;

            Assert.That(_store.QueueDepth, Is.EqualTo(2));
        }

        [Test]
        public void GetQueuedJobs_OrderedByCreationTime()
        {
            var j1 = _store.CreateJob("a", "first", false, ExecutionTier.Heavy);
            var j2 = _store.CreateJob("b", "second", false, ExecutionTier.Smooth);
            var queued = _store.GetQueuedJobs();
            Assert.That(queued[0].Ticket, Is.EqualTo(j1.Ticket));
            Assert.That(queued[1].Ticket, Is.EqualTo(j2.Ticket));
        }
    }
}
