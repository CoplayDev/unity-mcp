using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using MCPForUnity.Editor.Constants;
using MCPForUnity.Editor.Helpers;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace MCPForUnity.Editor.Windows
{
    /// <summary>
    /// Editor window for managing Unity EditorPrefs, specifically for MCP For Unity development
    /// </summary>
    public class EditorPrefsWindow : EditorWindow
    {
        // UI Elements
        private ScrollView scrollView;
        private VisualElement prefsContainer;
        private TextField searchField;
        private string searchFilter = "";

        // Data
        private List<EditorPrefItem> currentPrefs = new List<EditorPrefItem>();
        private HashSet<string> knownMcpKeys = new HashSet<string>();

        // Type mapping for known EditorPrefs
        private readonly Dictionary<string, EditorPrefType> knownPrefTypes = new Dictionary<string, EditorPrefType>
        {
            // Boolean prefs
            { EditorPrefKeys.DebugLogs, EditorPrefType.Bool },
            { EditorPrefKeys.UseHttpTransport, EditorPrefType.Bool },
            { EditorPrefKeys.ResumeHttpAfterReload, EditorPrefType.Bool },
            { EditorPrefKeys.ResumeStdioAfterReload, EditorPrefType.Bool },
            { EditorPrefKeys.UseEmbeddedServer, EditorPrefType.Bool },
            { EditorPrefKeys.LockCursorConfig, EditorPrefType.Bool },
            { EditorPrefKeys.AutoRegisterEnabled, EditorPrefType.Bool },
            { EditorPrefKeys.SetupCompleted, EditorPrefType.Bool },
            { EditorPrefKeys.SetupDismissed, EditorPrefType.Bool },
            { EditorPrefKeys.CustomToolRegistrationEnabled, EditorPrefType.Bool },
            { EditorPrefKeys.TelemetryDisabled, EditorPrefType.Bool },
            { EditorPrefKeys.DevModeForceServerRefresh, EditorPrefType.Bool },
            { EditorPrefKeys.UseBetaServer, EditorPrefType.Bool },
            { EditorPrefKeys.ProjectScopedToolsLocalHttp, EditorPrefType.Bool },
            
            // Integer prefs
            { EditorPrefKeys.UnitySocketPort, EditorPrefType.Int },
            { EditorPrefKeys.ValidationLevel, EditorPrefType.Int },
            { EditorPrefKeys.LastUpdateCheck, EditorPrefType.String },
            { EditorPrefKeys.LastStdIoUpgradeVersion, EditorPrefType.Int },
            { EditorPrefKeys.LastLocalHttpServerPid, EditorPrefType.Int },
            { EditorPrefKeys.LastLocalHttpServerPort, EditorPrefType.Int },
            
            // String prefs
            { EditorPrefKeys.EditorWindowActivePanel, EditorPrefType.String },
            { EditorPrefKeys.ClaudeCliPathOverride, EditorPrefType.String },
            { EditorPrefKeys.UvxPathOverride, EditorPrefType.String },
            { EditorPrefKeys.HttpBaseUrl, EditorPrefType.String },
            { EditorPrefKeys.HttpRemoteBaseUrl, EditorPrefType.String },
            { EditorPrefKeys.HttpTransportScope, EditorPrefType.String },
            { EditorPrefKeys.SessionId, EditorPrefType.String },
            { EditorPrefKeys.WebSocketUrlOverride, EditorPrefType.String },
            { EditorPrefKeys.GitUrlOverride, EditorPrefType.String },
            { EditorPrefKeys.PackageDeploySourcePath, EditorPrefType.String },
            { EditorPrefKeys.PackageDeployLastBackupPath, EditorPrefType.String },
            { EditorPrefKeys.PackageDeployLastTargetPath, EditorPrefType.String },
            { EditorPrefKeys.PackageDeployLastSourcePath, EditorPrefType.String },
            { EditorPrefKeys.ServerSrc, EditorPrefType.String },
            { EditorPrefKeys.LatestKnownVersion, EditorPrefType.String },
            { EditorPrefKeys.LastAssetStoreUpdateCheck, EditorPrefType.String },
            { EditorPrefKeys.LatestKnownAssetStoreVersion, EditorPrefType.String },
            { EditorPrefKeys.LastLocalHttpServerStartedUtc, EditorPrefType.String },
            { EditorPrefKeys.LastLocalHttpServerPidArgsHash, EditorPrefType.String },
            { EditorPrefKeys.LastLocalHttpServerPidFilePath, EditorPrefType.String },
            { EditorPrefKeys.LastLocalHttpServerInstanceToken, EditorPrefType.String },
        };

        // Templates
        private VisualTreeAsset itemTemplate;

        /// <summary>
        /// Show the EditorPrefs window
        /// </summary>
        public static void ShowWindow()
        {
            var window = GetWindow<EditorPrefsWindow>("EditorPrefs");
            window.minSize = new Vector2(600, 400);
            window.Show();
        }

        public void CreateGUI()
        {
            string basePath = AssetPathUtility.GetMcpPackageRootPath();

            // Load UXML
            var visualTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
                $"{basePath}/Editor/Windows/EditorPrefs/EditorPrefsWindow.uxml"
            );

            if (visualTree == null)
            {
                McpLog.Error("Failed to load EditorPrefsWindow.uxml template");
                return;
            }

            // Load item template
            itemTemplate = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
                $"{basePath}/Editor/Windows/EditorPrefs/EditorPrefItem.uxml"
            );

            if (itemTemplate == null)
            {
                McpLog.Error("Failed to load EditorPrefItem.uxml template");
                return;
            }

            visualTree.CloneTree(rootVisualElement);

            // Add search bar container at the top
            var searchContainer = new VisualElement();
            searchContainer.style.flexDirection = FlexDirection.Row;
            searchContainer.style.marginTop = 8;
            searchContainer.style.marginBottom = 20;
            searchContainer.style.marginLeft = 4;
            searchContainer.style.marginRight = 4;

            searchField = new TextField("Search");
            searchField.style.flexGrow = 1;
            searchField.style.height = 28;
            searchField.style.paddingTop = 2;
            searchField.style.paddingBottom = 2;
            searchField.labelElement.style.unityFontStyleAndWeight = FontStyle.Bold;
            searchField.RegisterValueChangedCallback(evt =>
            {
                searchFilter = evt.newValue ?? "";
                RefreshPrefs();
            });

            var refreshButton = new Button(RefreshPrefs);
            refreshButton.text = "↻";
            refreshButton.tooltip = "Refresh prefs";
            refreshButton.style.width = 30;
            refreshButton.style.height = 28;
            refreshButton.style.marginLeft = 6;
            refreshButton.style.backgroundColor = new Color(0.9f, 0.5f, 0.1f);

            searchContainer.Add(searchField);
            searchContainer.Add(refreshButton);
            rootVisualElement.Insert(0, searchContainer);

            // Get references
            scrollView = rootVisualElement.Q<ScrollView>("scroll-view");
            prefsContainer = rootVisualElement.Q<VisualElement>("prefs-container");

            // Load known MCP keys
            LoadKnownMcpKeys();

            // Load initial data
            RefreshPrefs();
        }

        private void LoadKnownMcpKeys()
        {
            knownMcpKeys.Clear();
            var fields = typeof(EditorPrefKeys).GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);

            foreach (var field in fields)
            {
                if (field.IsLiteral && !field.IsInitOnly)
                {
                    knownMcpKeys.Add(field.GetValue(null).ToString());
                }
            }
        }

        private void RefreshPrefs()
        {
            currentPrefs.Clear();
            prefsContainer.Clear();

            // Get all EditorPrefs keys
            var allKeys = new List<string>();

            // Always show all MCP keys
            allKeys.AddRange(knownMcpKeys);

            // Try to find additional MCP keys
            var mcpKeys = GetAllMcpKeys();
            foreach (var key in mcpKeys)
            {
                if (!allKeys.Contains(key))
                {
                    allKeys.Add(key);
                }
            }

            // Sort keys
            allKeys.Sort();

            // Pre-trim filter once outside the loop
            var filter = searchFilter?.Trim();

            // Create items for existing prefs
            foreach (var key in allKeys)
            {
                // Skip Customer UUID but show everything else that's defined
                if (key != EditorPrefKeys.CustomerUuid)
                {
                    // Apply search filter using OrdinalIgnoreCase for fewer allocations
                    if (!string.IsNullOrEmpty(filter) &&
                        key.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        continue;
                    }

                    var item = CreateEditorPrefItem(key);
                    if (item != null)
                    {
                        currentPrefs.Add(item);
                        prefsContainer.Add(CreateItemUI(item));
                    }
                }
            }
        }

        private List<string> GetAllMcpKeys()
        {
            // This is a simplified approach - in reality, getting all EditorPrefs is platform-specific
            // For now, we'll return known MCP keys that might exist
            var keys = new List<string>();

            // Add some common MCP keys that might not be in EditorPrefKeys
            keys.Add("MCPForUnity.TestKey");

            // Filter to only those that actually exist
            return keys.Where(EditorPrefs.HasKey).ToList();
        }

        private EditorPrefItem CreateEditorPrefItem(string key)
        {
            var item = new EditorPrefItem { Key = key, IsKnown = knownMcpKeys.Contains(key) };

            // Check if we know the type of this pref
            if (knownPrefTypes.TryGetValue(key, out var knownType))
            {
                // Use the known type
                switch (knownType)
                {
                    case EditorPrefType.Bool:
                        item.Type = EditorPrefType.Bool;
                        item.Value = EditorPrefs.GetBool(key, false).ToString();
                        break;
                    case EditorPrefType.Int:
                        item.Type = EditorPrefType.Int;
                        item.Value = EditorPrefs.GetInt(key, 0).ToString();
                        break;
                    case EditorPrefType.Float:
                        item.Type = EditorPrefType.Float;
                        item.Value = EditorPrefs.GetFloat(key, 0f).ToString();
                        break;
                    case EditorPrefType.String:
                        item.Type = EditorPrefType.String;
                        item.Value = EditorPrefs.GetString(key, "");
                        break;
                }
            }
            else
            {
                // Only try to detect type for unknown keys that actually exist
                if (!EditorPrefs.HasKey(key))
                {
                    // Key doesn't exist and we don't know its type, skip it
                    return null;
                }

                // Unknown pref - try to detect type
                var stringValue = EditorPrefs.GetString(key, "");

                if (int.TryParse(stringValue, out var intValue))
                {
                    item.Type = EditorPrefType.Int;
                    item.Value = intValue.ToString();
                }
                else if (float.TryParse(stringValue, out var floatValue))
                {
                    item.Type = EditorPrefType.Float;
                    item.Value = floatValue.ToString();
                }
                else if (bool.TryParse(stringValue, out var boolValue))
                {
                    item.Type = EditorPrefType.Bool;
                    item.Value = boolValue.ToString();
                }
                else
                {
                    item.Type = EditorPrefType.String;
                    item.Value = stringValue;
                }
            }

            return item;
        }

        private VisualElement CreateItemUI(EditorPrefItem item)
        {
            if (itemTemplate == null)
            {
                McpLog.Error("Item template not loaded");
                return new VisualElement();
            }

            var itemElement = itemTemplate.CloneTree();

            // Set values
            itemElement.Q<Label>("key-label").text = item.Key;
            var valueField = itemElement.Q<TextField>("value-field");
            valueField.value = item.Value;

            var typeDropdown = itemElement.Q<DropdownField>("type-dropdown");
            typeDropdown.index = (int)item.Type;

            // Buttons
            var saveButton = itemElement.Q<Button>("save-button");

            // Callbacks
            saveButton.clicked += () => SavePref(item, valueField.value, (EditorPrefType)typeDropdown.index);

            return itemElement;
        }

        private void SavePref(EditorPrefItem item, string newValue, EditorPrefType newType)
        {
            SaveValue(item.Key, newValue, newType);
            RefreshPrefs();
        }

        private void SaveValue(string key, string value, EditorPrefType type)
        {
            switch (type)
            {
                case EditorPrefType.String:
                    EditorPrefs.SetString(key, value);
                    break;
                case EditorPrefType.Int:
                    if (int.TryParse(value, out var intValue))
                    {
                        EditorPrefs.SetInt(key, intValue);
                    }
                    else
                    {
                        EditorUtility.DisplayDialog("Error", $"Cannot convert '{value}' to int", "OK");
                        return;
                    }
                    break;
                case EditorPrefType.Float:
                    if (float.TryParse(value, out var floatValue))
                    {
                        EditorPrefs.SetFloat(key, floatValue);
                    }
                    else
                    {
                        EditorUtility.DisplayDialog("Error", $"Cannot convert '{value}' to float", "OK");
                        return;
                    }
                    break;
                case EditorPrefType.Bool:
                    if (bool.TryParse(value, out var boolValue))
                    {
                        EditorPrefs.SetBool(key, boolValue);
                    }
                    else
                    {
                        EditorUtility.DisplayDialog("Error", $"Cannot convert '{value}' to bool (use 'True' or 'False')", "OK");
                        return;
                    }
                    break;
            }
        }
    }

    /// <summary>
    /// Represents an EditorPrefs item
    /// </summary>
    public class EditorPrefItem
    {
        public string Key { get; set; }
        public string Value { get; set; }
        public EditorPrefType Type { get; set; }
        public bool IsKnown { get; set; }
    }

    /// <summary>
    /// EditorPrefs value types
    /// </summary>
    public enum EditorPrefType
    {
        String,
        Int,
        Float,
        Bool
    }
}
