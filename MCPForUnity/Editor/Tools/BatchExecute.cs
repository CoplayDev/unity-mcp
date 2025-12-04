using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;

namespace MCPForUnity.Editor.Tools
{
    /// <summary>
    /// Executes multiple MCP commands within a single Unity-side handler. Commands are executed sequentially
    /// on the main thread to preserve determinism and Unity API safety.
    /// </summary>
    [McpForUnityTool("batch_execute", AutoRegister = false)]
    public static class BatchExecute
    {
        private const int MaxCommandsPerBatch = 25;

        public static async Task<object> HandleCommand(JObject @params)
        {
            if (@params == null)
            {
                return new ErrorResponse("'commands' payload is required.");
            }

            var commandsToken = @params["commands"] as JArray;
            if (commandsToken == null || commandsToken.Count == 0)
            {
                return new ErrorResponse("Provide at least one command entry in 'commands'.");
            }

            if (commandsToken.Count > MaxCommandsPerBatch)
            {
                return new ErrorResponse($"A maximum of {MaxCommandsPerBatch} commands are allowed per batch.");
            }

            bool failFast = @params.Value<bool?>("failFast") ?? false;
            bool parallelRequested = @params.Value<bool?>("parallel") ?? false;
            int? maxParallel = @params.Value<int?>("maxParallelism");

            if (parallelRequested)
            {
                McpLog.Warn("batch_execute parallel mode requested, but commands will run sequentially on the main thread for safety.");
            }

            var commandResults = new List<object>(commandsToken.Count);
            int successCount = 0;
            int failureCount = 0;

            foreach (var token in commandsToken)
            {
                if (token is not JObject commandObj)
                {
                    failureCount++;
                    commandResults.Add(new
                    {
                        success = false,
                        tool = (string)null,
                        error = "Command entries must be JSON objects."
                    });
                    if (failFast)
                    {
                        break;
                    }
                    continue;
                }

                string toolName = commandObj["tool"]?.ToString();
                var commandParams = commandObj["params"] as JObject ?? new JObject();

                if (string.IsNullOrWhiteSpace(toolName))
                {
                    failureCount++;
                    commandResults.Add(new
                    {
                        success = false,
                        tool = toolName,
                        error = "Each command must include a non-empty 'tool' field."
                    });
                    if (failFast)
                    {
                        break;
                    }
                    continue;
                }

                try
                {
                    var result = await CommandRegistry.InvokeCommandAsync(toolName, commandParams).ConfigureAwait(true);
                    successCount++;
                    commandResults.Add(new
                    {
                        success = true,
                        tool = toolName,
                        result
                    });
                }
                catch (Exception ex)
                {
                    failureCount++;
                    commandResults.Add(new
                    {
                        success = false,
                        tool = toolName,
                        error = ex.Message
                    });

                    if (failFast)
                    {
                        break;
                    }
                }
            }

            bool overallSuccess = failureCount == 0;
            var data = new
            {
                results = commandResults,
                successCount,
                failureCount,
                parallelRequested,
                parallelApplied = false,
                maxParallelism = maxParallel
            };

            return overallSuccess
                ? new SuccessResponse("Batch execution completed.", data)
                : new ErrorResponse("One or more commands failed.", data);
        }
    }
}
