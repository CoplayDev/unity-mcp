using System;
using System.Linq;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using MCPForUnity.Editor.ActionTrace.Analysis.Query;
using MCPForUnity.Editor.ActionTrace.Core.Store;
using MCPForUnity.Editor.ActionTrace.Core.Settings;
using MCPForUnity.Editor.Helpers;
using System.Collections.Generic;

namespace MCPForUnity.Editor.Windows.ActionTraceEditorWindow
{
    /// <summary>
    /// Section controller for the event list in ActionTrace window.
    /// Handles ListView, data querying, filtering, and sorting.
    /// </summary>
    internal sealed class EventListSection
    {
        // Constants
        private const int DefaultQueryLimit = 200;
        private const string EventItemClass = "event-item";
        private const string EventTimeClass = "event-time";
        private const string EventTypeIconClass = "event-type-icon";
        private const string EventTypeClass = "event-type";
        private const string EventSummaryClass = "event-summary";
        private const string EventItemDetailRowClass = "event-item-detail-row";
        private const string EventItemDetailTextClass = "event-item-detail-text";
        private const string EventItemBadgesClass = "event-item-badges";
        private const string ImportanceBadgeClass = "importance-badge";
        private const string ContextIndicatorClass = "context-indicator";

        // UI Elements
        private readonly ListView _eventListView;
        private readonly ToolbarToggle _importanceToggle;

        // Data
        private readonly ActionTraceQuery _query;
        private readonly ActionTraceCache _cache;
        private readonly List<ActionTraceQuery.ActionTraceViewItem> _currentEvents = new();

        // Filter state
        private string _searchText = string.Empty;
        private float _uiMinImportance = -1f;
        private bool _forceRefreshNextCall;
        private float EffectiveMinImportance => _uiMinImportance >= 0
            ? _uiMinImportance
            : (ActionTraceSettings.Instance?.Filtering.MinImportanceForRecording ?? 0.4f);

        // Change detection
        private int _lastEventStoreCount = -1;
        private float _lastKnownSettingsImportance;
        private float _lastRefreshedImportance = float.NaN;

        // Events
        public event Action DataChanged;
        public event Action<ActionTraceQuery.ActionTraceViewItem> SelectionChanged;

        public bool ShowSemantics => _importanceToggle != null && _importanceToggle.value;
        public IList<ActionTraceQuery.ActionTraceViewItem> CurrentEvents => _currentEvents;

        public EventListSection(VisualElement root, ActionTraceCache cache)
        {
            _cache = cache;
            _query = new ActionTraceQuery();
            _lastKnownSettingsImportance = ActionTraceSettings.Instance?.Filtering.MinImportanceForRecording ?? 0.4f;

            // Cache UI elements
            _eventListView = root.Q<ListView>("event-list");
            _importanceToggle = root.Q<ToolbarToggle>("importance-toggle");

            SetupListView();
            RegisterCallbacks();
        }

        private void SetupListView()
        {
            if (_eventListView == null) return;

            _eventListView.itemsSource = (System.Collections.IList)_currentEvents;
            _eventListView.selectionType = SelectionType.Single;
            _eventListView.fixedItemHeight = 60;
            _eventListView.virtualizationMethod = CollectionVirtualizationMethod.FixedHeight;
            _eventListView.makeItem = MakeListItem;
            _eventListView.bindItem = BindListItem;
        }

        private void RegisterCallbacks()
        {
            if (_eventListView != null)
            {
                _eventListView.selectionChanged += OnSelectionChanged;
            }

            // Register toggle callbacks
            if (_importanceToggle != null)
            {
                _importanceToggle.RegisterValueChangedCallback(OnImportanceToggleChanged);
            }
        }

        public void UnregisterCallbacks()
        {
            if (_eventListView != null)
            {
                _eventListView.selectionChanged -= OnSelectionChanged;
            }

            if (_importanceToggle != null)
            {
                _importanceToggle.UnregisterValueChangedCallback(OnImportanceToggleChanged);
            }
        }

        private void OnImportanceToggleChanged(ChangeEvent<bool> evt)
        {
            RefreshListView();
        }

        private VisualElement MakeListItem()
        {
            var root = new VisualElement();
            root.AddToClassList(EventItemClass);

            var mainRow = new VisualElement();
            mainRow.AddToClassList("event-item-main-row");

            var time = new Label { name = "time" };
            time.AddToClassList(EventTimeClass);
            mainRow.Add(time);

            var typeIcon = new Label { name = "type-icon" };
            typeIcon.AddToClassList(EventTypeIconClass);
            mainRow.Add(typeIcon);

            var type = new Label { name = "type" };
            type.AddToClassList(EventTypeClass);
            mainRow.Add(type);

            var summary = new Label { name = "summary" };
            summary.AddToClassList(EventSummaryClass);
            mainRow.Add(summary);

            root.Add(mainRow);

            var detailRow = new VisualElement { name = "detail-row" };
            detailRow.AddToClassList(EventItemDetailRowClass);
            detailRow.style.display = DisplayStyle.None;

            var detailText = new Label { name = "detail-text" };
            detailText.AddToClassList(EventItemDetailTextClass);
            detailRow.Add(detailText);

            root.Add(detailRow);

            var badgesRow = new VisualElement { name = "badges-row" };
            badgesRow.AddToClassList(EventItemBadgesClass);
            badgesRow.style.display = DisplayStyle.None;

            var importanceBadge = new Label { name = "importance-badge" };
            importanceBadge.AddToClassList(ImportanceBadgeClass);
            badgesRow.Add(importanceBadge);

            var contextIndicator = new Label { name = "context-indicator" };
            contextIndicator.AddToClassList(ContextIndicatorClass);
            badgesRow.Add(contextIndicator);

            root.Add(badgesRow);

            return root;
        }

        private void BindListItem(VisualElement element, int index)
        {
            if (index < 0 || index >= _currentEvents.Count) return;

            var item = _currentEvents[index];

            // Cache element references
            var timeLabel = element.Q<Label>(className: EventTimeClass);
            var typeIcon = element.Q<Label>(className: EventTypeIconClass);
            var typeLabel = element.Q<Label>(className: EventTypeClass);
            var summaryLabel = element.Q<Label>(className: EventSummaryClass);
            var detailRow = element.Q<VisualElement>(className: EventItemDetailRowClass);
            var detailText = element.Q<Label>(className: EventItemDetailTextClass);
            var badgesRow = element.Q<VisualElement>(className: EventItemBadgesClass);
            var importanceBadge = element.Q<Label>(className: ImportanceBadgeClass);
            var contextIndicator = element.Q<Label>(className: ContextIndicatorClass);

            // Bind data
            if (timeLabel != null && timeLabel.text != item.DisplayTime)
                timeLabel.text = item.DisplayTime;

            var iconText = _cache.GetEventTypeIcon(item.Event.Type);
            if (typeIcon != null && typeIcon.text != iconText)
                typeIcon.text = iconText;

            if (typeLabel != null && typeLabel.text != item.Event.Type)
            {
                typeLabel.text = item.Event.Type;
                typeLabel.ClearClassList();
                typeLabel.AddToClassList(EventTypeClass);
                typeLabel.AddToClassList($"{EventTypeClass}--{SanitizeClassName(item.Event.Type)}");
            }

            if (summaryLabel != null && summaryLabel.text != item.DisplaySummary)
                summaryLabel.text = item.DisplaySummary;

            // Detail row
            if (detailRow != null && detailText != null)
            {
                bool showDetail = _eventListView.selectedIndex == index || !string.IsNullOrEmpty(item.TargetName);
                var targetDisplay = showDetail ? DisplayStyle.Flex : DisplayStyle.None;
                if (detailRow.style.display != targetDisplay)
                    detailRow.style.display = targetDisplay;

                if (!string.IsNullOrEmpty(item.TargetName))
                {
                    var targetText = FormatTargetDisplay(item.TargetInstanceId, item.TargetName);
                    if (detailText.text != targetText)
                        detailText.text = targetText;
                }
            }

            // Badges
            if (badgesRow != null && importanceBadge != null)
            {
                var badgesDisplay = ShowSemantics ? DisplayStyle.Flex : DisplayStyle.None;
                if (badgesRow.style.display != badgesDisplay)
                    badgesRow.style.display = badgesDisplay;

                var categoryUpper = item.ImportanceCategory.ToUpperInvariant();
                if (importanceBadge.text != categoryUpper)
                {
                    importanceBadge.text = categoryUpper;
                    importanceBadge.ClearClassList();
                    importanceBadge.AddToClassList(ImportanceBadgeClass);
                    importanceBadge.AddToClassList($"{ImportanceBadgeClass}--{item.ImportanceCategory.ToLowerInvariant()}");
                }
            }

            // Context indicator
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
                        contextIndicator.AddToClassList(ContextIndicatorClass);
                        contextIndicator.AddToClassList("context-source--System");
                    }
                }
            }
        }

        private void OnSelectionChanged(IEnumerable<object> items)
        {
            var selectedItem = items.FirstOrDefault() as ActionTraceQuery.ActionTraceViewItem;
            SelectionChanged?.Invoke(selectedItem);
        }

        /// <summary>
        /// Set search filter and refresh.
        /// </summary>
        public void SetSearchFilter(string searchText)
        {
            _searchText = searchText?.ToLowerInvariant() ?? string.Empty;
            _forceRefreshNextCall = true;
            RefreshData();
        }

        /// <summary>
        /// Set importance filter and refresh.
        /// </summary>
        public void SetImportanceFilter(float minImportance)
        {
            _uiMinImportance = minImportance;
            _forceRefreshNextCall = true;
            RefreshData();
        }

        /// <summary>
        /// Set sort mode and refresh.
        /// </summary>
        public void SetSortMode(SortMode sortMode)
        {
            if (ActionTraceSettings.Instance != null)
                ActionTraceSettings.Instance.Filtering.BypassImportanceFilter = true;
            _forceRefreshNextCall = true;
            RefreshData();
        }

        /// <summary>
        /// Refresh data from EventStore with smart change detection.
        /// </summary>
        public void RefreshData()
        {
            if (ShouldSkipRefresh())
                return;

            _forceRefreshNextCall = false;  // Reset force flag after checking

            // Query data
            var source = _query.Project(EventStore.Query(DefaultQueryLimit));

            // Apply sorting and materialize to list
            var sorted = ApplySorting(source).ToList();

            // Apply filters
            var filtered = new List<ActionTraceQuery.ActionTraceViewItem>(DefaultQueryLimit);
            foreach (var item in sorted)
            {
                if (FilterEvent(item))
                    filtered.Add(item);
            }

            // Update data
            _currentEvents.Clear();
            _currentEvents.AddRange(filtered);

            // Force ListView to recognize data changes
            if (_eventListView != null)
            {
                _eventListView.itemsSource = null;  // Reset first
                _eventListView.itemsSource = _currentEvents;  // Reassign
                _eventListView.RefreshItems();
            }

            DataChanged?.Invoke();
        }

        /// <summary>
        /// Refresh ListView display to show current data.
        /// </summary>
        public void RefreshListView()
        {
            if (_eventListView != null)
            {
                // Force ListView to recognize data changes
                _eventListView.itemsSource = _currentEvents;
                _eventListView.RefreshItems();
                _eventListView.Rebuild();
            }
        }

        /// <summary>
        /// Clear all events and filters.
        /// </summary>
        public void Clear()
        {
            _currentEvents.Clear();
            _lastEventStoreCount = 0;
            _searchText = string.Empty;
            _uiMinImportance = -1f;

            // Force ListView refresh
            RefreshListView();
            DataChanged?.Invoke();
        }

        private bool ShouldSkipRefresh()
        {
            // Always refresh if manually triggered (filter/sort/search changed)
            if (_forceRefreshNextCall)
                return false;

            // Check if Settings value changed
            float currentSettingsImportance = ActionTraceSettings.Instance?.Filtering.MinImportanceForRecording ?? 0.4f;
            bool settingsChanged = currentSettingsImportance != _lastKnownSettingsImportance;

            if (settingsChanged)
                _lastKnownSettingsImportance = currentSettingsImportance;

            // Check if effective filter value changed
            float effectiveImportance = EffectiveMinImportance;
            bool effectiveImportanceChanged = !float.IsNaN(_lastRefreshedImportance) &&
                                                 !Mathf.Approximately(effectiveImportance, _lastRefreshedImportance);

            if (effectiveImportanceChanged)
                _lastRefreshedImportance = effectiveImportance;

            // If filter changed, always refresh regardless of new events
            if (effectiveImportanceChanged)
                return false;

            // Check for new events
            int currentStoreCount = EventStore.Count;
            bool noNewEvents = currentStoreCount == _lastEventStoreCount;
            bool noSearchText = string.IsNullOrEmpty(_searchText);
            bool inStaticMode = _uiMinImportance >= 0;

            // Skip if nothing changed
            if (noNewEvents && noSearchText && !effectiveImportanceChanged &&
                (inStaticMode || !settingsChanged) && _currentEvents.Count > 0)
            {
                return true;
            }

            _lastEventStoreCount = currentStoreCount;
            return false;
        }

        private IEnumerable<ActionTraceQuery.ActionTraceViewItem> ApplySorting(IEnumerable<ActionTraceQuery.ActionTraceViewItem> source)
        {
            // Sort mode is controlled externally via _sortMode
            // For now, default to time-based sorting
            return source.OrderByDescending(e => e.Event.TimestampUnixMs);
        }

        private bool FilterEvent(ActionTraceQuery.ActionTraceViewItem e)
        {
            if (EffectiveMinImportance > 0 && e.ImportanceScore < EffectiveMinImportance)
                return false;

            if (!string.IsNullOrEmpty(_searchText))
            {
                return e.DisplaySummaryLower.Contains(_searchText)
                    || e.DisplayTargetIdLower.Contains(_searchText)
                    || e.Event.Type.ToLowerInvariant().Contains(_searchText);
            }

            return true;
        }

        private string FormatTargetDisplay(int? instanceId, string displayName)
        {
            if (instanceId.HasValue)
                return $"{displayName} ({instanceId.Value})";
            return displayName;
        }

        private string SanitizeClassName(string eventType)
        {
            return eventType?.Replace("_", "-").Replace(" ", "-") ?? "unknown";
        }
    }
}
