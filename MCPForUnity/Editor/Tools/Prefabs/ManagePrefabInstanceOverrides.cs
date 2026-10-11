using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Runtime.Helpers;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MCPForUnity.Editor.Tools.Prefabs
{
    internal static class ManagePrefabInstanceOverrides
    {
        // Removed GameObject overrides were added after Unity 2021. Cache the optional API
        // so the rest of this tool compiles against the oldest supported editor.
        private static readonly MethodInfo RemovedGameObjectsMethod = typeof(PrefabUtility).GetMethod(
            "GetRemovedGameObjects", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(GameObject) }, null);
        private static readonly Type RemovedGameObjectType = RemovedGameObjectsMethod?.ReturnType.GetGenericArguments().FirstOrDefault();
        private static readonly PropertyInfo RemovedParentProperty = RemovedGameObjectType?.GetProperty("parentOfRemovedGameObjectInInstance");
        private static readonly PropertyInfo RemovedAssetProperty = RemovedGameObjectType?.GetProperty("assetGameObject");
        private static readonly string[] Kinds = { "property", "added_component", "removed_component", "added_gameobject", "removed_gameobject" };
        private static bool SupportsRemovedGameObjects => RemovedParentProperty != null && RemovedAssetProperty != null;

        private sealed class Entry
        {
            public PrefabOverride Native;
            public string Kind;
            public int Index;
            public GameObject Owner;
            public Object Instance;
            public Object Asset;
            public string Path;
            public Type ComponentType => (Instance as Component ?? Asset as Component)?.GetType();
        }

        public static object HandleCommand(JObject args, string action)
        {
            bool listing = action == "list_overrides";
            bool applying = action == "apply_overrides";
            string target = args["target"]?.ToString();
            if (string.IsNullOrWhiteSpace(target)) return new ErrorResponse("'target' is required for prefab override actions.");
            string search = int.TryParse(target, out _) ? "by_id" : target.Contains("/") ? "by_path" : "by_name";
            var matches = GameObjectLookup.SearchGameObjects(search, target, true, 2);
            if (matches.Count != 1) return new ErrorResponse(matches.Count == 0
                ? $"Scene GameObject '{target}' was not found."
                : "Target is ambiguous. Use a hierarchy path or instance ID.");
            var selected = GameObjectLookup.FindById(matches[0]);
            if (EditorUtility.IsPersistent(selected) || !selected.scene.IsValid() || !selected.scene.isLoaded
                || StageUtility.GetStageHandle(selected) != StageUtility.GetMainStageHandle())
                return new ErrorResponse("Target must be a loaded scene instance, outside Prefab Stage.");
            var root = PrefabUtility.GetOutermostPrefabInstanceRoot(selected);
            // Added descendants may not themselves have a prefab connection yet.
            for (var parent = selected.transform.parent; root == null && parent != null; parent = parent.parent)
                root = PrefabUtility.GetOutermostPrefabInstanceRoot(parent.gameObject);
            if (root == null) return new ErrorResponse("Target is not part of a prefab instance.");

            string child = args["childPath"]?.ToString() ?? "";
            if (child.StartsWith("/", StringComparison.Ordinal) || child.Contains("\\")
                || (child.Length > 0 && child.Split('/').Any(part => part == "" || part == "." || part == "..")))
                return new ErrorResponse("childPath must be a relative hierarchy path without empty, '.' or '..' segments.");
            string scope = JoinPath(RelativePath(root, selected), child);
            var scopeObjects = new List<GameObject> { selected };
            foreach (var segment in child.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries))
                scopeObjects = scopeObjects.SelectMany(obj => obj.transform.Cast<Transform>()
                    .Where(transform => transform.name == segment).Select(transform => transform.gameObject)).ToList();
            if (scopeObjects.Count > 1) return new ErrorResponse("Child path is ambiguous. Target a child's instance ID instead.");
            var scopeObject = scopeObjects.SingleOrDefault();
            string component = args["componentType"]?.ToString();
            string property = args["propertyPath"]?.ToString();
            string prefix = args["propertyPrefix"]?.ToString();
            string kind = args["overrideType"]?.ToString() ?? (listing ? null : "property");
            int? index = args["overrideIndex"]?.ToObject<int>();
            bool includeDefaults = args["includeDefaultOverrides"]?.ToObject<bool>() ?? false;
            if (kind != null && !Kinds.Contains(kind)) return new ErrorResponse("Unknown overrideType. Valid kinds: " + string.Join(", ", Kinds));
            if (index < 0 || (index.HasValue && kind == null)) return new ErrorResponse("overrideIndex must be non-negative and accompanied by overrideType.");
            if (!listing && prefix != null) return new ErrorResponse("propertyPrefix is a listing filter. Use propertyPath for writes.");
            if (!listing && kind != "property" && (property != null || includeDefaults))
                return new ErrorResponse("propertyPath and includeDefaultOverrides apply only to property overrides.");
            if (!listing && includeDefaults && string.IsNullOrWhiteSpace(property))
                return new ErrorResponse("Default overrides require an explicit propertyPath for writes.");
            if (kind == "removed_gameobject" && !SupportsRemovedGameObjects)
                return new ErrorResponse("Removed GameObject overrides are not supported by this Unity version.");

            var entries = Collect(root, includeDefaults);
            var scoped = entries.Where(entry => InScope(entry.Path, scope)
                && IsDescendantOrSelf(entry.Owner, scopeObject ?? selected)
                && (kind == null || entry.Kind == kind) && (!index.HasValue || entry.Index == index.Value)
                && MatchesComponent(entry, component)).ToList();
            if (scopeObject == null && !scoped.Any())
                return new ErrorResponse($"Child path '{child}' was not found, including removed GameObject overrides.");

            if (listing) return List(root, scope, scoped, property, prefix, includeDefaults);

            // A write without an explicit index affects this object and its components only.
            // An index returned by list_overrides can explicitly select a descendant entry.
            var candidates = scoped.Where(entry => index.HasValue || entry.Path == scope).ToList();
            if (kind == "property" && property != null)
                candidates = candidates.Where(entry => HasPropertyOverride(entry.Instance, property, includeDefaults)).ToList();
            if ((kind != "property" || property != null || component != null) && candidates.Count > 1)
                return new ErrorResponse("Override selection is ambiguous. Specify childPath, componentType or overrideIndex from list_overrides.");
            if (candidates.Count == 0)
                return new SuccessResponse("No matching overrides.", new { changed = 0, childPath = scope, overrideType = kind });

            var destinations = new Dictionary<Entry, string>();
            foreach (var entry in candidates)
            {
                if (applying)
                {
                    string path = args["prefabPath"]?.ToString() ?? AssetDatabase.GetAssetPath(entry.Asset);
                    Object source = entry.Kind == "property" ? entry.Instance : entry.Asset;
                    if (!AssetPathUtility.IsValidAssetPath(path) || !path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)
                        || AssetDatabase.LoadAssetAtPath<GameObject>(path) == null
                        || (AssetDatabase.GetAssetPath(entry.Asset) != path
                            && PrefabUtility.GetCorrespondingObjectFromSourceAtPath(source, path) == null))
                        return new ErrorResponse("prefabPath must identify this override's source prefab or one of its prefab ancestors.");
                    if (PrefabUtility.IsPartOfImmutablePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path)))
                        return new ErrorResponse("Cannot apply overrides to an immutable prefab asset.");
                    if (property != null && !CanApplyProperty(entry.Instance, property, path))
                        return new ErrorResponse("This property cannot be applied alone at the destination. Select its containing array or managed reference explicitly.");
                    if (HasSceneReferences(entry, property, includeDefaults, path, root))
                        return new ErrorResponse("Cannot apply a scene-only object reference to a prefab asset. Revert it or use an asset/prefab reference.");
                    destinations[entry] = path;
                }
            }

            var scene = selected.scene;
            foreach (var entry in candidates)
            {
                if (property != null)
                {
                    using (var serialized = new SerializedObject(entry.Instance))
                    {
                        var serializedProperty = serialized.FindProperty(property);
                        if (applying) PrefabUtility.ApplyPropertyOverride(serializedProperty, destinations[entry], InteractionMode.AutomatedAction);
                        else PrefabUtility.RevertPropertyOverride(serializedProperty, InteractionMode.AutomatedAction);
                    }
                }
                else if (applying) entry.Native.Apply(destinations[entry], InteractionMode.AutomatedAction);
                else entry.Native.Revert(InteractionMode.AutomatedAction);
            }
            EditorSceneManager.MarkSceneDirty(scene);
            return new SuccessResponse(applying ? "Selected overrides applied to prefab assets." : "Selected overrides reverted.",
                new { changed = candidates.Count, childPath = scope, overrideType = kind, prefabPaths = destinations.Values.Distinct().ToArray() });
        }

        private static List<Entry> Collect(GameObject root, bool includeDefaults)
        {
            var entries = new List<Entry>();
            void Add(PrefabOverride item, string kind, int index, GameObject owner, Object instance, Object asset, string path = null)
            {
                entries.Add(new Entry { Native = item, Kind = kind, Index = index, Owner = owner,
                    Instance = instance, Asset = asset, Path = path ?? RelativePath(root, owner) });
            }
            int i = 0;
            var nonDefaultObjects = includeDefaults ? null : new HashSet<Object>(
                PrefabUtility.GetObjectOverrides(root, false).Select(item => item.instanceObject));
            foreach (var item in PrefabUtility.GetObjectOverrides(root, true))
            {
                int index = i++;
                var owner = item.instanceObject as GameObject ?? (item.instanceObject as Component)?.gameObject;
                if (owner != null && (includeDefaults || nonDefaultObjects.Contains(item.instanceObject)))
                    Add(item, "property", index, owner, item.instanceObject, item.GetAssetObject());
            }
            i = 0;
            foreach (var item in PrefabUtility.GetAddedComponents(root))
                Add(item, "added_component", i++, item.instanceComponent.gameObject, item.instanceComponent, item.GetAssetObject());
            i = 0;
            foreach (var item in PrefabUtility.GetRemovedComponents(root))
                Add(item, "removed_component", i++, item.containingInstanceGameObject, null, item.assetComponent);
            i = 0;
            foreach (var item in PrefabUtility.GetAddedGameObjects(root))
                Add(item, "added_gameobject", i++, item.instanceGameObject, item.instanceGameObject, item.GetAssetObject());
            if (SupportsRemovedGameObjects)
            {
                i = 0;
                foreach (var item in (IEnumerable)RemovedGameObjectsMethod.Invoke(null, new object[] { root }))
                {
                    var parent = (GameObject)RemovedParentProperty.GetValue(item);
                    var asset = (GameObject)RemovedAssetProperty.GetValue(item);
                    Add((PrefabOverride)item, "removed_gameobject", i++, parent, null, asset, JoinPath(RelativePath(root, parent), asset.name));
                }
            }
            return entries;
        }

        private static object List(GameObject root, string scope, List<Entry> entries, string property, string prefix, bool includeDefaults)
        {
            var groups = new JArray();
            foreach (var entry in entries.Where(entry => entry.Kind == "property"))
            {
                var modifications = new JArray();
                using (var instance = new SerializedObject(entry.Instance))
                using (var asset = entry.Asset == null ? null : new SerializedObject(entry.Asset))
                {
                    foreach (var iterator in Properties(instance))
                    {
                        if (!iterator.prefabOverride || (!includeDefaults && iterator.isDefaultOverride)
                            || (property != null && iterator.propertyPath != property)
                            || (prefix != null && !iterator.propertyPath.StartsWith(prefix, StringComparison.Ordinal))) continue;
                        var prefabProperty = asset?.FindProperty(iterator.propertyPath);
                        bool instanceAvailable = TryReadValue(iterator, out var instanceValue);
                        bool prefabAvailable = TryReadValue(prefabProperty, out var prefabValue);
                        modifications.Add(new JObject {
                            ["propertyPath"] = iterator.propertyPath, ["valueType"] = iterator.propertyType.ToString(),
                            ["instanceValue"] = instanceValue, ["prefabValue"] = prefabValue,
                            ["valueAvailable"] = instanceAvailable && prefabAvailable,
                            ["instanceValueAvailable"] = instanceAvailable, ["prefabValueAvailable"] = prefabAvailable,
                            ["isDefaultOverride"] = iterator.isDefaultOverride
                        });
                    }
                }
                if (modifications.Count == 0) continue;
                var group = Describe(root, entry);
                group["modifications"] = modifications;
                groups.Add(group);
            }
            JArray Structural(string kind) => new JArray(entries.Where(entry => entry.Kind == kind && property == null && prefix == null)
                .Select(entry => Describe(root, entry)));
            return new SuccessResponse("Prefab instance overrides listed.", new JObject {
                ["instanceId"] = root.GetInstanceIDCompat(), ["instanceName"] = root.name,
                ["prefabPath"] = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(root), ["childPath"] = scope,
                ["propertyOverrides"] = groups, ["addedComponents"] = Structural("added_component"),
                ["removedComponents"] = Structural("removed_component"), ["addedGameObjects"] = Structural("added_gameobject"),
                ["removedGameObjects"] = Structural("removed_gameobject"), ["removedGameObjectsSupported"] = SupportsRemovedGameObjects
            });
        }

        private static JObject Describe(GameObject root, Entry entry) => new JObject {
            ["overrideType"] = entry.Kind, ["overrideIndex"] = entry.Index, ["childPath"] = entry.Path,
            ["objectPath"] = JoinPath(root.name, entry.Path), ["objectName"] = entry.Owner.name,
            ["componentType"] = entry.ComponentType?.FullName,
            ["instanceId"] = entry.Instance != null ? new JValue(entry.Instance.GetInstanceIDCompat()) : JValue.CreateNull(),
            ["prefabAssetPath"] = AssetDatabase.GetAssetPath(entry.Asset)
        };

        private static bool HasPropertyOverride(Object target, string path, bool includeDefaults)
        {
            using (var serialized = new SerializedObject(target))
            {
                var property = serialized.FindProperty(path);
                return property != null && property.prefabOverride && (includeDefaults || !property.isDefaultOverride);
            }
        }

        private static bool HasSceneReferences(Entry entry, string path, bool includeDefaults, string prefabPath, GameObject root)
        {
            // References to objects in this same prefab can be remapped by Unity; references
            // to unrelated scene objects cannot be stored in an asset.
            IEnumerable<Object> objects = entry.Kind == "added_gameobject"
                ? entry.Owner.GetComponentsInChildren<Component>(true).Cast<Object>().Concat(entry.Owner.GetComponentsInChildren<Transform>(true).Select(t => (Object)t.gameObject))
                : new[] { entry.Instance };
            foreach (var target in objects.Where(obj => obj != null))
            using (var serialized = new SerializedObject(target))
            {
                foreach (var iterator in Properties(serialized))
                {
                    if (iterator.propertyType != SerializedPropertyType.ObjectReference
                        || (entry.Kind == "property" && (!iterator.prefabOverride || (!includeDefaults && iterator.isDefaultOverride)))
                        || (path != null && iterator.propertyPath != path && !iterator.propertyPath.StartsWith(path + ".", StringComparison.Ordinal))) continue;
                    var reference = iterator.objectReferenceValue;
                    if (reference == null || EditorUtility.IsPersistent(reference)) continue;
                    var referencedObject = reference as GameObject ?? (reference as Component)?.gameObject;
                    if (entry.Kind == "added_gameobject" && referencedObject != null
                        && (referencedObject == entry.Owner || referencedObject.transform.IsChildOf(entry.Owner.transform))) continue;
                    // A different instance of the same prefab is still a scene-only reference.
                    if (referencedObject == null || PrefabUtility.GetOutermostPrefabInstanceRoot(referencedObject) != root
                        || PrefabUtility.GetCorrespondingObjectFromSourceAtPath(reference, prefabPath) == null) return true;
                }
            }
            return false;
        }

        private static IEnumerable<SerializedProperty> Properties(SerializedObject serialized)
        {
            var visited = new HashSet<long>();
            using (var iterator = serialized.GetIterator())
            {
                bool enterChildren = true;
                while (iterator.Next(enterChildren))
                {
                    enterChildren = iterator.propertyType != SerializedPropertyType.String
                        && (iterator.propertyType != SerializedPropertyType.ManagedReference || visited.Add(iterator.managedReferenceId));
                    yield return iterator;
                }
            }
        }

        private static bool CanApplyProperty(Object target, string path, string prefabPath)
        {
            var source = PrefabUtility.GetCorrespondingObjectFromSourceAtPath(target, prefabPath);
            using (var instance = new SerializedObject(target))
            using (var asset = new SerializedObject(source))
            {
                // Unity silently promotes a missing array element to an apply of the whole
                // array in AutomatedAction mode. Require the caller to select that array.
                const string element = ".Array.data[";
                int start = 0;
                while ((start = path.IndexOf(element, start, StringComparison.Ordinal)) >= 0)
                {
                    var array = asset.FindProperty(path.Substring(0, start));
                    int numberStart = start + element.Length;
                    int numberEnd = path.IndexOf(']', numberStart);
                    if (array == null || numberEnd < 0 || !int.TryParse(path.Substring(numberStart, numberEnd - numberStart), out int index)
                        || index >= array.arraySize) return false;
                    start = numberEnd + 1;
                }
                var instanceProperty = instance.FindProperty(path);
                var assetProperty = asset.FindProperty(path);
                if (assetProperty == null || instanceProperty.propertyType != assetProperty.propertyType) return false;
                for (int end = path.IndexOf('.'); end >= 0; end = path.IndexOf('.', end + 1))
                {
                    var parent = instance.FindProperty(path.Substring(0, end));
                    if (parent?.propertyType != SerializedPropertyType.ManagedReference) continue;
                    var assetParent = asset.FindProperty(path.Substring(0, end));
                    if (assetParent?.propertyType != SerializedPropertyType.ManagedReference
                        || parent.managedReferenceFullTypename != assetParent.managedReferenceFullTypename) return false;
                }
                return true;
            }
        }

        private static bool TryReadValue(SerializedProperty property, out JToken value)
        {
            value = JValue.CreateNull();
            if (property == null) return false;
            object raw;
            switch (property.propertyType)
            {
                case SerializedPropertyType.Integer:
                case SerializedPropertyType.ArraySize:
                case SerializedPropertyType.Character: raw = property.longValue; break;
                case SerializedPropertyType.Boolean: raw = property.boolValue; break;
                case SerializedPropertyType.Float: raw = property.doubleValue; break;
                case SerializedPropertyType.String: raw = property.stringValue; break;
                case SerializedPropertyType.Enum: raw = property.intValue; break;
                case SerializedPropertyType.ObjectReference:
                    var reference = property.objectReferenceValue;
                    value = reference == null ? JValue.CreateNull() : new JObject {
                        ["name"] = reference.name, ["instanceId"] = reference.GetInstanceIDCompat(),
                        ["assetPath"] = AssetDatabase.GetAssetPath(reference)
                    };
                    return true;
                case SerializedPropertyType.Vector2: raw = property.vector2Value; break;
                case SerializedPropertyType.Vector3: raw = property.vector3Value; break;
                case SerializedPropertyType.Vector4: raw = property.vector4Value; break;
                case SerializedPropertyType.Quaternion: raw = property.quaternionValue; break;
                case SerializedPropertyType.Color: raw = property.colorValue; break;
                case SerializedPropertyType.Rect: raw = property.rectValue; break;
                case SerializedPropertyType.Bounds: raw = property.boundsValue; break;
                default: return false;
            }
            value = JToken.FromObject(raw, UnityJsonSerializer.Instance);
            return true;
        }

        private static bool MatchesComponent(Entry entry, string component) => component == null
            || entry.ComponentType?.Name == component || entry.ComponentType?.FullName == component;
        private static bool InScope(string path, string scope) => scope.Length == 0 || path == scope || path.StartsWith(scope + "/", StringComparison.Ordinal);
        private static bool IsDescendantOrSelf(GameObject child, GameObject parent) => child == parent || child.transform.IsChildOf(parent.transform);
        private static string JoinPath(string parent, string child) => parent.Length == 0 ? child : child.Length == 0 ? parent : parent + "/" + child;
        private static string RelativePath(GameObject root, GameObject child)
        {
            var names = new List<string>();
            for (var current = child.transform; current != root.transform && current != null; current = current.parent) names.Add(current.name);
            names.Reverse();
            return string.Join("/", names);
        }
    }
}
