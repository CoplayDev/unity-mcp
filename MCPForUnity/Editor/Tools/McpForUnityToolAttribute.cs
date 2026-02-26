using System;

namespace MCPForUnity.Editor.Tools
{
    /// <summary>
    /// Marks a class as an MCP tool handler
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public class McpForUnityToolAttribute : Attribute
    {
        /// <summary>
        /// Tool name (if null, derived from class name)
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Tool description for LLM
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// Whether this tool returns structured output
        /// </summary>
        public bool StructuredOutput { get; set; } = true;

        /// <summary>
        /// Controls whether this tool is automatically registered with FastMCP.
        /// Defaults to true so most tools opt-in automatically. Set to false
        /// for legacy/built-in tools that already exist server-side.
        /// </summary>
        public bool AutoRegister { get; set; } = true;

        /// <summary>
        /// Enables the polling middleware for long-running tools. When true, Unity
        /// should return a PendingResponse and the Python side will poll using
        /// <see cref="PollAction"/> until completion.
        /// </summary>
        public bool RequiresPolling { get; set; } = false;

        /// <summary>
        /// The action name to use when polling for status. Defaults to "status".
        /// </summary>
        public string PollAction { get; set; } = "status";

        /// <summary>
        /// Execution tier for queue dispatch. Default: Smooth (safe conservative default).
        /// Instant = read-only, inline. Smooth = non-blocking write. Heavy = exclusive access.
        /// </summary>
        public ExecutionTier Tier { get; set; } = ExecutionTier.Smooth;

        /// <summary>
        /// The command name used to route requests to this tool.
        /// If not specified, defaults to the PascalCase class name converted to snake_case.
        /// Kept for backward compatibility.
        /// </summary>
        public string CommandName
        {
            get => Name;
            set => Name = value;
        }

        /// <summary>
        /// Create an MCP tool attribute with auto-generated command name.
        /// The command name will be derived from the class name (PascalCase → snake_case).
        /// Example: ManageAsset → manage_asset
        /// </summary>
        public McpForUnityToolAttribute()
        {
            Name = null; // Will be auto-generated
        }

        /// <summary>
        /// Create an MCP tool attribute with explicit command name.
        /// </summary>
        /// <param name="name">The command name (e.g., "manage_asset")</param>
        public McpForUnityToolAttribute(string name = null)
        {
            Name = name;
        }
    }

    /// <summary>
    /// Describes a tool parameter
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
    public class ToolParameterAttribute : Attribute
    {
        /// <summary>
        /// Parameter name (if null, derived from property/field name)
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Parameter description for LLM
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// Whether this parameter is required
        /// </summary>
        public bool Required { get; set; } = true;

        /// <summary>
        /// Default value (as string)
        /// </summary>
        public string DefaultValue { get; set; }

        public ToolParameterAttribute(string description)
        {
            Description = description;
        }
    }
}
