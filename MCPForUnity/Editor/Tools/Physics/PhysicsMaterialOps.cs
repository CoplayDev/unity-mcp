using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using MCPForUnity.Editor.Helpers;

namespace MCPForUnity.Editor.Tools.Physics
{
    internal static class PhysicsMaterialOps
    {
        public static object Create(JObject @params)
        {
            var p = new ToolParams(@params);
            string dimension = (p.Get("dimension") ?? "3d").ToLowerInvariant();

            var nameResult = p.GetRequired("name");
            var nameErr = nameResult.GetOrError(out string name);
            if (nameErr != null) return nameErr;

            string folder = p.Get("path") ?? "Assets/Physics Materials";
            folder = AssetPathUtility.SanitizeAssetPath(folder);
            if (string.IsNullOrEmpty(folder))
                return new ErrorResponse("Invalid folder path.");

            if (!EnsureFolderExists(folder, out string folderError))
                return new ErrorResponse(folderError);

            if (dimension == "2d")
                return Create2D(name, folder, p);

            if (dimension != "3d")
                return new ErrorResponse($"Invalid dimension: '{dimension}'. Use '3d' or '2d'.");

            return Create3D(name, folder, p);
        }

        public static object Configure(JObject @params)
        {
            var p = new ToolParams(@params);
            string dimension = (p.Get("dimension") ?? "3d").ToLowerInvariant();

            var pathResult = p.GetRequired("path");
            var pathErr = pathResult.GetOrError(out string path);
            if (pathErr != null) return pathErr;

            path = AssetPathUtility.SanitizeAssetPath(path);
            if (string.IsNullOrEmpty(path))
                return new ErrorResponse("Invalid asset path.");

            var properties = p.GetRaw("properties") as JObject;
            if (properties == null || properties.Count == 0)
                return new ErrorResponse("'properties' parameter is required and must be a non-empty object.");

            if (dimension != "3d" && dimension != "2d")
                return new ErrorResponse($"Invalid dimension: '{dimension}'. Use '3d' or '2d'.");

            if (dimension == "2d")
                return Configure2D(path, properties);

            return Configure3D(path, properties);
        }

        public static object Assign(JObject @params)
        {
            var p = new ToolParams(@params);

            var targetToken = p.GetRaw("target");
            if (targetToken == null || string.IsNullOrEmpty(targetToken.ToString()))
                return new ErrorResponse("'target' parameter is required.");

            var matPathResult = p.GetRequired("material_path");
            var matPathErr = matPathResult.GetOrError(out string materialPath);
            if (matPathErr != null) return matPathErr;

            materialPath = AssetPathUtility.SanitizeAssetPath(materialPath);
            if (string.IsNullOrEmpty(materialPath))
                return new ErrorResponse("Invalid material path.");

            string searchMethod = p.Get("search_method") ?? "by_name";
            string colliderType = p.Get("collider_type");
            int? componentIndex = ParamCoercion.CoerceIntNullable(p.GetRaw("componentIndex") ?? p.GetRaw("component_index"));

            var go = GameObjectLookup.FindByTarget(targetToken, searchMethod);
            if (go == null)
                return new ErrorResponse($"GameObject not found: '{targetToken}'.");

            // Try to load as 3D physics material first
#if UNITY_6000_0_OR_NEWER
            var mat3D = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(materialPath);
#else
            var mat3D = AssetDatabase.LoadAssetAtPath<PhysicMaterial>(materialPath);
#endif
            var mat2D = AssetDatabase.LoadAssetAtPath<PhysicsMaterial2D>(materialPath);

            if (mat3D == null && mat2D == null)
                return new ErrorResponse($"No physics material found at path: '{materialPath}'.");

            // Try 3D colliders first
            if (mat3D != null)
            {
                var collider3D = FindCollider3D(go, colliderType, componentIndex);
                if (collider3D != null)
                {
                    Undo.RecordObject(collider3D, "Assign Physics Material");
                    collider3D.sharedMaterial = mat3D;
                    EditorUtility.SetDirty(collider3D);
                    return new
                    {
                        success = true,
                        message = $"Assigned 3D physics material to {collider3D.GetType().Name} on '{go.name}'.",
                        data = new
                        {
                            gameObject = go.name,
                            collider = collider3D.GetType().Name,
                            materialPath
                        }
                    };
                }
                if (componentIndex.HasValue)
                {
                    var type3D = !string.IsNullOrEmpty(colliderType) ? UnityTypeResolver.ResolveComponent(colliderType) : typeof(Collider);
                    int count3D = type3D != null ? go.GetComponents(type3D).Length : 0;
                    return new ErrorResponse($"component_index {componentIndex.Value} out of range. Found {count3D} '{(type3D ?? typeof(Collider)).Name}' collider(s) on '{go.name}'.");
                }
            }

            // Try 2D colliders
            if (mat2D != null)
            {
                var collider2D = FindCollider2D(go, colliderType, componentIndex);
                if (collider2D != null)
                {
                    Undo.RecordObject(collider2D, "Assign Physics Material 2D");
                    collider2D.sharedMaterial = mat2D;
                    EditorUtility.SetDirty(collider2D);
                    return new
                    {
                        success = true,
                        message = $"Assigned 2D physics material to {collider2D.GetType().Name} on '{go.name}'.",
                        data = new
                        {
                            gameObject = go.name,
                            collider = collider2D.GetType().Name,
                            materialPath
                        }
                    };
                }
                if (componentIndex.HasValue)
                {
                    var type2D = !string.IsNullOrEmpty(colliderType) ? UnityTypeResolver.ResolveComponent(colliderType) : typeof(Collider2D);
                    int count2D = type2D != null ? go.GetComponents(type2D).Length : 0;
                    return new ErrorResponse($"component_index {componentIndex.Value} out of range. Found {count2D} '{(type2D ?? typeof(Collider2D)).Name}' collider(s) on '{go.name}'.");
                }
            }

            return new ErrorResponse($"No suitable collider found on '{go.name}'.");
        }

        // =====================================================================
        // Create helpers
        // =====================================================================

        private static object Create3D(string name, string folder, ToolParams p)
        {
            float dynamicFriction = p.GetFloat("dynamic_friction") ?? 0.6f;
            float staticFriction = p.GetFloat("static_friction") ?? 0.6f;
            float bounciness = p.GetFloat("bounciness") ?? 0f;
            string frictionCombine = p.Get("friction_combine");
            string bounceCombine = p.Get("bounce_combine");

            string assetPath = $"{folder}/{name}.physicMaterial";

            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath) != null)
                return new ErrorResponse($"A physics material already exists at '{assetPath}'. Use configure_physics_material to modify it.");

#if UNITY_6000_0_OR_NEWER
            var mat = new PhysicsMaterial(name)
            {
                dynamicFriction = dynamicFriction,
                staticFriction = staticFriction,
                bounciness = bounciness
            };

            if (!string.IsNullOrEmpty(frictionCombine) &&
                Enum.TryParse<PhysicsMaterialCombine>(frictionCombine, true, out var fc))
                mat.frictionCombine = fc;

            if (!string.IsNullOrEmpty(bounceCombine) &&
                Enum.TryParse<PhysicsMaterialCombine>(bounceCombine, true, out var bc))
                mat.bounceCombine = bc;
#else
            var mat = new PhysicMaterial(name)
            {
                dynamicFriction = dynamicFriction,
                staticFriction = staticFriction,
                bounciness = bounciness
            };

            if (!string.IsNullOrEmpty(frictionCombine) &&
                Enum.TryParse<PhysicMaterialCombine>(frictionCombine, true, out var fc))
                mat.frictionCombine = fc;

            if (!string.IsNullOrEmpty(bounceCombine) &&
                Enum.TryParse<PhysicMaterialCombine>(bounceCombine, true, out var bc))
                mat.bounceCombine = bc;
#endif

            AssetDatabase.CreateAsset(mat, assetPath);
            AssetDatabase.SaveAssets();

            return new
            {
                success = true,
                message = $"Created 3D physics material '{name}' at '{assetPath}'.",
                data = new
                {
                    path = assetPath,
                    dimension = "3d",
                    dynamicFriction,
                    staticFriction,
                    bounciness,
                    frictionCombine = mat.frictionCombine.ToString(),
                    bounceCombine = mat.bounceCombine.ToString()
                }
            };
        }

        private static object Create2D(string name, string folder, ToolParams p)
        {
            float friction = p.GetFloat("friction") ?? 0.4f;
            float bounciness = p.GetFloat("bounciness") ?? 0f;

            string assetPath = $"{folder}/{name}.physicsMaterial2D";

            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath) != null)
                return new ErrorResponse($"A 2D physics material already exists at '{assetPath}'. Use configure_physics_material to modify it.");

            var mat = new PhysicsMaterial2D(name)
            {
                friction = friction,
                bounciness = bounciness
            };

            AssetDatabase.CreateAsset(mat, assetPath);
            AssetDatabase.SaveAssets();

            return new
            {
                success = true,
                message = $"Created 2D physics material '{name}' at '{assetPath}'.",
                data = new
                {
                    path = assetPath,
                    dimension = "2d",
                    friction,
                    bounciness
                }
            };
        }

        // =====================================================================
        // Configure helpers
        // =====================================================================

        private static readonly HashSet<string> Valid3DMatKeys = new HashSet<string>
        {
            "dynamicfriction", "staticfriction", "bounciness", "frictioncombine", "bouncecombine"
        };

        private static object Configure3D(string path, JObject properties)
        {
            // Validate all keys before applying any changes
            var unknown = new List<string>();
            foreach (var prop in properties.Properties())
            {
                string key = prop.Name.ToLowerInvariant().Replace("_", "");
                if (!Valid3DMatKeys.Contains(key))
                    unknown.Add(prop.Name);
            }
            if (unknown.Count > 0)
                return new ErrorResponse(
                    $"Unknown 3D physics material property(ies): {string.Join(", ", unknown)}.");

#if UNITY_6000_0_OR_NEWER
            var mat = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(path);
#else
            var mat = AssetDatabase.LoadAssetAtPath<PhysicMaterial>(path);
#endif
            if (mat == null)
                return new ErrorResponse($"No 3D physics material found at: '{path}'.");

            Undo.RecordObject(mat, "Configure Physics Material");

            var changed = new List<string>();
            foreach (var prop in properties.Properties())
            {
                string key = prop.Name.ToLowerInvariant().Replace("_", "");
                switch (key)
                {
                    case "dynamicfriction":
                        mat.dynamicFriction = prop.Value.Value<float>();
                        changed.Add("dynamicFriction");
                        break;
                    case "staticfriction":
                        mat.staticFriction = prop.Value.Value<float>();
                        changed.Add("staticFriction");
                        break;
                    case "bounciness":
                        mat.bounciness = prop.Value.Value<float>();
                        changed.Add("bounciness");
                        break;
                    case "frictioncombine":
                    {
#if UNITY_6000_0_OR_NEWER
                        if (!Enum.TryParse<PhysicsMaterialCombine>(prop.Value.ToString(), true, out var fc))
                            return new ErrorResponse($"Invalid friction_combine value: '{prop.Value}'. Valid values: Average, Minimum, Maximum, Multiply.");
                        mat.frictionCombine = fc;
                        changed.Add("frictionCombine");
#else
                        if (!Enum.TryParse<PhysicMaterialCombine>(prop.Value.ToString(), true, out var fc))
                            return new ErrorResponse($"Invalid friction_combine value: '{prop.Value}'. Valid values: Average, Minimum, Maximum, Multiply.");
                        mat.frictionCombine = fc;
                        changed.Add("frictionCombine");
#endif
                        break;
                    }
                    case "bouncecombine":
                    {
#if UNITY_6000_0_OR_NEWER
                        if (!Enum.TryParse<PhysicsMaterialCombine>(prop.Value.ToString(), true, out var bc))
                            return new ErrorResponse($"Invalid bounce_combine value: '{prop.Value}'. Valid values: Average, Minimum, Maximum, Multiply.");
                        mat.bounceCombine = bc;
                        changed.Add("bounceCombine");
#else
                        if (!Enum.TryParse<PhysicMaterialCombine>(prop.Value.ToString(), true, out var bc))
                            return new ErrorResponse($"Invalid bounce_combine value: '{prop.Value}'. Valid values: Average, Minimum, Maximum, Multiply.");
                        mat.bounceCombine = bc;
                        changed.Add("bounceCombine");
#endif
                        break;
                    }
                }
            }

            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssets();

            return new
            {
                success = true,
                message = $"Updated {changed.Count} property(ies) on 3D physics material at '{path}'.",
                data = new { path, changed }
            };
        }

        private static readonly HashSet<string> Valid2DMatKeys = new HashSet<string>
        {
            "friction", "bounciness"
        };

        private static object Configure2D(string path, JObject properties)
        {
            // Validate all keys before applying any changes
            var unknown = new List<string>();
            foreach (var prop in properties.Properties())
            {
                string key = prop.Name.ToLowerInvariant().Replace("_", "");
                if (!Valid2DMatKeys.Contains(key))
                    unknown.Add(prop.Name);
            }
            if (unknown.Count > 0)
                return new ErrorResponse(
                    $"Unknown 2D physics material property(ies): {string.Join(", ", unknown)}.");

            var mat = AssetDatabase.LoadAssetAtPath<PhysicsMaterial2D>(path);
            if (mat == null)
                return new ErrorResponse($"No 2D physics material found at: '{path}'.");

            Undo.RecordObject(mat, "Configure Physics Material 2D");

            var changed = new List<string>();
            foreach (var prop in properties.Properties())
            {
                string key = prop.Name.ToLowerInvariant().Replace("_", "");
                switch (key)
                {
                    case "friction":
                        mat.friction = prop.Value.Value<float>();
                        changed.Add("friction");
                        break;
                    case "bounciness":
                        mat.bounciness = prop.Value.Value<float>();
                        changed.Add("bounciness");
                        break;
                }
            }

            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssets();

            return new
            {
                success = true,
                message = $"Updated {changed.Count} property(ies) on 2D physics material at '{path}'.",
                data = new { path, changed }
            };
        }

        // =====================================================================
        // Assign helpers
        // =====================================================================

        private static Collider FindCollider3D(GameObject go, string colliderType, int? index = null)
        {
            if (!string.IsNullOrEmpty(colliderType))
            {
                var type = UnityTypeResolver.ResolveComponent(colliderType);
                if (type != null && typeof(Collider).IsAssignableFrom(type))
                {
                    if (index.HasValue)
                    {
                        var components = go.GetComponents(type);
                        if (index.Value < 0 || index.Value >= components.Length)
                            return null;
                        return components[index.Value] as Collider;
                    }
                    return go.GetComponent(type) as Collider;
                }
                return null;
            }

            if (index.HasValue)
            {
                var colliders = go.GetComponents<Collider>();
                if (index.Value < 0 || index.Value >= colliders.Length)
                    return null;
                return colliders[index.Value];
            }

            return go.GetComponent<Collider>();
        }

        private static Collider2D FindCollider2D(GameObject go, string colliderType, int? index = null)
        {
            if (!string.IsNullOrEmpty(colliderType))
            {
                var type = UnityTypeResolver.ResolveComponent(colliderType);
                if (type != null && typeof(Collider2D).IsAssignableFrom(type))
                {
                    if (index.HasValue)
                    {
                        var components = go.GetComponents(type);
                        if (index.Value < 0 || index.Value >= components.Length)
                            return null;
                        return components[index.Value] as Collider2D;
                    }
                    return go.GetComponent(type) as Collider2D;
                }
                return null;
            }

            if (index.HasValue)
            {
                var colliders = go.GetComponents<Collider2D>();
                if (index.Value < 0 || index.Value >= colliders.Length)
                    return null;
                return colliders[index.Value];
            }

            return go.GetComponent<Collider2D>();
        }

        // =====================================================================
        // Folder helpers
        // =====================================================================

        private static bool EnsureFolderExists(string folderPath, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(folderPath))
            {
                error = "Folder path is empty.";
                return false;
            }

            folderPath = folderPath.TrimEnd('/');

            if (!folderPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(folderPath, "Assets", StringComparison.OrdinalIgnoreCase))
            {
                error = "Folder path must be under Assets/.";
                return false;
            }

            if (AssetDatabase.IsValidFolder(folderPath))
                return true;

            var parts = folderPath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            string current = "Assets";
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    string guid = AssetDatabase.CreateFolder(current, parts[i]);
                    if (string.IsNullOrEmpty(guid))
                    {
                        error = $"Failed to create folder: {next}";
                        return false;
                    }
                }
                current = next;
            }

            return true;
        }
    }
}
