using Newtonsoft.Json.Linq;
using MCPForUnity.Editor.Helpers;

namespace MCPForUnity.Editor.Tools
{
    /// <summary>
    /// Compressed queue summary — returns all tickets and their status in one shot.
    /// Instant tier: never queued, executes inline for non-blocking batch status checks.
    /// Fields: t=ticket, s=status, a=agent, l=label, p=progress, b=blocked, e=error.
    /// </summary>
    [McpForUnityTool("queue_status", Tier = ExecutionTier.Instant)]
    public static class QueueStatus
    {
        public static object HandleCommand(JObject @params)
        {
            var summary = CommandGatewayState.Queue.GetSummary();
            return new SuccessResponse("Queue snapshot.", summary);
        }
    }
}
