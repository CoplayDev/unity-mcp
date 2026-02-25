using Newtonsoft.Json.Linq;
using MCPForUnity.Editor.Helpers;

namespace MCPForUnity.Editor.Tools
{
    /// <summary>
    /// Get overall command queue status. No ticket needed.
    /// Shows queue depth, active heavy job, smooth in-flight count, per-agent stats.
    /// </summary>
    [McpForUnityTool("queue_status", Tier = ExecutionTier.Instant)]
    public static class QueueStatus
    {
        public static object HandleCommand(JObject @params)
        {
            var status = CommandGatewayState.Queue.GetStatus();
            return new SuccessResponse("Queue status.", status);
        }
    }
}
