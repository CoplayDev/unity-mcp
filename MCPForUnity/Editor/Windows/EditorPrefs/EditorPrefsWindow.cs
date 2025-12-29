using System;
using System.Collections.Generic;
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
        
        // Data
        private List<EditorPrefItem> currentPrefs = new List<EditorPrefItem>();
        private HashSet<string> knownMcpKeys = new HashSet<string>();
        
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
                Debug.LogError("Failed to load EditorPrefsWindow.uxml template");
                return;
            }
            
            // Load item template
            itemTemplate = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
                $"{basePath}/Editor/Windows/EditorPrefs/EditorPrefItem.uxml"
            );
            
            if (itemTemplate == null)
            {
                Debug.LogError("Failed to load EditorPrefItem.uxml template");
                return;
            }
            
            visualTree.CloneTree(rootVisualElement);
            
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
            
            // Create items for existing prefs
            foreach (var key in allKeys)
            {
                if (EditorPrefs.HasKey(key) && key != EditorPrefKeys.CustomerUuid)
                {
                    var item = CreateEditorPrefItem(key);
                    currentPrefs.Add(item);
                    prefsContainer.Add(CreateItemUI(item));
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
            
            // Check known MCP keys for their expected types
            if (key == EditorPrefKeys.DebugLogs || key == EditorPrefKeys.UseHttpTransport || 
                key == EditorPrefKeys.ResumeHttpAfterReload || key == EditorPrefKeys.ResumeStdioAfterReload ||
                key == EditorPrefKeys.UseEmbeddedServer || key == EditorPrefKeys.LockCursorConfig ||
                key == EditorPrefKeys.AutoRegisterEnabled || key == EditorPrefKeys.SetupCompleted ||
                key == EditorPrefKeys.SetupDismissed || key == EditorPrefKeys.CustomToolRegistrationEnabled ||
                key == EditorPrefKeys.TelemetryDisabled)
            {
                // These are boolean values
                item.Type = EditorPrefType.Bool;
                item.Value = EditorPrefs.GetBool(key, false).ToString();
            }
            else if (key == EditorPrefKeys.UnitySocketPort || key == EditorPrefKeys.ValidationLevel ||
                     key == EditorPrefKeys.LastUpdateCheck || key == EditorPrefKeys.LastStdIoUpgradeVersion)
            {
                // These are integer values
                item.Type = EditorPrefType.Int;
                item.Value = EditorPrefs.GetInt(key, 0).ToString();
            }
            else
            {
                // Try to determine type by parsing the stored string value
                var stringValue = EditorPrefs.GetString(key, "");
                
                if (TryGetInt(key, out var intValue))
                {
                    item.Type = EditorPrefType.Int;
                    item.Value = intValue.ToString();
                }
                else if (TryGetFloat(key, out var floatValue))
                {
                    item.Type = EditorPrefType.Float;
                    item.Value = floatValue.ToString();
                }
                else if (TryGetBool(key, out var boolValue))
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
        
        private bool TryGetInt(string key, out int value)
        {
            // EditorPrefs doesn't have TryGetInt in Unity 2021.3, so we need to get the value as string and parse
            if (EditorPrefs.HasKey(key))
            {
                var stringValue = EditorPrefs.GetString(key, "");
                if (int.TryParse(stringValue, out value))
                {
                    return true;
                }
            }
            value = default;
            return false;
        }
        
        private bool TryGetFloat(string key, out float value)
        {
            if (EditorPrefs.HasKey(key))
            {
                var stringValue = EditorPrefs.GetString(key, "");
                if (float.TryParse(stringValue, out value))
                {
                    return true;
                }
            }
            value = default;
            return false;
        }
        
        private bool TryGetBool(string key, out bool value)
        {
            if (EditorPrefs.HasKey(key))
            {
                var stringValue = EditorPrefs.GetString(key, "");
                if (bool.TryParse(stringValue, out value))
                {
                    return true;
                }
            }
            value = default;
            return false;
        }
        
        private VisualElement CreateItemUI(EditorPrefItem item)
        {
            if (itemTemplate == null)
            {
                Debug.LogError("Item template not loaded");
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
