using System;
using System.Collections.Generic;
using System.Linq;
using MCPForUnity.Editor.ActionTrace.Core;
using MCPForUnity.Editor.ActionTrace.Semantics;

namespace MCPForUnity.Editor.ActionTrace.Query
{
    /// <summary>
    /// Sort order for query results.
    /// </summary>
    public enum QuerySortOrder
    {
        NewestFirst,      // Descending by timestamp
        OldestFirst,      // Ascending by timestamp
        HighestImportance,
        LowestImportance,
        MostRecentTarget
    }

    /// <summary>
    /// Time range filter for queries.
    /// </summary>
    public readonly struct QueryTimeRange
    {
        public readonly long StartMs;
        public readonly long EndMs;

        public QueryTimeRange(long startMs, long endMs)
        {
            StartMs = startMs;
            EndMs = endMs;
        }

        /// <summary>
        /// Last N minutes from now.
        /// </summary>
        public static QueryTimeRange LastMinutes(int minutes)
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            return new QueryTimeRange(now - minutes * 60000, now);
        }

        /// <summary>
        /// Last N hours from now.
        /// </summary>
        public static QueryTimeRange LastHours(int hours)
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            return new QueryTimeRange(now - hours * 3600000, now);
        }

        /// <summary>
        /// Between two Unix timestamps.
        /// </summary>
        public static QueryTimeRange Between(long startMs, long endMs)
        {
            return new QueryTimeRange(startMs, endMs);
        }

        /// <summary>
        /// Since a specific Unix timestamp.
        /// </summary>
        public static QueryTimeRange Since(long timestampMs)
        {
            return new QueryTimeRange(timestampMs, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        }
    }

    /// <summary>
    /// Fluent builder for querying ActionTrace events.
    ///
    /// Provides a chainable API for common query patterns:
    ///
    /// var results = EventQuery.Query()
    ///     .OfType(EventTypes.PropertyModified)
    ///     .WithImportance(ImportanceLevel.High)
    ///     .InLastMinutes(10)
    ///     .OrderBy(QuerySortOrder.NewestFirst)
    ///     .Limit(50)
    ///     .Execute();
    /// </summary>
    public sealed class EventQueryBuilder
    {
        private readonly IEventScorer _scorer;
        private readonly IEventCategorizer _categorizer;

        // Filter state
        private HashSet<string> _includedTypes;
        private HashSet<string> _excludedTypes;
        private HashSet<EventCategory> _includedCategories;
        private HashSet<EventCategory> _excludedCategories;
        private HashSet<string> _includedTargets;
        private HashSet<string> _searchTerms;
        private float? _minImportance;
        private float? _maxImportance;
        private QueryTimeRange? _timeRange;
        private QuerySortOrder _sortOrder = QuerySortOrder.NewestFirst;
        private int? _limit;
        private int? _offset;

        public EventQueryBuilder(IEventScorer scorer = null, IEventCategorizer categorizer = null)
        {
            _scorer = scorer ?? new DefaultEventScorer();
            _categorizer = categorizer ?? new DefaultCategorizer();
        }

        // ========== Type Filters ==========

        /// <summary>
        /// Filter to events of the specified type.
        /// </summary>
        public EventQueryBuilder OfType(string eventType)
        {
            _includedTypes ??= new HashSet<string>();
            _includedTypes.Add(eventType);
            return this;
        }

        /// <summary>
        /// Filter to events of any of the specified types.
        /// </summary>
        public EventQueryBuilder OfTypes(params string[] eventTypes)
        {
            _includedTypes ??= new HashSet<string>();
            foreach (string type in eventTypes)
                _includedTypes.Add(type);
            return this;
        }

        /// <summary>
        /// Exclude events of the specified type.
        /// </summary>
        public EventQueryBuilder NotOfType(string eventType)
        {
            _excludedTypes ??= new HashSet<string>();
            _excludedTypes.Add(eventType);
            return this;
        }

        /// <summary>
        /// Exclude events of any of the specified types.
        /// </summary>
        public EventQueryBuilder NotOfTypes(params string[] eventTypes)
        {
            _excludedTypes ??= new HashSet<string>();
            foreach (string type in eventTypes)
                _excludedTypes.Add(type);
            return this;
        }

        // ========== Category Filters ==========

        /// <summary>
        /// Filter to events in the specified category.
        /// </summary>
        public EventQueryBuilder InCategory(EventCategory category)
        {
            _includedCategories ??= new HashSet<EventCategory>();
            _includedCategories.Add(category);
            return this;
        }

        /// <summary>
        /// Filter to events in any of the specified categories.
        /// </summary>
        public EventQueryBuilder InCategories(params EventCategory[] categories)
        {
            _includedCategories ??= new HashSet<EventCategory>();
            foreach (var cat in categories)
                _includedCategories.Add(cat);
            return this;
        }

        /// <summary>
        /// Exclude events in the specified category.
        /// </summary>
        public EventQueryBuilder NotInCategory(EventCategory category)
        {
            _excludedCategories ??= new HashSet<EventCategory>();
            _excludedCategories.Add(category);
            return this;
        }

        // ========== Target Filters ==========

        /// <summary>
        /// Filter to events for the specified target ID.
        /// </summary>
        public EventQueryBuilder ForTarget(string targetId)
        {
            _includedTargets ??= new HashSet<string>();
            _includedTargets.Add(targetId);
            return this;
        }

        /// <summary>
        /// Filter to events for any of the specified targets.
        /// </summary>
        public EventQueryBuilder ForTargets(params string[] targetIds)
        {
            _includedTargets ??= new HashSet<string>();
            foreach (string id in targetIds)
                _includedTargets.Add(id);
            return this;
        }

        // ========== Importance Filters ==========

        /// <summary>
        /// Filter to events with minimum importance score.
        /// </summary>
        public EventQueryBuilder WithMinImportance(float minScore)
        {
            _minImportance = minScore;
            return this;
        }

        /// <summary>
        /// Filter to events with maximum importance score.
        /// </summary>
        public EventQueryBuilder WithMaxImportance(float maxScore)
        {
            _maxImportance = maxScore;
            return this;
        }

        /// <summary>
        /// Filter to events within an importance range.
        /// </summary>
        public EventQueryBuilder WithImportanceBetween(float minScore, float maxScore)
        {
            _minImportance = minScore;
            _maxImportance = maxScore;
            return this;
        }

        /// <summary>
        /// Filter to critical events only.
        /// </summary>
        public EventQueryBuilder CriticalOnly()
        {
            return WithMinImportance(0.9f);
        }

        /// <summary>
        /// Filter to important events (high and critical).
        /// </summary>
        public EventQueryBuilder ImportantOnly()
        {
            return WithMinImportance(0.7f);
        }

        // ========== Time Filters ==========

        /// <summary>
        /// Filter to events within the specified time range.
        /// </summary>
        public EventQueryBuilder InTimeRange(QueryTimeRange range)
        {
            _timeRange = range;
            return this;
        }

        /// <summary>
        /// Filter to events in the last N minutes.
        /// </summary>
        public EventQueryBuilder InLastMinutes(int minutes)
        {
            _timeRange = QueryTimeRange.LastMinutes(minutes);
            return this;
        }

        /// <summary>
        /// Filter to events in the last N hours.
        /// </summary>
        public EventQueryBuilder InLastHours(int hours)
        {
            _timeRange = QueryTimeRange.LastHours(hours);
            return this;
        }

        /// <summary>
        /// Filter to events since a specific timestamp.
        /// </summary>
        public EventQueryBuilder Since(long timestampMs)
        {
            _timeRange = QueryTimeRange.Since(timestampMs);
            return this;
        }

        /// <summary>
        /// Filter to events between two timestamps.
        /// </summary>
        public EventQueryBuilder Between(long startMs, long endMs)
        {
            _timeRange = QueryTimeRange.Between(startMs, endMs);
            return this;
        }

        // ========== Search Filters ==========

        /// <summary>
        /// Filter to events containing any of the search terms (case-insensitive).
        /// Searches in summary text and target ID.
        /// </summary>
        public EventQueryBuilder WithSearchTerm(string term)
        {
            _searchTerms ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _searchTerms.Add(term);
            return this;
        }

        /// <summary>
        /// Filter to events containing all of the search terms.
        /// </summary>
        public EventQueryBuilder WithAllSearchTerms(params string[] terms)
        {
            _searchTerms ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string term in terms)
                _searchTerms.Add(term);
            return this;
        }

        // ========== Sort & Pagination ==========

        /// <summary>
        /// Set the sort order for results.
        /// </summary>
        public EventQueryBuilder OrderBy(QuerySortOrder order)
        {
            _sortOrder = order;
            return this;
        }

        /// <summary>
        /// Limit the number of results.
        /// </summary>
        public EventQueryBuilder Limit(int count)
        {
            _limit = count;
            return this;
        }

        /// <summary>
        /// Skip the first N results (for pagination).
        /// </summary>
        public EventQueryBuilder Skip(int count)
        {
            _offset = count;
            return this;
        }

        /// <summary>
        /// Set pagination with page number and page size.
        /// </summary>
        public EventQueryBuilder Page(int pageNumber, int pageSize)
        {
            _offset = pageNumber * pageSize;
            _limit = pageSize;
            return this;
        }

        // ========== Execution ==========

        /// <summary>
        /// Execute the query and return matching events.
        /// </summary>
        public List<EditorEvent> Execute()
        {
            // Get all events from store
            var allEvents = EventStore.Query(int.MaxValue);

            // Apply filters
            var filtered = allEvents.Where(MatchesFilters);

            // Sort
            filtered = ApplySorting(filtered);

            // Pagination
            if (_offset.HasValue)
                filtered = filtered.Skip(_offset.Value);
            if (_limit.HasValue)
                filtered = filtered.Take(_limit.Value);

            return filtered.ToList();
        }

        /// <summary>
        /// Execute the query and return projected view items.
        /// </summary>
        public List<ActionTraceQuery.ActionTraceViewItem> ExecuteProjected()
        {
            var events = Execute();
            var query = new ActionTraceQuery(_scorer, _categorizer, null);
            return query.Project(events).ToList();
        }

        /// <summary>
        /// Execute the query and return the first matching event, or null.
        /// </summary>
        public EditorEvent FirstOrDefault()
        {
            return Execute().FirstOrDefault();
        }

        /// <summary>
        /// Execute the query and return the last matching event, or null.
        /// </summary>
        public EditorEvent LastOrDefault()
        {
            return Execute().LastOrDefault();
        }

        /// <summary>
        /// Count events matching the query (without fetching full results).
        /// </summary>
        public int Count()
        {
            var allEvents = EventStore.Query(int.MaxValue);
            return allEvents.Count(MatchesFilters);
        }

        /// <summary>
        /// Check if any events match the query.
        /// </summary>
        public bool Any()
        {
            var allEvents = EventStore.Query(int.MaxValue);
            return allEvents.Any(MatchesFilters);
        }

        // ========== Internal ==========

        private bool MatchesFilters(EditorEvent evt)
        {
            // Type filters
            if (_includedTypes != null && !_includedTypes.Contains(evt.Type))
                return false;

            if (_excludedTypes != null && _excludedTypes.Contains(evt.Type))
                return false;

            // Category filters
            if (_includedCategories != null || _excludedCategories != null)
            {
                var meta = EventTypes.Metadata.Get(evt.Type);
                EventCategory category = meta.Category;

                if (_includedCategories != null && !_includedCategories.Contains(category))
                    return false;

                if (_excludedCategories != null && _excludedCategories.Contains(category))
                    return false;
            }

            // Target filters
            if (_includedTargets != null && !_includedTargets.Contains(evt.TargetId))
                return false;

            // Importance filters
            float score = _scorer.Score(evt);
            if (_minImportance.HasValue && score < _minImportance.Value)
                return false;

            if (_maxImportance.HasValue && score > _maxImportance.Value)
                return false;

            // Time range filters
            if (_timeRange.HasValue)
            {
                var range = _timeRange.Value;
                if (evt.TimestampUnixMs < range.StartMs || evt.TimestampUnixMs > range.EndMs)
                    return false;
            }

            // Search filters
            if (_searchTerms != null && _searchTerms.Count > 0)
            {
                string summary = (evt.GetSummary() ?? "").ToLowerInvariant();
                string target = (evt.TargetId ?? "").ToLowerInvariant();

                bool matchesAny = false;
                foreach (string term in _searchTerms)
                {
                    string lowerTerm = term.ToLowerInvariant();
                    if (summary.Contains(lowerTerm) || target.Contains(lowerTerm))
                    {
                        matchesAny = true;
                        break;
                    }
                }

                if (!matchesAny)
                    return false;
            }

            return true;
        }

        private IEnumerable<EditorEvent> ApplySorting(IEnumerable<EditorEvent> source)
        {
            return _sortOrder switch
            {
                QuerySortOrder.NewestFirst => source.OrderByDescending(e => e.TimestampUnixMs),
                QuerySortOrder.OldestFirst => source.OrderBy(e => e.TimestampUnixMs),
                QuerySortOrder.HighestImportance => source.OrderByDescending(e => _scorer.Score(e)),
                QuerySortOrder.LowestImportance => source.OrderBy(e => _scorer.Score(e)),
                QuerySortOrder.MostRecentTarget => source.GroupBy(e => e.TargetId)
                    .Select(g => g.OrderByDescending(e => e.TimestampUnixMs).First())
                    .OrderByDescending(e => e.TimestampUnixMs),
                _ => source.OrderByDescending(e => e.TimestampUnixMs)
            };
        }
    }

    /// <summary>
    /// Static entry point for creating queries.
    /// </summary>
    public static class EventQuery
    {
        /// <summary>
        /// Create a new query builder.
        /// </summary>
        public static EventQueryBuilder Query()
        {
            return new EventQueryBuilder();
        }

        /// <summary>
        /// Create a query with custom semantic components.
        /// </summary>
        public static EventQueryBuilder Query(IEventScorer scorer, IEventCategorizer categorizer = null)
        {
            return new EventQueryBuilder(scorer, categorizer);
        }

        /// <summary>
        /// Get all events (unfiltered).
        /// </summary>
        public static List<EditorEvent> All()
        {
            return EventStore.Query(int.MaxValue).ToList();
        }

        /// <summary>
        /// Get recent events from the last N minutes.
        /// </summary>
        public static List<EditorEvent> Recent(int minutes = 10)
        {
            return Query()
                .InLastMinutes(minutes)
                .Execute();
        }

        /// <summary>
        /// Get events for a specific target.
        /// </summary>
        public static List<EditorEvent> ForTarget(string targetId)
        {
            return Query()
                .ForTarget(targetId)
                .Execute();
        }

        /// <summary>
        /// Get critical events.
        /// </summary>
        public static List<EditorEvent> Critical()
        {
            return Query()
                .CriticalOnly()
                .Execute();
        }

        /// <summary>
        /// Get events of a specific type.
        /// </summary>
        public static List<EditorEvent> ByType(string eventType)
        {
            return Query()
                .OfType(eventType)
                .Execute();
        }

        /// <summary>
        /// Search events by text.
        /// </summary>
        public static List<EditorEvent> Search(string searchTerm)
        {
            return Query()
                .WithSearchTerm(searchTerm)
                .Execute();
        }

        /// <summary>
        /// Get the most recent N events.
        /// </summary>
        public static List<EditorEvent> Latest(int count = 50)
        {
            return Query()
                .OrderBy(QuerySortOrder.NewestFirst)
                .Limit(count)
                .Execute();
        }
    }

    /// <summary>
    /// Extension methods for IEnumerable<EditorEvent> to enable post-query filtering.
    /// </summary>
    public static class EventQueryExtensions
    {
        /// <summary>
        /// Filter to events of specific types.
        /// </summary>
        public static IEnumerable<EditorEvent> OfTypes(this IEnumerable<EditorEvent> source, params string[] types)
        {
            var typeSet = new HashSet<string>(types);
            return source.Where(e => typeSet.Contains(e.Type));
        }

        /// <summary>
        /// Filter to events within a time range.
        /// </summary>
        public static IEnumerable<EditorEvent> InRange(this IEnumerable<EditorEvent> source, long startMs, long endMs)
        {
            return source.Where(e => e.TimestampUnixMs >= startMs && e.TimestampUnixMs <= endMs);
        }

        /// <summary>
        /// Filter to recent events within N minutes.
        /// </summary>
        public static IEnumerable<EditorEvent> Recent(this IEnumerable<EditorEvent> source, int minutes)
        {
            long threshold = DateTimeOffset.UtcNow.AddMinutes(-minutes).ToUnixTimeMilliseconds();
            return source.Where(e => e.TimestampUnixMs >= threshold);
        }

        /// <summary>
        /// Filter to events for a specific target.
        /// </summary>
        public static IEnumerable<EditorEvent> ForTarget(this IEnumerable<EditorEvent> source, string targetId)
        {
            return source.Where(e => e.TargetId == targetId);
        }

        /// <summary>
        /// Sort events by timestamp (newest first).
        /// </summary>
        public static IEnumerable<EditorEvent> NewestFirst(this IEnumerable<EditorEvent> source)
        {
            return source.OrderByDescending(e => e.TimestampUnixMs);
        }

        /// <summary>
        /// Sort events by timestamp (oldest first).
        /// </summary>
        public static IEnumerable<EditorEvent> OldestFirst(this IEnumerable<EditorEvent> source)
        {
            return source.OrderBy(e => e.TimestampUnixMs);
        }

        /// <summary>
        /// Get unique target IDs from events.
        /// </summary>
        public static IEnumerable<string> UniqueTargets(this IEnumerable<EditorEvent> source)
        {
            return source.Select(e => e.TargetId)
                .Where(id => !string.IsNullOrEmpty(id))
                .Distinct();
        }

        /// <summary>
        /// Group events by target ID.
        /// </summary>
        public static IEnumerable<IGrouping<string, EditorEvent>> GroupByTarget(this IEnumerable<EditorEvent> source)
        {
            return source.Where(e => !string.IsNullOrEmpty(e.TargetId))
                .GroupBy(e => e.TargetId);
        }

        /// <summary>
        /// Group events by type.
        /// </summary>
        public static IEnumerable<IGrouping<string, EditorEvent>> GroupByType(this IEnumerable<EditorEvent> source)
        {
            return source.GroupBy(e => e.Type ?? "Unknown");
        }

        /// <summary>
        /// Convert to list (convenience method).
        /// </summary>
        public static List<EditorEvent> ToList(this IEnumerable<EditorEvent> source)
        {
            return new List<EditorEvent>(source);
        }
    }
}
