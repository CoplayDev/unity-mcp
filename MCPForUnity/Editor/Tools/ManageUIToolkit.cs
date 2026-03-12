using System;
using System.Collections.Generic;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace MCPForUnity.Editor.Tools
{
    [McpForUnityTool("manage_ui_toolkit", AutoRegister = true)]
    public static class ManageUIToolkit
    {
        public static object HandleCommand(JObject @params)
        {
            if (@params == null) return new ErrorResponse("Parameters cannot be null.");
            var p = new ToolParams(@params);
            var actionResult = p.GetRequired("action");
            if (!actionResult.IsSuccess) return new ErrorResponse(actionResult.ErrorMessage);
            string action = actionResult.Value.ToLowerInvariant();

            try
            {
                return action switch
                {
                    "list_documents"  => ListDocuments(p),
                    "get_document"    => GetDocument(p),
                    "query_elements"  => QueryElements(p),
                    "get_element"     => GetElement(p),
                    "set_style"       => SetStyle(p),
                    "list_uxml_assets" => ListUxmlAssets(p),
                    _ => new ErrorResponse($"Unknown action: '{action}'.")
                };
            }
            catch (Exception e)
            {
                McpLog.Error($"[ManageUIToolkit] Action '{action}' failed: {e}");
                return new ErrorResponse($"Internal error: {e.Message}");
            }
        }

        private static object ListDocuments(ToolParams p)
        {
            var docs = UnityEngine.Object.FindObjectsByType<UIDocument>(FindObjectsSortMode.InstanceID);
            var pagination = PaginationRequest.FromParams(
                new JObject { ["page_size"] = p.GetInt("page_size"), ["cursor"] = p.GetInt("cursor") });

            var allItems = new List<Dictionary<string, object>>();
            foreach (var doc in docs)
            {
                allItems.Add(new Dictionary<string, object>
                {
                    ["name"] = doc.gameObject.name,
                    ["instance_id"] = doc.gameObject.GetInstanceID(),
                    ["sort_order"] = doc.sortingOrder,
                    ["has_panel_settings"] = doc.panelSettings != null,
                    ["has_source_asset"] = doc.visualTreeAsset != null,
                    ["source_asset"] = doc.visualTreeAsset != null ? doc.visualTreeAsset.name : null,
                });
            }

            var page = PaginationResponse<Dictionary<string, object>>.Create(allItems, pagination);
            return new SuccessResponse($"Found {docs.Length} UIDocument(s).", page);
        }

        private static object GetDocument(ToolParams p)
        {
            var doc = ResolveUIDocument(p);
            if (doc == null) return new ErrorResponse("UIDocument not found.");

            var root = doc.rootVisualElement;
            int childCount = root != null ? root.childCount : 0;

            return new SuccessResponse($"UIDocument '{doc.gameObject.name}'.", new Dictionary<string, object>
            {
                ["name"] = doc.gameObject.name,
                ["sort_order"] = doc.sortingOrder,
                ["panel_settings"] = doc.panelSettings != null ? doc.panelSettings.name : null,
                ["source_asset"] = doc.visualTreeAsset != null ? doc.visualTreeAsset.name : null,
                ["root_child_count"] = childCount,
                ["root_classes"] = root != null ? ListToStringList(root.GetClasses()) : new List<string>(),
            });
        }

        private static object QueryElements(ToolParams p)
        {
            var doc = ResolveUIDocument(p);
            if (doc == null) return new ErrorResponse("UIDocument not found.");

            var queryStr = p.Get("query");
            if (string.IsNullOrEmpty(queryStr))
                return new ErrorResponse("'query' parameter is required (USS selector).");

            var root = doc.rootVisualElement;
            if (root == null) return new ErrorResponse("UIDocument has no root element.");

            var pagination = PaginationRequest.FromParams(
                new JObject { ["page_size"] = p.GetInt("page_size"), ["cursor"] = p.GetInt("cursor") });

            var elements = root.Query(queryStr).ToList();
            var allItems = new List<Dictionary<string, object>>();
            foreach (var el in elements)
            {
                allItems.Add(new Dictionary<string, object>
                {
                    ["name"] = el.name,
                    ["type"] = el.GetType().Name,
                    ["class_list"] = ListToStringList(el.GetClasses()),
                    ["child_count"] = el.childCount,
                    ["visible"] = el.visible,
                });
            }

            var page = PaginationResponse<Dictionary<string, object>>.Create(allItems, pagination);
            return new SuccessResponse($"Query '{queryStr}': {allItems.Count} element(s).", page);
        }

        private static object GetElement(ToolParams p)
        {
            var doc = ResolveUIDocument(p);
            if (doc == null) return new ErrorResponse("UIDocument not found.");

            var queryStr = p.Get("query");
            if (string.IsNullOrEmpty(queryStr))
                return new ErrorResponse("'query' parameter is required.");

            var root = doc.rootVisualElement;
            if (root == null) return new ErrorResponse("UIDocument has no root element.");

            var el = root.Q(queryStr);
            if (el == null) return new ErrorResponse($"Element not found for query '{queryStr}'.");

            var layout = el.layout;
            var data = new Dictionary<string, object>
            {
                ["name"] = el.name,
                ["type"] = el.GetType().Name,
                ["class_list"] = ListToStringList(el.GetClasses()),
                ["visible"] = el.visible,
                ["enabled"] = el.enabledSelf,
                ["child_count"] = el.childCount,
                ["layout_x"] = Math.Round(layout.x, 2),
                ["layout_y"] = Math.Round(layout.y, 2),
                ["layout_width"] = Math.Round(layout.width, 2),
                ["layout_height"] = Math.Round(layout.height, 2),
            };

            if (el is TextElement textEl)
                data["text"] = textEl.text;

            return new SuccessResponse($"Element '{el.name}'.", data);
        }

        private static object SetStyle(ToolParams p)
        {
            var doc = ResolveUIDocument(p);
            if (doc == null) return new ErrorResponse("UIDocument not found.");

            var queryStr = p.Get("query");
            if (string.IsNullOrEmpty(queryStr))
                return new ErrorResponse("'query' parameter is required.");

            var property = p.Get("property");
            if (string.IsNullOrEmpty(property))
                return new ErrorResponse("'property' parameter is required.");

            var value = p.Get("value");
            if (string.IsNullOrEmpty(value))
                return new ErrorResponse("'value' parameter is required.");

            var root = doc.rootVisualElement;
            if (root == null) return new ErrorResponse("UIDocument has no root element.");

            var el = root.Q(queryStr);
            if (el == null) return new ErrorResponse($"Element not found for query '{queryStr}'.");

            el.style.SetPropertyFromString(property, value);

            return new SuccessResponse($"Style '{property}' set on element matching '{queryStr}'.");
        }

        private static object ListUxmlAssets(ToolParams p)
        {
            string filter = p.Get("filter") ?? "";
            var guids = AssetDatabase.FindAssets($"t:VisualTreeAsset {filter}");
            var pagination = PaginationRequest.FromParams(
                new JObject { ["page_size"] = p.GetInt("page_size"), ["cursor"] = p.GetInt("cursor") });

            var allItems = new List<Dictionary<string, object>>();
            foreach (var guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(path);
                if (asset == null) continue;
                allItems.Add(new Dictionary<string, object>
                {
                    ["name"] = asset.name,
                    ["path"] = path,
                });
            }

            var page = PaginationResponse<Dictionary<string, object>>.Create(allItems, pagination);
            return new SuccessResponse($"Found {allItems.Count} UXML asset(s).", page);
        }

        #region Helpers

        private static UIDocument ResolveUIDocument(ToolParams p)
        {
            string target = p.Get("target");
            if (string.IsNullOrEmpty(target)) return null;
            var go = ObjectResolver.ResolveGameObject(new JValue(target));
            return go != null ? go.GetComponent<UIDocument>() : null;
        }

        private static List<string> ListToStringList(IEnumerable<string> source)
        {
            var list = new List<string>();
            foreach (var s in source) list.Add(s);
            return list;
        }

        #endregion
    }
}
