using System;
using System.Collections.Generic;
using System.Linq;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.ActionTrace.Core.Store;
using MCPForUnity.Editor.ActionTrace.Analysis.Query;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using MCPForUnity.Editor.ActionTrace.Core.Settings;
using static MCPForUnity.Editor.ActionTrace.Analysis.Query.ActionTraceQuery;

namespace MCPForUnity.Editor.ActionTrace.UI.Windows
{
    /// <summary>
    /// Sort mode: controls how the event list is sorted
    /// </summary>
    public enum SortMode
    {
        /// <summary>Pure time sorting (newest first) - for users viewing records</summary>
        ByTimeDesc,
        /// <summary>AI perspective sorting - grouped by time then importance</summary>
        AIFiltered
    }

    public sealed class ActionTraceEditorWindow : EditorWindow
    {
        #region Constants

        private const string UxmlName = "ActionTraceEditorWindow";
        private const double RefreshInterval = 1.0;
        private const int DefaultQueryLimit = 200;

        // UI Element Names
        private static class UINames
        {
            public const string EventList = "event-list";
            public const string DetailScrollView = "detail-scroll-view";
            public const string StatusLabel = "status-label";
            public const string CountLabel = "count-label";
            public const string SearchField = "search-field";
            public const string ImportanceToggle = "importance-toggle";
            public const string ContextToggle = "context-toggle";
            public const string FilterMenu = "filter-menu";
            public const string SortMenu = "sort-menu";
            public const string SettingsButton = "settings-button";
            public const string RefreshButton = "refresh-button";
            public const string ClearButton = "clear-button";
        }

        // USS Class Names
        private static class Classes
        {
            public const string EventItem = "event-item";
            public const string EventTime = "event-time";
            public const string EventType = "event-type";
            public const string EventSummary = "event-summary";
            public const string ImportanceBadge = "importance-badge";
            public const string HasContext = "has-context";
            public const string DetailContainer = "detail-container";
            public const string DetailRow = "detail-row";
            public const string DetailLabel = "detail-label";
            public const string DetailValue = "detail-value";
        }

        #endregion

        // UI
        private ListView _eventListView;
        private ScrollView _detailScrollView;
        private Label _statusLabel;
        private Label _countLabel;
        private ToolbarSearchField _searchField;
        private ToolbarToggle _importanceToggle;
        private ToolbarToggle _contextToggle;
        private ToolbarMenu _filterMenu;
        private ToolbarMenu _sortMenu;
        private ToolbarButton _settingsButton;
        private ToolbarButton _refreshButton;
        private ToolbarButton _clearButton;

        // Data
        private readonly List<ActionTraceQuery.ActionTraceViewItem> _currentEvents = new();
        private ActionTraceQuery _actionTraceQuery;

        // Track previous BypassImportanceFilter value to restore on window close
        private bool? _previousBypassImportanceFilter;

        private string _searchText = string.Empty;
        private float _minImportance;
        private bool _showSemantics;
        private bool _showContext;
        private SortMode _sortMode = SortMode.ByTimeDesc;

        private double _lastRefreshTime;

        public static void ShowWindow()
        {
            var window = GetWindow<ActionTraceEditorWindow>("ActionTrace");
            window.minSize = new Vector2(900, 600);
        }

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

            SetupReferences();
            ValidateRequiredElements();
            SetupListView();
            SetupToolbar();

            _actionTraceQuery = new ActionTraceQuery();
            _minImportance = ActionTraceSettings.Instance?.Filtering.MinImportanceForRecording ?? 0.4f;

            // Always record all events, filter at query time based on mode
            // Save current value and enable bypass for this window
            if (ActionTraceSettings.Instance != null)
            {
                _previousBypassImportanceFilter = ActionTraceSettings.Instance.Filtering.BypassImportanceFilter;
                ActionTraceSettings.Instance.Filtering.BypassImportanceFilter = true;
            }

            RefreshEvents();
            UpdateStatus();
        }

        private VisualTreeAsset LoadUxmlAsset()
        {
            // Try loading by name first (simplest approach)
            var guids = AssetDatabase.FindAssets($"{UxmlName} t:VisualTreeAsset");
            if (guids?.Length > 0)
            {
                var path = AssetDatabase.GUIDToAssetPath(guids[0]);
                var asset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(path);
                if (asset != null) return asset;
            }

            // Fallback: try package-relative path
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

        private void ValidateRequiredElements()
        {
            if (_eventListView == null)
                McpLog.Error($"'{UINames.EventList}' ListView not found in UXML.");
            if (_detailScrollView == null)
                McpLog.Error($"'{UINames.DetailScrollView}' ScrollView not found in UXML.");
            if (_statusLabel == null)
                McpLog.Error($"'{UINames.StatusLabel}' Label not found in UXML.");
            if (_countLabel == null)
                McpLog.Error($"'{UINames.CountLabel}' Label not found in UXML.");
        }

        private void SetupReferences()
        {
            _eventListView = rootVisualElement.Q<ListView>(UINames.EventList);
            _detailScrollView = rootVisualElement.Q<ScrollView>(UINames.DetailScrollView);
            _statusLabel = rootVisualElement.Q<Label>(UINames.StatusLabel);
            _countLabel = rootVisualElement.Q<Label>(UINames.CountLabel);

            _searchField = rootVisualElement.Q<ToolbarSearchField>(UINames.SearchField);
            _importanceToggle = rootVisualElement.Q<ToolbarToggle>(UINames.ImportanceToggle);
            _contextToggle = rootVisualElement.Q<ToolbarToggle>(UINames.ContextToggle);
            _filterMenu = rootVisualElement.Q<ToolbarMenu>(UINames.FilterMenu);
            _sortMenu = rootVisualElement.Q<ToolbarMenu>(UINames.SortMenu);
            _settingsButton = rootVisualElement.Q<ToolbarButton>(UINames.SettingsButton);
            _refreshButton = rootVisualElement.Q<ToolbarButton>(UINames.RefreshButton);
            _clearButton = rootVisualElement.Q<ToolbarButton>(UINames.ClearButton);
        }

        private void SetupListView()
        {
            _eventListView.itemsSource = _currentEvents;
            _eventListView.selectionType = SelectionType.Single;

            _eventListView.makeItem = () =>
            {
                var root = new VisualElement();
                root.AddToClassList(Classes.EventItem);

                var time = new Label { name = "time" };
                time.AddToClassList(Classes.EventTime);
                root.Add(time);

                var type = new Label { name = "type" };
                type.AddToClassList(Classes.EventType);
                root.Add(type);

                var summary = new Label { name = "summary" };
                summary.AddToClassList(Classes.EventSummary);
                root.Add(summary);

                var badge = new Label { name = "badge" };
                badge.AddToClassList(Classes.ImportanceBadge);
                root.Add(badge);

                return root;
            };

            _eventListView.bindItem = (element, index) =>
            {
                var item = _currentEvents[index];

                element.Q<Label>("time").text = item.DisplayTime;
                element.Q<Label>("type").text = item.Event.Type;
                element.Q<Label>("summary").text = item.DisplaySummary;

                var badge = element.Q<Label>("badge");
                badge.style.display = _showSemantics ? DisplayStyle.Flex : DisplayStyle.None;
                badge.text = item.ImportanceCategory.ToUpperInvariant();
                badge.style.backgroundColor = item.ImportanceBadgeColor;

                element.EnableInClassList(Classes.HasContext, item.Context != null);
            };

            _eventListView.selectionChanged += OnSelectionChanged;
        }

        private void SetupToolbar()
        {
            _searchField?.RegisterValueChangedCallback(e =>
            {
                _searchText = e.newValue.ToLowerInvariant();
                RefreshEvents();
            });

            _importanceToggle?.RegisterValueChangedCallback(e =>
            {
                _showSemantics = e.newValue;
                _eventListView.RefreshItems();
            });

            _contextToggle?.RegisterValueChangedCallback(e =>
            {
                _showContext = e.newValue;
                RefreshEvents();
            });

            // Filter menu: add All option
            _filterMenu?.menu.AppendAction("All", a => SetImportance(0f));
            _filterMenu?.menu.AppendAction("/", a => { });
            _filterMenu?.menu.AppendAction("AI Can See", a => SetImportanceFromSettings());
            _filterMenu?.menu.AppendAction("Low+", a => SetImportance(0f));
            _filterMenu?.menu.AppendAction("Medium+", a => SetImportance(0.4f));
            _filterMenu?.menu.AppendAction("High+", a => SetImportance(0.7f));

            // Sort menu
            _sortMenu?.menu.AppendAction("By Time (Newest)", a => SetSortMode(SortMode.ByTimeDesc));
            _sortMenu?.menu.AppendAction("AI Filtered", a => SetSortMode(SortMode.AIFiltered));

            _settingsButton?.RegisterCallback<ClickEvent>(_ => OnSettingsClicked());
            _refreshButton?.RegisterCallback<ClickEvent>(_ => OnRefreshClicked());
            _clearButton?.RegisterCallback<ClickEvent>(_ => OnClearClicked());
        }

        private void SetImportance(float value)
        {
            _minImportance = value;
            RefreshEvents();
        }

        private void SetImportanceFromSettings()
        {
            var settings = ActionTraceSettings.Instance;
            _minImportance = settings != null ? settings.Filtering.MinImportanceForRecording : 0.4f;
            RefreshEvents();
        }

        private void SetSortMode(SortMode mode)
        {
            _sortMode = mode;

            // Note: BypassImportanceFilter is already set in CreateGUI and restored in OnDisable
            // No need to modify global settings here

            UpdateSortButtonText();
            RefreshEvents();
        }

        private void UpdateSortButtonText()
        {
            if (_sortMenu == null) return;

            string text = _sortMode switch
            {
                SortMode.ByTimeDesc => "Sort: Time",
                SortMode.AIFiltered => "Sort: AI",
                _ => "Sort: ?"
            };
            _sortMenu.text = text;
        }

        private void OnRefreshClicked()
        {
            _lastRefreshTime = EditorApplication.timeSinceStartup;
            RefreshEvents();
            McpLog.Debug("[ActionTraceEditorWindow] Refresh clicked");
        }

        private void OnSettingsClicked()
        {
            ActionTraceSettings.ShowSettingsWindow();
        }

        private void OnClearClicked()
        {
            EventStore.Clear();
            _currentEvents.Clear();
            _eventListView.RefreshItems();
            _detailScrollView.Clear();
            UpdateStatus();
            McpLog.Debug("[ActionTraceEditorWindow] Clear clicked");
        }

        private void RefreshEvents()
        {
            IEnumerable<ActionTraceViewItem> source = _showContext
                ? _actionTraceQuery.ProjectWithContext(EventStore.QueryWithContext(DefaultQueryLimit))
                : _actionTraceQuery.Project(EventStore.Query(DefaultQueryLimit));

            // Apply sorting
            source = ApplySorting(source);

            _currentEvents.Clear();
            _currentEvents.AddRange(source.Where(FilterEvent));

            _eventListView.RefreshItems();
            UpdateStatus();
        }

        /// <summary>
        /// Apply current sort mode to event list
        /// </summary>
        private IEnumerable<ActionTraceQuery.ActionTraceViewItem> ApplySorting(IEnumerable<ActionTraceQuery.ActionTraceViewItem> source)
        {
            return _sortMode switch
            {
                SortMode.ByTimeDesc => source.OrderByDescending(e => e.Event.TimestampUnixMs),
                SortMode.AIFiltered => source
                    .OrderByDescending(e => e.Event.TimestampUnixMs)
                    .ThenByDescending(e => e.ImportanceScore),
                _ => source
            };
        }

        private bool FilterEvent(ActionTraceQuery.ActionTraceViewItem e)
        {
            // ByTime mode: show all records (including low importance)
            // AI Filtered mode: apply importance filter (AI perspective)
            if (_sortMode == SortMode.AIFiltered && e.ImportanceScore < _minImportance)
                return false;

            if (!string.IsNullOrEmpty(_searchText))
            {
                return e.DisplaySummaryLower.Contains(_searchText)
                    || e.DisplayTargetIdLower.Contains(_searchText)
                    || e.Event.Type.ToLowerInvariant().Contains(_searchText);
            }

            return true;
        }

        private void OnSelectionChanged(IEnumerable<object> items)
        {
            _detailScrollView.Clear();

            var item = items.FirstOrDefault() as ActionTraceQuery.ActionTraceViewItem;
            if (item == null) return;

            var container = new VisualElement();
            container.AddToClassList(Classes.DetailContainer);

            AddDetail(container, "Sequence", item.Event.Sequence.ToString());
            AddDetail(container, "Type", item.Event.Type);
            AddDetail(container, "Summary", item.DisplaySummary);
            AddDetail(container, "Importance", item.ImportanceScore.ToString());
            _detailScrollView.Add(container);
        }

        private static void AddDetail(VisualElement parent, string key, string value)
        {
            var row = new VisualElement();
            row.AddToClassList(Classes.DetailRow);

            var keyLabel = new Label { text = key };
            keyLabel.AddToClassList(Classes.DetailLabel);

            var valueLabel = new Label { text = value };
            valueLabel.AddToClassList(Classes.DetailValue);

            row.Add(keyLabel);
            row.Add(valueLabel);
            parent.Add(row);
        }

        private void UpdateStatus()
        {
            _countLabel.text = $"Events: {_currentEvents.Count}";
            _statusLabel.text = $"Updated: {DateTime.Now:HH:mm:ss}";
        }

        private void OnEnable()
        {
            EditorApplication.update += OnEditorUpdate;
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;

            // Restore the previous BypassImportanceFilter value
            if (ActionTraceSettings.Instance != null && _previousBypassImportanceFilter.HasValue)
            {
                ActionTraceSettings.Instance.Filtering.BypassImportanceFilter = _previousBypassImportanceFilter.Value;
            }
        }

        private void OnEditorUpdate()
        {
            if (EditorApplication.timeSinceStartup - _lastRefreshTime > RefreshInterval)
            {
                _lastRefreshTime = EditorApplication.timeSinceStartup;
                RefreshEvents();
            }
        }
    }
}
