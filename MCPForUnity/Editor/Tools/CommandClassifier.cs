using Newtonsoft.Json.Linq;

namespace MCPForUnity.Editor.Tools
{
    /// <summary>
    /// Refines a tool's ExecutionTier based on action-level parameters.
    /// Tools declare a base tier via [McpForUnityTool(Tier=...)]; this classifier
    /// can promote or demote based on specific action strings or param values.
    /// </summary>
    public static class CommandClassifier
    {
        /// <summary>
        /// Classify a single command. Returns the effective tier after action-level overrides.
        /// </summary>
        public static ExecutionTier Classify(string toolName, ExecutionTier attributeTier, JObject @params)
        {
            if (@params == null) return attributeTier;

            string action = @params.Value<string>("action");

            return toolName switch
            {
                "manage_scene" => ClassifyManageScene(action, attributeTier),
                "refresh_unity" => ClassifyRefreshUnity(@params, attributeTier),
                "manage_editor" => ClassifyManageEditor(action, attributeTier),
                _ => attributeTier
            };
        }

        /// <summary>
        /// Classify a batch of commands. Returns the highest (most restrictive) tier.
        /// </summary>
        public static ExecutionTier ClassifyBatch(
            (string toolName, ExecutionTier attributeTier, JObject @params)[] commands)
        {
            var max = ExecutionTier.Instant;
            foreach (var (toolName, attributeTier, @params) in commands)
            {
                var tier = Classify(toolName, attributeTier, @params);
                if (tier > max) max = tier;
            }
            return max;
        }

        static ExecutionTier ClassifyManageScene(string action, ExecutionTier fallback)
        {
            return action switch
            {
                "get_hierarchy" or "get_active" or "get_build_settings" or "screenshot"
                    => ExecutionTier.Instant,
                "create" or "load" or "save"
                    => ExecutionTier.Heavy,
                _ => fallback
            };
        }

        static ExecutionTier ClassifyRefreshUnity(JObject @params, ExecutionTier fallback)
        {
            string compile = @params.Value<string>("compile");
            if (compile == "none") return ExecutionTier.Smooth;
            return fallback; // Heavy by default
        }

        static ExecutionTier ClassifyManageEditor(string action, ExecutionTier fallback)
        {
            return action switch
            {
                "telemetry_status" or "telemetry_ping" => ExecutionTier.Instant,
                "play" or "pause" or "stop" => ExecutionTier.Heavy,
                _ => fallback
            };
        }
    }
}
