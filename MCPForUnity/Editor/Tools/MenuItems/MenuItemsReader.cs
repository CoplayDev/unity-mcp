using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using MCPForUnity.Editor.Helpers;

namespace MCPForUnity.Editor.Tools.MenuItems
{
    /// <summary>
    /// Provides read/list/exists capabilities for Unity menu items with caching.
    /// </summary>
    public static class MenuItemsReader
    {
        private static List<string> _cached;

        [InitializeOnLoadMethod]
        private static void Build() => Refresh();

        /// <summary>
        /// Returns the cached list, refreshing if necessary.
        /// </summary>
        public static IReadOnlyList<string> AllMenuItems() => _cached ??= Refresh();

        /// <summary>
        /// Rebuilds the cached list from reflection.
        /// </summary>
        private static List<string> Refresh()
        {
            try
            {
                var methods = TypeCache.GetMethodsWithAttribute<MenuItem>();
                _cached = methods
                    // Methods can have multiple [MenuItem] attributes; collect them all
                    .SelectMany(m => m
                        .GetCustomAttributes(typeof(MenuItem), false)
                        .OfType<MenuItem>()
                        .Select(attr => attr.menuItem))
                    .Where(s => !string.IsNullOrEmpty(s))
                    .Distinct(StringComparer.Ordinal) // Ensure no duplicates
                    .OrderBy(s => s, StringComparer.Ordinal) // Ensure consistent ordering
                    .ToList();
                return _cached;
            }
            catch (Exception e)
            {
                McpLog.Error($"[MenuItemsReader] Failed to scan menu items: {e}");
                _cached = _cached ?? new List<string>();
                return _cached;
            }
        }

        /// <summary>
        /// Returns a list of menu items. Optional 'search' param filters results.
        /// </summary>
        public static object List(JObject @params)
        {
            string search = @params["search"]?.ToString();
            bool doRefresh = @params["refresh"]?.ToObject<bool>() ?? false;
            if (doRefresh || _cached == null)
            {
                Refresh();
            }

            IEnumerable<string> result = _cached ?? Enumerable.Empty<string>();
            if (!string.IsNullOrEmpty(search))
            {
                result = result.Where(s => s.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0);
            }

            return Response.Success("Menu items retrieved.", result.ToList());
        }

        /// <summary>
        /// Checks if a given menu path exists in the cache.
        /// </summary>
        public static object Exists(JObject @params)
        {
            string menuPath = @params["menu_path"]?.ToString() ?? @params["menuPath"]?.ToString();
            if (string.IsNullOrWhiteSpace(menuPath))
            {
                return Response.Error("Required parameter 'menu_path' or 'menuPath' is missing or empty.");
            }

            bool doRefresh = @params["refresh"]?.ToObject<bool>() ?? false;
            if (doRefresh || _cached == null)
            {
                Refresh();
            }

            bool exists = (_cached ?? new List<string>()).Contains(menuPath);
            return Response.Success($"Exists check completed for '{menuPath}'.", new { exists });
        }
    }
}
