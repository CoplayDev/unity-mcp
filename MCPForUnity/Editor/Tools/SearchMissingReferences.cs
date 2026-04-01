using System.Collections.Generic;
using System.Linq;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace MCPForUnity.Editor.Tools
{
    [McpForUnityTool("search_missing_references")]
    public static class SearchMissingReferences
    {
        private const int MaxIssueDetails = 5000;

        public static object HandleCommand(JObject @params)
        {
            if (@params == null)
            {
                return new ErrorResponse("Parameters cannot be null.");
            }

            var p = new ToolParams(@params);
            var pagination = PaginationRequest.FromParams(@params, defaultPageSize: 100);
            pagination.PageSize = Mathf.Clamp(pagination.PageSize, 1, 500);
            pagination.Cursor = Mathf.Max(0, pagination.Cursor);

            string scope = p.Get("scope", "scene");
            string pathFilter = p.Get("pathFilter");
            string componentFilter = p.Get("componentFilter");
            bool includeMissingScripts = p.GetBool("includeMissingScripts", true);
            bool includeBrokenReferences = p.GetBool("includeBrokenReferences", true);
            bool includeBrokenPrefabs = p.GetBool("includeBrokenPrefabs", true);
            bool autoRepair = p.GetBool("autoRepair", false);

            try
            {
                var allIssues = new List<object>();
                int missingScripts = 0;
                int brokenReferences = 0;
                int brokenPrefabs = 0;
                int repaired = 0;
                bool sceneDirty = false;
                bool assetsDirty = false;

                if (scope == "scene")
                {
                    var activeScene = SceneManager.GetActiveScene();
                    if (!activeScene.IsValid() || !activeScene.isLoaded)
                    {
                        return new ErrorResponse("No active scene found.");
                    }

                    foreach (var root in activeScene.GetRootGameObjects())
                    {
                        foreach (var transform in root.GetComponentsInChildren<Transform>(true))
                        {
                            ScanGameObject(
                                transform.gameObject,
                                GetGameObjectPath(transform.gameObject),
                                includeMissingScripts,
                                includeBrokenReferences,
                                includeBrokenPrefabs,
                                componentFilter,
                                autoRepair,
                                allIssues,
                                ref missingScripts,
                                ref brokenReferences,
                                ref brokenPrefabs,
                                ref repaired,
                                ref sceneDirty,
                                ref assetsDirty,
                                isProjectAsset: false);
                        }
                    }

                    if (sceneDirty)
                    {
                        EditorSceneManager.MarkSceneDirty(activeScene);
                    }
                }
                else if (scope == "project")
                {
                    string[] searchInFolders = string.IsNullOrWhiteSpace(pathFilter)
                        ? null
                        : new[] { pathFilter };

                    var guids = AssetDatabase.FindAssets("t:Prefab t:ScriptableObject t:Material", searchInFolders)
                        .Distinct()
                        .ToArray();

                    foreach (var guid in guids)
                    {
                        string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                        if (string.IsNullOrEmpty(assetPath))
                        {
                            continue;
                        }

                        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
                        if (prefab != null)
                        {
                            foreach (var transform in prefab.GetComponentsInChildren<Transform>(true))
                            {
                                ScanGameObject(
                                    transform.gameObject,
                                    assetPath,
                                    includeMissingScripts,
                                    includeBrokenReferences,
                                    includeBrokenPrefabs,
                                    componentFilter,
                                    autoRepair,
                                    allIssues,
                                    ref missingScripts,
                                    ref brokenReferences,
                                    ref brokenPrefabs,
                                    ref repaired,
                                    ref sceneDirty,
                                    ref assetsDirty,
                                    isProjectAsset: true);
                            }
                            continue;
                        }

                        var scriptableObject = AssetDatabase.LoadAssetAtPath<ScriptableObject>(assetPath);
                        if (scriptableObject != null)
                        {
                            ScanSerializedObject(
                                scriptableObject,
                                assetPath,
                                scriptableObject.GetType().Name,
                                includeBrokenReferences,
                                componentFilter,
                                allIssues,
                                ref brokenReferences);
                            continue;
                        }

                        var material = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
                        if (material != null)
                        {
                            ScanSerializedObject(
                                material,
                                assetPath,
                                material.GetType().Name,
                                includeBrokenReferences,
                                componentFilter,
                                allIssues,
                                ref brokenReferences);
                        }
                    }

                    if (assetsDirty)
                    {
                        AssetDatabase.SaveAssets();
                    }
                }
                else
                {
                    return new ErrorResponse("Invalid 'scope'. Expected 'scene' or 'project'.");
                }

                int totalIssues = missingScripts + brokenReferences + brokenPrefabs;
                int totalCount = allIssues.Count;
                int cursor = Mathf.Clamp(pagination.Cursor, 0, totalCount);
                var pagedIssues = allIssues.Skip(cursor).Take(pagination.PageSize).ToList();
                int endIndex = cursor + pagedIssues.Count;
                int? nextCursor = endIndex < totalCount ? endIndex : (int?)null;

                string note = autoRepair
                    ? "Auto-repair removes missing scripts only. Broken references and broken prefab links are reported but not auto-repaired."
                    : "Broken references and broken prefab links are not auto-repaired.";

                return new SuccessResponse("Missing reference search completed.", new
                {
                    scope,
                    totalIssues,
                    missingScripts,
                    brokenReferences,
                    brokenPrefabs,
                    repaired,
                    issues = pagedIssues,
                    pageSize = pagination.PageSize,
                    cursor,
                    nextCursor,
                    hasMore = nextCursor.HasValue,
                    totalCount,
                    truncated = totalIssues > allIssues.Count,
                    note
                });
            }
            catch (System.Exception ex)
            {
                McpLog.Error($"[SearchMissingReferences] Error searching missing references: {ex.Message}");
                return new ErrorResponse($"Error searching missing references: {ex.Message}");
            }
        }

        private static void ScanGameObject(
            GameObject go,
            string path,
            bool includeMissingScripts,
            bool includeBrokenReferences,
            bool includeBrokenPrefabs,
            string componentFilter,
            bool autoRepair,
            List<object> issues,
            ref int missingScripts,
            ref int brokenReferences,
            ref int brokenPrefabs,
            ref int repaired,
            ref bool sceneDirty,
            ref bool assetsDirty,
            bool isProjectAsset)
        {
            if (go == null)
            {
                return;
            }

            bool hasComponentFilter = !string.IsNullOrWhiteSpace(componentFilter);

            if (includeMissingScripts && !hasComponentFilter)
            {
                int missing = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(go);
                if (missing > 0)
                {
                    missingScripts += missing;
                    if (issues.Count < MaxIssueDetails)
                    {
                        issues.Add(new
                        {
                            type = "missing_script",
                            gameObject = go.name,
                            path,
                            component = "MissingMonoBehaviour",
                            property = "m_Script",
                            count = missing
                        });
                    }

                    if (autoRepair)
                    {
                        Undo.RegisterCompleteObjectUndo(go, "Remove Missing Scripts");
                        int removed = GameObjectUtility.RemoveMonoBehavioursWithMissingScript(go);
                        if (removed > 0)
                        {
                            repaired += removed;
                            if (isProjectAsset)
                            {
                                EditorUtility.SetDirty(go);
                                assetsDirty = true;
                            }
                            else
                            {
                                sceneDirty = true;
                            }
                        }
                    }
                }
            }

            if (includeBrokenPrefabs && !hasComponentFilter)
            {
                var prefabStatus = PrefabUtility.GetPrefabInstanceStatus(go);
                if (prefabStatus == PrefabInstanceStatus.MissingAsset)
                {
                    brokenPrefabs++;
                    if (issues.Count < MaxIssueDetails)
                    {
                        issues.Add(new
                        {
                            type = "broken_prefab",
                            gameObject = go.name,
                            path,
                            component = "PrefabInstance",
                            property = "prefabAsset",
                            count = 1
                        });
                    }
                }
            }

            if (!includeBrokenReferences)
            {
                return;
            }

            foreach (var component in go.GetComponents<Component>())
            {
                if (component == null)
                {
                    continue;
                }

                string componentTypeName = component.GetType().Name;
                if (hasComponentFilter && componentTypeName != componentFilter)
                {
                    continue;
                }

                ScanSerializedObject(
                    component,
                    path,
                    componentTypeName,
                    includeBrokenReferences,
                    componentFilter,
                    issues,
                    ref brokenReferences,
                    go.name);
            }
        }

        private static void ScanSerializedObject(
            Object obj,
            string path,
            string componentTypeName,
            bool includeBrokenReferences,
            string componentFilter,
            List<object> issues,
            ref int brokenReferences,
            string gameObjectName = null)
        {
            if (!includeBrokenReferences || obj == null)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(componentFilter) && componentTypeName != componentFilter)
            {
                return;
            }

            var serializedObject = new SerializedObject(obj);
            var property = serializedObject.GetIterator();

            while (property.NextVisible(true))
            {
                if (property.propertyType != SerializedPropertyType.ObjectReference)
                {
                    continue;
                }

                if (property.objectReferenceValue != null || property.objectReferenceInstanceIDValue == 0)
                {
                    continue;
                }

                brokenReferences++;
                if (issues.Count < MaxIssueDetails)
                {
                    issues.Add(new
                    {
                        type = "broken_reference",
                        gameObject = gameObjectName ?? obj.name,
                        path,
                        component = componentTypeName,
                        property = property.propertyPath,
                        count = 1
                    });
                }
            }
        }

        private static string GetGameObjectPath(GameObject go)
        {
            if (go == null) return string.Empty;
            try
            {
                var names = new Stack<string>();
                Transform t = go.transform;
                while (t != null)
                {
                    names.Push(t.name);
                    t = t.parent;
                }
                return string.Join("/", names);
            }
            catch
            {
                return go.name;
            }
        }
    }
}
