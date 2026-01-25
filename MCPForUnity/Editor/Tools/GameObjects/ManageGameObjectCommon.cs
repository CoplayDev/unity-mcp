#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Tools;
using Newtonsoft.Json.Linq;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MCPForUnity.Editor.Tools.GameObjects
{
    internal static class ManageGameObjectCommon
    {
        internal static GameObject FindObjectInternal(JToken targetToken, string searchMethod, JObject findParams = null)
        {
            bool findAll = findParams?["findAll"]?.ToObject<bool>() ?? false;

            if (
                targetToken?.Type == JTokenType.Integer
                || (searchMethod == "by_id" && int.TryParse(targetToken?.ToString(), out _))
            )
            {
                findAll = false;
            }

            List<GameObject> results = FindObjectsInternal(targetToken, searchMethod, findAll, findParams);
            return results.Count > 0 ? results[0] : null;
        }

        internal static List<GameObject> FindObjectsInternal(
            JToken targetToken,
            string searchMethod,
            bool findAll,
            JObject findParams = null
        )
        {
            List<GameObject> results = new List<GameObject>();
            string searchTerm = findParams?["searchTerm"]?.ToString() ?? targetToken?.ToString();
            bool searchInChildren = findParams?["searchInChildren"]?.ToObject<bool>() ?? false;
            bool searchInactive = findParams?["searchInactive"]?.ToObject<bool>() ?? false;

            if (string.IsNullOrEmpty(searchMethod))
            {
                if (targetToken?.Type == JTokenType.Integer)
                    searchMethod = "by_id";
                else if (!string.IsNullOrEmpty(searchTerm) && searchTerm.Contains('/'))
                    searchMethod = "by_path";
                else
                    searchMethod = "by_name";
            }

            GameObject rootSearchObject = null;
            if (searchInChildren && targetToken != null)
            {
                rootSearchObject = FindObjectInternal(targetToken, "by_id_or_name_or_path");
                if (rootSearchObject == null)
                {
                    McpLog.Warn($"[ManageGameObject.Find] Root object '{targetToken}' for child search not found.");
                    return results;
                }
            }

            switch (searchMethod)
            {
                case "by_id":
                    if (int.TryParse(searchTerm, out int instanceId))
                    {
                        var allObjects = GetAllSceneObjects(searchInactive);
                        GameObject obj = allObjects.FirstOrDefault(go => go.GetInstanceID() == instanceId);
                        if (obj != null)
                            results.Add(obj);
                    }
                    break;

                case "by_name":
                    var searchPoolName = rootSearchObject
                        ? rootSearchObject
                            .GetComponentsInChildren<Transform>(searchInactive)
                            .Select(t => t.gameObject)
                        : GetAllSceneObjects(searchInactive);
                    results.AddRange(searchPoolName.Where(go => go.name == searchTerm));
                    break;

                case "by_path":
                    if (rootSearchObject != null)
                    {
                        Transform foundTransform = rootSearchObject.transform.Find(searchTerm);
                        if (foundTransform != null)
                            results.Add(foundTransform.gameObject);
                    }
                    else
                    {
                        var prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
                        if (prefabStage != null || searchInactive)
                        {
                            // In Prefab Stage, GameObject.Find() doesn't work, need to search manually
                            var allObjects = GetAllSceneObjects(searchInactive);
                            foreach (var go in allObjects)
                            {
                                if (GameObjectLookup.MatchesPath(go, searchTerm))
                                {
                                    results.Add(go);
                                }
                            }
                        }
                        else
                        {
                            var found = GameObject.Find(searchTerm);
                            if (found != null)
                                results.Add(found);
                        }
                    }
                    break;

                case "by_tag":
                    var searchPoolTag = rootSearchObject
                        ? rootSearchObject
                            .GetComponentsInChildren<Transform>(searchInactive)
                            .Select(t => t.gameObject)
                        : GetAllSceneObjects(searchInactive);
                    results.AddRange(searchPoolTag.Where(go => go.CompareTag(searchTerm)));
                    break;

                case "by_layer":
                    var searchPoolLayer = rootSearchObject
                        ? rootSearchObject
                            .GetComponentsInChildren<Transform>(searchInactive)
                            .Select(t => t.gameObject)
                        : GetAllSceneObjects(searchInactive);
                    if (int.TryParse(searchTerm, out int layerIndex))
                    {
                        results.AddRange(searchPoolLayer.Where(go => go.layer == layerIndex));
                    }
                    else
                    {
                        int namedLayer = LayerMask.NameToLayer(searchTerm);
                        if (namedLayer != -1)
                            results.AddRange(searchPoolLayer.Where(go => go.layer == namedLayer));
                    }
                    break;

                case "by_component":
                    Type componentType = FindType(searchTerm);
                    if (componentType != null)
                    {
                        IEnumerable<GameObject> searchPoolComp;
                        if (rootSearchObject)
                        {
                            searchPoolComp = rootSearchObject
                                .GetComponentsInChildren(componentType, searchInactive)
                                .Select(c => (c as Component).gameObject);
                        }
                        else
                        {
#if UNITY_2023_1_OR_NEWER
                            var inactive = searchInactive ? FindObjectsInactive.Include : FindObjectsInactive.Exclude;
                            searchPoolComp = UnityEngine.Object.FindObjectsByType(componentType, inactive, FindObjectsSortMode.None)
                                .Cast<Component>()
                                .Select(c => c.gameObject);
#else
                            searchPoolComp = UnityEngine.Object.FindObjectsOfType(componentType, searchInactive)
                                .Cast<Component>()
                                .Select(c => c.gameObject);
#endif
                        }
                        results.AddRange(searchPoolComp.Where(go => go != null));
                    }
                    else
                    {
                        McpLog.Warn($"[ManageGameObject.Find] Component type not found: {searchTerm}");
                    }
                    break;

                case "by_id_or_name_or_path":
                    if (int.TryParse(searchTerm, out int id))
                    {
                        var allObjectsId = GetAllSceneObjects(true);
                        GameObject objById = allObjectsId.FirstOrDefault(go => go.GetInstanceID() == id);
                        if (objById != null)
                        {
                            results.Add(objById);
                            break;
                        }
                    }

                    // Try path search - in Prefab Stage, GameObject.Find() doesn't work
                    var allObjectsForPath = GetAllSceneObjects(true);
                    GameObject objByPath = allObjectsForPath.FirstOrDefault(go =>
                    {
                        return GameObjectLookup.MatchesPath(go, searchTerm);
                    });
                    if (objByPath != null)
                    {
                        results.Add(objByPath);
                        break;
                    }

                    var allObjectsName = GetAllSceneObjects(true);
                    results.AddRange(allObjectsName.Where(go => go.name == searchTerm));
                    break;

                default:
                    McpLog.Warn($"[ManageGameObject.Find] Unknown search method: {searchMethod}");
                    break;
            }

            if (!findAll && results.Count > 1)
            {
                return new List<GameObject> { results[0] };
            }

            return results.Distinct().ToList();
        }

        private static IEnumerable<GameObject> GetAllSceneObjects(bool includeInactive)
        {
            // Delegate to GameObjectLookup to avoid code duplication and ensure consistent behavior
            return GameObjectLookup.GetAllSceneObjects(includeInactive);
        }

        private static Type FindType(string typeName)
        {
            if (ComponentResolver.TryResolve(typeName, out Type resolvedType, out string error))
            {
                return resolvedType;
            }

            if (!string.IsNullOrEmpty(error))
            {
                McpLog.Warn($"[FindType] {error}");
            }

            return null;
        }
    }
}
