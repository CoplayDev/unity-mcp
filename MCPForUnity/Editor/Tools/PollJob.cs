using Newtonsoft.Json.Linq;
using MCPForUnity.Editor.Helpers;

namespace MCPForUnity.Editor.Tools
{
    /// <summary>
    /// Poll the status of an async batch job by ticket ID.
    /// Generalizes the get_test_job pattern to any queued batch.
    /// Terminal jobs (Done, Failed, Cancelled) are auto-removed after the
    /// response is built. If gateway logging is enabled, the job data is
    /// written to a log file before removal.
    /// </summary>
    [McpForUnityTool("poll_job", Tier = ExecutionTier.Instant)]
    public static class PollJob
    {
        public static object HandleCommand(JObject @params)
        {
            var p = new ToolParams(@params);
            var ticketResult = p.GetRequired("ticket");
            if (!ticketResult.IsSuccess)
                return new ErrorResponse(ticketResult.ErrorMessage);

            string ticket = ticketResult.Value;
            var job = CommandGatewayState.Queue.Poll(ticket);
            if (job == null)
                return new ErrorResponse($"Ticket '{ticket}' not found or expired.");

            object response;

            switch (job.Status)
            {
                case JobStatus.Queued:
                    var ahead = CommandGatewayState.Queue.GetAheadOf(ticket);
                    string blockedBy = CommandGatewayState.Queue.GetBlockedReason(ticket);
                    response = new PendingResponse(
                        $"Queued at position {ahead.Count}.",
                        pollIntervalSeconds: 2.0,
                        data: new
                        {
                            ticket = job.Ticket,
                            status = "queued",
                            position = ahead.Count,
                            agent = job.Agent,
                            label = job.Label,
                            blocked_by = blockedBy,
                            ahead = ahead.ConvertAll(j => (object)new
                            {
                                ticket = j.Ticket,
                                agent = j.Agent,
                                label = j.Label,
                                tier = j.Tier.ToString().ToLowerInvariant(),
                                status = j.Status.ToString().ToLowerInvariant()
                            })
                        });
                    break;

                case JobStatus.Running:
                    response = new PendingResponse(
                        $"Running command {job.CurrentIndex + 1}/{job.Commands.Count}.",
                        pollIntervalSeconds: 1.0,
                        data: new
                        {
                            ticket = job.Ticket,
                            status = "running",
                            progress = $"{job.CurrentIndex + 1}/{job.Commands.Count}",
                            agent = job.Agent,
                            label = job.Label
                        });
                    break;

                case JobStatus.Done:
                    response = new SuccessResponse(
                        $"Batch complete. {job.Results.Count} results.",
                        new
                        {
                            ticket = job.Ticket,
                            status = "done",
                            results = job.Results,
                            agent = job.Agent,
                            label = job.Label,
                            atomic = job.Atomic
                        });
                    break;

                case JobStatus.Failed:
                    response = new ErrorResponse(
                        job.Error ?? "Batch failed.",
                        new
                        {
                            ticket = job.Ticket,
                            status = "failed",
                            results = job.Results,
                            error = job.Error,
                            failed_at_command = job.CurrentIndex,
                            atomic = job.Atomic,
                            rolled_back = job.Atomic
                        });
                    break;

                case JobStatus.Cancelled:
                    response = new ErrorResponse("Job was cancelled.", new
                    {
                        ticket = job.Ticket,
                        status = "cancelled"
                    });
                    break;

                default:
                    return new ErrorResponse($"Unknown status: {job.Status}");
            }

            // Auto-cleanup: remove terminal jobs after building the response.
            // The agent has consumed the result — no need to keep it in the queue.
            if (job.Status == JobStatus.Done
                || job.Status == JobStatus.Failed
                || job.Status == JobStatus.Cancelled)
            {
                if (GatewayJobLogger.IsEnabled)
                    GatewayJobLogger.Log(job);

                CommandGatewayState.Queue.Remove(ticket);
            }

            return response;
        }
    }
}
