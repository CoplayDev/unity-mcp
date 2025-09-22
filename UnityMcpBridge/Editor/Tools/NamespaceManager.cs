using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;
using System;

namespace UnityMcpBridge
{
    /// <summary>
    /// Manages client namespaces for multi-tenant Unity MCP deployment
    /// Provides isolation between different clients on the same Unity instance
    /// </summary>
    public class NamespaceManager : MonoBehaviour
    {
        private static Dictionary<string, ClientNamespace> namespaces = 
            new Dictionary<string, ClientNamespace>();
        
        private static string currentNamespace = "";
        private static Dictionary<string, bool> namespaceVisibility = 
            new Dictionary<string, bool>();
        
        public class ClientNamespace
        {
            public string ClientId { get; set; }
            public string Namespace { get; set; }
            public List<GameObject> RootObjects { get; set; } = new List<GameObject>();
            public List<string> LoadedScenes { get; set; } = new List<string>();
            public Dictionary<string, object> Metadata { get; set; } = new Dictionary<string, object>();
            public DateTime CreatedAt { get; set; } = DateTime.Now;
            public DateTime LastActivity { get; set; } = DateTime.Now;
            public int CommandCount { get; set; } = 0;
            
            public void UpdateActivity()
            {
                LastActivity = DateTime.Now;
                CommandCount++;
            }
        }
        
        /// <summary>
        /// Component attached to GameObjects to track namespace ownership
        /// </summary>
        public class NamespaceComponent : MonoBehaviour
        {
            public string Namespace { get; set; }
            public string ClientId { get; set; }
            public Dictionary<string, object> Metadata { get; set; } = new Dictionary<string, object>();
        }
        
        #region Menu Items
        
        [MenuItem("Unity MCP/Namespace/List All Namespaces")]
        public static void ListNamespaces()
        {
            Debug.Log($"=== Active Namespaces: {namespaces.Count} ===");
            foreach (var ns in namespaces.Values)
            {
                Debug.Log($"• {ns.Namespace}: {ns.RootObjects.Count} objects, " +
                         $"{ns.LoadedScenes.Count} scenes, Commands: {ns.CommandCount}");
                Debug.Log($"  Client: {ns.ClientId}, Created: {ns.CreatedAt:yyyy-MM-dd HH:mm:ss}");
            }
        }
        
        [MenuItem("Unity MCP/Namespace/Show Current Namespace")]
        public static void ShowCurrentNamespace()
        {
            if (string.IsNullOrEmpty(currentNamespace))
            {
                Debug.Log("No current namespace set");
                return;
            }
            
            if (namespaces.ContainsKey(currentNamespace))
            {
                var ns = namespaces[currentNamespace];
                Debug.Log($"Current Namespace: {currentNamespace}");
                Debug.Log($"Client ID: {ns.ClientId}");
                Debug.Log($"Objects: {ns.RootObjects.Count}");
                Debug.Log($"Scenes: {string.Join(", ", ns.LoadedScenes)}");
            }
        }
        
        [MenuItem("Unity MCP/Namespace/Cleanup All Namespaces")]
        public static void CleanupAllNamespaces()
        {
            if (EditorUtility.DisplayDialog("Cleanup All Namespaces",
                "This will destroy all namespace-managed objects. Continue?", "Yes", "No"))
            {
                var namespaceList = namespaces.Keys.ToList();
                foreach (var namespaceName in namespaceList)
                {
                    CleanupNamespace(namespaceName);
                }
                Debug.Log("All namespaces cleaned up");
            }
        }
        
        #endregion
        
        #region Public API
        
        /// <summary>
        /// Register a new client namespace
        /// </summary>
        public static void RegisterNamespace(string clientId, string namespaceName)
        {
            if (!namespaces.ContainsKey(namespaceName))
            {
                namespaces[namespaceName] = new ClientNamespace
                {
                    ClientId = clientId,
                    Namespace = namespaceName
                };
                
                namespaceVisibility[namespaceName] = true;
                Debug.Log($"Registered namespace: {namespaceName} for client: {clientId}");
            }
            else
            {
                Debug.LogWarning($"Namespace {namespaceName} already exists");
            }
        }
        
        /// <summary>
        /// Set the currently active namespace (affects visibility)
        /// </summary>
        public static void SetCurrentNamespace(string namespaceName)
        {
            if (namespaces.ContainsKey(namespaceName))
            {
                currentNamespace = namespaceName;
                namespaces[namespaceName].UpdateActivity();
                ApplyNamespaceVisibility();
                Debug.Log($"Switched to namespace: {namespaceName}");
            }
            else
            {
                Debug.LogWarning($"Namespace {namespaceName} not found");
            }
        }
        
        /// <summary>
        /// Create a GameObject within a specific namespace
        /// </summary>
        public static GameObject CreateGameObjectInNamespace(string name, string namespaceName, Transform parent = null)
        {
            if (!namespaces.ContainsKey(namespaceName))
            {
                Debug.LogError($"Namespace {namespaceName} not registered");
                return null;
            }
            
            var prefixedName = $"{namespaceName}_{name}";
            var go = new GameObject(prefixedName);
            
            // Set parent if specified
            if (parent != null)
            {
                go.transform.SetParent(parent);
            }
            
            // Add namespace tracking
            go.tag = "MCP_Namespaced";
            
            // Add namespace component
            var nsComponent = go.AddComponent<NamespaceComponent>();
            nsComponent.Namespace = namespaceName;
            nsComponent.ClientId = namespaces[namespaceName].ClientId;
            
            // Track in namespace
            if (parent == null) // Only track root objects
            {
                namespaces[namespaceName].RootObjects.Add(go);
            }
            
            namespaces[namespaceName].UpdateActivity();
            
            Debug.Log($"Created GameObject {prefixedName} in namespace {namespaceName}");
            return go;
        }
        
        /// <summary>
        /// Get all GameObjects belonging to a namespace
        /// </summary>
        public static List<GameObject> GetNamespaceObjects(string namespaceName)
        {
            if (!namespaces.ContainsKey(namespaceName))
                return new List<GameObject>();
                
            // Clean up null references
            namespaces[namespaceName].RootObjects.RemoveAll(obj => obj == null);
            
            // Get all objects including children
            var allObjects = new List<GameObject>();
            foreach (var root in namespaces[namespaceName].RootObjects)
            {
                if (root != null)
                {
                    allObjects.Add(root);
                    allObjects.AddRange(GetAllChildren(root));
                }
            }
            
            return allObjects;
        }
        
        /// <summary>
        /// Find GameObjects by name within a namespace
        /// </summary>
        public static GameObject FindObjectInNamespace(string objectName, string namespaceName)
        {
            var prefixedName = $"{namespaceName}_{objectName}";
            var objects = GetNamespaceObjects(namespaceName);
            
            return objects.FirstOrDefault(obj => obj.name == prefixedName || obj.name == objectName);
        }
        
        /// <summary>
        /// Set visibility for a specific namespace
        /// </summary>
        public static void SetNamespaceVisibility(string namespaceName, bool visible)
        {
            if (!namespaces.ContainsKey(namespaceName))
                return;
                
            namespaceVisibility[namespaceName] = visible;
            
            var objects = GetNamespaceObjects(namespaceName);
            foreach (var obj in objects)
            {
                if (obj != null)
                {
                    obj.SetActive(visible);
                }
            }
            
            Debug.Log($"Set namespace {namespaceName} visibility to {visible}");
        }
        
        /// <summary>
        /// Get namespace information
        /// </summary>
        public static ClientNamespace GetNamespaceInfo(string namespaceName)
        {
            return namespaces.ContainsKey(namespaceName) ? namespaces[namespaceName] : null;
        }
        
        /// <summary>
        /// Check if a namespace exists
        /// </summary>
        public static bool NamespaceExists(string namespaceName)
        {
            return namespaces.ContainsKey(namespaceName);
        }
        
        /// <summary>
        /// Get list of all registered namespaces
        /// </summary>
        public static List<string> GetAllNamespaces()
        {
            return namespaces.Keys.ToList();
        }
        
        /// <summary>
        /// Completely remove a namespace and all its objects
        /// </summary>
        public static void CleanupNamespace(string namespaceName)
        {
            if (!namespaces.ContainsKey(namespaceName))
            {
                Debug.LogWarning($"Namespace {namespaceName} not found for cleanup");
                return;
            }
                
            var ns = namespaces[namespaceName];
            
            // Destroy all root objects (children will be destroyed automatically)
            foreach (var obj in ns.RootObjects.ToList())
            {
                if (obj != null)
                {
                    Debug.Log($"Destroying object: {obj.name}");
                    DestroyImmediate(obj);
                }
            }
            
            // Unload scenes (if any are loaded)
            foreach (var sceneName in ns.LoadedScenes.ToList())
            {
                try
                {
                    UnityEditor.SceneManagement.EditorSceneManager.UnloadSceneAsync(sceneName);
                    Debug.Log($"Unloaded scene: {sceneName}");
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"Could not unload scene {sceneName}: {e.Message}");
                }
            }
            
            // Remove from tracking
            namespaces.Remove(namespaceName);
            namespaceVisibility.Remove(namespaceName);
            
            // Clear current namespace if it was the one being cleaned
            if (currentNamespace == namespaceName)
            {
                currentNamespace = "";
            }
            
            Debug.Log($"Cleaned up namespace: {namespaceName}");
        }
        
        /// <summary>
        /// Clean up idle namespaces (no activity for specified minutes)
        /// </summary>
        public static List<string> CleanupIdleNamespaces(int idleMinutes = 30)
        {
            var currentTime = DateTime.Now;
            var cleanedNamespaces = new List<string>();
            
            var namespacesToClean = namespaces.Where(kvp => 
                (currentTime - kvp.Value.LastActivity).TotalMinutes > idleMinutes
            ).Select(kvp => kvp.Key).ToList();
            
            foreach (var namespaceName in namespacesToClean)
            {
                CleanupNamespace(namespaceName);
                cleanedNamespaces.Add(namespaceName);
            }
            
            if (cleanedNamespaces.Count > 0)
            {
                Debug.Log($"Cleaned up {cleanedNamespaces.Count} idle namespaces: {string.Join(", ", cleanedNamespaces)}");
            }
            
            return cleanedNamespaces;
        }
        
        #endregion
        
        #region Private Methods
        
        private static List<GameObject> GetAllChildren(GameObject parent)
        {
            var children = new List<GameObject>();
            foreach (Transform child in parent.transform)
            {
                children.Add(child.gameObject);
                children.AddRange(GetAllChildren(child.gameObject));
            }
            return children;
        }
        
        private static void ApplyNamespaceVisibility()
        {
            // Hide objects from all namespaces except current
            foreach (var kvp in namespaces)
            {
                string namespaceName = kvp.Key;
                var ns = kvp.Value;
                
                bool shouldBeVisible = (namespaceName == currentNamespace) && 
                                     namespaceVisibility.GetValueOrDefault(namespaceName, true);
                
                foreach (var obj in ns.RootObjects)
                {
                    if (obj != null)
                    {
                        obj.SetActive(shouldBeVisible);
                    }
                }
            }
            
            Debug.Log($"Applied visibility rules. Current namespace: {currentNamespace}");
        }
        
        #endregion
        
        #region Unity Callbacks
        
        void Start()
        {
            // Auto-cleanup on start if needed
            if (Application.isPlaying)
            {
                InvokeRepeating(nameof(PeriodicCleanup), 300f, 300f); // Every 5 minutes
            }
        }
        
        void PeriodicCleanup()
        {
            CleanupIdleNamespaces(30); // Clean up namespaces idle for 30+ minutes
        }
        
        #endregion
        
        #region Editor Integration
        
        [System.Serializable]
        public class NamespaceStats
        {
            public string namespaceName;
            public string clientId;
            public int objectCount;
            public int sceneCount;
            public string lastActivity;
            public int commandCount;
        }
        
        /// <summary>
        /// Get statistics for all namespaces (useful for monitoring)
        /// </summary>
        public static List<NamespaceStats> GetNamespaceStats()
        {
            var stats = new List<NamespaceStats>();
            
            foreach (var kvp in namespaces)
            {
                var ns = kvp.Value;
                stats.Add(new NamespaceStats
                {
                    namespaceName = kvp.Key,
                    clientId = ns.ClientId,
                    objectCount = GetNamespaceObjects(kvp.Key).Count,
                    sceneCount = ns.LoadedScenes.Count,
                    lastActivity = ns.LastActivity.ToString("yyyy-MM-dd HH:mm:ss"),
                    commandCount = ns.CommandCount
                });
            }
            
            return stats;
        }
        
        #endregion
    }
}