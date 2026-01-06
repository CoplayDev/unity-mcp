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

            // Parse search parameters
            string searchMethod = ParamCoercion.CoerceString(@params["searchMethod"] ?? @params["search_method"], "by_name");
            string searchTerm = ParamCoercion.CoerceString(@params["searchTerm"] ?? @params["search_term"] ?? @params["target"], null);

            if (string.IsNullOrEmpty(searchTerm))
            {
                return new ErrorResponse("'searchTerm' or 'target' parameter is required.");
            }

            // Pagination parameters
            int pageSize = ParamCoercion.CoerceInt(@params["pageSize"] ?? @params["page_size"], 50);
            int cursor = ParamCoercion.CoerceInt(@params["cursor"], 0);

            // Search options
            bool includeInactive = ParamCoercion.CoerceBool(@params["includeInactive"] ?? @params["searchInactive"] ?? @params["include_inactive"], false);

            // Validate pageSize bounds
            pageSize = Mathf.Clamp(pageSize, 1, 500);

            try
            {
                // Get all matching instance IDs
                var allIds = GameObjectLookup.SearchGameObjects(searchMethod, searchTerm, includeInactive, 0);
                int totalCount = allIds.Count;

                // Apply pagination
                var pagedIds = allIds.Skip(cursor).Take(pageSize).ToList();

                // Calculate next cursor
                int? nextCursor = cursor + pagedIds.Count < totalCount ? cursor + pagedIds.Count : (int?)null;

                return new
                {
                    success = true,
                    data = new
                    {
                        instanceIDs = pagedIds,
                        pageSize = pageSize,
                        cursor = cursor,
                        nextCursor = nextCursor,
                        totalCount = totalCount,
                        hasMore = nextCursor.HasValue
                    }
                };
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[FindGameObjects] Error searching GameObjects: {ex.Message}");
                return new ErrorResponse($"Error searching GameObjects: {ex.Message}");
            }
        }
    }
}

