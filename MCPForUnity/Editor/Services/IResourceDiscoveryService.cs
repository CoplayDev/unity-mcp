using System.Collections.Generic;

namespace MCPForUnity.Editor.Services
{
    /// <summary>
    /// Metadata for a discovered resource
    /// </summary>
    public class ResourceMetadata
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public string ClassName { get; set; }
        public string Namespace { get; set; }
        public string AssemblyName { get; set; }
        public bool IsBuiltIn { get; set; }
    }

    /// <summary>
    /// Service for discovering MCP resources via reflection
    /// </summary>
    public interface IResourceDiscoveryService
    {
        /// <summary>
        /// Discovers all resources marked with [McpForUnityResource]
        /// </summary>
        List<ResourceMetadata> DiscoverAllResources();

        /// <summary>
        /// Gets metadata for a specific resource
        /// </summary>
        ResourceMetadata GetResourceMetadata(string resourceName);

        /// <summary>
        /// Returns only the resources currently enabled
        /// </summary>
        List<ResourceMetadata> GetEnabledResources();

        /// <summary>
        /// Checks whether a resource is currently enabled
        /// </summary>
        bool IsResourceEnabled(string resourceName);

        /// <summary>
        /// Updates the enabled state for a resource
        /// </summary>
        void SetResourceEnabled(string resourceName, bool enabled);

        /// <summary>
        /// Invalidates the resource discovery cache
        /// </summary>
        void InvalidateCache();
    }
}
