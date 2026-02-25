using System.Collections.Generic;
using System.Linq;
using MCPForUnity.Editor.Tools;
using UnityEngine.UIElements;

namespace MCPForUnity.Editor.Windows.Components.Queue
{
    /// <summary>
    /// Controller for the Queue tab in the MCP For Unity editor window.
    /// Displays real-time command gateway queue state.
    /// </summary>
    public class McpQueueSection
    {
        private Label heavyTicketLabel;
        private Label queuedCount;
        private Label runningCount;
        private Label doneCount;
        private Label failedCount;
        private VisualElement jobListContainer;
        private VisualElement jobListHeader;
        private Label emptyLabel;

        public VisualElement Root { get; private set; }

        public McpQueueSection(VisualElement root)
        {
            Root = root;
            CacheUIElements();
        }

        private void CacheUIElements()
        {
            heavyTicketLabel = Root.Q<Label>("heavy-ticket-label");
            queuedCount = Root.Q<Label>("queued-count");
            runningCount = Root.Q<Label>("running-count");
            doneCount = Root.Q<Label>("done-count");
            failedCount = Root.Q<Label>("failed-count");
            jobListContainer = Root.Q<VisualElement>("job-list-container");
            jobListHeader = Root.Q<VisualElement>("job-list-header");
            emptyLabel = Root.Q<Label>("empty-label");
        }

        /// <summary>
        /// Refresh the queue display from current queue state.
        /// Called every 1 second while the Queue tab is visible.
        /// </summary>
        public void Refresh()
        {
            var queue = CommandGatewayState.Queue;
            var allJobs = queue.GetAllJobs();

            UpdateStatusBar(queue, allJobs);
            UpdateJobList(allJobs, queue);
        }

        private void UpdateStatusBar(CommandQueue queue, List<BatchJob> allJobs)
        {
            if (queue.HasActiveHeavy)
            {
                var active = allJobs.FirstOrDefault(j => j.Status == JobStatus.Running && j.Tier == ExecutionTier.Heavy);
                if (heavyTicketLabel != null)
                    heavyTicketLabel.text = active != null
                        ? $"{active.Ticket} ({active.Agent}: \"{Truncate(active.Label, 20)}\")"
                        : "Yes";
            }
            else
            {
                if (heavyTicketLabel != null)
                    heavyTicketLabel.text = "None";
            }

            int queued = 0, running = 0, done = 0, failed = 0;
            foreach (var j in allJobs)
            {
                switch (j.Status)
                {
                    case JobStatus.Queued: queued++; break;
                    case JobStatus.Running: running++; break;
                    case JobStatus.Done: done++; break;
                    case JobStatus.Failed: failed++; break;
                }
            }

            if (queuedCount != null) queuedCount.text = $"Queued: {queued}";
            if (runningCount != null) runningCount.text = $"Running: {running}";
            if (doneCount != null) doneCount.text = $"Done: {done}";
            if (failedCount != null) failedCount.text = $"Failed: {failed}";
        }

        private void UpdateJobList(List<BatchJob> allJobs, CommandQueue queue)
        {
            if (jobListContainer == null) return;

            jobListContainer.Clear();

            bool hasJobs = allJobs.Count > 0;
            if (jobListHeader != null)
                jobListHeader.style.display = hasJobs ? DisplayStyle.Flex : DisplayStyle.None;
            if (emptyLabel != null)
                emptyLabel.style.display = hasJobs ? DisplayStyle.None : DisplayStyle.Flex;

            foreach (var job in allJobs)
            {
                var row = CreateJobRow(job, queue);
                jobListContainer.Add(row);
            }
        }

        private VisualElement CreateJobRow(BatchJob job, CommandQueue queue)
        {
            var row = new VisualElement();
            row.AddToClassList("queue-job-row");

            // Status dot
            var dot = new VisualElement();
            dot.AddToClassList("queue-status-dot");
            dot.AddToClassList($"status-{job.Status.ToString().ToLowerInvariant()}");
            row.Add(dot);

            // Ticket
            var ticket = new Label(job.Ticket);
            ticket.AddToClassList("queue-col-ticket");
            row.Add(ticket);

            // Status text
            var status = new Label(job.Status.ToString().ToLowerInvariant());
            status.AddToClassList("queue-col-status");
            row.Add(status);

            // Blocked badge
            if (job.Status == JobStatus.Queued && job.CausesDomainReload && queue.IsEditorBusy())
            {
                var badge = new Label("BLK");
                badge.AddToClassList("queue-blocked-badge");
                row.Add(badge);
            }

            // Agent
            var agent = new Label(Truncate(job.Agent, 12));
            agent.AddToClassList("queue-col-agent");
            agent.tooltip = job.Agent;
            row.Add(agent);

            // Label
            var label = new Label(Truncate(job.Label, 24));
            label.AddToClassList("queue-col-label");
            label.tooltip = job.Label;
            row.Add(label);

            // Progress
            string progressText = "";
            if (job.Commands != null && job.Commands.Count > 0)
            {
                int idx = job.Status == JobStatus.Done ? job.Commands.Count : job.CurrentIndex + 1;
                progressText = $"{idx}/{job.Commands.Count}";
            }
            var progress = new Label(progressText);
            progress.AddToClassList("queue-col-progress");
            row.Add(progress);

            // Error tooltip for failed jobs
            if (job.Status == JobStatus.Failed && !string.IsNullOrEmpty(job.Error))
                row.tooltip = $"Error: {job.Error}";

            return row;
        }

        private static string Truncate(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return text.Length <= maxLength ? text : text.Substring(0, maxLength - 1) + "\u2026";
        }
    }
}
