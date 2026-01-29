using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Resources;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MCPForUnity.Editor.Tools
{
    /// <summary>
    /// Holds information about a registered command handler.
    /// </summary>
    class HandlerInfo
    {
        public string CommandName { get; }
        public Func<JObject, object> SyncHandler { get; }
        public Func<JObject, Task<object>> AsyncHandler { get; }

        public bool IsAsync => AsyncHandler != null;

        public HandlerInfo(string commandName, Func<JObject, object> syncHandler, Func<JObject, Task<object>> asyncHandler)
        {
            CommandName = commandName;
            SyncHandler = syncHandler;
            AsyncHandler = asyncHandler;
        }
    }

    /// <summary>
    /// Registry for all MCP command handlers via reflection.
    /// Handles both MCP tools and resources.
    /// </summary>
    public static class CommandRegistry
    {
        private static readonly Dictionary<string, HandlerInfo> _handlers = new();
        private static bool _initialized = false;

        /// <summary>
        /// Initialize and auto-discover all tools and resources marked with
        /// [McpForUnityTool] or [McpForUnityResource]
        /// </summary>
        public static void Initialize()
        {
            if (_initialized) return;

            AutoDiscoverCommands();
            _initialized = true;
        }

        private static string ToSnakeCase(string name) => StringCaseUtility.ToSnakeCase(name);

        /// <summary>
        /// Auto-discover all types with [McpForUnityTool] or [McpForUnityResource] attributes
        /// </summary>
        private static void AutoDiscoverCommands()
        {
            try
            {
                var allTypes = AppDomain.CurrentDomain.GetAssemblies()
                    .Where(a => !a.IsDynamic)
                    .SelectMany(a =>
                    {
                        try { return a.GetTypes(); }
                        catch { return new Type[0]; }
                    })
                    .ToList();

                // Discover tools
                var toolTypes = allTypes.Where(t => t.GetCustomAttribute<McpForUnityToolAttribute>() != null);
                int toolCount = 0;
                foreach (var type in toolTypes)
                {
                    if (RegisterCommandType(type, isResource: false))
                        toolCount++;
                }

                // Discover resources
                var resourceTypes = allTypes.Where(t => t.GetCustomAttribute<McpForUnityResourceAttribute>() != null);
                int resourceCount = 0;
                foreach (var type in resourceTypes)
                {
                    if (RegisterCommandType(type, isResource: true))
                        resourceCount++;
                }

                McpLog.Info($"Auto-discovered {toolCount} tools and {resourceCount} resources ({_handlers.Count} total handlers)", false);
            }
            catch (Exception ex)
            {
                McpLog.Error($"Failed to auto-discover MCP commands: {ex.Message}");
            }
        }

        /// <summary>
        /// Register a command type (tool or resource) with the registry.
        /// Returns true if successfully registered, false otherwise.
        /// </summary>
        private static bool RegisterCommandType(Type type, bool isResource)
        {
            string commandName;
            string typeLabel = isResource ? "resource" : "tool";

            // Get command name from appropriate attribute
            if (isResource)
            {
                var resourceAttr = type.GetCustomAttribute<McpForUnityResourceAttribute>();
                commandName = resourceAttr.ResourceName;
            }
            else
            {
                var toolAttr = type.GetCustomAttribute<McpForUnityToolAttribute>();
                commandName = toolAttr.CommandName;
            }

            // Auto-generate command name if not explicitly provided
            if (string.IsNullOrEmpty(commandName))
            {
                commandName = ToSnakeCase(type.Name);
            }

            // Check for duplicate command names
            if (_handlers.ContainsKey(commandName))
            {
                McpLog.Warn(
                    $"Duplicate command name '{commandName}' detected. " +
                    $"{typeLabel} {type.Name} will override previously registered handler."
                );
            }

            // Find HandleCommand method
            var method = type.GetMethod(
                "HandleCommand",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[] { typeof(JObject) },
                null
            );

            if (method == null)
            {
                McpLog.Warn(
                    $"MCP {typeLabel} {type.Name} is marked with [McpForUnity{(isResource ? "Resource" : "Tool")}] " +
                    $"but has no public static HandleCommand(JObject) method"
                );
                return false;
            }

            try
            {
                HandlerInfo handlerInfo;

                if (typeof(Task).IsAssignableFrom(method.ReturnType))
                {
                    var asyncHandler = CreateAsyncHandlerDelegate(method, commandName);
                    handlerInfo = new HandlerInfo(commandName, null, asyncHandler);
                }
                else
                {
                    var handler = (Func<JObject, object>)Delegate.CreateDelegate(
                        typeof(Func<JObject, object>),
                        method
                    );
                    handlerInfo = new HandlerInfo(commandName, handler, null);
                }

                _handlers[commandName] = handlerInfo;
                return true;
            }
            catch (Exception ex)
            {
                McpLog.Error($"Failed to register {typeLabel} {type.Name}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Get a command handler by name
        /// </summary>
        private static HandlerInfo GetHandlerInfo(string commandName)
        {
            if (!_handlers.TryGetValue(commandName, out var handler))
            {
                throw new InvalidOperationException(
                    $"Unknown or unsupported command type: {commandName}"
                );
            }
            return handler;
        }

        /// <summary>
        /// Get a synchronous command handler by name.
        /// Throws if the command is asynchronous.
        /// </summary>
        /// <param name="commandName"></param>
        /// <returns></returns>
        /// <exception cref="InvalidOperationException"></exception>
        public static Func<JObject, object> GetHandler(string commandName)
        {
            var handlerInfo = GetHandlerInfo(commandName);
            if (handlerInfo.IsAsync)
            {
                throw new InvalidOperationException(
                    $"Command '{commandName}' is asynchronous and must be executed via ExecuteCommand"
                );
            }

            return handlerInfo.SyncHandler;
        }

        /// <summary>
        /// Execute a command handler, supporting both synchronous and asynchronous (coroutine) handlers.
        /// If the handler returns an IEnumerator, it will be executed as a coroutine.
        /// </summary>
        /// <param name="commandName">The command name to execute</param>
        /// <param name="params">Command parameters</param>
        /// <param name="tcs">TaskCompletionSource to complete when async operation finishes</param>
        /// <returns>The result for synchronous commands, or null for async commands (TCS will be completed later)</returns>
        public static object ExecuteCommand(string commandName, JObject @params, TaskCompletionSource<string> tcs)
        {
            var handlerInfo = GetHandlerInfo(commandName);

            if (handlerInfo.IsAsync)
            {
                ExecuteAsyncHandler(handlerInfo, @params, commandName, tcs);
                return null;
            }

            if (handlerInfo.SyncHandler == null)
            {
                throw new InvalidOperationException($"Handler for '{commandName}' does not provide a synchronous implementation");
            }

            return handlerInfo.SyncHandler(@params);
        }

        /// <summary>
        /// Execute a command handler and return its raw result, regardless of sync or async implementation.
        /// Used internally for features like batch execution where commands need to be composed.
        /// </summary>
        /// <param name="commandName">The registered command to execute.</param>
        /// <param name="params">Parameters to pass to the command (optional).</param>
        public static Task<object> InvokeCommandAsync(string commandName, JObject @params)
        {
            var handlerInfo = GetHandlerInfo(commandName);
            var payload = @params ?? new JObject();

            if (handlerInfo.IsAsync)
            {
                if (handlerInfo.AsyncHandler == null)
                {
                    throw new InvalidOperationException($"Async handler for '{commandName}' is not configured correctly");
                }

                return handlerInfo.AsyncHandler(payload);
            }

            if (handlerInfo.SyncHandler == null)
            {
                throw new InvalidOperationException($"Handler for '{commandName}' does not provide a synchronous implementation");
            }

            object result = handlerInfo.SyncHandler(payload);
            return Task.FromResult(result);
        }

        /// <summary>
        /// Create a delegate for an async handler method that returns Task or Task<T>.
        /// The delegate will invoke the method and await its completion, returning the result.
        /// </summary>
        /// <param name="method"></param>
        /// <param name="commandName"></param>
        /// <returns></returns>
        /// <exception cref="InvalidOperationException"></exception>
        private static Func<JObject, Task<object>> CreateAsyncHandlerDelegate(MethodInfo method, string commandName)
        {
            return async (JObject parameters) =>
            {
                object rawResult;

                try
                {
                    rawResult = method.Invoke(null, new object[] { parameters });
                }
                catch (TargetInvocationException ex)
                {
                    throw ex.InnerException ?? ex;
                }

                if (rawResult == null)
                {
                    return null;
                }

                if (rawResult is not Task task)
                {
                    throw new InvalidOperationException(
                        $"Async handler '{commandName}' returned an object that is not a Task"
                    );
                }

                await task.ConfigureAwait(true);

                var taskType = task.GetType();
                if (taskType.IsGenericType)
                {
                    var resultProperty = taskType.GetProperty("Result");
                    if (resultProperty != null)
                    {
                        return resultProperty.GetValue(task);
                    }
                }

                return null;
            };
        }

        private static void ExecuteAsyncHandler(
            HandlerInfo handlerInfo,
            JObject parameters,
            string commandName,
            TaskCompletionSource<string> tcs)
        {
            if (handlerInfo.AsyncHandler == null)
            {
                throw new InvalidOperationException($"Async handler for '{commandName}' is not configured correctly");
            }

            Task<object> handlerTask;

            try
            {
                handlerTask = handlerInfo.AsyncHandler(parameters);
            }
            catch (Exception ex)
            {
                ReportAsyncFailure(commandName, tcs, ex);
                return;
            }

            if (handlerTask == null)
            {
                CompleteAsyncCommand(commandName, tcs, null);
                return;
            }

            async void AwaitHandler()
            {
                try
                {
                    var finalResult = await handlerTask.ConfigureAwait(true);
                    CompleteAsyncCommand(commandName, tcs, finalResult);
                }
                catch (Exception ex)
                {
                    ReportAsyncFailure(commandName, tcs, ex);
                }
            }

            AwaitHandler();
        }

        /// <summary>
        /// Complete the TaskCompletionSource for an async command with a success result.
        /// </summary>
        /// <param name="commandName"></param>
        /// <param name="tcs"></param>
        /// <param name="result"></param>
        private static void CompleteAsyncCommand(string commandName, TaskCompletionSource<string> tcs, object result)
        {
            try
            {
                var response = new { status = "success", result };
                string json = JsonConvert.SerializeObject(response);

                if (!tcs.TrySetResult(json))
                {
                    McpLog.Warn($"TCS for async command '{commandName}' was already completed");
                }
            }
            catch (Exception ex)
            {
                McpLog.Error($"Error completing async command '{commandName}': {ex.Message}\n{ex.StackTrace}");
                ReportAsyncFailure(commandName, tcs, ex);
            }
        }

        /// <summary>
        /// Report an error that occurred during async command execution.
        /// Completes the TaskCompletionSource with an error response.
        /// </summary>
        /// <param name="commandName"></param>
        /// <param name="tcs"></param>
        /// <param name="ex"></param>
        private static void ReportAsyncFailure(string commandName, TaskCompletionSource<string> tcs, Exception ex)
        {
            McpLog.Error($"Error in async command '{commandName}': {ex.Message}\n{ex.StackTrace}");

            var errorResponse = new
            {
                status = "error",
                error = ex.Message,
                command = commandName,
                stackTrace = ex.StackTrace
            };

            string json;
            try
            {
                json = JsonConvert.SerializeObject(errorResponse);
            }
            catch (Exception serializationEx)
            {
                McpLog.Error($"Failed to serialize error response for '{commandName}': {serializationEx.Message}");
                json = "{\"status\":\"error\",\"error\":\"Failed to complete command\"}";
            }

            if (!tcs.TrySetResult(json))
            {
                McpLog.Warn($"TCS for async command '{commandName}' was already completed when trying to report error");
            }
        }
    }
}
