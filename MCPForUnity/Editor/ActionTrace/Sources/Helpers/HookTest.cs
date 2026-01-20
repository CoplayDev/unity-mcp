// using UnityEngine;
// using UnityEditor;
// using UnityEngine.SceneManagement;

// namespace MCPForUnity.Editor.ActionTrace.Sources
// {
//     /// <summary>
//     /// Test script to verify HookRegistry events are firing correctly.
//     /// Check the Unity Console for output when interacting with the editor.
//     /// </summary>
//     [InitializeOnLoad]
//     public static class HookTest
//     {
//         static HookTest()
//         {
//             // Subscribe to hook events for testing
//             HookRegistry.OnScriptCompiled += OnScriptCompiled;
//             HookRegistry.OnScriptCompilationFailed += OnScriptCompilationFailed;
//             HookRegistry.OnSceneSaved += OnSceneSaved;
//             HookRegistry.OnSceneOpened += OnSceneOpened;
//             HookRegistry.OnPlayModeChanged += OnPlayModeChanged;
//             HookRegistry.OnSelectionChanged += OnSelectionChanged;
//             HookRegistry.OnHierarchyChanged += OnHierarchyChanged;
//             HookRegistry.OnGameObjectCreated += OnGameObjectCreated;
//             HookRegistry.OnGameObjectDestroyed += OnGameObjectDestroyed;
//             HookRegistry.OnComponentAdded += OnComponentAdded;

//             Debug.Log("[HookTest] HookRegistry test initialized. Events are being monitored.");
//         }

//         private static void OnScriptCompiled()
//         {
//             Debug.Log("[HookTest] ✅ ScriptCompiled event fired!");
//         }

//         private static void OnScriptCompilationFailed(int errorCount)
//         {
//             Debug.Log($"[HookTest] ❌ ScriptCompilationFailed event fired! Errors: {errorCount}");
//         }

//         private static void OnSceneSaved(Scene scene)
//         {
//             Debug.Log($"[HookTest] 💾 SceneSaved event fired: {scene.name}");
//         }

//         private static void OnSceneOpened(Scene scene)
//         {
//             Debug.Log($"[HookTest] 📂 SceneOpened event fired: {scene.name}");
//         }

//         private static void OnPlayModeChanged(bool isPlaying)
//         {
//             Debug.Log($"[HookTest] ▶️ PlayModeChanged event fired: isPlaying={isPlaying}");
//         }

//         private static void OnSelectionChanged(GameObject selectedGo)
//         {
//             string name = selectedGo != null ? selectedGo.name : "null";
//             Debug.Log($"[HookTest] 🔍 SelectionChanged event fired: {name}");
//         }

//         private static void OnHierarchyChanged()
//         {
//             Debug.Log("[HookTest] 🏗️ HierarchyChanged event fired");
//         }

//         private static void OnGameObjectCreated(GameObject go)
//         {
//             if (go != null)
//                 Debug.Log($"[HookTest] 🎮 GameObjectCreated event fired: {go.name}");
//         }

//         private static void OnGameObjectDestroyed(GameObject go)
//         {
//             Debug.Log("[HookTest] 🗑️ GameObjectDestroyed event fired");
//         }

//         private static void OnComponentAdded(Component component)
//         {
//             if (component != null)
//                 Debug.Log($"[HookTest] 🔧 ComponentAdded event fired: {component.GetType().Name} to {component.gameObject.name}");
//         }
//     }
// }
