#if UNITY_ADDRESSABLES
using System;
using System.Collections.Generic;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Build;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace MCPForUnity.Editor.Tools
{
    [McpForUnityTool("manage_addressables", AutoRegister = true)]
    public static class ManageAddressables
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
                    "list_groups"  => ListGroups(),
                    "get_group"    => GetGroup(p),
                    "list_entries" => ListEntries(p),
                    "get_entry"    => GetEntry(p),
                    "list_labels"  => ListLabels(),
                    "build"        => Build(p),
                    "analyze"      => Analyze(),
                    _ => new ErrorResponse($"Unknown action: '{action}'.")
                };
            }
            catch (Exception e)
            {
                McpLog.Error($"[ManageAddressables] Action '{action}' failed: {e}");
                return new ErrorResponse($"Internal error: {e.Message}");
            }
        }

        private static AddressableAssetSettings GetSettings()
        {
            return AddressableAssetSettingsDefaultObject.Settings;
        }

        private static object ListGroups()
        {
            var settings = GetSettings();
            if (settings == null) return new ErrorResponse("Addressables not initialized.");

            var groups = new List<Dictionary<string, object>>();
            foreach (var group in settings.groups)
            {
                if (group == null) continue;
                groups.Add(new Dictionary<string, object>
                {
                    ["name"] = group.Name,
                    ["guid"] = group.Guid,
                    ["entry_count"] = group.entries.Count,
                    ["read_only"] = group.ReadOnly,
                });
            }

            return new SuccessResponse($"Found {groups.Count} Addressable group(s).", new Dictionary<string, object>
            {
                ["groups"] = groups,
            });
        }

        private static object GetGroup(ToolParams p)
        {
            var settings = GetSettings();
            if (settings == null) return new ErrorResponse("Addressables not initialized.");

            var nameResult = p.GetRequired("group_name");
            if (!nameResult.IsSuccess) return new ErrorResponse(nameResult.ErrorMessage);

            AddressableAssetGroup found = null;
            foreach (var group in settings.groups)
            {
                if (group != null && string.Equals(group.Name, nameResult.Value, StringComparison.OrdinalIgnoreCase))
                {
                    found = group;
                    break;
                }
            }
            if (found == null) return new ErrorResponse($"Group '{nameResult.Value}' not found.");

            var schemas = new List<string>();
            foreach (var schema in found.Schemas)
            {
                if (schema != null) schemas.Add(schema.GetType().Name);
            }

            return new SuccessResponse($"Group '{found.Name}'.", new Dictionary<string, object>
            {
                ["name"] = found.Name,
                ["guid"] = found.Guid,
                ["entry_count"] = found.entries.Count,
                ["read_only"] = found.ReadOnly,
                ["schemas"] = schemas,
            });
        }

        private static object ListEntries(ToolParams p)
        {
            var settings = GetSettings();
            if (settings == null) return new ErrorResponse("Addressables not initialized.");

            var nameResult = p.GetRequired("group_name");
            if (!nameResult.IsSuccess) return new ErrorResponse(nameResult.ErrorMessage);

            AddressableAssetGroup found = null;
            foreach (var group in settings.groups)
            {
                if (group != null && string.Equals(group.Name, nameResult.Value, StringComparison.OrdinalIgnoreCase))
                {
                    found = group;
                    break;
                }
            }
            if (found == null) return new ErrorResponse($"Group '{nameResult.Value}' not found.");

            var pagination = PaginationRequest.FromParams(
                new JObject { ["page_size"] = p.GetInt("page_size"), ["cursor"] = p.GetInt("cursor") });

            var allItems = new List<Dictionary<string, object>>();
            foreach (var entry in found.entries)
            {
                var labels = new List<string>(entry.labels);
                allItems.Add(new Dictionary<string, object>
                {
                    ["address"] = entry.address,
                    ["guid"] = entry.guid,
                    ["asset_path"] = entry.AssetPath,
                    ["labels"] = labels,
                });
            }

            var page = PaginationResponse<Dictionary<string, object>>.Create(allItems, pagination);
            return new SuccessResponse($"Group '{found.Name}': {allItems.Count} entries.", page);
        }

        private static object GetEntry(ToolParams p)
        {
            var settings = GetSettings();
            if (settings == null) return new ErrorResponse("Addressables not initialized.");

            string address = p.Get("address");
            string guid = p.Get("guid");

            if (string.IsNullOrEmpty(address) && string.IsNullOrEmpty(guid))
                return new ErrorResponse("'address' or 'guid' parameter is required.");

            AddressableAssetEntry found = null;
            foreach (var group in settings.groups)
            {
                if (group == null) continue;
                foreach (var entry in group.entries)
                {
                    if (!string.IsNullOrEmpty(guid) && string.Equals(entry.guid, guid, StringComparison.OrdinalIgnoreCase))
                    {
                        found = entry;
                        break;
                    }
                    if (!string.IsNullOrEmpty(address) && string.Equals(entry.address, address, StringComparison.OrdinalIgnoreCase))
                    {
                        found = entry;
                        break;
                    }
                }
                if (found != null) break;
            }

            if (found == null) return new ErrorResponse("Entry not found.");

            var labels = new List<string>(found.labels);
            return new SuccessResponse($"Entry '{found.address}'.", new Dictionary<string, object>
            {
                ["address"] = found.address,
                ["guid"] = found.guid,
                ["asset_path"] = found.AssetPath,
                ["group"] = found.parentGroup?.Name,
                ["labels"] = labels,
                ["read_only"] = found.ReadOnly,
            });
        }

        private static object ListLabels()
        {
            var settings = GetSettings();
            if (settings == null) return new ErrorResponse("Addressables not initialized.");

            var labels = settings.GetLabels();
            return new SuccessResponse($"Found {labels.Count} label(s).", new Dictionary<string, object>
            {
                ["labels"] = labels,
            });
        }

        private static object Build(ToolParams p)
        {
            bool clean = p.GetBool("clean") ?? false;

            if (clean)
            {
                AddressableAssetSettings.CleanPlayerContent();
            }

            AddressableAssetSettings.BuildPlayerContent(out AddressablesPlayerBuildResult result);

            return new SuccessResponse($"Addressables build complete.", new Dictionary<string, object>
            {
                ["duration"] = Math.Round(result.Duration, 2),
                ["error"] = result.Error,
                ["location_count"] = result.LocationCount,
            });
        }

        private static object Analyze()
        {
            // AnalyzeSystem.AnalyzeData is available but complex; simple approach:
            return new SuccessResponse("Use Window > Asset Management > Addressables > Analyze for full analysis.", new Dictionary<string, object>
            {
                ["note"] = "Programmatic analyze requires custom AnalyzeRule setup. Use the Editor window for interactive analysis.",
            });
        }
    }
}
#endif
