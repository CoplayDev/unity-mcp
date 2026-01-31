using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MCPForUnity.Editor.Constants;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Resources;
using UnityEditor;

namespace MCPForUnity.Editor.Services
{
    public class ResourceDiscoveryService : IResourceDiscoveryService
    {
        private Dictionary<string, ResourceMetadata> _cachedResources;

        public List<ResourceMetadata> DiscoverAllResources()
        {
            if (_cachedResources != null)
            {
                return _cachedResources.Values.ToList();
            }

            _cachedResources = new Dictionary<string, ResourceMetadata>();

            var resourceTypes = TypeCache.GetTypesWithAttribute<McpForUnityResourceAttribute>();
            foreach (var type in resourceTypes)
            {
                McpForUnityResourceAttribute resourceAttr;
                try
                {
                    resourceAttr = type.GetCustomAttribute<McpForUnityResourceAttribute>();
                }
                catch (Exception ex)
                {
                    McpLog.Warn($"Failed to read [McpForUnityResource] for {type.FullName}: {ex.Message}");
                    continue;
                }

                if (resourceAttr == null)
                {
                    continue;
                }

                var metadata = ExtractResourceMetadata(type, resourceAttr);
                if (metadata != null)
                {
                    if (_cachedResources.ContainsKey(metadata.Name))
                    {
                        McpLog.Warn($"Duplicate resource name '{metadata.Name}' from {type.FullName}; overwriting previous registration.");
                    }
                    _cachedResources[metadata.Name] = metadata;
                    EnsurePreferenceInitialized(metadata);
                }
            }

            McpLog.Info($"Discovered {_cachedResources.Count} MCP resources via reflection", false);
            return _cachedResources.Values.ToList();
        }

        public ResourceMetadata GetResourceMetadata(string resourceName)
        {
            if (string.IsNullOrEmpty(resourceName))
            {
                return null;
            }

            if (_cachedResources == null)
            {
                DiscoverAllResources();
            }

            return _cachedResources.TryGetValue(resourceName, out var metadata) ? metadata : null;
        }

        public List<ResourceMetadata> GetEnabledResources()
        {
            return DiscoverAllResources()
                .Where(r => IsResourceEnabled(r.Name))
                .ToList();
        }

        public bool IsResourceEnabled(string resourceName)
        {
            if (string.IsNullOrEmpty(resourceName))
            {
                return false;
            }

            string key = GetResourcePreferenceKey(resourceName);
            if (EditorPrefs.HasKey(key))
            {
                return EditorPrefs.GetBool(key, true);
            }

            // Default: all resources enabled
            return true;
        }

        public void SetResourceEnabled(string resourceName, bool enabled)
        {
            if (string.IsNullOrEmpty(resourceName))
            {
                return;
            }

            string key = GetResourcePreferenceKey(resourceName);
            EditorPrefs.SetBool(key, enabled);
        }

        public void InvalidateCache()
        {
            _cachedResources = null;
        }

        private ResourceMetadata ExtractResourceMetadata(Type type, McpForUnityResourceAttribute resourceAttr)
        {
            try
            {
                string resourceName = resourceAttr.ResourceName;
                if (string.IsNullOrEmpty(resourceName))
                {
                    resourceName = StringCaseUtility.ToSnakeCase(type.Name);
                }

                string description = resourceAttr.Description ?? $"Resource: {resourceName}";

                var metadata = new ResourceMetadata
                {
                    Name = resourceName,
                    Description = description,
                    ClassName = type.Name,
                    Namespace = type.Namespace ?? "",
                    AssemblyName = type.Assembly.GetName().Name
                };

                metadata.IsBuiltIn = StringCaseUtility.IsBuiltInMcpType(
                    type, metadata.AssemblyName, "MCPForUnity.Editor.Resources");

                return metadata;
            }
            catch (Exception ex)
            {
                McpLog.Error($"Failed to extract metadata for resource {type.Name}: {ex.Message}");
                return null;
            }
        }

        private void EnsurePreferenceInitialized(ResourceMetadata metadata)
        {
            if (metadata == null || string.IsNullOrEmpty(metadata.Name))
            {
                return;
            }

            string key = GetResourcePreferenceKey(metadata.Name);
            if (!EditorPrefs.HasKey(key))
            {
                EditorPrefs.SetBool(key, true);
            }
        }

        private static string GetResourcePreferenceKey(string resourceName)
        {
            return EditorPrefKeys.ResourceEnabledPrefix + resourceName;
        }
    }
}
