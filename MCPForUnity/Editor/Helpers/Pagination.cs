using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MCPForUnity.Editor.Helpers
{
    /// <summary>
    /// Standard pagination request for all paginated tool operations.
    /// Provides consistent handling of page_size/pageSize and cursor/page_number parameters.
    /// </summary>
    public class PaginationRequest
    {
        /// <summary>
        /// Number of items per page. Default is 50.
        /// </summary>
        public int PageSize { get; set; } = 50;

        /// <summary>
        /// 0-based cursor position for the current page.
        /// </summary>
        public int Cursor { get; set; } = 0;

        /// <summary>
        /// Creates a PaginationRequest from JObject parameters.
        /// Accepts both snake_case and camelCase parameter names for flexibility.
        /// Converts 1-based page_number to 0-based cursor if needed.
        /// </summary>
        public static PaginationRequest FromParams(JObject @params, int defaultPageSize = 50)
        {
            if (@params == null)
                return new PaginationRequest { PageSize = defaultPageSize };

            // Accept both page_size and pageSize
            int pageSize = ParamCoercion.CoerceInt(
                @params["page_size"] ?? @params["pageSize"], 
                defaultPageSize
            );

            // Accept both cursor (0-based) and page_number (convert 1-based to 0-based)
            var cursorToken = @params["cursor"];
            var pageNumberToken = @params["page_number"] ?? @params["pageNumber"];

            int cursor;
            if (cursorToken != null)
            {
                cursor = ParamCoercion.CoerceInt(cursorToken, 0);
            }
            else if (pageNumberToken != null)
            {
                // Convert 1-based page_number to 0-based cursor
                int pageNumber = ParamCoercion.CoerceInt(pageNumberToken, 1);
                cursor = (pageNumber - 1) * pageSize;
                if (cursor < 0) cursor = 0;
            }
            else
            {
                cursor = 0;
            }

            return new PaginationRequest
            {
                PageSize = pageSize > 0 ? pageSize : defaultPageSize,
                Cursor = cursor
            };
        }
    }

    /// <summary>
    /// Standard pagination response for all paginated tool operations.
    /// Provides consistent response structure across all tools.
    /// </summary>
    /// <typeparam name="T">The type of items in the paginated list</typeparam>
    public class PaginationResponse<T>
    {
        /// <summary>
        /// The items on the current page.
        /// </summary>
        [JsonProperty("items")]
        public List<T> Items { get; set; } = new List<T>();

        /// <summary>
        /// The cursor position for the current page (0-based).
        /// </summary>
        [JsonProperty("cursor")]
        public int Cursor { get; set; }

        /// <summary>
        /// The cursor for the next page, or null if this is the last page.
        /// </summary>
        [JsonProperty("nextCursor")]
        public int? NextCursor { get; set; }

        /// <summary>
        /// Total number of items across all pages.
        /// </summary>
        [JsonProperty("totalCount")]
        public int TotalCount { get; set; }

        /// <summary>
        /// Number of items per page.
        /// </summary>
        [JsonProperty("pageSize")]
        public int PageSize { get; set; }

        /// <summary>
        /// Whether there are more items after this page.
        /// </summary>
        [JsonProperty("hasMore")]
        public bool HasMore => NextCursor.HasValue;

        /// <summary>
        /// Creates a PaginationResponse from a full list of items and pagination parameters.
        /// </summary>
        /// <param name="allItems">The full list of items to paginate</param>
        /// <param name="request">The pagination request parameters</param>
        /// <returns>A paginated response with the appropriate slice of items</returns>
        public static PaginationResponse<T> Create(IList<T> allItems, PaginationRequest request)
        {
            int totalCount = allItems.Count;
            int cursor = request.Cursor;
            int pageSize = request.PageSize;

            // Clamp cursor to valid range
            if (cursor < 0) cursor = 0;
            if (cursor > totalCount) cursor = totalCount;

            // Get the page of items
            var items = new List<T>();
            int endIndex = System.Math.Min(cursor + pageSize, totalCount);
            for (int i = cursor; i < endIndex; i++)
            {
                items.Add(allItems[i]);
            }

            // Calculate next cursor
            int? nextCursor = endIndex < totalCount ? endIndex : (int?)null;

            return new PaginationResponse<T>
            {
                Items = items,
                Cursor = cursor,
                NextCursor = nextCursor,
                TotalCount = totalCount,
                PageSize = pageSize
            };
        }
    }
}

