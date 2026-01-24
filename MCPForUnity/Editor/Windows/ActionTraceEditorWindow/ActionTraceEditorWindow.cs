using System;
using System.Collections.Generic;
using System.Linq;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.ActionTrace.Core.Store;
using MCPForUnity.Editor.ActionTrace.Core.Settings;
using MCPForUnity.Editor.ActionTrace.Context;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using MCPForUnity.Editor.ActionTrace.Analysis.Query;

namespace MCPForUnity.Editor.Windows.ActionTraceEditorWindow
{
    public enum SortMode
    {
        ByTimeDesc,
        AIFiltered
    }

    /// <summary>
    /// Main editor window for ActionTrace event viewer.
    /// Follows project Section pattern similar to MCPForUnityEditorWindow.
    /// </summary>
    public sealed class ActionTraceEditorWindow : EditorWindow
    {
        #region Constants

        private const string UxmlName = "ActionTraceEditorWindow";
        private const double RefreshInterval = 1.0;

        private static class UINames
        {
            public const string EventCountBadge = "event-count-badge";
            public const string SearchField = "search-field";
            public const string FilterMenu = "filter-menu";
            public const string SortMenu = "sort-menu";
            public const string ImportanceToggle = "importance-toggle";
            public const string SettingsButton = "settings-button";
            public const string RefreshButton = "refresh-button";
            public const string ClearButton = "clear-button";
            public const string FilterSummaryBar = "filter-summary-bar";
            public const string FilterSummaryText = "filter-summary-text";
            public const string ClearFiltersButton = "clear-filters-button";
            public const string EventList = "event-list";
            public const string EventListCount = "event-list-count";
            public const string EmptyState = "empty-state";
            public const string NoResultsState = "no-results-state";
            public const string NoResultsFilters = "no-results-filters";
            public const string DetailScrollView = "detail-scroll-view";
            public const string DetailPlaceholder = "detail-placeholder";
            public const string DetailContent = "detail-content";
            public const string DetailActions = "detail-actions";
            public const string CopySummaryButton = "copy-summary-button";
            public const string CountLabel = "count-label";
            public const string StatusLabel = "status-label";
            public const string ModeLabel = "mode-label";
            public const string RefreshIndicator = "refresh-indicator";
        }

        #endregion

        #region Sections

        private EventListSection _eventListSection;
        private FilterSection _filterSection;
        private DetailSection _detailSection;

        #endregion

        #region Shared Resources

        private ActionTraceCache _cache;
        private SortMode _sortMode = SortMode.ByTimeDesc;

        // Wrapper to allow FilterSection to update sortMode
        private SortMode SortModeValue
        {
            get => _sortMode;
            set => _sortMode = value;
        }

        #endregion

        #region State

        private bool? _previousBypassImportanceFilter;
        private double _lastRefreshTime;
        private bool _isScheduledRefreshActive;

        #endregion

        #region Window Management

        public static void ShowWindow()
        {
            var window = GetWindow<ActionTraceEditorWindow>("ActionTrace");
            window.minSize = new Vector2(1000, 650);
        }

        #endregion

        #region UI Setup

        private void CreateGUI()
        {
            var uxml = LoadUxmlAsset();
            if (uxml == null) return;

            uxml.CloneTree(rootVisualElement);
            if (rootVisualElement.childCount == 0)
            {
                McpLog.Error("ActionTraceEditorWindow: UXML loaded but rootVisualElement is empty.");
                return;
            }

            // Initialize shared resources
            _cache = new ActionTraceCache();

            // Create sections
            CreateSections();

            // Initialize Settings
            InitializeSettings();

            // Initial refresh
            _eventListSection.RefreshData();
            UpdateUI();
        }

        private VisualTreeAsset LoadUxmlAsset()
        {
            var guids = AssetDatabase.FindAssets($"{UxmlName} t:VisualTreeAsset");
            if (guids?.Length > 0)
            {
                var path = AssetDatabase.GUIDToAssetPath(guids[0]);
                var asset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(path);
                if (asset != null) return asset;
            }

            var basePath = AssetPathUtility.GetMcpPackageRootPath();
            if (!string.IsNullOrEmpty(basePath))
            {
                var expectedPath = $"{basePath}/Editor/Windows/{UxmlName}.uxml";
                var sanitized = AssetPathUtility.SanitizeAssetPath(expectedPath);
                var asset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(sanitized);
                if (asset != null) return asset;
            }

            McpLog.Error($"ActionTraceEditorWindow.uxml not found in project.");
            return null;
        }

        private void CreateSections()
        {
            _eventListSection = new EventListSection(rootVisualElement, _cache);
            _eventListSection.DataChanged += OnEventDataChanged;
            _eventListSection.SelectionChanged += OnSelectionChanged;

            _filterSection = new FilterSection(rootVisualElement, _cache, _eventListSection, () => SortModeValue, v => SortModeValue = v);
            _detailSection = new DetailSection(rootVisualElement, _cache);
        }

        private void InitializeSettings()
        {
            if (ActionTraceSettings.Instance != null)
            {
                _previousBypassImportanceFilter = ActionTraceSettings.Instance.Filtering.BypassImportanceFilter;
                ActionTraceSettings.Instance.Filtering.BypassImportanceFilter = true;
            }
        }

        #endregion

        #region Event Handlers

        private ActionTraceQuery.ActionTraceViewItem _lastSelectedItem;

        private void OnEventDataChanged()
        {
            // Refresh ListView to show new data
            _eventListSection.RefreshListView();
            UpdateUI();
        }

        private void OnSelectionChanged(ActionTraceQuery.ActionTraceViewItem item)
        {
            _lastSelectedItem = item;
            _detailSection.SetSelectedItem(item);
        }

        #endregion

        #region UI Updates

        private void UpdateUI()
        {
            _filterSection.UpdateUI(_eventListSection.CurrentEvents.Count);
            _detailSection.UpdateStatus(_eventListSection.CurrentEvents.Count, EventStore.Count, _sortMode);
        }

        #endregion

        #region Editor Lifecycle

        private void OnEnable()
        {
            EditorApplication.update += OnEditorUpdate;
            _isScheduledRefreshActive = true;
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
            _isScheduledRefreshActive = false;

            // Clean up sections
            _eventListSection?.UnregisterCallbacks();
            _cache?.ClearAll();

            // Restore Settings
            if (ActionTraceSettings.Instance != null && _previousBypassImportanceFilter.HasValue)
            {
                ActionTraceSettings.Instance.Filtering.BypassImportanceFilter = _previousBypassImportanceFilter.Value;
            }
        }

        private void OnEditorUpdate()
        {
            // Guard against null references
            if (_eventListSection == null)
                return;

            // Auto-refresh at interval
            if (_isScheduledRefreshActive &&
                EditorApplication.timeSinceStartup - _lastRefreshTime > RefreshInterval)
            {
                _lastRefreshTime = EditorApplication.timeSinceStartup;
                _eventListSection.RefreshData();
                UpdateUI();
            }
        }

        #endregion

        #region Filter Section

        /// <summary>
        /// Internal section for filtering controls (search, filter menu, sort menu).
        /// </summary>
        private sealed class FilterSection
        {
            private readonly VisualElement _root;
            private readonly ActionTraceCache _cache;
            private readonly EventListSection _eventListSection;
            private readonly Func<SortMode> _getSortMode;
            private readonly Action<SortMode> _setSortMode;

            // UI Elements
            private readonly Label _eventCountBadge;
            private readonly ToolbarSearchField _searchField;
            private readonly ToolbarMenu _filterMenu;
            private readonly ToolbarMenu _sortMenu;
            private readonly ToolbarButton _settingsButton;
            private readonly ToolbarButton _refreshButton;
            private readonly ToolbarButton _clearButton;
            private readonly VisualElement _filterSummaryBar;
            private readonly Label _filterSummaryText;
            private readonly ToolbarButton _clearFiltersButton;
            private readonly Label _refreshIndicator;

            // Callbacks for cleanup
            private EventCallback<ChangeEvent<string>> _searchChangedCallback;
            private EventCallback<ClickEvent> _settingsCallback;
            private EventCallback<ClickEvent> _refreshCallback;
            private EventCallback<ClickEvent> _clearCallback;
            private EventCallback<ClickEvent> _clearFiltersCallback;

            public FilterSection(
                VisualElement root,
                ActionTraceCache cache,
                EventListSection eventListSection,
                Func<SortMode> getSortMode,
                Action<SortMode> setSortMode)
            {
                _root = root;
                _cache = cache;
                _eventListSection = eventListSection;
                _getSortMode = getSortMode;
                _setSortMode = setSortMode;

                // Cache UI elements
                _eventCountBadge = root.Q<Label>(UINames.EventCountBadge);
                _searchField = root.Q<ToolbarSearchField>(UINames.SearchField);
                _filterMenu = root.Q<ToolbarMenu>(UINames.FilterMenu);
                _sortMenu = root.Q<ToolbarMenu>(UINames.SortMenu);
                _settingsButton = root.Q<ToolbarButton>(UINames.SettingsButton);
                _refreshButton = root.Q<ToolbarButton>(UINames.RefreshButton);
                _clearButton = root.Q<ToolbarButton>(UINames.ClearButton);
                _filterSummaryBar = root.Q<VisualElement>(UINames.FilterSummaryBar);
                _filterSummaryText = root.Q<Label>(UINames.FilterSummaryText);
                _clearFiltersButton = root.Q<ToolbarButton>(UINames.ClearFiltersButton);
                _refreshIndicator = root.Q<Label>(UINames.RefreshIndicator);

                SetupToolbar();
            }

            private void SetupToolbar()
            {
                // Create callbacks
                _searchChangedCallback = new EventCallback<ChangeEvent<string>>(OnSearchChanged);
                _settingsCallback = new EventCallback<ClickEvent>(_ => OnSettingsClicked());
                _refreshCallback = new EventCallback<ClickEvent>(_ => OnRefreshClicked());
                _clearCallback = new EventCallback<ClickEvent>(_ => OnClearClicked());
                _clearFiltersCallback = new EventCallback<ClickEvent>(_ => OnClearFiltersClicked());

                _searchField?.RegisterValueChangedCallback(_searchChangedCallback);
                _settingsButton?.RegisterCallback(_settingsCallback);
                _refreshButton?.RegisterCallback(_refreshCallback);
                _clearButton?.RegisterCallback(_clearCallback);
                _clearFiltersButton?.RegisterCallback(_clearFiltersCallback);

                // Filter menu
                _filterMenu?.menu.AppendAction("All Events", _ => OnFilterAllEvents());
                _filterMenu?.menu.AppendAction("", _ => { });
                _filterMenu?.menu.AppendAction("AI Can See (Settings)", _ => OnFilterFromSettings());
                _filterMenu?.menu.AppendAction("", _ => { });
                _filterMenu?.menu.AppendAction("Medium+ Only", _ => OnFilterMediumPlus());
                _filterMenu?.menu.AppendAction("High+ Only", _ => OnFilterHighPlus());

                // Sort menu
                _sortMenu?.menu.AppendAction("By Time (Newest First)", _ => OnSortByTime());
                _sortMenu?.menu.AppendAction("By Importance (AI First)", _ => OnSortByImportance());
            }

            public void UpdateUI(int eventCount)
            {
                UpdateEventCount(eventCount);
                UpdateFilterMenuText();
                UpdateSortButtonText();
                UpdateFilterSummary(eventCount);
            }

            private void UpdateEventCount(int eventCount)
            {
                if (_eventCountBadge != null)
                    _eventCountBadge.text = EventStore.Count.ToString();
            }

            private void UpdateFilterMenuText()
            {
                if (_filterMenu == null) return;

                // This would need to track current filter value
                // For simplicity, just show "Filter"
                _filterMenu.text = "Filter";
            }

            private void UpdateSortButtonText()
            {
                if (_sortMenu == null) return;

                string text = _getSortMode() switch
                {
                    SortMode.ByTimeDesc => "Sort: Time",
                    SortMode.AIFiltered => "Sort: Importance",
                    _ => "Sort: ?"
                };
                _sortMenu.text = text;
            }

            private void UpdateFilterSummary(int eventCount)
            {
                if (_filterSummaryBar == null || _filterSummaryText == null) return;

                // Simple implementation - show if there are filters
                bool hasFilters = !string.IsNullOrEmpty(_searchField?.value);
                _filterSummaryBar.style.display = hasFilters ? DisplayStyle.Flex : DisplayStyle.None;

                if (hasFilters)
                {
                    var sb = _cache.GetStringBuilder();
                    sb.Append("Showing ");
                    sb.Append(eventCount);
                    sb.Append(" events");
                    _filterSummaryText.text = sb.ToString();
                }
            }

            private void OnSearchChanged(ChangeEvent<string> e)
            {
                _eventListSection.SetSearchFilter(e.newValue);
            }

            private void OnRefreshClicked()
            {
                _eventListSection.RefreshData();
                AnimateRefreshIndicator();
            }

            private void OnClearClicked()
            {
                EventStore.Clear();
                _eventListSection.Clear();
                AnimateRefreshIndicator();
            }

            private void OnClearFiltersClicked()
            {
                _eventListSection.SetSearchFilter(string.Empty);
                _eventListSection.SetImportanceFilter(-1f);

                if (_searchField != null)
                    _searchField.SetValueWithoutNotify(string.Empty);
            }

            private void OnSettingsClicked()
            {
                // Open ActionTraceSettings in Inspector
                ActionTraceSettings.ShowSettingsWindow();
                McpLog.Info("[ActionTrace] Opened settings window. You can configure filtering, merging, and storage options.");
            }

            private void OnFilterAllEvents() => _eventListSection.SetImportanceFilter(0f);
            private void OnFilterFromSettings() => _eventListSection.SetImportanceFilter(-1f);
            private void OnFilterMediumPlus() => _eventListSection.SetImportanceFilter(0.4f);
            private void OnFilterHighPlus() => _eventListSection.SetImportanceFilter(0.7f);

            private void OnSortByTime()
            {
                _setSortMode(SortMode.ByTimeDesc);
                _eventListSection.SetSortMode(SortMode.ByTimeDesc);
            }

            private void OnSortByImportance()
            {
                _setSortMode(SortMode.AIFiltered);
                _eventListSection.SetSortMode(SortMode.AIFiltered);
            }

            private void AnimateRefreshIndicator()
            {
                if (_refreshIndicator != null)
                {
                    _refreshIndicator.RemoveFromClassList("active");
                    _refreshIndicator.schedule.Execute(() =>
                    {
                        _refreshIndicator.AddToClassList("active");
                    }).ExecuteLater(100);

                    _refreshIndicator.schedule.Execute(() =>
                    {
                        _refreshIndicator.RemoveFromClassList("active");
                    }).ExecuteLater(1000);
                }
            }
        }

        #endregion

        #region Detail Section

        /// <summary>
        /// Internal section for detail panel showing selected event information.
        /// </summary>
        private sealed class DetailSection
        {
            private readonly VisualElement _root;
            private readonly ActionTraceCache _cache;

            // UI Elements
            private readonly VisualElement _detailScrollView;
            private readonly Label _detailPlaceholder;
            private readonly VisualElement _detailContent;
            private readonly VisualElement _detailActions;
            private readonly ToolbarButton _copySummaryButton;
            private readonly Label _countLabel;
            private readonly Label _statusLabel;
            private readonly Label _modeLabel;

            // Callback
            private EventCallback<ClickEvent> _copySummaryCallback;

            // State
            private ActionTraceQuery.ActionTraceViewItem _selectedItem;

            public DetailSection(VisualElement root, ActionTraceCache cache)
            {
                _root = root;
                _cache = cache;

                // Cache UI elements
                _detailScrollView = root.Q<VisualElement>(UINames.DetailScrollView);
                _detailPlaceholder = root.Q<Label>(UINames.DetailPlaceholder);
                _detailContent = root.Q<VisualElement>(UINames.DetailContent);
                _detailActions = root.Q<VisualElement>(UINames.DetailActions);
                _copySummaryButton = root.Q<ToolbarButton>(UINames.CopySummaryButton);
                _countLabel = root.Q<Label>(UINames.CountLabel);
                _statusLabel = root.Q<Label>(UINames.StatusLabel);
                _modeLabel = root.Q<Label>(UINames.ModeLabel);

                SetupActions();
            }

            private void SetupActions()
            {
                _copySummaryCallback = new EventCallback<ClickEvent>(_ => OnCopySummaryClicked());
                _copySummaryButton?.RegisterCallback(_copySummaryCallback);
            }

            public void SetSelectedItem(ActionTraceQuery.ActionTraceViewItem item)
            {
                _selectedItem = item;

                if (item == null)
                {
                    _detailPlaceholder.style.display = DisplayStyle.Flex;
                    _detailContent.style.display = DisplayStyle.None;
                    _detailActions.style.display = DisplayStyle.None;
                    return;
                }

                _detailPlaceholder.style.display = DisplayStyle.None;
                _detailContent.style.display = DisplayStyle.Flex;
                _detailActions.style.display = DisplayStyle.Flex;

                BuildDetailPanel(item);
            }

            public void UpdateStatus(int currentCount, int totalCount, SortMode sortMode)
            {
                if (_countLabel != null)
                    _countLabel.text = currentCount.ToString();

                if (_statusLabel != null)
                    _statusLabel.text = DateTime.Now.ToString("HH:mm:ss");

                if (_modeLabel != null)
                    _modeLabel.text = sortMode == SortMode.ByTimeDesc ? "Time" : "AI";
            }

            private void BuildDetailPanel(ActionTraceQuery.ActionTraceViewItem item)
            {
                if (_detailContent == null) return;

                _detailContent.Clear();

                // Add sections
                AddDetailSection("EVENT OVERVIEW", section =>
                {
                    var header = new VisualElement();
                    header.AddToClassList("detail-section-header");

                    var icon = new Label { text = _cache.GetEventTypeIcon(item.Event.Type) };
                    icon.AddToClassList("detail-type-icon");
                    header.Add(icon);

                    var title = new Label { text = item.Event.Type };
                    header.Add(title);

                    section.Add(header);

                    AddDetailRow(section, "Sequence", item.Event.Sequence.ToString());
                    AddDetailRow(section, "Timestamp", $"{item.DisplayTime} ({item.Event.TimestampUnixMs})");
                    if (item.ImportanceScore > 0)
                    {
                        AddImportanceBar(section, item.ImportanceScore, item.ImportanceCategory);
                    }
                });

                AddDetailSection("SUMMARY", section =>
                {
                    AddDetailRow(section, "Description", item.DisplaySummary);
                });

                if (!string.IsNullOrEmpty(item.TargetName))
                {
                    AddDetailSection("TARGET INFORMATION", section =>
                    {
                        AddDetailRow(section, "Name", item.TargetName);
                        if (item.TargetInstanceId.HasValue)
                            AddDetailRow(section, "InstanceID", item.TargetInstanceId.Value.ToString());
                        AddDetailRow(section, "GlobalID", item.Event.TargetId);
                    });
                }

                if (item.ImportanceScore > 0 || !string.IsNullOrEmpty(item.ImportanceCategory))
                {
                    AddDetailSection("SEMANTICS", section =>
                    {
                        if (!string.IsNullOrEmpty(item.ImportanceCategory))
                            AddDetailRow(section, "Category", item.ImportanceCategory);
                        if (!string.IsNullOrEmpty(item.InferredIntent))
                            AddDetailRow(section, "Intent", item.InferredIntent);
                        if (item.ImportanceScore > 0)
                            AddDetailRow(section, "Importance Score", item.ImportanceScore.ToString("F2"));
                    });
                }

                AddDetailSection("METADATA", section =>
                {
                    AddDetailRow(section, "Type", item.Event.Type);
                    AddDetailRow(section, "Has Payload", item.Event.Payload != null ? "Yes" : "No");
                    if (item.Event.Payload != null)
                    {
                        AddDetailRow(section, "Payload Size", FormatBytes(item.Event.Payload.Count));
                    }
                });
            }

            private void AddDetailSection(string title, Action<VisualElement> contentBuilder)
            {
                var section = new VisualElement();
                section.AddToClassList("detail-section");

                var header = new Label { text = title };
                header.AddToClassList("detail-section-header");
                section.Add(header);

                contentBuilder(section);

                _detailContent.Add(section);
            }

            private void AddDetailRow(VisualElement parent, string label, string value)
            {
                var row = new VisualElement();
                row.AddToClassList("detail-row");

                var labelElement = new Label { text = label };
                labelElement.AddToClassList("detail-label");

                var valueElement = new Label { text = value };
                valueElement.AddToClassList("detail-value");

                row.Add(labelElement);
                row.Add(valueElement);
                parent.Add(row);
            }

            private void AddImportanceBar(VisualElement parent, float score, string category)
            {
                var container = new VisualElement();
                container.AddToClassList("importance-bar-container");

                var label = new Label { text = "Importance" };
                label.AddToClassList("importance-bar-label");
                container.Add(label);

                var bar = new VisualElement();
                bar.AddToClassList("importance-bar");

                var fill = new VisualElement();
                fill.AddToClassList("importance-bar-fill");
                fill.style.width = Length.Percent(score * 100);
                fill.AddToClassList($"importance-bar-fill--{category.ToLowerInvariant()}");
                bar.Add(fill);

                container.Add(bar);

                var valueLabel = new Label { text = $"{score:F2}" };
                valueLabel.AddToClassList("importance-bar-value");
                container.Add(valueLabel);

                parent.Add(container);
            }

            private string FormatBytes(int bytes)
            {
                return bytes < 1024 ? $"{bytes} B" :
                       bytes < 1024 * 1024 ? $"{bytes / 1024.0:F1} KB" :
                       $"{bytes / (1024.0 * 1024.0):F1} MB";
            }

            private void OnCopySummaryClicked()
            {
                if (_selectedItem == null) return;

                var summary = $"[{_selectedItem.DisplayTime}] {_selectedItem.Event.Type}: {_selectedItem.DisplaySummary}";
                GUIUtility.systemCopyBuffer = summary;
                McpLog.Info($"[ActionTrace] Copied event summary to clipboard.");
            }
        }

        #endregion
    }
}
