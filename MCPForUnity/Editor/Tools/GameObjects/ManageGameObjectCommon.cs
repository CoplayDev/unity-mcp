#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Tools;
using Newtonsoft.Json.Linq;
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
                    Transform foundTransform = rootSearchObject
                        ? rootSearchObject.transform.Find(searchTerm)
                        : GameObject.Find(searchTerm)?.transform;
                    if (foundTransform != null)
                        results.Add(foundTransform.gameObject);
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
                            searchPoolComp = UnityEngine.Object.FindObjectsOfType(componentType, searchInactive)
                                .Cast<Component>()
                                .Select(c => c.gameObject);
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

                    GameObject objByPath = GameObject.Find(searchTerm);
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
            var rootObjects = SceneManager.GetActiveScene().GetRootGameObjects();
            var allObjects = new List<GameObject>();
            foreach (var root in rootObjects)
            {
                allObjects.AddRange(
                    root.GetComponentsInChildren<Transform>(includeInactive)
                        .Select(t => t.gameObject)
                );
            }
            return allObjects;
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
