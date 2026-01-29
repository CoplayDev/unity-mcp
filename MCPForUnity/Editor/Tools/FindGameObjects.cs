using System.Collections.Generic;
using System.Linq;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace MCPForUnity.Editor.Tools
{
    /// <summary>
    /// Tool for searching GameObjects in the scene.
    /// Returns only instance IDs with pagination support.
    /// 
    /// This is a focused search tool that returns lightweight results (IDs only).
    /// For detailed GameObject data, use the unity://scene/gameobject/{id} resource.
    /// </summary>
    [McpForUnityTool("find_gameobjects")]
    public static class FindGameObjects
    {
        /// <summary>
        /// Handles the find_gameobjects command.
        /// </summary>
        /// <param name="params">Command parameters</param>
        /// <returns>Paginated list of instance IDs</returns>
        public static object HandleCommand(JObject @params)
        {
            if (@params == null)
            {
                return new ErrorResponse("Parameters cannot be null.");
            }

            var p = new ToolParams(@params);

            // Parse search parameters
            string searchMethod = p.Get("searchMethod", "by_name");

            // Try searchTerm, search_term, or target (for backwards compatibility)
            string searchTerm = p.Get("searchTerm");
            if (string.IsNullOrEmpty(searchTerm))
            {
                searchTerm = p.Get("target");
            }

            if (string.IsNullOrEmpty(searchTerm))
            {
                return new ErrorResponse("'searchTerm' or 'target' parameter is required.");
            }

            // Pagination parameters using standard PaginationRequest
            var pagination = PaginationRequest.FromParams(@params, defaultPageSize: 50);
            pagination.PageSize = Mathf.Clamp(pagination.PageSize, 1, 500);

            // Search options (supports multiple parameter name variants)
            bool includeInactive = p.GetBool("includeInactive", false) ||
                                   p.GetBool("searchInactive", false);

            try
            {
                // Get all matching instance IDs
                var allIds = GameObjectLookup.SearchGameObjects(searchMethod, searchTerm, includeInactive, 0);
                
                // Use standard pagination response
                var paginatedResult = PaginationResponse<int>.Create(allIds, pagination);

                return new SuccessResponse("Found GameObjects", new
                {
                    instanceIDs = paginatedResult.Items,
                    pageSize = paginatedResult.PageSize,
                    cursor = paginatedResult.Cursor,
                    nextCursor = paginatedResult.NextCursor,
                    totalCount = paginatedResult.TotalCount,
                    hasMore = paginatedResult.HasMore
                });
            }
            catch (System.Exception ex)
            {
                McpLog.Error($"[FindGameObjects] Error searching GameObjects: {ex.Message}");
                return new ErrorResponse($"Error searching GameObjects: {ex.Message}");
            }
        }
    }
}
