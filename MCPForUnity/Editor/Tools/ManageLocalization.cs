#if UNITY_LOCALIZATION
using System;
using System.Collections.Generic;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Localization;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.Localization.Tables;

namespace MCPForUnity.Editor.Tools
{
    [McpForUnityTool("manage_localization", AutoRegister = true)]
    public static class ManageLocalization
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
                    "list_locales"      => ListLocales(),
                    "get_active_locale" => GetActiveLocale(),
                    "set_active_locale" => SetActiveLocale(p),
                    "list_tables"       => ListTables(p),
                    "get_entry"         => GetEntry(p),
                    "set_entry"         => SetEntry(p),
                    _ => new ErrorResponse($"Unknown action: '{action}'.")
                };
            }
            catch (Exception e)
            {
                McpLog.Error($"[ManageLocalization] Action '{action}' failed: {e}");
                return new ErrorResponse($"Internal error: {e.Message}");
            }
        }

        private static object ListLocales()
        {
            var settings = LocalizationSettings.Instance;
            if (settings == null) return new ErrorResponse("LocalizationSettings not found.");

            var locales = settings.GetAvailableLocales().Locales;
            var results = new List<Dictionary<string, object>>();
            foreach (var locale in locales)
            {
                results.Add(new Dictionary<string, object>
                {
                    ["name"] = locale.LocaleName,
                    ["code"] = locale.Identifier.Code,
                });
            }

            return new SuccessResponse($"Found {results.Count} locale(s).", new Dictionary<string, object>
            {
                ["locales"] = results,
            });
        }

        private static object GetActiveLocale()
        {
            var settings = LocalizationSettings.Instance;
            if (settings == null) return new ErrorResponse("LocalizationSettings not found.");

            var selected = settings.GetSelectedLocale();
            if (selected == null) return new SuccessResponse("No active locale.", new Dictionary<string, object>
            {
                ["active_locale"] = (object)null,
            });

            return new SuccessResponse($"Active locale: {selected.LocaleName}.", new Dictionary<string, object>
            {
                ["name"] = selected.LocaleName,
                ["code"] = selected.Identifier.Code,
            });
        }

        private static object SetActiveLocale(ToolParams p)
        {
            var codeResult = p.GetRequired("locale_code");
            if (!codeResult.IsSuccess) return new ErrorResponse(codeResult.ErrorMessage);

            var settings = LocalizationSettings.Instance;
            if (settings == null) return new ErrorResponse("LocalizationSettings not found.");

            Locale found = null;
            foreach (var locale in settings.GetAvailableLocales().Locales)
            {
                if (string.Equals(locale.Identifier.Code, codeResult.Value, StringComparison.OrdinalIgnoreCase))
                {
                    found = locale;
                    break;
                }
            }

            if (found == null) return new ErrorResponse($"Locale '{codeResult.Value}' not found.");

            settings.SetSelectedLocale(found);
            return new SuccessResponse($"Active locale set to '{found.LocaleName}'.");
        }

        private static object ListTables(ToolParams p)
        {
            string tableType = p.Get("type") ?? "string";

            var results = new List<Dictionary<string, object>>();

            if (tableType.Equals("string", StringComparison.OrdinalIgnoreCase))
            {
                var collections = LocalizationEditorSettings.GetStringTableCollections();
                foreach (var col in collections)
                {
                    results.Add(new Dictionary<string, object>
                    {
                        ["name"] = col.TableCollectionName,
                        ["type"] = "string",
                        ["table_count"] = col.Tables.Count,
                    });
                }
            }
            else
            {
                var collections = LocalizationEditorSettings.GetAssetTableCollections();
                foreach (var col in collections)
                {
                    results.Add(new Dictionary<string, object>
                    {
                        ["name"] = col.TableCollectionName,
                        ["type"] = "asset",
                        ["table_count"] = col.Tables.Count,
                    });
                }
            }

            return new SuccessResponse($"Found {results.Count} {tableType} table collection(s).", new Dictionary<string, object>
            {
                ["tables"] = results,
            });
        }

        private static object GetEntry(ToolParams p)
        {
            var tableResult = p.GetRequired("table");
            if (!tableResult.IsSuccess) return new ErrorResponse(tableResult.ErrorMessage);
            var keyResult = p.GetRequired("key");
            if (!keyResult.IsSuccess) return new ErrorResponse(keyResult.ErrorMessage);

            string localeCode = p.Get("locale");

            var collections = LocalizationEditorSettings.GetStringTableCollections();
            StringTableCollection found = null;
            foreach (var col in collections)
            {
                if (string.Equals(col.TableCollectionName, tableResult.Value, StringComparison.OrdinalIgnoreCase))
                {
                    found = col;
                    break;
                }
            }
            if (found == null) return new ErrorResponse($"String table '{tableResult.Value}' not found.");

            var entry = found.SharedData.GetEntry(keyResult.Value);
            if (entry == null) return new ErrorResponse($"Key '{keyResult.Value}' not found in '{tableResult.Value}'.");

            if (!string.IsNullOrEmpty(localeCode))
            {
                foreach (var table in found.Tables)
                {
                    var st = table.asset as StringTable;
                    if (st == null) continue;
                    if (string.Equals(st.LocaleIdentifier.Code, localeCode, StringComparison.OrdinalIgnoreCase))
                    {
                        var tableEntry = st.GetEntry(entry.Id);
                        return new SuccessResponse($"Entry '{keyResult.Value}' [{localeCode}].", new Dictionary<string, object>
                        {
                            ["key"] = keyResult.Value,
                            ["locale"] = localeCode,
                            ["value"] = tableEntry?.LocalizedValue,
                        });
                    }
                }
                return new ErrorResponse($"Locale '{localeCode}' not found in table.");
            }

            return new SuccessResponse($"Entry '{keyResult.Value}'.", new Dictionary<string, object>
            {
                ["key"] = keyResult.Value,
                ["key_id"] = entry.Id,
            });
        }

        private static object SetEntry(ToolParams p)
        {
            var tableResult = p.GetRequired("table");
            if (!tableResult.IsSuccess) return new ErrorResponse(tableResult.ErrorMessage);
            var keyResult = p.GetRequired("key");
            if (!keyResult.IsSuccess) return new ErrorResponse(keyResult.ErrorMessage);
            var localeResult = p.GetRequired("locale");
            if (!localeResult.IsSuccess) return new ErrorResponse(localeResult.ErrorMessage);
            var valueResult = p.GetRequired("value");
            if (!valueResult.IsSuccess) return new ErrorResponse(valueResult.ErrorMessage);

            var collections = LocalizationEditorSettings.GetStringTableCollections();
            StringTableCollection found = null;
            foreach (var col in collections)
            {
                if (string.Equals(col.TableCollectionName, tableResult.Value, StringComparison.OrdinalIgnoreCase))
                {
                    found = col;
                    break;
                }
            }
            if (found == null) return new ErrorResponse($"String table '{tableResult.Value}' not found.");

            foreach (var table in found.Tables)
            {
                var st = table.asset as StringTable;
                if (st == null) continue;
                if (string.Equals(st.LocaleIdentifier.Code, localeResult.Value, StringComparison.OrdinalIgnoreCase))
                {
                    Undo.RecordObject(st, "MCP SetEntry");
                    st.AddEntry(keyResult.Value, valueResult.Value);
                    EditorUtility.SetDirty(st);
                    return new SuccessResponse($"Entry '{keyResult.Value}' [{localeResult.Value}] set.");
                }
            }

            return new ErrorResponse($"Locale '{localeResult.Value}' not found in table.");
        }
    }
}
#endif
