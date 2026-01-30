using System;
using System.Collections.Generic;
using System.Linq;
using MCPForUnity.Editor.Constants;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Services;
using UnityEditor;
using UnityEngine.UIElements;

namespace MCPForUnity.Editor.Windows.Components.Resources
{
    /// <summary>
    /// Controller for the Resources section inside the MCP For Unity editor window.
    /// Provides discovery, filtering, and per-resource enablement toggles.
    /// </summary>
    public class McpResourcesSection
    {
        private readonly Dictionary<string, Toggle> resourceToggleMap = new();
        private Label summaryLabel;
        private Label noteLabel;
        private Button enableAllButton;
        private Button disableAllButton;
        private Button rescanButton;
        private VisualElement categoryContainer;
        private List<ResourceMetadata> allResources = new();

        public VisualElement Root { get; }

        public McpResourcesSection(VisualElement root)
        {
            Root = root;
            CacheUIElements();
            RegisterCallbacks();
        }

        private void CacheUIElements()
        {
            summaryLabel = Root.Q<Label>("resources-summary");
            noteLabel = Root.Q<Label>("resources-note");
            enableAllButton = Root.Q<Button>("enable-all-resources-button");
            disableAllButton = Root.Q<Button>("disable-all-resources-button");
            rescanButton = Root.Q<Button>("rescan-resources-button");
            categoryContainer = Root.Q<VisualElement>("resource-category-container");
        }

        private void RegisterCallbacks()
        {
            if (enableAllButton != null)
            {
                enableAllButton.AddToClassList("tool-action-button");
                enableAllButton.style.marginRight = 4;
                enableAllButton.clicked += () => SetAllResourcesState(true);
            }

            if (disableAllButton != null)
            {
                disableAllButton.AddToClassList("tool-action-button");
                disableAllButton.style.marginRight = 4;
                disableAllButton.clicked += () => SetAllResourcesState(false);
            }

            if (rescanButton != null)
            {
                rescanButton.AddToClassList("tool-action-button");
                rescanButton.clicked += () =>
                {
                    McpLog.Info("Rescanning MCP resources from the editor window.");
                    MCPServiceLocator.ResourceDiscovery.InvalidateCache();
                    Refresh();
                };
            }
        }

        /// <summary>
        /// Rebuilds the resource list and synchronises toggle states.
        /// </summary>
        public void Refresh()
        {
            resourceToggleMap.Clear();
            categoryContainer?.Clear();

            var service = MCPServiceLocator.ResourceDiscovery;
            allResources = service.DiscoverAllResources()
                .OrderBy(r => r.IsBuiltIn ? 0 : 1)
                .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            bool hasResources = allResources.Count > 0;
            enableAllButton?.SetEnabled(hasResources);
            disableAllButton?.SetEnabled(hasResources);

            if (noteLabel != null)
            {
                noteLabel.style.display = hasResources ? DisplayStyle.Flex : DisplayStyle.None;
            }

            if (!hasResources)
            {
                AddInfoLabel("No MCP resources found. Add classes decorated with [McpForUnityResource] to expose resources.");
                UpdateSummary();
                return;
            }

            BuildCategory("Built-in Resources", "built-in", allResources.Where(r => r.IsBuiltIn));

            var customResources = allResources.Where(r => !r.IsBuiltIn).ToList();
            if (customResources.Count > 0)
            {
                BuildCategory("Custom Resources", "custom", customResources);
            }
            else
            {
                AddInfoLabel("No custom resources detected in loaded assemblies.");
            }

            UpdateSummary();
        }

        private void BuildCategory(string title, string prefsSuffix, IEnumerable<ResourceMetadata> resources)
        {
            var resourceList = resources.ToList();
            if (resourceList.Count == 0)
            {
                return;
            }

            var foldout = new Foldout
            {
                text = $"{title} ({resourceList.Count})",
                value = EditorPrefs.GetBool(EditorPrefKeys.ResourceFoldoutStatePrefix + prefsSuffix, true)
            };

            foldout.RegisterValueChangedCallback(evt =>
            {
                EditorPrefs.SetBool(EditorPrefKeys.ResourceFoldoutStatePrefix + prefsSuffix, evt.newValue);
            });

            foreach (var resource in resourceList)
            {
                foldout.Add(CreateResourceRow(resource));
            }

            categoryContainer?.Add(foldout);
        }

        private VisualElement CreateResourceRow(ResourceMetadata resource)
        {
            var row = new VisualElement();
            row.AddToClassList("tool-item");

            var header = new VisualElement();
            header.AddToClassList("tool-item-header");

            var toggle = new Toggle(resource.Name)
            {
                value = MCPServiceLocator.ResourceDiscovery.IsResourceEnabled(resource.Name)
            };
            toggle.AddToClassList("tool-item-toggle");
            toggle.tooltip = string.IsNullOrWhiteSpace(resource.Description) ? resource.Name : resource.Description;

            toggle.RegisterValueChangedCallback(evt =>
            {
                HandleToggleChange(resource, evt.newValue);
            });

            resourceToggleMap[resource.Name] = toggle;
            header.Add(toggle);

            var tagsContainer = new VisualElement();
            tagsContainer.AddToClassList("tool-tags");

            tagsContainer.Add(CreateTag(resource.IsBuiltIn ? "Built-in" : "Custom"));

            header.Add(tagsContainer);
            row.Add(header);

            if (!string.IsNullOrWhiteSpace(resource.Description) && !resource.Description.StartsWith("Resource:", StringComparison.Ordinal))
            {
                var description = new Label(resource.Description);
                description.AddToClassList("tool-item-description");
                row.Add(description);
            }

            return row;
        }

        private void HandleToggleChange(ResourceMetadata resource, bool enabled, bool updateSummary = true)
        {
            MCPServiceLocator.ResourceDiscovery.SetResourceEnabled(resource.Name, enabled);

            if (updateSummary)
            {
                UpdateSummary();
            }
        }

        private void SetAllResourcesState(bool enabled)
        {
            foreach (var resource in allResources)
            {
                if (!resourceToggleMap.TryGetValue(resource.Name, out var toggle))
                {
                    MCPServiceLocator.ResourceDiscovery.SetResourceEnabled(resource.Name, enabled);
                    continue;
                }

                if (toggle.value == enabled)
                {
                    continue;
                }

                toggle.SetValueWithoutNotify(enabled);
                HandleToggleChange(resource, enabled, updateSummary: false);
            }

            UpdateSummary();
        }

        private void UpdateSummary()
        {
            if (summaryLabel == null)
            {
                return;
            }

            if (allResources.Count == 0)
            {
                summaryLabel.text = "No MCP resources discovered.";
                return;
            }

            int enabledCount = allResources.Count(r => MCPServiceLocator.ResourceDiscovery.IsResourceEnabled(r.Name));
            summaryLabel.text = $"{enabledCount} of {allResources.Count} resources enabled.";
        }

        private void AddInfoLabel(string message)
        {
            var label = new Label(message);
            label.AddToClassList("help-text");
            categoryContainer?.Add(label);
        }

        private static Label CreateTag(string text)
        {
            var tag = new Label(text);
            tag.AddToClassList("tool-tag");
            return tag;
        }
    }
}
