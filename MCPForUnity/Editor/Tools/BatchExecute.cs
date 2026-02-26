using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MCPForUnity.Editor.Constants;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Services;
using Newtonsoft.Json.Linq;
using UnityEditor;

namespace MCPForUnity.Editor.Tools
{
    /// <summary>
    /// Executes multiple MCP commands within a single Unity-side handler. Commands are executed sequentially
    /// on the main thread to preserve determinism and Unity API safety.
    /// </summary>
    [McpForUnityTool("batch_execute", AutoRegister = false)]
    public static class BatchExecute
    {
        /// <summary>Default limit when no EditorPrefs override is set.</summary>
        internal const int DefaultMaxCommandsPerBatch = 25;

        /// <summary>Hard ceiling to prevent extreme editor freezes regardless of user setting.</summary>
        internal const int AbsoluteMaxCommandsPerBatch = 100;

        /// <summary>
        /// Returns the user-configured max commands per batch, clamped between 1 and <see cref="AbsoluteMaxCommandsPerBatch"/>.
        /// </summary>
        internal static int GetMaxCommandsPerBatch()
        {
            int configured = EditorPrefs.GetInt(EditorPrefKeys.BatchExecuteMaxCommands, DefaultMaxCommandsPerBatch);
            return Math.Clamp(configured, 1, AbsoluteMaxCommandsPerBatch);
        }

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

            int maxCommands = GetMaxCommandsPerBatch();
            if (commandsToken.Count > maxCommands)
            {
                return new ErrorResponse(
                    $"A maximum of {maxCommands} commands are allowed per batch (configurable in MCP Tools window, hard max {AbsoluteMaxCommandsPerBatch}).");
            }

            // --- Async gateway path ---
            bool isAsync = @params.Value<bool?>("async") ?? false;
            if (isAsync)
            {
                return HandleAsyncSubmit(@params, commandsToken);
            }

            // --- Legacy synchronous path (unchanged) ---
            bool failFast = @params.Value<bool?>("failFast") ?? false;
            bool parallelRequested = @params.Value<bool?>("parallel") ?? false;
            int? maxParallel = @params.Value<int?>("maxParallelism");

            if (parallelRequested)
            {
                McpLog.Warn("batch_execute parallel mode requested, but commands will run sequentially on the main thread for safety.");
            }

            var commandResults = new List<object>(commandsToken.Count);
            int invocationSuccessCount = 0;
            int invocationFailureCount = 0;
            bool anyCommandFailed = false;

            foreach (var token in commandsToken)
            {
                if (token is not JObject commandObj)
                {
                    invocationFailureCount++;
                    anyCommandFailed = true;
                    commandResults.Add(new
                    {
                        tool = (string)null,
                        callSucceeded = false,
                        error = "Command entries must be JSON objects."
                    });
                    if (failFast)
                    {
                        break;
                    }
                    continue;
                }

                string toolName = commandObj["tool"]?.ToString();
                var rawParams = commandObj["params"] as JObject ?? new JObject();
                var commandParams = NormalizeParameterKeys(rawParams);

                if (string.IsNullOrWhiteSpace(toolName))
                {
                    invocationFailureCount++;
                    anyCommandFailed = true;
                    commandResults.Add(new
                    {
                        tool = toolName,
                        callSucceeded = false,
                        error = "Each command must include a non-empty 'tool' field."
                    });
                    if (failFast)
                    {
                        break;
                    }
                    continue;
                }

                // Block disabled tools (mirrors TransportCommandDispatcher check)
                var toolMeta = MCPServiceLocator.ToolDiscovery.GetToolMetadata(toolName);
                if (toolMeta != null && !MCPServiceLocator.ToolDiscovery.IsToolEnabled(toolName))
                {
                    invocationFailureCount++;
                    anyCommandFailed = true;
                    commandResults.Add(new
                    {
                        tool = toolName,
                        callSucceeded = false,
                        result = new ErrorResponse($"Tool '{toolName}' is disabled in the Unity Editor.")
                    });
                    if (failFast) break;
                    continue;
                }

                try
                {
                    var result = await CommandRegistry.InvokeCommandAsync(toolName, commandParams).ConfigureAwait(true);
                    bool callSucceeded = DetermineCallSucceeded(result);
                    if (callSucceeded)
                    {
                        invocationSuccessCount++;
                    }
                    else
                    {
                        invocationFailureCount++;
                        anyCommandFailed = true;
                    }

                    commandResults.Add(new
                    {
                        tool = toolName,
                        callSucceeded,
                        result
                    });

                    if (!callSucceeded && failFast)
                    {
                        break;
                    }
                }
                catch (Exception ex)
                {
                    invocationFailureCount++;
                    anyCommandFailed = true;
                    commandResults.Add(new
                    {
                        tool = toolName,
                        callSucceeded = false,
                        error = ex.Message
                    });

                    if (failFast)
                    {
                        break;
                    }
                }
            }

            bool overallSuccess = !anyCommandFailed;
            var data = new
            {
                results = commandResults,
                callSuccessCount = invocationSuccessCount,
                callFailureCount = invocationFailureCount,
                parallelRequested,
                parallelApplied = false,
                maxParallelism = maxParallel
            };

            return overallSuccess
                ? new SuccessResponse("Batch execution completed.", data)
                : new ErrorResponse("One or more commands failed.", data);
        }

        private static bool DetermineCallSucceeded(object result)
        {
            if (result == null)
            {
                return true;
            }

            if (result is IMcpResponse response)
            {
                return response.Success;
            }

            if (result is JObject obj)
            {
                var successToken = obj["success"];
                if (successToken != null && successToken.Type == JTokenType.Boolean)
                {
                    return successToken.Value<bool>();
                }
            }

            if (result is JToken token)
            {
                var successToken = token["success"];
                if (successToken != null && successToken.Type == JTokenType.Boolean)
                {
                    return successToken.Value<bool>();
                }
            }

            return true;
        }

        private static JObject NormalizeParameterKeys(JObject source)
        {
            if (source == null)
            {
                return new JObject();
            }

            var normalized = new JObject();
            foreach (var property in source.Properties())
            {
                string normalizedName = ToCamelCase(property.Name);
                normalized[normalizedName] = property.Value;
            }
            return normalized;
        }

        private static string ToCamelCase(string key) => StringCaseUtility.ToCamelCase(key);

        /// <summary>
        /// Handle async batch submission. Queues commands via CommandGateway and returns
        /// a ticket (for non-instant batches) or results inline (for instant batches).
        /// </summary>
        private static object HandleAsyncSubmit(JObject @params, JArray commandsToken)
        {
            bool atomic = @params.Value<bool?>("atomic") ?? false;
            string agent = @params.Value<string>("agent") ?? "anonymous";
            string label = @params.Value<string>("label") ?? "";

            var commands = new List<BatchCommand>();
            foreach (var token in commandsToken)
            {
                if (token is not JObject cmdObj) continue;
                string toolName = cmdObj["tool"]?.ToString();
                if (string.IsNullOrWhiteSpace(toolName)) continue;

                var rawParams = cmdObj["params"] as JObject ?? new JObject();
                var cmdParams = NormalizeParameterKeys(rawParams);

                var toolTier = CommandRegistry.GetToolTier(toolName);
                var effectiveTier = CommandClassifier.Classify(toolName, toolTier, cmdParams);

                commands.Add(new BatchCommand { Tool = toolName, Params = cmdParams, Tier = effectiveTier, CausesDomainReload = CommandClassifier.CausesDomainReload(toolName, cmdParams) });
            }

            if (commands.Count == 0)
            {
                return new ErrorResponse("No valid commands in async batch.");
            }

            var job = CommandGatewayState.Queue.Submit(agent, label, atomic, commands);

            if (job.Tier == ExecutionTier.Instant)
            {
                // Execute inline, return results directly
                foreach (var cmd in commands)
                {
                    try
                    {
                        var result = CommandRegistry.InvokeCommandAsync(cmd.Tool, cmd.Params)
                            .ConfigureAwait(true).GetAwaiter().GetResult();
                        job.Results.Add(result);
                    }
                    catch (Exception ex)
                    {
                        job.Results.Add(new ErrorResponse(ex.Message));
                        if (atomic)
                        {
                            job.Status = JobStatus.Failed;
                            job.Error = ex.Message;
                            job.CompletedAt = DateTime.UtcNow;
                            return new ErrorResponse($"Instant batch failed at command '{cmd.Tool}': {ex.Message}",
                                new { ticket = job.Ticket, results = job.Results });
                        }
                    }
                }
                job.Status = JobStatus.Done;
                job.CompletedAt = DateTime.UtcNow;
                return new SuccessResponse("Batch completed (instant).",
                    new { ticket = job.Ticket, results = job.Results });
            }

            // Non-instant: return ticket for polling
            return new PendingResponse(
                $"Batch queued as {job.Ticket}. Poll with poll_job.",
                pollIntervalSeconds: 2.0,
                data: new
                {
                    ticket = job.Ticket,
                    status = job.Status.ToString().ToLowerInvariant(),
                    position = CommandGatewayState.Queue.GetAheadOf(job.Ticket).Count,
                    tier = job.Tier.ToString().ToLowerInvariant(),
                    agent,
                    label,
                    atomic
                });
        }
    }
}
