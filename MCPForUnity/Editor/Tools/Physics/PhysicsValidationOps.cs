using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using MCPForUnity.Editor.Helpers;

namespace MCPForUnity.Editor.Tools.Physics
{
    internal static class PhysicsValidationOps
    {
        private const string Cat_NonConvexMesh = "non_convex_mesh";
        private const string Cat_MissingRigidbody = "missing_rigidbody";
        private const string Cat_NonUniformScale = "non_uniform_scale";
        private const string Cat_FastObjectDiscrete = "fast_object_discrete";
        private const string Cat_MissingPhysicsMaterial = "missing_physics_material";
        private const string Cat_CollisionMatrix = "collision_matrix";
        private const string Cat_Mixed2D3D = "mixed_2d_3d";

        public static object Validate(JObject @params)
        {
            var p = new ToolParams(@params);
            string dimension = (p.Get("dimension") ?? "both").ToLowerInvariant();
            string targetStr = p.Get("target");
            string searchMethod = p.Get("search_method");
            int pageSize = p.GetInt("page_size") ?? p.GetInt("pageSize") ?? 50;
            int cursor = p.GetInt("cursor") ?? 0;

            var warnings = new List<string>();
            var categoryCounts = new Dictionary<string, int>
            {
                { Cat_NonConvexMesh, 0 },
                { Cat_MissingRigidbody, 0 },
                { Cat_NonUniformScale, 0 },
                { Cat_FastObjectDiscrete, 0 },
                { Cat_MissingPhysicsMaterial, 0 },
                { Cat_CollisionMatrix, 0 },
                { Cat_Mixed2D3D, 0 },
            };
            int scanned = 0;

            if (!string.IsNullOrEmpty(targetStr))
            {
                GameObject go = GameObjectLookup.FindByTarget(
                    @params["target"], searchMethod ?? "by_name", true);
                if (go == null)
                    return new ErrorResponse($"Target GameObject '{targetStr}' not found.");

                ValidateGameObject(go, dimension, warnings, categoryCounts);
                scanned = 1;
            }
            else
            {
                var rootObjects = GetAllRootGameObjects();
                foreach (var root in rootObjects)
                {
                    ValidateRecursive(root, dimension, warnings, categoryCounts, ref scanned);
                }

                if (dimension != "2d")
                    CheckCollisionMatrix(warnings, categoryCounts);
            }

            int totalWarnings = warnings.Count;
            int clampedCursor = Math.Min(cursor, totalWarnings);
            var page = warnings.Skip(clampedCursor).Take(pageSize).ToList();
            int? nextCursor = (clampedCursor + pageSize < totalWarnings)
                ? (int?)(clampedCursor + pageSize)
                : null;

            return new
            {
                success = true,
                message = totalWarnings == 0
                    ? $"No physics warnings found ({scanned} object(s) scanned)."
                    : $"Found {totalWarnings} warning(s) across {scanned} object(s).",
                data = new
                {
                    warnings = page,
                    warning_count = totalWarnings,
                    objects_scanned = scanned,
                    page_size = pageSize,
                    cursor = clampedCursor,
                    next_cursor = nextCursor,
                    summary = categoryCounts
                }
            };
        }

        private static void ValidateRecursive(GameObject go, string dimension, List<string> warnings,
            Dictionary<string, int> categoryCounts, ref int scanned)
        {
            ValidateGameObject(go, dimension, warnings, categoryCounts);
            scanned++;

            for (int i = 0; i < go.transform.childCount; i++)
            {
                ValidateRecursive(go.transform.GetChild(i).gameObject, dimension, warnings, categoryCounts, ref scanned);
            }
        }

        private static void ValidateGameObject(GameObject go, string dimension, List<string> warnings,
            Dictionary<string, int> categoryCounts)
        {
            bool check3D = dimension == "3d" || dimension == "both";
            bool check2D = dimension == "2d" || dimension == "both";

            // Check 1: MeshCollider without Convex on non-kinematic Rigidbody
            if (check3D)
            {
                var rb = go.GetComponent<Rigidbody>();
                if (rb != null && !rb.isKinematic)
                {
                    foreach (var mc in go.GetComponents<MeshCollider>())
                    {
                        if (!mc.convex)
                        {
                            warnings.Add(
                                $"MeshCollider on '{go.name}' must be Convex for non-kinematic Rigidbody.");
                            categoryCounts[Cat_NonConvexMesh]++;
                        }
                    }
                }
            }

            // Check 2: Collider without Rigidbody on non-static object
            if (check3D)
            {
                var colliders3D = go.GetComponents<Collider>();
                if (colliders3D.Length > 0 && go.GetComponent<Rigidbody>() == null && !go.isStatic)
                {
                    bool hasAnimator = go.GetComponent<Animator>() != null || HasAnimatorInParent(go);
                    if (hasAnimator)
                    {
                        warnings.Add(
                            $"'{go.name}' has a Collider but no Rigidbody. Moving it via Transform causes broadphase rebuild every frame.");
                    }
                    else
                    {
                        warnings.Add(
                            $"[Info] '{go.name}' has a Collider but no Rigidbody. This is fine if the object isn't moved at runtime.");
                    }
                    categoryCounts[Cat_MissingRigidbody]++;
                }
            }

            if (check2D)
            {
                var colliders2D = go.GetComponents<Collider2D>();
                if (colliders2D.Length > 0 && go.GetComponent<Rigidbody2D>() == null && !go.isStatic)
                {
                    bool hasAnimator = go.GetComponent<Animator>() != null || HasAnimatorInParent(go);
                    if (hasAnimator)
                    {
                        warnings.Add(
                            $"'{go.name}' has a Collider2D but no Rigidbody2D. Moving it via Transform causes broadphase rebuild every frame.");
                    }
                    else
                    {
                        warnings.Add(
                            $"[Info] '{go.name}' has a Collider2D but no Rigidbody2D. This is fine if the object isn't moved at runtime.");
                    }
                    categoryCounts[Cat_MissingRigidbody]++;
                }
            }

            // Check 3: Non-uniform scale
            {
                var scale = go.transform.lossyScale;
                bool hasCollider = go.GetComponent<Collider>() != null || go.GetComponent<Collider2D>() != null;
                if (hasCollider)
                {
                    bool nonUniform = Mathf.Abs(scale.x - scale.y) > 0.01f
                                      || Mathf.Abs(scale.y - scale.z) > 0.01f;
                    if (nonUniform)
                    {
                        warnings.Add(
                            $"'{go.name}' has non-uniform scale ({scale.x:F2}, {scale.y:F2}, {scale.z:F2}) which degrades physics performance.");
                        categoryCounts[Cat_NonUniformScale]++;
                    }
                }
            }

            // Check 4: Fast object with Discrete collision detection
            if (check3D)
            {
                var rb = go.GetComponent<Rigidbody>();
                if (rb != null && rb.collisionDetectionMode == CollisionDetectionMode.Discrete)
                {
                    string nameLower = go.name.ToLowerInvariant();
                    if (nameLower.Contains("bullet") || nameLower.Contains("projectile") || nameLower.Contains("fast"))
                    {
                        warnings.Add(
                            $"'{go.name}' uses Discrete collision detection but appears to be a fast-moving object. Consider ContinuousDynamic.");
                        categoryCounts[Cat_FastObjectDiscrete]++;
                    }
                }
            }

            // Check 5: Missing physics material
            if (check3D)
            {
                foreach (var col in go.GetComponents<Collider>())
                {
                    if (col.sharedMaterial == null)
                    {
                        warnings.Add(
                            $"[Info] Collider ({col.GetType().Name}) on '{go.name}' has no physics material (using defaults).");
                        categoryCounts[Cat_MissingPhysicsMaterial]++;
                    }
                }
            }

            if (check2D)
            {
                foreach (var col in go.GetComponents<Collider2D>())
                {
                    if (col.sharedMaterial == null)
                    {
                        warnings.Add(
                            $"[Info] Collider2D ({col.GetType().Name}) on '{go.name}' has no physics material (using defaults).");
                        categoryCounts[Cat_MissingPhysicsMaterial]++;
                    }
                }
            }

            // Check 7: 2D/3D physics mixing
            if (dimension == "both")
            {
                bool has3D = go.GetComponent<Rigidbody>() != null || go.GetComponent<Collider>() != null;
                bool has2D = go.GetComponent<Rigidbody2D>() != null || go.GetComponent<Collider2D>() != null;

                if (has3D && has2D)
                {
                    var components3D = new List<string>();
                    var components2D = new List<string>();

                    if (go.GetComponent<Rigidbody>() != null) components3D.Add("Rigidbody");
                    foreach (var c in go.GetComponents<Collider>()) components3D.Add(c.GetType().Name);

                    if (go.GetComponent<Rigidbody2D>() != null) components2D.Add("Rigidbody2D");
                    foreach (var c in go.GetComponents<Collider2D>()) components2D.Add(c.GetType().Name);

                    warnings.Add(
                        $"'{go.name}' has both 3D ({string.Join(", ", components3D)}) and 2D ({string.Join(", ", components2D)}) physics components.");
                    categoryCounts[Cat_Mixed2D3D]++;
                }
            }
        }

        private static bool HasAnimatorInParent(GameObject go)
        {
            Transform parent = go.transform.parent;
            while (parent != null)
            {
                if (parent.GetComponent<Animator>() != null)
                    return true;
                parent = parent.parent;
            }
            return false;
        }

        // Check 6 (scene-wide): Unconfigured collision matrix
        private static void CheckCollisionMatrix(List<string> warnings, Dictionary<string, int> categoryCounts)
        {
            var populatedLayers = new List<int>();
            for (int i = 0; i < 32; i++)
            {
                if (!string.IsNullOrEmpty(LayerMask.LayerToName(i)))
                    populatedLayers.Add(i);
            }

            bool allCollide = true;
            foreach (int i in populatedLayers)
            {
                foreach (int j in populatedLayers)
                {
                    if (j > i) continue;
                    if (UnityEngine.Physics.GetIgnoreLayerCollision(i, j))
                    {
                        allCollide = false;
                        break;
                    }
                }
                if (!allCollide) break;
            }

            if (allCollide && populatedLayers.Count > 2)
            {
                warnings.Add(
                    "All layers are set to collide with all other layers. Consider disabling unused layer pairs for performance.");
                categoryCounts[Cat_CollisionMatrix]++;
            }
        }

        private static List<GameObject> GetAllRootGameObjects()
        {
            var roots = new List<GameObject>();
            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
            {
                var scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
                if (scene.isLoaded)
                    roots.AddRange(scene.GetRootGameObjects());
            }
            return roots;
        }
    }
}
