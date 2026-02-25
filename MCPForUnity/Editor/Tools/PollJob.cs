using Newtonsoft.Json.Linq;
using MCPForUnity.Editor.Helpers;

namespace MCPForUnity.Editor.Tools
{
    /// <summary>
    /// Poll the status of an async batch job by ticket ID.
    /// Generalizes the get_test_job pattern to any queued batch.
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

            switch (job.Status)
            {
                case JobStatus.Queued:
                    var ahead = CommandGatewayState.Queue.GetAheadOf(ticket);
                    return new PendingResponse(
                        $"Queued at position {ahead.Count}.",
                        pollIntervalSeconds: 2.0,
                        data: new
                        {
                            ticket = job.Ticket,
                            status = "queued",
                            position = ahead.Count,
                            agent = job.Agent,
                            label = job.Label,
                            ahead = ahead.ConvertAll(j => (object)new
                            {
                                ticket = j.Ticket,
                                agent = j.Agent,
                                label = j.Label,
                                tier = j.Tier.ToString().ToLowerInvariant(),
                                status = j.Status.ToString().ToLowerInvariant()
                            })
                        });

                case JobStatus.Running:
                    return new PendingResponse(
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

                case JobStatus.Done:
                    return new SuccessResponse(
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

                case JobStatus.Failed:
                    return new ErrorResponse(
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

                case JobStatus.Cancelled:
                    return new ErrorResponse("Job was cancelled.", new
                    {
                        ticket = job.Ticket,
                        status = "cancelled"
                    });

                default:
                    return new ErrorResponse($"Unknown status: {job.Status}");
            }
        }
    }
}
