using System;
using System.Collections.Generic;
using System.Linq;
using MCPForUnity.Editor.Constants;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Services;
using MCPForUnity.Editor.Tools;
using UnityEditor;
using UnityEngine.UIElements;

namespace MCPForUnity.Editor.Windows.Components.Tools
{
    /// <summary>
    /// Controller for the Tools section inside the MCP For Unity editor window.
    /// Provides discovery, filtering, and per-tool enablement toggles.
    /// </summary>
    public class McpToolsSection
    {
        private readonly Dictionary<string, Toggle> toolToggleMap = new();
        private Toggle projectScopedToolsToggle;
        private Label summaryLabel;
        private Label noteLabel;
        private Button enableAllButton;
        private Button disableAllButton;
        private Button rescanButton;
        private VisualElement categoryContainer;
        private List<ToolMetadata> allTools = new();

        public VisualElement Root { get; }

        public McpToolsSection(VisualElement root)
        {
            Root = root;
            CacheUIElements();
            RegisterCallbacks();
        }

        private void CacheUIElements()
        {
            projectScopedToolsToggle = Root.Q<Toggle>("project-scoped-tools-toggle");
            summaryLabel = Root.Q<Label>("tools-summary");
            noteLabel = Root.Q<Label>("tools-note");
            enableAllButton = Root.Q<Button>("enable-all-button");
            disableAllButton = Root.Q<Button>("disable-all-button");
            rescanButton = Root.Q<Button>("rescan-button");
            categoryContainer = Root.Q<VisualElement>("tool-category-container");
        }

        private void RegisterCallbacks()
        {
            if (projectScopedToolsToggle != null)
            {
                projectScopedToolsToggle.value = EditorPrefs.GetBool(
                    EditorPrefKeys.ProjectScopedToolsLocalHttp,
                    false
                );
                projectScopedToolsToggle.tooltip = "When enabled, register project-scoped tools with HTTP Local transport. Allows per-project tool customization.";
                projectScopedToolsToggle.RegisterValueChangedCallback(evt =>
                {
                    EditorPrefs.SetBool(EditorPrefKeys.ProjectScopedToolsLocalHttp, evt.newValue);
                });
            }

            if (enableAllButton != null)
            {
                enableAllButton.AddToClassList("tool-action-button");
                enableAllButton.style.marginRight = 4;
                enableAllButton.clicked += () => SetAllToolsState(true);
            }

            if (disableAllButton != null)
            {
                disableAllButton.AddToClassList("tool-action-button");
                disableAllButton.style.marginRight = 4;
                disableAllButton.clicked += () => SetAllToolsState(false);
            }

            if (rescanButton != null)
            {
                rescanButton.AddToClassList("tool-action-button");
                rescanButton.clicked += () =>
                {
                    McpLog.Info("Rescanning MCP tools from the editor window.");
                    MCPServiceLocator.ToolDiscovery.InvalidateCache();
                    Refresh();
                };
            }
        }

        /// <summary>
        /// Rebuilds the tool list and synchronises toggle states.
        /// </summary>
        public void Refresh()
        {
            toolToggleMap.Clear();
            categoryContainer?.Clear();

            var service = MCPServiceLocator.ToolDiscovery;
            allTools = service.DiscoverAllTools()
                .OrderBy(tool => IsBuiltIn(tool) ? 0 : 1)
                .ThenBy(tool => tool.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            bool hasTools = allTools.Count > 0;
            enableAllButton?.SetEnabled(hasTools);
            disableAllButton?.SetEnabled(hasTools);

            if (noteLabel != null)
            {
                noteLabel.style.display = hasTools ? DisplayStyle.Flex : DisplayStyle.None;
            }

            if (!hasTools)
            {
                AddInfoLabel("No MCP tools found. Add classes decorated with [McpForUnityTool] to expose tools.");
                UpdateSummary();
                return;
            }

            BuildCategory("Built-in Tools", "built-in", allTools.Where(IsBuiltIn));

            var customTools = allTools.Where(tool => !IsBuiltIn(tool)).ToList();
            if (customTools.Count > 0)
            {
                BuildCategory("Custom Tools", "custom", customTools);
            }
            else
            {
                AddInfoLabel("No custom tools detected in loaded assemblies.");
            }

            UpdateSummary();
        }

        private void BuildCategory(string title, string prefsSuffix, IEnumerable<ToolMetadata> tools)
        {
            var toolList = tools.ToList();
            if (toolList.Count == 0)
            {
                return;
            }

            var foldout = new Foldout
            {
                text = $"{title} ({toolList.Count})",
                value = EditorPrefs.GetBool(EditorPrefKeys.ToolFoldoutStatePrefix + prefsSuffix, true)
            };

            foldout.RegisterValueChangedCallback(evt =>
            {
                EditorPrefs.SetBool(EditorPrefKeys.ToolFoldoutStatePrefix + prefsSuffix, evt.newValue);
            });

            foreach (var tool in toolList)
            {
                foldout.Add(CreateToolRow(tool));
            }

            categoryContainer?.Add(foldout);
        }

        private VisualElement CreateToolRow(ToolMetadata tool)
        {
            var row = new VisualElement();
            row.AddToClassList("tool-item");

            var header = new VisualElement();
            header.AddToClassList("tool-item-header");

            var toggle = new Toggle(tool.Name)
            {
                value = MCPServiceLocator.ToolDiscovery.IsToolEnabled(tool.Name)
            };
            toggle.AddToClassList("tool-item-toggle");
            toggle.tooltip = string.IsNullOrWhiteSpace(tool.Description) ? tool.Name : tool.Description;

            toggle.RegisterValueChangedCallback(evt =>
            {
                HandleToggleChange(tool, evt.newValue);
            });

            toolToggleMap[tool.Name] = toggle;
            header.Add(toggle);

            var tagsContainer = new VisualElement();
            tagsContainer.AddToClassList("tool-tags");

            bool defaultEnabled = tool.AutoRegister || tool.IsBuiltIn;
            tagsContainer.Add(CreateTag(defaultEnabled ? "On by default" : "Off by default"));

            tagsContainer.Add(CreateTag(tool.StructuredOutput ? "Structured output" : "Free-form"));

            if (tool.RequiresPolling)
            {
                tagsContainer.Add(CreateTag($"Polling: {tool.PollAction}"));
            }

            header.Add(tagsContainer);
            row.Add(header);

            if (!string.IsNullOrWhiteSpace(tool.Description))
            {
                var description = new Label(tool.Description);
                description.AddToClassList("tool-item-description");
                row.Add(description);
            }

            if (tool.Parameters != null && tool.Parameters.Count > 0)
            {
                var paramSummary = string.Join(", ", tool.Parameters.Select(p =>
                    $"{p.Name}{(p.Required ? string.Empty : " (optional)")}: {p.Type}"));

                var parametersLabel = new Label(paramSummary);
                parametersLabel.AddToClassList("tool-parameters");
                row.Add(parametersLabel);
            }

            if (IsManageSceneTool(tool))
            {
                row.Add(CreateManageSceneActions());
            }

            return row;
        }

        private void HandleToggleChange(ToolMetadata tool, bool enabled, bool updateSummary = true)
        {
            MCPServiceLocator.ToolDiscovery.SetToolEnabled(tool.Name, enabled);

            if (updateSummary)
            {
                UpdateSummary();
            }
        }

        private void SetAllToolsState(bool enabled)
        {
            foreach (var tool in allTools)
            {
                if (!toolToggleMap.TryGetValue(tool.Name, out var toggle))
                {
                    MCPServiceLocator.ToolDiscovery.SetToolEnabled(tool.Name, enabled);
                    continue;
                }

                if (toggle.value == enabled)
                {
                    continue;
                }

                toggle.SetValueWithoutNotify(enabled);
                HandleToggleChange(tool, enabled, updateSummary: false);
            }

            UpdateSummary();
        }

        private void UpdateSummary()
        {
            if (summaryLabel == null)
            {
                return;
            }

            if (allTools.Count == 0)
            {
                summaryLabel.text = "No MCP tools discovered.";
                return;
            }

            int enabledCount = allTools.Count(tool => MCPServiceLocator.ToolDiscovery.IsToolEnabled(tool.Name));
            summaryLabel.text = $"{enabledCount} of {allTools.Count} tools will register with connected clients.";
        }

        private void AddInfoLabel(string message)
        {
            var label = new Label(message);
            label.AddToClassList("help-text");
            categoryContainer?.Add(label);
        }

        private VisualElement CreateManageSceneActions()
        {
            var actions = new VisualElement();
            actions.AddToClassList("tool-item-actions");

            var screenshotButton = new Button(OnManageSceneScreenshotClicked)
            {
                text = "Capture Screenshot"
            };
            screenshotButton.AddToClassList("tool-action-button");
            screenshotButton.style.marginTop = 4;
            screenshotButton.tooltip = "Capture a screenshot to Assets/Screenshots via manage_scene.";

            actions.Add(screenshotButton);
            return actions;
        }

        private void OnManageSceneScreenshotClicked()
        {
            try
            {
                var response = ManageScene.ExecuteScreenshot();
                if (response is SuccessResponse success && !string.IsNullOrWhiteSpace(success.Message))
                {
                    McpLog.Info(success.Message);
                }
                else if (response is ErrorResponse error && !string.IsNullOrWhiteSpace(error.Error))
                {
                    McpLog.Error(error.Error);
                }
                else
                {
                    McpLog.Info("Screenshot capture requested.");
                }
            }
            catch (Exception ex)
            {
                McpLog.Error($"Failed to capture screenshot: {ex.Message}");
            }
        }

        private static Label CreateTag(string text)
        {
            var tag = new Label(text);
            tag.AddToClassList("tool-tag");
            return tag;
        }

        private static bool IsManageSceneTool(ToolMetadata tool) => string.Equals(tool?.Name, "manage_scene", StringComparison.OrdinalIgnoreCase);

        private static bool IsBuiltIn(ToolMetadata tool) => tool?.IsBuiltIn ?? false;
    }
}
