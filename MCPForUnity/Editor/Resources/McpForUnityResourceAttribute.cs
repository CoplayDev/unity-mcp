using System;

namespace MCPForUnity.Editor.Resources
{
    /// <summary>
    /// Marks a class as an MCP resource handler for auto-discovery.
    /// The class must have a public static HandleCommand(JObject) method.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public class McpForUnityResourceAttribute : Attribute
    {
        /// <summary>
        /// The resource name used to route requests to this resource.
        /// If not specified, defaults to the PascalCase class name converted to snake_case.
        /// </summary>
        public string ResourceName { get; }

        /// <summary>
        /// Human-readable description of what this resource provides.
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// Create an MCP resource attribute with auto-generated resource name.
        /// The resource name will be derived from the class name (PascalCase → snake_case).
        /// Example: ManageAsset → manage_asset
        /// </summary>
        public McpForUnityResourceAttribute()
        {
            ResourceName = null; // Will be auto-generated
        }

        /// <summary>
        /// Create an MCP resource attribute with explicit resource name.
        /// </summary>
        /// <param name="resourceName">The resource name (e.g., "manage_asset")</param>
        public McpForUnityResourceAttribute(string resourceName)
        {
            ResourceName = resourceName;
        }
    }
}
