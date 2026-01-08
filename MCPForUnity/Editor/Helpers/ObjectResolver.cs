using System;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace MCPForUnity.Editor.Helpers
{
    /// <summary>
    /// Resolves Unity Objects by instruction (handles GameObjects, Components, Assets).
    /// Extracted from ManageGameObject to eliminate cross-tool dependencies.
    /// </summary>
    public static class ObjectResolver
    {
        /// <summary>
        /// Resolves any Unity Object by instruction.
        /// </summary>
        /// <typeparam name="T">The type of Unity Object to resolve</typeparam>
        /// <param name="instruction">JObject with "find" (required), "method" (optional), "component" (optional)</param>
        /// <returns>The resolved object, or null if not found</returns>
        public static T Resolve<T>(JObject instruction) where T : UnityEngine.Object
        {
            return Resolve(instruction, typeof(T)) as T;
        }

        /// <summary>
        /// Resolves any Unity Object by instruction.
        /// </summary>
        /// <param name="instruction">JObject with "find" (required), "method" (optional), "component" (optional)</param>
        /// <param name="targetType">The type of Unity Object to resolve</param>
        /// <returns>The resolved object, or null if not found</returns>
        public static UnityEngine.Object Resolve(JObject instruction, Type targetType)
        {
            if (instruction == null)
                return null;

            string findTerm = instruction["find"]?.ToString();
            string method = instruction["method"]?.ToString()?.ToLower();
            string componentName = instruction["component"]?.ToString();

            if (string.IsNullOrEmpty(findTerm))
            {
                McpLog.Warn("[ObjectResolver] Find instruction missing 'find' term.");
                return null;
            }

            // Use a flexible default search method if none provided
            string searchMethodToUse = string.IsNullOrEmpty(method) ? "by_id_or_name_or_path" : method;

            // --- Asset Search ---
            // Normalize path separators before checking asset paths
            string normalizedPath = AssetPathUtility.NormalizeSeparators(findTerm);
            
            // If the target is an asset type, try AssetDatabase first
            if (IsAssetType(targetType) || 
                (typeof(GameObject).IsAssignableFrom(targetType) && normalizedPath.StartsWith("Assets/")))
            {
                UnityEngine.Object asset = TryLoadAsset(normalizedPath, targetType);
                if (asset != null)
                    return asset;
                // If still not found, fall through to scene search
            }

            // --- Scene Object Search ---
            GameObject foundGo = GameObjectLookup.FindByTarget(new JValue(findTerm), searchMethodToUse, includeInactive: false);

            if (foundGo == null)
            {
                return null;
            }

            // Get the target object/component from the found GameObject
            if (targetType == typeof(GameObject))
            {
                return foundGo;
            }
            else if (typeof(Component).IsAssignableFrom(targetType))
            {
                Type componentToGetType = targetType;
                if (!string.IsNullOrEmpty(componentName))
                {
                    Type specificCompType = GameObjectLookup.FindComponentType(componentName);
                    if (specificCompType != null && typeof(Component).IsAssignableFrom(specificCompType))
                    {
                        componentToGetType = specificCompType;
                    }
                    else
                    {
                        McpLog.Warn($"[ObjectResolver] Could not find component type '{componentName}'. Falling back to target type '{targetType.Name}'.");
                    }
                }

                Component foundComp = foundGo.GetComponent(componentToGetType);
                if (foundComp == null)
                {
                    McpLog.Warn($"[ObjectResolver] Found GameObject '{foundGo.name}' but could not find component of type '{componentToGetType.Name}'.");
                }
                return foundComp;
            }
            else
            {
                McpLog.Warn($"[ObjectResolver] Find instruction handling not implemented for target type: {targetType.Name}");
                return null;
            }
        }

        /// <summary>
        /// Convenience method to resolve a GameObject.
        /// </summary>
        public static GameObject ResolveGameObject(JToken target, string searchMethod = null)
        {
            if (target == null)
                return null;

            // If target is a simple value, use GameObjectLookup directly
            if (target.Type != JTokenType.Object)
            {
                return GameObjectLookup.FindByTarget(target, searchMethod ?? "by_id_or_name_or_path");
            }

            // If target is an instruction object
            var instruction = target as JObject;
            if (instruction != null)
            {
                return Resolve<GameObject>(instruction);
            }

            return null;
        }

        /// <summary>
        /// Convenience method to resolve a Material.
        /// </summary>
        public static Material ResolveMaterial(string pathOrName)
        {
            if (string.IsNullOrEmpty(pathOrName))
                return null;

            var instruction = new JObject { ["find"] = pathOrName };
            return Resolve<Material>(instruction);
        }

        /// <summary>
        /// Convenience method to resolve a Texture.
        /// </summary>
        public static Texture ResolveTexture(string pathOrName)
        {
            if (string.IsNullOrEmpty(pathOrName))
                return null;

            var instruction = new JObject { ["find"] = pathOrName };
            return Resolve<Texture>(instruction);
        }

        // --- Private Helpers ---

        private static bool IsAssetType(Type type)
        {
            return typeof(Material).IsAssignableFrom(type) ||
                   typeof(Texture).IsAssignableFrom(type) ||
                   typeof(ScriptableObject).IsAssignableFrom(type) ||
                   type.FullName?.StartsWith("UnityEngine.U2D") == true ||
                   typeof(AudioClip).IsAssignableFrom(type) ||
                   typeof(AnimationClip).IsAssignableFrom(type) ||
                   typeof(Font).IsAssignableFrom(type) ||
                   typeof(Shader).IsAssignableFrom(type) ||
                   typeof(ComputeShader).IsAssignableFrom(type);
        }

        private static UnityEngine.Object TryLoadAsset(string findTerm, Type targetType)
        {
            // Try loading directly by path first
            UnityEngine.Object asset = AssetDatabase.LoadAssetAtPath(findTerm, targetType);
            if (asset != null) 
                return asset;
            
            // Try generic load if type-specific failed
            asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(findTerm);
            if (asset != null && targetType.IsAssignableFrom(asset.GetType())) 
                return asset;

            // Try finding by name/type using FindAssets
            string searchFilter = $"t:{targetType.Name} {System.IO.Path.GetFileNameWithoutExtension(findTerm)}";
            string[] guids = AssetDatabase.FindAssets(searchFilter);

            if (guids.Length == 1)
            {
                asset = AssetDatabase.LoadAssetAtPath(AssetDatabase.GUIDToAssetPath(guids[0]), targetType);
                if (asset != null) 
                    return asset;
            }
            else if (guids.Length > 1)
            {
                McpLog.Warn($"[ObjectResolver] Ambiguous asset find: Found {guids.Length} assets matching filter '{searchFilter}'. Provide a full path or unique name.");
                return null;
            }

            return null;
        }
    }
}

