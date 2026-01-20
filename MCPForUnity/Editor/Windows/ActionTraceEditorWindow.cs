using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.ActionTrace.Core.Store;
using MCPForUnity.Editor.ActionTrace.Analysis.Query;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using MCPForUnity.Editor.ActionTrace.Context;
using MCPForUnity.Editor.ActionTrace.Core.Settings;
using static MCPForUnity.Editor.ActionTrace.Analysis.Query.ActionTraceQuery;

namespace MCPForUnity.Editor.ActionTrace.UI.Windows
{
    public enum SortMode
    {
        ByTimeDesc,
        AIFiltered
    }

    public sealed class ActionTraceEditorWindow : EditorWindow
    {
        #region Constants

        private const string UxmlName = "ActionTraceEditorWindow";
        private const double RefreshInterval = 1.0;
        private const int DefaultQueryLimit = 200;

        private static class UINames
        {
            public const string EventCountBadge = "event-count-badge";
            public const string SearchField = "search-field";
            public const string FilterMenu = "filter-menu";
            public const string SortMenu = "sort-menu";
            public const string ImportanceToggle = "importance-toggle";
            public const string ContextToggle = "context-toggle";
            public const string AutoRefreshStatus = "auto-refresh-status";
            public const string ExportButton = "export-button";
            public const string SettingsButton = "settings-button";
            public const string RefreshButton = "refresh-button";
            public const string ClearButton = "clear-button";
            public const string FilterSummaryBar = "filter-summary-bar";
            public const string FilterSummaryText = "filter-summary-text";
            public const string ClearFiltersButton = "clear-filters-button";
            public const string EventList = "event-list";
            public const string EventListHeader = "event-list-header";
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

        private static class Classes
        {
            public const string EventItem = "event-item";
            public const string EventItemMainRow = "event-item-main-row";
            public const string EventItemDetailRow = "event-item-detail-row";
            public const string EventItemDetailText = "event-item-detail-text";
            public const string EventItemBadges = "event-item-badges";
            public const string EventItemBadge = "event-item-badge";
            public const string EventTime = "event-time";
            public const string EventTypeIcon = "event-type-icon";
            public const string EventType = "event-type";
            public const string EventSummary = "event-summary";
            public const string ImportanceBadge = "importance-badge";
            public const string ContextIndicator = "context-indicator";
            public const string DetailSection = "detail-section";
            public const string DetailSectionHeader = "detail-section-header";
            public const string DetailRow = "detail-row";
            public const string DetailLabel = "detail-label";
            public const string DetailValue = "detail-value";
            public const string DetailSubsection = "detail-subsection";
            public const string DetailSubsectionTitle = "detail-subsection-title";
            public const string ImportanceBarContainer = "importance-bar-container";
            public const string ImportanceBar = "importance-bar";
            public const string ImportanceBarFill = "importance-bar-fill";
            public const string ImportanceBarValue = "importance-bar-value";
            public const string ImportanceBarLabel = "importance-bar-label";
        }

        #endregion

        // UI Elements
        private Label _eventCountBadge;
        private ToolbarSearchField _searchField;
        private ToolbarMenu _filterMenu;
        private ToolbarMenu _sortMenu;
        private ToolbarToggle _importanceToggle;
        private ToolbarToggle _contextToggle;
        private Label _autoRefreshStatus;
        private ToolbarButton _exportButton;
        private ToolbarButton _settingsButton;
        private ToolbarButton _refreshButton;
        private ToolbarButton _clearButton;
        private VisualElement _filterSummaryBar;
        private Label _filterSummaryText;
        private ToolbarButton _clearFiltersButton;
        private ListView _eventListView;
        private Label _eventListCountLabel;
        private VisualElement _emptyState;
        private VisualElement _noResultsState;
        private Label _noResultsFiltersLabel;
        private ScrollView _detailScrollView;
        private Label _detailPlaceholder;
        private VisualElement _detailContent;
        private VisualElement _detailActions;
        private ToolbarButton _copySummaryButton;
        private Label _countLabel;
        private Label _statusLabel;
        private Label _modeLabel;
        private Label _refreshIndicator;

        // Data
        private readonly List<ActionTraceQuery.ActionTraceViewItem> _currentEvents = new();
        private ActionTraceQuery _actionTraceQuery;
        private bool? _previousBypassImportanceFilter;

        private string _searchText = string.Empty;
        private float _minImportance;
        private bool _showSemantics;
        private bool _showContext;
        private SortMode _sortMode = SortMode.ByTimeDesc;

        private double _lastRefreshTime;
        private ActionTraceQuery.ActionTraceViewItem _selectedItem;

        // 性能优化：缓存
        private int _lastEventStoreCount = -1;
        private readonly Dictionary<string, string> _iconCache = new();
        private readonly StringBuilder _stringBuilder = new();
        private bool _isScheduledRefreshActive;

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

            SetupReferences();
            ValidateRequiredElements();
            SetupListView();
            SetupToolbar();
            SetupDetailActions();

            _actionTraceQuery = new ActionTraceQuery();
            _minImportance = ActionTraceSettings.Instance?.Filtering.MinImportanceForRecording ?? 0.4f;

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

        private void ValidateRequiredElements()
        {
            if (_eventListView == null)
                McpLog.Error($"'{UINames.EventList}' ListView not found in UXML.");
            if (_detailScrollView == null)
                McpLog.Error($"'{UINames.DetailScrollView}' ScrollView not found in UXML.");
            if (_countLabel == null)
                McpLog.Error($"'{UINames.CountLabel}' Label not found in UXML.");
            if (_statusLabel == null)
                McpLog.Error($"'{UINames.StatusLabel}' Label not found in UXML.");
        }

        private void SetupReferences()
        {
            _eventCountBadge = rootVisualElement.Q<Label>(UINames.EventCountBadge);
            _searchField = rootVisualElement.Q<ToolbarSearchField>(UINames.SearchField);
            _filterMenu = rootVisualElement.Q<ToolbarMenu>(UINames.FilterMenu);
            _sortMenu = rootVisualElement.Q<ToolbarMenu>(UINames.SortMenu);
            _importanceToggle = rootVisualElement.Q<ToolbarToggle>(UINames.ImportanceToggle);
            _contextToggle = rootVisualElement.Q<ToolbarToggle>(UINames.ContextToggle);
            _autoRefreshStatus = rootVisualElement.Q<Label>(UINames.AutoRefreshStatus);
            _exportButton = rootVisualElement.Q<ToolbarButton>(UINames.ExportButton);
            _settingsButton = rootVisualElement.Q<ToolbarButton>(UINames.SettingsButton);
            _refreshButton = rootVisualElement.Q<ToolbarButton>(UINames.RefreshButton);
            _clearButton = rootVisualElement.Q<ToolbarButton>(UINames.ClearButton);
            _filterSummaryBar = rootVisualElement.Q<VisualElement>(UINames.FilterSummaryBar);
            _filterSummaryText = rootVisualElement.Q<Label>(UINames.FilterSummaryText);
            _clearFiltersButton = rootVisualElement.Q<ToolbarButton>(UINames.ClearFiltersButton);
            _eventListView = rootVisualElement.Q<ListView>(UINames.EventList);
            _eventListCountLabel = rootVisualElement.Q<Label>(UINames.EventListCount);
            _emptyState = rootVisualElement.Q<VisualElement>(UINames.EmptyState);
            _noResultsState = rootVisualElement.Q<VisualElement>(UINames.NoResultsState);
            _noResultsFiltersLabel = rootVisualElement.Q<Label>(UINames.NoResultsFilters);
            _detailScrollView = rootVisualElement.Q<ScrollView>(UINames.DetailScrollView);
            _detailPlaceholder = rootVisualElement.Q<Label>(UINames.DetailPlaceholder);
            _detailContent = rootVisualElement.Q<VisualElement>(UINames.DetailContent);
            _detailActions = rootVisualElement.Q<VisualElement>(UINames.DetailActions);
            _copySummaryButton = rootVisualElement.Q<ToolbarButton>(UINames.CopySummaryButton);
            _countLabel = rootVisualElement.Q<Label>(UINames.CountLabel);
            _statusLabel = rootVisualElement.Q<Label>(UINames.StatusLabel);
            _modeLabel = rootVisualElement.Q<Label>(UINames.ModeLabel);
            _refreshIndicator = rootVisualElement.Q<Label>(UINames.RefreshIndicator);
        }

        private void SetupListView()
        {
            _eventListView.itemsSource = _currentEvents;
            _eventListView.selectionType = SelectionType.Single;
            _eventListView.fixedItemHeight = 60;
            _eventListView.virtualizationMethod = CollectionVirtualizationMethod.FixedHeight;

            _eventListView.makeItem = MakeListItem;
            _eventListView.bindItem = BindListItem;
            _eventListView.selectionChanged += OnSelectionChanged;
        }

        private VisualElement MakeListItem()
        {
            var root = new VisualElement();
            root.AddToClassList(Classes.EventItem);

            var mainRow = new VisualElement();
            mainRow.AddToClassList(Classes.EventItemMainRow);

            var time = new Label { name = "time" };
            time.AddToClassList(Classes.EventTime);
            mainRow.Add(time);

            var typeIcon = new Label { name = "type-icon" };
            typeIcon.AddToClassList(Classes.EventTypeIcon);
            mainRow.Add(typeIcon);

            var type = new Label { name = "type" };
            type.AddToClassList(Classes.EventType);
            mainRow.Add(type);

            var summary = new Label { name = "summary" };
            summary.AddToClassList(Classes.EventSummary);
            mainRow.Add(summary);

            root.Add(mainRow);

            var detailRow = new VisualElement { name = "detail-row" };
            detailRow.AddToClassList(Classes.EventItemDetailRow);
            detailRow.style.display = DisplayStyle.None;

            var detailText = new Label { name = "detail-text" };
            detailText.AddToClassList(Classes.EventItemDetailText);
            detailRow.Add(detailText);

            root.Add(detailRow);

            var badgesRow = new VisualElement { name = "badges-row" };
            badgesRow.AddToClassList(Classes.EventItemBadges);
            badgesRow.style.display = DisplayStyle.None;

            var importanceBadge = new Label { name = "importance-badge" };
            importanceBadge.AddToClassList(Classes.ImportanceBadge);
            badgesRow.Add(importanceBadge);

            var contextIndicator = new Label { name = "context-indicator" };
            contextIndicator.AddToClassList(Classes.ContextIndicator);
            badgesRow.Add(contextIndicator);

            root.Add(badgesRow);

            return root;
        }

        private void BindListItem(VisualElement element, int index)
        {
            if (index < 0 || index >= _currentEvents.Count) return;

            var item = _currentEvents[index];

            // 性能优化：只更新变化的内容
            var timeLabel = element.Q<Label>("time");
            if (timeLabel.text != item.DisplayTime)
                timeLabel.text = item.DisplayTime;

            var typeIcon = element.Q<Label>("type-icon");
            var iconText = GetEventTypeIconCached(item.Event.Type);
            if (typeIcon.text != iconText)
                typeIcon.text = iconText;

            var typeLabel = element.Q<Label>("type");
            if (typeLabel.text != item.Event.Type)
            {
                typeLabel.text = item.Event.Type;
                typeLabel.ClearClassList();
                typeLabel.AddToClassList(Classes.EventType);
                typeLabel.AddToClassList($"{Classes.EventType}--{SanitizeClassName(item.Event.Type)}");
            }

            var summaryLabel = element.Q<Label>("summary");
            if (summaryLabel.text != item.DisplaySummary)
                summaryLabel.text = item.DisplaySummary;

            var detailRow = element.Q<VisualElement>("detail-row");
            var detailText = element.Q<Label>("detail-text");

            if (detailRow != null && detailText != null)
            {
                bool showDetail = _eventListView.selectedIndex == index || !string.IsNullOrEmpty(item.Event.TargetId);
                var targetDisplay = showDetail ? DisplayStyle.Flex : DisplayStyle.None;
                if (detailRow.style.display != targetDisplay)
                    detailRow.style.display = targetDisplay;
                
                if (!string.IsNullOrEmpty(item.Event.TargetId))
                {
                    var targetText = $"Target: {item.Event.TargetId}";
                    if (detailText.text != targetText)
                        detailText.text = targetText;
                }
            }

            var badgesRow = element.Q<VisualElement>("badges-row");
            var badge = element.Q<Label>("importance-badge");
            var contextIndicator = element.Q<Label>("context-indicator");

            if (badgesRow != null && badge != null)
            {
                var badgesDisplay = _showSemantics ? DisplayStyle.Flex : DisplayStyle.None;
                if (badgesRow.style.display != badgesDisplay)
                    badgesRow.style.display = badgesDisplay;

                var categoryUpper = item.ImportanceCategory.ToUpperInvariant();
                if (badge.text != categoryUpper)
                {
                    badge.text = categoryUpper;
                    badge.ClearClassList();
                    badge.AddToClassList(Classes.ImportanceBadge);
                    badge.AddToClassList($"{Classes.ImportanceBadge}--{item.ImportanceCategory.ToLowerInvariant()}");
                }
            }

            if (contextIndicator != null)
            {
                var hasContext = item.Context != null;
                var contextDisplay = hasContext ? DisplayStyle.Flex : DisplayStyle.None;
                if (contextIndicator.style.display != contextDisplay)
                {
                    contextIndicator.style.display = contextDisplay;
                    if (hasContext && contextIndicator.text != "🔗")
                    {
                        contextIndicator.text = "🔗";
                        contextIndicator.ClearClassList();
                        contextIndicator.AddToClassList(Classes.ContextIndicator);
                        contextIndicator.AddToClassList("context-source--System");
                    }
                }
            }
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

            _filterMenu?.menu.AppendAction("All", a => SetImportance(0f));
            _filterMenu?.menu.AppendAction("/", a => { });
            _filterMenu?.menu.AppendAction("AI Can See", a => SetImportanceFromSettings());
            _filterMenu?.menu.AppendAction("Low+", a => SetImportance(0f));
            _filterMenu?.menu.AppendAction("Medium+", a => SetImportance(0.4f));
            _filterMenu?.menu.AppendAction("High+", a => SetImportance(0.7f));

            _sortMenu?.menu.AppendAction("By Time (Newest)", a => SetSortMode(SortMode.ByTimeDesc));
            _sortMenu?.menu.AppendAction("AI Filtered", a => SetSortMode(SortMode.AIFiltered));

            _exportButton?.RegisterCallback<ClickEvent>(_ => OnExportClicked());
            _settingsButton?.RegisterCallback<ClickEvent>(_ => OnSettingsClicked());
            _refreshButton?.RegisterCallback<ClickEvent>(_ => OnRefreshClicked());
            _clearButton?.RegisterCallback<ClickEvent>(_ => OnClearClicked());
            _clearFiltersButton?.RegisterCallback<ClickEvent>(_ => OnClearFiltersClicked());
        }

        private void SetupDetailActions()
        {
            _copySummaryButton?.RegisterCallback<ClickEvent>(_ => OnCopySummaryClicked());
        }

        #endregion

        #region Event Handlers

        private void OnSelectionChanged(IEnumerable<object> items)
        {
            _detailPlaceholder.style.display = DisplayStyle.None;
            _detailContent.style.display = DisplayStyle.Flex;
            _detailActions.style.display = DisplayStyle.Flex;

            _detailContent.Clear();
            _selectedItem = items.FirstOrDefault() as ActionTraceQuery.ActionTraceViewItem;

            if (_selectedItem == null)
            {
                _detailPlaceholder.style.display = DisplayStyle.Flex;
                _detailContent.style.display = DisplayStyle.None;
                _detailActions.style.display = DisplayStyle.None;
                return;
            }

            BuildDetailPanel(_selectedItem);
        }

        private void BuildDetailPanel(ActionTraceQuery.ActionTraceViewItem item)
        {
            AddDetailSection("EVENT OVERVIEW", section =>
            {
                var header = new VisualElement();
                header.AddToClassList(Classes.DetailSectionHeader);

                var icon = new Label { text = GetEventTypeIconCached(item.Event.Type) };
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

            if (!string.IsNullOrEmpty(item.Event.TargetId))
            {
                AddDetailSection("TARGET INFORMATION", section =>
                {
                    AddDetailRow(section, "Target ID", item.Event.TargetId);
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

            if (item.Context != null)
            {
                AddDetailSection("CONTEXT", section =>
                {
                    AddDetailRow(section, "Context ID", item.Context.ContextId.ToString());
                    AddDetailRow(section, "Event Sequence", item.Context.EventSequence.ToString());
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
            section.AddToClassList(Classes.DetailSection);

            var header = new Label { text = title };
            header.AddToClassList(Classes.DetailSectionHeader);
            section.Add(header);

            contentBuilder(section);

            _detailContent.Add(section);
        }

        private void AddDetailRow(VisualElement parent, string label, string value)
        {
            var row = new VisualElement();
            row.AddToClassList(Classes.DetailRow);

            var labelElement = new Label { text = label };
            labelElement.AddToClassList(Classes.DetailLabel);

            var valueElement = new Label { text = value };
            valueElement.AddToClassList(Classes.DetailValue);

            row.Add(labelElement);
            row.Add(valueElement);
            parent.Add(row);
        }

        private void AddImportanceBar(VisualElement parent, float score, string category)
        {
            var container = new VisualElement();
            container.AddToClassList(Classes.ImportanceBarContainer);

            var label = new Label { text = "Importance" };
            label.AddToClassList(Classes.ImportanceBarLabel);
            container.Add(label);

            var bar = new VisualElement();
            bar.AddToClassList(Classes.ImportanceBar);

            var fill = new VisualElement();
            fill.AddToClassList(Classes.ImportanceBarFill);
            fill.style.width = Length.Percent(score * 100);
            fill.AddToClassList($"{Classes.ImportanceBarFill}--{category.ToLowerInvariant()}");
            bar.Add(fill);

            container.Add(bar);

            var valueLabel = new Label { text = $"{score:F2}" };
            valueLabel.AddToClassList(Classes.ImportanceBarValue);
            container.Add(valueLabel);

            parent.Add(container);
        }

        #endregion

        #region Action Handlers

        private void OnExportClicked()
        {
            if (_currentEvents.Count == 0)
            {
                Debug.Log("[ActionTrace] No events to export.");
                return;
            }

            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Save as JSON..."), false, () => SaveJsonToFile());
            menu.AddItem(new GUIContent("Save as CSV..."), false, () => SaveCsvToFile());

            menu.ShowAsContext();
        }

        private void SaveJsonToFile()
        {
            var path = EditorUtility.SaveFilePanel("Save Events as JSON", "", "action-trace", "json");
            if (string.IsNullOrEmpty(path)) return;

            try
            {
                var json = BuildJsonExport();
                System.IO.File.WriteAllText(path, json);
                Debug.Log($"[ActionTrace] Saved {_currentEvents.Count} events to {path}");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[ActionTrace] Failed to save JSON: {ex.Message}");
            }
        }

        private void SaveCsvToFile()
        {
            var path = EditorUtility.SaveFilePanel("Save Events as CSV", "", "action-trace", "csv");
            if (string.IsNullOrEmpty(path)) return;

            try
            {
                var csv = BuildCsvExport();
                System.IO.File.WriteAllText(path, csv);
                Debug.Log($"[ActionTrace] Saved {_currentEvents.Count} events to {path}");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[ActionTrace] Failed to save CSV: {ex.Message}");
            }
        }

        private string BuildJsonExport()
        {
            _stringBuilder.Clear();
            _stringBuilder.AppendLine("{");
            _stringBuilder.AppendLine($"  \"exportTime\": \"{DateTime.Now:O}\",");
            _stringBuilder.AppendLine($"  \"totalEvents\": {_currentEvents.Count},");
            _stringBuilder.AppendLine("  \"events\": [");

            for (int i = 0; i < _currentEvents.Count; i++)
            {
                var e = _currentEvents[i];
                _stringBuilder.AppendLine("    {");
                _stringBuilder.AppendLine($"      \"sequence\": {e.Event.Sequence},");
                _stringBuilder.AppendLine($"      \"type\": \"{SanitizeJson(e.Event.Type)}\",");
                _stringBuilder.AppendLine($"      \"timestamp\": {e.Event.TimestampUnixMs},");
                _stringBuilder.AppendLine($"      \"displayTime\": \"{SanitizeJson(e.DisplayTime)}\",");
                _stringBuilder.AppendLine($"      \"summary\": \"{SanitizeJson(e.DisplaySummary)}\",");

                if (!string.IsNullOrEmpty(e.Event.TargetId))
                    _stringBuilder.AppendLine($"      \"targetId\": \"{SanitizeJson(e.Event.TargetId)}\",");
                else
                    _stringBuilder.AppendLine($"      \"targetId\": null,");

                _stringBuilder.AppendLine($"      \"importanceScore\": {e.ImportanceScore:F2},");
                _stringBuilder.AppendLine($"      \"importanceCategory\": \"{SanitizeJson(e.ImportanceCategory)}\"");

                if (!string.IsNullOrEmpty(e.InferredIntent))
                    _stringBuilder.AppendLine($"      ,\"inferredIntent\": \"{SanitizeJson(e.InferredIntent)}\"");

                if (e.Event.Payload != null && e.Event.Payload.Count > 0)
                {
                    _stringBuilder.AppendLine("      ,\"payload\": {");
                    var payloadKeys = e.Event.Payload.Keys.ToList();
                    for (int j = 0; j < payloadKeys.Count; j++)
                    {
                        var key = payloadKeys[j];
                        var value = e.Event.Payload[key];
                        var valueStr = value?.ToString() ?? "null";
                        _stringBuilder.AppendLine($"        \"{SanitizeJson(key)}\": \"{SanitizeJson(valueStr)}\"{(j < payloadKeys.Count - 1 ? "," : "")}");
                    }
                    _stringBuilder.AppendLine("      }");
                }

                _stringBuilder.Append(i < _currentEvents.Count - 1 ? "    }," : "    }");
                if (i < _currentEvents.Count - 1)
                    _stringBuilder.AppendLine();
            }

            _stringBuilder.AppendLine();
            _stringBuilder.AppendLine("  ]");
            _stringBuilder.AppendLine("}");

            return _stringBuilder.ToString();
        }

        private string BuildCsvExport()
        {
            _stringBuilder.Clear();
            _stringBuilder.AppendLine("Sequence,Time,Type,Summary,Target,Importance,Category,Intent");

            foreach (var e in _currentEvents)
            {
                _stringBuilder.Append(e.Event.Sequence);
                _stringBuilder.Append(",\"");
                _stringBuilder.Append(SanitizeCsv(e.DisplayTime));
                _stringBuilder.Append("\",\"");
                _stringBuilder.Append(SanitizeCsv(e.Event.Type));
                _stringBuilder.Append("\",\"");
                _stringBuilder.Append(SanitizeCsv(e.DisplaySummary));
                _stringBuilder.Append("\",\"");
                _stringBuilder.Append(SanitizeCsv(e.Event.TargetId ?? ""));
                _stringBuilder.Append("\",");
                _stringBuilder.Append(e.ImportanceScore.ToString("F2"));
                _stringBuilder.Append(",\"");
                _stringBuilder.Append(SanitizeCsv(e.ImportanceCategory));
                _stringBuilder.Append("\",\"");
                _stringBuilder.Append(SanitizeCsv(e.InferredIntent ?? ""));
                _stringBuilder.AppendLine("\"");
            }

            return _stringBuilder.ToString();
        }

        private string SanitizeJson(string input)
        {
            if (string.IsNullOrEmpty(input)) return "";

            // 性能优化：使用 StringBuilder 避免多次 Replace 产生的中间字符串
            var sb = _stringBuilder;
            sb.Clear();
            sb.EnsureCapacity(input.Length * 2);

            foreach (char c in input)
            {
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '\"': sb.Append("\\\""); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default: sb.Append(c); break;
                }
            }

            return sb.ToString();
        }

        private string SanitizeCsv(string input)
        {
            if (string.IsNullOrEmpty(input)) return "";
            return input.Replace("\"", "\"\"");
        }

        private void CopySummaryToClipboard()
        {
            if (_selectedItem == null) return;

            var summary = $"[{_selectedItem.DisplayTime}] {_selectedItem.Event.Type}: {_selectedItem.DisplaySummary}";
            GUIUtility.systemCopyBuffer = summary;
            Debug.Log($"[ActionTrace] Copied event summary to clipboard.");
        }

        private void OnCopySummaryClicked()
        {
            CopySummaryToClipboard();
        }

        private void OnSettingsClicked()
        {
            ActionTraceSettings.ShowSettingsWindow();
        }

        private void OnRefreshClicked()
        {
            _lastRefreshTime = EditorApplication.timeSinceStartup;
            RefreshEvents();
            AnimateRefreshIndicator();
            McpLog.Debug("[ActionTraceEditorWindow] Refresh clicked");
        }

        private void OnClearClicked()
        {
            EventStore.Clear();
            _currentEvents.Clear();
            _lastEventStoreCount = 0;
            _eventListView.RefreshItems();
            _detailScrollView.Clear();
            UpdateStatus();
            McpLog.Debug("[ActionTraceEditorWindow] Clear clicked");
        }

        private void OnClearFiltersClicked()
        {
            _searchText = string.Empty;
            _searchField.value = string.Empty;
            _minImportance = 0f;
            RefreshEvents();
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

        #endregion

        #region Data Management

        private void RefreshEvents()
        {
            // 性能优化：检查是否有新事件
            int currentStoreCount = EventStore.Count;
            if (currentStoreCount == _lastEventStoreCount && 
                _currentEvents.Count > 0 && 
                string.IsNullOrEmpty(_searchText))
            {
                // 没有新事件且无搜索条件，跳过刷新
                return;
            }
            _lastEventStoreCount = currentStoreCount;

            IEnumerable<ActionTraceViewItem> source = _showContext
                ? _actionTraceQuery.ProjectWithContext(EventStore.QueryWithContext(DefaultQueryLimit))
                : _actionTraceQuery.Project(EventStore.Query(DefaultQueryLimit));

            source = ApplySorting(source);

            // 性能优化：使用预分配的列表
            var filtered = new List<ActionTraceViewItem>(DefaultQueryLimit);
            foreach (var item in source)
            {
                if (FilterEvent(item))
                    filtered.Add(item);
            }

            _currentEvents.Clear();
            _currentEvents.AddRange(filtered);

            _eventListView.RefreshItems();
            UpdateDisplayStates();
            UpdateStatus();
            UpdateFilterSummary();
        }

        private void UpdateDisplayStates()
        {
            bool hasEvents = _currentEvents.Count > 0;

            _eventListView.style.display = hasEvents ? DisplayStyle.Flex : DisplayStyle.None;

            bool hasFilters = !string.IsNullOrEmpty(_searchText) || _minImportance > 0;

            if (!hasEvents)
            {
                if (hasFilters)
                {
                    _noResultsState.style.display = DisplayStyle.Flex;
                    _emptyState.style.display = DisplayStyle.None;
                    UpdateNoResultsText();
                }
                else
                {
                    _emptyState.style.display = DisplayStyle.Flex;
                    _noResultsState.style.display = DisplayStyle.None;
                }
            }
            else
            {
                _emptyState.style.display = DisplayStyle.None;
                _noResultsState.style.display = DisplayStyle.None;
            }
        }

        private void UpdateNoResultsText()
        {
            _stringBuilder.Clear();
            if (!string.IsNullOrEmpty(_searchText))
            {
                _stringBuilder.Append("Search: \"");
                _stringBuilder.Append(_searchText);
                _stringBuilder.Append("\"");
            }
            if (_minImportance > 0)
            {
                if (_stringBuilder.Length > 0)
                    _stringBuilder.Append("\n");
                _stringBuilder.Append("Importance: ≥");
                _stringBuilder.Append(_minImportance.ToString("F2"));
            }

            _noResultsFiltersLabel.text = _stringBuilder.ToString();
        }

        private void UpdateFilterSummary()
        {
            if (_filterSummaryBar == null) return;

            bool hasActiveFilters = !string.IsNullOrEmpty(_searchText) || _minImportance > 0;

            if (hasActiveFilters)
            {
                _filterSummaryBar.style.display = DisplayStyle.Flex;

                _stringBuilder.Clear();
                _stringBuilder.Append("Active filters: ");

                bool first = true;
                if (!string.IsNullOrEmpty(_searchText))
                {
                    _stringBuilder.Append("search: \"");
                    _stringBuilder.Append(_searchText);
                    _stringBuilder.Append("\"");
                    first = false;
                }
                if (_minImportance > 0)
                {
                    if (!first)
                        _stringBuilder.Append(", ");
                    _stringBuilder.Append("importance: ");
                    _stringBuilder.Append(_minImportance.ToString("F2"));
                    _stringBuilder.Append("+");
                }

                _stringBuilder.Append(" | Showing ");
                _stringBuilder.Append(_currentEvents.Count);
                _stringBuilder.Append(" events");

                _filterSummaryText.text = _stringBuilder.ToString();
            }
            else
            {
                _filterSummaryBar.style.display = DisplayStyle.None;
            }
        }

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

            if (ActionTraceSettings.Instance != null)
                ActionTraceSettings.Instance.Filtering.BypassImportanceFilter = true;

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

        #endregion

        #region Status Updates

        private void UpdateStatus()
        {
            var totalEvents = EventStore.Count;
            if (_eventCountBadge != null)
                _eventCountBadge.text = totalEvents.ToString();

            if (_eventListCountLabel != null)
                _eventListCountLabel.text = _currentEvents.Count.ToString();

            if (_countLabel != null)
                _countLabel.text = $"{_currentEvents.Count}";

            if (_statusLabel != null)
                _statusLabel.text = DateTime.Now.ToString("HH:mm:ss");

            if (_modeLabel != null)
                _modeLabel.text = _sortMode == SortMode.ByTimeDesc ? "Time" : "AI";
        }

        #endregion

        #region Utility Methods

        private string GetEventTypeIconCached(string eventType)
        {
            if (_iconCache.TryGetValue(eventType, out var icon))
                return icon;

            icon = eventType switch
            {
                "ASSET_CHANGE" => "📝",
                "COMPILATION" => "⚙️",
                "PROPERTY_EDIT" => "🔧",
                "SCENE_SAVE" => "🎨",
                "SELECTION" => "📦",
                "MENU_ACTION" => "🔨",
                "BUILD_START" => "💾",
                "ERROR" => "⚠️",
                _ => "•"
            };

            _iconCache[eventType] = icon;
            return icon;
        }

        private string FormatBytes(int bytes)
        {
            return bytes < 1024 ? $"{bytes} B" :
                   bytes < 1024 * 1024 ? $"{bytes / 1024.0:F1} KB" :
                   $"{bytes / (1024.0 * 1024.0):F1} MB";
        }

        private string SanitizeClassName(string eventType)
        {
            return eventType?.Replace("_", "-").Replace(" ", "-") ?? "unknown";
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

            if (ActionTraceSettings.Instance != null && _previousBypassImportanceFilter.HasValue)
            {
                ActionTraceSettings.Instance.Filtering.BypassImportanceFilter = _previousBypassImportanceFilter.Value;
            }
        }

        private void OnEditorUpdate()
        {
            // 性能优化：使用时间间隔控制刷新频率
            if (_isScheduledRefreshActive && 
                EditorApplication.timeSinceStartup - _lastRefreshTime > RefreshInterval)
            {
                _lastRefreshTime = EditorApplication.timeSinceStartup;
                RefreshEvents();
            }
        }

        #endregion
    }
}