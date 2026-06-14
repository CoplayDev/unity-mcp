using System;
using System.Collections.Generic;
using System.Reflection;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Resources.Scene;
using MCPForUnity.Runtime.Helpers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace MCPForUnity.Editor.Tools
{
    /// <summary>
    /// Read-only screenshot coordinate picker for Unity camera and Scene View captures.
    /// </summary>
    [McpForUnityTool("pick_gameobject_from_image", AutoRegister = false, Group = "core")]
    public static class PickGameObjectFromImage
    {
        internal struct SceneViewPickResult
        {
            public readonly GameObject GameObject;
            public readonly int MaterialIndex;
            public readonly string Backend;
            public readonly bool HasHitDetails;
            public readonly Vector3 Point;
            public readonly Vector3 Normal;
            public readonly float Distance;

            public SceneViewPickResult(GameObject gameObject, int materialIndex)
                : this(gameObject, materialIndex, "handle_utility")
            {
            }

            public SceneViewPickResult(GameObject gameObject, int materialIndex, string backend)
                : this(gameObject, materialIndex, backend, false, default, default, 0f)
            {
            }

            public SceneViewPickResult(
                GameObject gameObject,
                int materialIndex,
                string backend,
                bool hasHitDetails,
                Vector3 point,
                Vector3 normal,
                float distance)
            {
                GameObject = gameObject;
                MaterialIndex = materialIndex;
                Backend = backend;
                HasHitDetails = hasHitDetails;
                Point = point;
                Normal = normal;
                Distance = distance;
            }
        }

        internal static Func<Vector2, SceneViewPickResult> SceneViewPickOverrideForTests { get; set; }
        private const float SceneViewPositionTolerance = 0.01f;
        private const float SceneViewRotationTolerance = 0.1f;
        private const float SceneViewScalarTolerance = 0.01f;
        private const float SceneViewViewportTolerancePixels = 1f;
        private static MethodInfo _intersectRayMeshMethod;
        private static bool _intersectRayMeshMethodProbed;

        public static object HandleCommand(JObject @params)
        {
            if (@params == null)
                return new ErrorResponse("Parameters cannot be null.");

            try
            {
                var p = new ToolParams(@params);

                float? imageX = ParamCoercion.CoerceFloatNullable(p.GetRaw("imageX"));
                float? imageY = ParamCoercion.CoerceFloatNullable(p.GetRaw("imageY"));
                int? imageWidth = ParamCoercion.CoerceIntNullable(p.GetRaw("imageWidth"));
                int? imageHeight = ParamCoercion.CoerceIntNullable(p.GetRaw("imageHeight"));

                if (!IsFinite(imageX))
                    return new ErrorResponse("'image_x' parameter is required and must be a finite number.");
                if (!IsFinite(imageY))
                    return new ErrorResponse("'image_y' parameter is required and must be a finite number.");
                if (!imageWidth.HasValue || imageWidth.Value <= 0)
                    return new ErrorResponse("'image_width' parameter is required and must be a positive integer.");
                if (!imageHeight.HasValue || imageHeight.Value <= 0)
                    return new ErrorResponse("'image_height' parameter is required and must be a positive integer.");
                if (imageX.Value < 0 || imageX.Value >= imageWidth.Value)
                    return new ErrorResponse("'image_x' must be within [0, image_width).");
                if (imageY.Value < 0 || imageY.Value >= imageHeight.Value)
                    return new ErrorResponse("'image_y' must be within [0, image_height).");

                float scaleX = ParamCoercion.CoerceFloatNullable(p.GetRaw("scaleX")) ?? 1f;
                float scaleY = ParamCoercion.CoerceFloatNullable(p.GetRaw("scaleY")) ?? scaleX;
                if (!IsFinite(scaleX) || scaleX <= 0f)
                    return new ErrorResponse("'scale_x' must be a positive finite number.");
                if (!IsFinite(scaleY) || scaleY <= 0f)
                    return new ErrorResponse("'scale_y' must be a positive finite number.");

                string dimension = (p.Get("dimension") ?? "3d").ToLowerInvariant();
                if (dimension != "3d" && dimension != "2d")
                    return new ErrorResponse($"Invalid dimension: '{dimension}'. Use '3d' or '2d'.");

                JObject pickView = NormalizePickView(p.GetRaw("pickView"));
                string cameraRef = p.Get("camera");
                if (pickView == null && string.IsNullOrWhiteSpace(cameraRef))
                {
                    return new ErrorResponse(
                        "Provide either 'pick_view' from a supported screenshot response or a still-unchanged 'camera' reference.");
                }

                float viewportWidth = ParamCoercion.CoerceFloatNullable(p.GetRaw("viewportWidth"))
                    ?? ParamCoercion.CoerceFloatNullable(pickView?["viewportWidth"] ?? pickView?["viewport_width"])
                    ?? imageWidth.Value * scaleX;
                float viewportHeight = ParamCoercion.CoerceFloatNullable(p.GetRaw("viewportHeight"))
                    ?? ParamCoercion.CoerceFloatNullable(pickView?["viewportHeight"] ?? pickView?["viewport_height"])
                    ?? imageHeight.Value * scaleY;

                if (!IsFinite(viewportWidth) || viewportWidth <= 0f)
                    return new ErrorResponse("'viewport_width' must be a positive finite number.");
                if (!IsFinite(viewportHeight) || viewportHeight <= 0f)
                    return new ErrorResponse("'viewport_height' must be a positive finite number.");

                float viewportX = imageX.Value * scaleX;
                float viewportYFromTop = imageY.Value * scaleY;
                float viewportU = viewportX / viewportWidth;
                float viewportV = 1f - (viewportYFromTop / viewportHeight);
                if (!IsFinite(viewportU) || !IsFinite(viewportV) || viewportU < 0f || viewportU > 1f || viewportV < 0f || viewportV > 1f)
                    return new ErrorResponse("Computed viewport coordinate is outside [0, 1]. Check image dimensions, scale, and viewport dimensions.");

                JToken layerMaskToken = p.GetRaw("layerMask");
                float maxDistance = ParamCoercion.CoerceFloatNullable(p.GetRaw("maxDistance")) ?? Mathf.Infinity;
                if (!IsFinite(maxDistance) && !float.IsPositiveInfinity(maxDistance))
                    return new ErrorResponse("'max_distance' must be a positive finite number or omitted.");
                if (maxDistance <= 0f)
                    return new ErrorResponse("'max_distance' must be greater than zero.");

                QueryTriggerInteraction triggerInteraction = QueryTriggerInteraction.UseGlobal;
                string qti = p.Get("queryTriggerInteraction");
                if (!string.IsNullOrWhiteSpace(qti) &&
                    !Enum.TryParse(qti, true, out triggerInteraction))
                {
                    return new ErrorResponse("Invalid query_trigger_interaction. Valid values: UseGlobal, Ignore, Collide.");
                }

                GameObject tempCameraGo = null;
                try
                {
                    Camera camera = pickView != null
                        ? CreateCameraFromPickView(pickView, viewportWidth, viewportHeight, out tempCameraGo)
                        : ResolveCamera(cameraRef);

                    if (camera == null)
                        return new ErrorResponse($"Camera '{cameraRef}' not found. Provide a Camera GameObject name, path, or instance ID.");

                    int layerMask = ResolveLayerMask(layerMaskToken, ResolveDefaultLayerMask(pickView, camera));
                    var ray = camera.ViewportPointToRay(new Vector3(viewportU, viewportV, 0f));
                    string viewSource = pickView != null ? "pick_view" : "camera";

                    if (IsSceneViewPickView(pickView) &&
                        TryPickSceneViewObject(pickView, ray, viewportU, 1f - viewportV, viewportWidth, viewportHeight, layerMask, maxDistance, out var sceneViewPick, out var sceneViewPickMessage))
                    {
                        if (sceneViewPick.GameObject != null)
                        {
                            return new SuccessResponse(
                                $"Picked GameObject '{sceneViewPick.GameObject.name}' from Scene View image coordinate.",
                                BuildSceneViewHitData(
                                    dimension,
                                    ray,
                                    imageX.Value,
                                    imageY.Value,
                                    imageWidth.Value,
                                    imageHeight.Value,
                                    scaleX,
                                    scaleY,
                                    viewportU,
                                    viewportV,
                                    viewportWidth,
                                    viewportHeight,
                                    viewSource,
                                    sceneViewPick));
                        }

                        McpLog.Debug($"[PickGameObjectFromImage] Scene View editor pick did not hit: {sceneViewPickMessage}");
                    }

                    return dimension == "2d"
                        ? Pick2D(ray, maxDistance, layerMask, imageX.Value, imageY.Value, imageWidth.Value, imageHeight.Value, scaleX, scaleY, viewportU, viewportV, viewportWidth, viewportHeight, viewSource)
                        : Pick3D(ray, maxDistance, layerMask, triggerInteraction, imageX.Value, imageY.Value, imageWidth.Value, imageHeight.Value, scaleX, scaleY, viewportU, viewportV, viewportWidth, viewportHeight, viewSource);
                }
                finally
                {
                    if (tempCameraGo != null)
                        UnityEngine.Object.DestroyImmediate(tempCameraGo);
                }
            }
            catch (Exception ex)
            {
                McpLog.Error($"[PickGameObjectFromImage] Failed: {ex}");
                return new ErrorResponse($"Error picking GameObject from image: {ex.Message}");
            }
        }

        private static object Pick3D(
            Ray ray,
            float maxDistance,
            int layerMask,
            QueryTriggerInteraction triggerInteraction,
            float imageX,
            float imageY,
            int imageWidth,
            int imageHeight,
            float scaleX,
            float scaleY,
            float viewportU,
            float viewportV,
            float viewportWidth,
            float viewportHeight,
            string viewSource)
        {
            UnityEngine.Physics.SyncTransforms();
            RaycastHit[] hits = UnityEngine.Physics.RaycastAll(ray, maxDistance, layerMask, triggerInteraction);
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

            if (hits.Length == 0)
            {
                return new SuccessResponse(
                    "No 3D Collider was hit at the requested image coordinate.",
                    BuildNoHitData("3d", ray, imageX, imageY, imageWidth, imageHeight, scaleX, scaleY, viewportU, viewportV, viewportWidth, viewportHeight, viewSource));
            }

            var hit = hits[0];
            var go = hit.collider.gameObject;
            var data = BuildHitData(
                "3d",
                ray,
                imageX,
                imageY,
                imageWidth,
                imageHeight,
                scaleX,
                scaleY,
                viewportU,
                viewportV,
                viewportWidth,
                viewportHeight,
                viewSource,
                new[] { hit.point.x, hit.point.y, hit.point.z },
                new[] { hit.normal.x, hit.normal.y, hit.normal.z },
                hit.distance,
                hit.collider.GetType().Name,
                go);

            return new SuccessResponse($"Picked GameObject '{go.name}' from image coordinate.", data);
        }

        private static bool TryPickSceneViewObject(
            JObject pickView,
            Ray ray,
            float viewportU,
            float viewportTopFraction,
            float viewportWidth,
            float viewportHeight,
            int layerMask,
            float maxDistance,
            out SceneViewPickResult result,
            out string message)
        {
            result = default;
            message = null;

            Vector2 guiPoint;
            if (SceneViewPickOverrideForTests != null)
            {
                guiPoint = new Vector2(viewportU, viewportTopFraction);
                result = SceneViewPickOverrideForTests(guiPoint);
                return FilterSceneViewPickByLayerMask(result, layerMask, out result, out message);
            }

            string handleUtilityMessage = null;

            if (!Application.isBatchMode)
            {
                var sceneView = SceneView.lastActiveSceneView;
                if (LiveSceneViewMatchesPickView(sceneView, pickView, viewportWidth, viewportHeight, out Rect viewportRect, out handleUtilityMessage))
                {
                    guiPoint = new Vector2(
                        viewportRect.x + viewportU * viewportRect.width,
                        viewportRect.y + viewportTopFraction * viewportRect.height);

                    try
                    {
                        int materialIndex;
                        GameObject picked = HandleUtility.PickGameObject(guiPoint, out materialIndex);
                        var handleUtilityResult = new SceneViewPickResult(picked, materialIndex, "handle_utility");
                        if (picked != null)
                            return FilterSceneViewPickByLayerMask(handleUtilityResult, layerMask, out result, out message);
                        handleUtilityMessage = "HandleUtility.PickGameObject did not hit a selectable object.";
                    }
                    catch (Exception ex)
                    {
                        handleUtilityMessage = $"HandleUtility.PickGameObject was unavailable: {ex.Message}";
                    }
                }
            }
            else
            {
                handleUtilityMessage = "Scene View HandleUtility picking is not available in batch mode.";
            }

            if (TryPickRendererMesh(ray, layerMask, maxDistance, out result, out string meshMessage))
            {
                message = meshMessage;
                return true;
            }

            message = $"{handleUtilityMessage} {meshMessage}".Trim();
            return true;
        }

        private static bool TryPickRendererMesh(
            Ray ray,
            int layerMask,
            float maxDistance,
            out SceneViewPickResult result,
            out string message)
        {
            result = default;
            message = null;

            float nearestDistance = float.PositiveInfinity;
            GameObject nearestObject = null;

            var renderers = UnityFindObjectsCompat.FindAll<Renderer>();
            foreach (var renderer in renderers)
            {
                if (renderer == null || !renderer.enabled)
                    continue;

                var go = renderer.gameObject;
                if (go == null || !go.activeInHierarchy)
                    continue;
                if (EditorUtility.IsPersistent(go))
                    continue;
                if ((go.hideFlags & (HideFlags.HideInHierarchy | HideFlags.HideAndDontSave)) != 0)
                    continue;
                if ((layerMask & (1 << go.layer)) == 0)
                    continue;
                if (!renderer.bounds.IntersectRay(ray, out float boundsDistance) ||
                    boundsDistance > nearestDistance ||
                    boundsDistance > maxDistance)
                    continue;

                Mesh bakedMesh = null;
                try
                {
                    Mesh mesh = null;
                    Matrix4x4 matrix = renderer.transform.localToWorldMatrix;

                    if (renderer is SkinnedMeshRenderer skinnedRenderer)
                    {
                        if (skinnedRenderer.sharedMesh == null)
                            continue;
                        bakedMesh = new Mesh { hideFlags = HideFlags.HideAndDontSave };
                        skinnedRenderer.BakeMesh(bakedMesh);
                        mesh = bakedMesh;
                    }
                    else
                    {
                        var meshFilter = renderer.GetComponent<MeshFilter>();
                        if (meshFilter != null)
                            mesh = meshFilter.sharedMesh;
                    }

                    if (mesh == null)
                        continue;

                    if (TryIntersectRayMesh(ray, mesh, matrix, out RaycastHit hit) &&
                        hit.distance >= 0f &&
                        hit.distance <= maxDistance &&
                        hit.distance < nearestDistance)
                    {
                        nearestDistance = hit.distance;
                        result = new SceneViewPickResult(
                            go,
                            -1,
                            "mesh_intersection",
                            true,
                            hit.point,
                            hit.normal,
                            hit.distance);
                        nearestObject = go;
                    }
                }
                finally
                {
                    if (bakedMesh != null)
                        UnityEngine.Object.DestroyImmediate(bakedMesh);
                }
            }

            if (nearestObject == null)
            {
                message = "No visible MeshRenderer or SkinnedMeshRenderer intersected the Scene View ray.";
                return false;
            }

            message = "Scene View mesh intersection hit a renderer without requiring a Collider.";
            return true;
        }

        private static bool TryIntersectRayMesh(Ray ray, Mesh mesh, Matrix4x4 matrix, out RaycastHit hit)
        {
            hit = default;
            if (mesh == null)
                return false;

            if (!_intersectRayMeshMethodProbed)
            {
                _intersectRayMeshMethodProbed = true;
                _intersectRayMeshMethod = typeof(HandleUtility).GetMethod(
                    "IntersectRayMesh",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    new[] { typeof(Ray), typeof(Mesh), typeof(Matrix4x4), typeof(RaycastHit).MakeByRefType() },
                    null);
            }

            if (_intersectRayMeshMethod == null)
                return false;

            var args = new object[] { ray, mesh, matrix, hit };
            try
            {
                bool didHit = (bool)_intersectRayMeshMethod.Invoke(null, args);
                if (didHit)
                    hit = (RaycastHit)args[3];
                return didHit;
            }
            catch
            {
                return false;
            }
        }

        private static bool FilterSceneViewPickByLayerMask(
            SceneViewPickResult input,
            int layerMask,
            out SceneViewPickResult result,
            out string message)
        {
            result = input;
            message = null;

            if (input.GameObject == null)
            {
                message = "No selectable Scene View object under the coordinate.";
                return true;
            }

            int layerBit = 1 << input.GameObject.layer;
            if ((layerMask & layerBit) == 0)
            {
                result = default;
                message = $"Picked object '{input.GameObject.name}' is excluded by layer_mask.";
                return true;
            }

            return true;
        }

        private static Dictionary<string, object> BuildSceneViewHitData(
            string dimension,
            Ray ray,
            float imageX,
            float imageY,
            int imageWidth,
            int imageHeight,
            float scaleX,
            float scaleY,
            float viewportU,
            float viewportV,
            float viewportWidth,
            float viewportHeight,
            string viewSource,
            SceneViewPickResult pickResult)
        {
            var data = BuildBaseData(dimension, ray, imageX, imageY, imageWidth, imageHeight, scaleX, scaleY, viewportU, viewportV, viewportWidth, viewportHeight, viewSource);
            data["hit"] = true;
            data["pickMode"] = "scene_view_editor_pick";
            data["requiresCollider"] = false;
            data["sceneViewPickBackend"] = string.IsNullOrEmpty(pickResult.Backend) ? "handle_utility" : pickResult.Backend;
            data["materialIndex"] = pickResult.MaterialIndex;
            if (pickResult.HasHitDetails)
            {
                data["point"] = new[] { pickResult.Point.x, pickResult.Point.y, pickResult.Point.z };
                data["normal"] = new[] { pickResult.Normal.x, pickResult.Normal.y, pickResult.Normal.z };
                data["distance"] = pickResult.Distance;
            }
            data["gameObject"] = GameObjectResource.SerializeGameObject(pickResult.GameObject);
            data["instanceID"] = pickResult.GameObject.GetInstanceIDCompat();
            data["gameObjectName"] = pickResult.GameObject.name;
            return data;
        }

        private static object Pick2D(
            Ray ray,
            float maxDistance,
            int layerMask,
            float imageX,
            float imageY,
            int imageWidth,
            int imageHeight,
            float scaleX,
            float scaleY,
            float viewportU,
            float viewportV,
            float viewportWidth,
            float viewportHeight,
            string viewSource)
        {
            Physics2D.SyncTransforms();
            RaycastHit2D[] hits = Physics2D.GetRayIntersectionAll(ray, maxDistance, layerMask);
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

            if (hits.Length == 0)
            {
                return new SuccessResponse(
                    "No 2D Collider2D was hit at the requested image coordinate.",
                    BuildNoHitData("2d", ray, imageX, imageY, imageWidth, imageHeight, scaleX, scaleY, viewportU, viewportV, viewportWidth, viewportHeight, viewSource));
            }

            var hit = hits[0];
            var go = hit.collider.gameObject;
            var data = BuildHitData(
                "2d",
                ray,
                imageX,
                imageY,
                imageWidth,
                imageHeight,
                scaleX,
                scaleY,
                viewportU,
                viewportV,
                viewportWidth,
                viewportHeight,
                viewSource,
                new[] { hit.point.x, hit.point.y },
                new[] { hit.normal.x, hit.normal.y },
                hit.distance,
                hit.collider.GetType().Name,
                go);

            return new SuccessResponse($"Picked GameObject '{go.name}' from image coordinate.", data);
        }

        private static Dictionary<string, object> BuildNoHitData(
            string dimension,
            Ray ray,
            float imageX,
            float imageY,
            int imageWidth,
            int imageHeight,
            float scaleX,
            float scaleY,
            float viewportU,
            float viewportV,
            float viewportWidth,
            float viewportHeight,
            string viewSource)
        {
            var data = BuildBaseData(dimension, ray, imageX, imageY, imageWidth, imageHeight, scaleX, scaleY, viewportU, viewportV, viewportWidth, viewportHeight, viewSource);
            data["hit"] = false;
            data["pickMode"] = dimension == "2d" ? "physics_2d" : "physics_3d";
            data["requiresCollider"] = true;
            return data;
        }

        private static Dictionary<string, object> BuildHitData(
            string dimension,
            Ray ray,
            float imageX,
            float imageY,
            int imageWidth,
            int imageHeight,
            float scaleX,
            float scaleY,
            float viewportU,
            float viewportV,
            float viewportWidth,
            float viewportHeight,
            string viewSource,
            float[] point,
            float[] normal,
            float distance,
            string colliderType,
            GameObject gameObject)
        {
            var data = BuildBaseData(dimension, ray, imageX, imageY, imageWidth, imageHeight, scaleX, scaleY, viewportU, viewportV, viewportWidth, viewportHeight, viewSource);
            data["hit"] = true;
            data["pickMode"] = dimension == "2d" ? "physics_2d" : "physics_3d";
            data["requiresCollider"] = true;
            data["point"] = point;
            data["normal"] = normal;
            data["distance"] = distance;
            data["colliderType"] = colliderType;
            data["collider_type"] = colliderType;
            data["gameObject"] = GameObjectResource.SerializeGameObject(gameObject);
            data["instanceID"] = gameObject.GetInstanceIDCompat();
            data["gameObjectName"] = gameObject.name;
            return data;
        }

        private static Dictionary<string, object> BuildBaseData(
            string dimension,
            Ray ray,
            float imageX,
            float imageY,
            int imageWidth,
            int imageHeight,
            float scaleX,
            float scaleY,
            float viewportU,
            float viewportV,
            float viewportWidth,
            float viewportHeight,
            string viewSource)
        {
            return new Dictionary<string, object>
            {
                { "dimension", dimension },
                { "viewSource", viewSource },
                { "image", new Dictionary<string, object>
                    {
                        { "x", imageX },
                        { "y", imageY },
                        { "width", imageWidth },
                        { "height", imageHeight },
                        { "scaleX", scaleX },
                        { "scaleY", scaleY },
                    }
                },
                { "viewport", new Dictionary<string, object>
                    {
                        { "x", viewportU },
                        { "y", viewportV },
                        { "width", viewportWidth },
                        { "height", viewportHeight },
                    }
                },
                { "ray", new Dictionary<string, object>
                    {
                        { "origin", new[] { ray.origin.x, ray.origin.y, ray.origin.z } },
                        { "direction", new[] { ray.direction.x, ray.direction.y, ray.direction.z } },
                    }
                },
            };
        }

        private static bool IsSceneViewPickView(JObject pickView)
        {
            string captureSource = ParamCoercion.CoerceString(pickView?["captureSource"] ?? pickView?["capture_source"], null);
            return string.Equals(captureSource, "scene_view", StringComparison.OrdinalIgnoreCase);
        }

        private static Rect GetSceneViewViewportRectPoints(SceneView sceneView)
        {
            Rect? cameraViewport = GetRectProperty(sceneView, "cameraViewport");
            if (cameraViewport.HasValue && cameraViewport.Value.width > 0f && cameraViewport.Value.height > 0f)
                return cameraViewport.Value;

            Camera camera = sceneView.camera;
            if (camera == null)
                return Rect.zero;

            float pixelsPerPoint = Mathf.Max(0.0001f, EditorGUIUtility.pixelsPerPoint);
            float viewportWidth = camera.pixelWidth / pixelsPerPoint;
            float viewportHeight = camera.pixelHeight / pixelsPerPoint;
            Rect windowRect = sceneView.position;

            return new Rect(
                0f,
                Mathf.Max(0f, windowRect.height - viewportHeight),
                Mathf.Min(windowRect.width, viewportWidth),
                Mathf.Min(windowRect.height, viewportHeight));
        }

        private static Rect? GetRectProperty(object instance, string propertyName)
        {
            if (instance == null)
                return null;

            var property = instance.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property == null || property.PropertyType != typeof(Rect))
                return null;

            try
            {
                return (Rect)property.GetValue(instance, null);
            }
            catch
            {
                return null;
            }
        }

        internal static bool LiveSceneViewMatchesPickView(
            SceneView sceneView,
            JObject pickView,
            float fallbackViewportWidth,
            float fallbackViewportHeight,
            out Rect viewportRect,
            out string message)
        {
            viewportRect = Rect.zero;
            message = null;

            if (sceneView == null)
            {
                message = "No active Scene View is available.";
                return false;
            }

            Camera camera = sceneView.camera;
            if (camera == null)
            {
                message = "Active Scene View has no camera.";
                return false;
            }

            if (pickView == null)
            {
                message = "Scene View pickView metadata is missing.";
                return false;
            }

            Vector3? expectedPosition = ReadVector3(pickView["position"]);
            Vector3? expectedRotation = ReadVector3(pickView["rotation"] ?? pickView["eulerAngles"] ?? pickView["euler_angles"]);
            if (!expectedPosition.HasValue || !expectedRotation.HasValue)
            {
                message = "Scene View pickView is missing camera position or rotation.";
                return false;
            }

            if ((camera.transform.position - expectedPosition.Value).sqrMagnitude >
                SceneViewPositionTolerance * SceneViewPositionTolerance)
            {
                message = "Active Scene View camera position no longer matches the screenshot pickView.";
                return false;
            }

            if (Quaternion.Angle(camera.transform.rotation, Quaternion.Euler(expectedRotation.Value)) >
                SceneViewRotationTolerance)
            {
                message = "Active Scene View camera rotation no longer matches the screenshot pickView.";
                return false;
            }

            string projection = ParamCoercion.CoerceString(pickView["projection"], null)?.ToLowerInvariant();
            bool expectedOrthographic = ParamCoercion.CoerceBool(pickView["orthographic"], projection == "orthographic");
            if (camera.orthographic != expectedOrthographic)
            {
                message = "Active Scene View projection no longer matches the screenshot pickView.";
                return false;
            }

            float? expectedScalar = expectedOrthographic
                ? ParamCoercion.CoerceFloatNullable(pickView["orthographicSize"] ?? pickView["orthographic_size"])
                : ParamCoercion.CoerceFloatNullable(pickView["fieldOfView"] ?? pickView["field_of_view"]);
            float liveScalar = expectedOrthographic ? camera.orthographicSize : camera.fieldOfView;
            if (IsFinite(expectedScalar) && Mathf.Abs(liveScalar - expectedScalar.Value) > SceneViewScalarTolerance)
            {
                message = "Active Scene View projection settings no longer match the screenshot pickView.";
                return false;
            }

            float? expectedNear = ParamCoercion.CoerceFloatNullable(pickView["nearClipPlane"] ?? pickView["near_clip_plane"]);
            if (IsFinite(expectedNear) && Mathf.Abs(camera.nearClipPlane - expectedNear.Value) > SceneViewScalarTolerance)
            {
                message = "Active Scene View near clip plane no longer matches the screenshot pickView.";
                return false;
            }

            float? expectedFar = ParamCoercion.CoerceFloatNullable(pickView["farClipPlane"] ?? pickView["far_clip_plane"]);
            if (IsFinite(expectedFar) && Mathf.Abs(camera.farClipPlane - expectedFar.Value) > SceneViewScalarTolerance)
            {
                message = "Active Scene View far clip plane no longer matches the screenshot pickView.";
                return false;
            }

            viewportRect = GetSceneViewViewportRectPoints(sceneView);
            if (viewportRect.width <= 0f || viewportRect.height <= 0f)
            {
                message = "Active Scene View viewport is empty.";
                return false;
            }

            float pixelsPerPoint = Mathf.Max(0.0001f, EditorGUIUtility.pixelsPerPoint);
            float liveViewportWidth = Mathf.Round(viewportRect.width * pixelsPerPoint);
            float liveViewportHeight = Mathf.Round(viewportRect.height * pixelsPerPoint);
            float expectedViewportWidth = ParamCoercion.CoerceFloatNullable(pickView["viewportWidth"] ?? pickView["viewport_width"]) ?? fallbackViewportWidth;
            float expectedViewportHeight = ParamCoercion.CoerceFloatNullable(pickView["viewportHeight"] ?? pickView["viewport_height"]) ?? fallbackViewportHeight;

            if (Mathf.Abs(liveViewportWidth - expectedViewportWidth) > SceneViewViewportTolerancePixels ||
                Mathf.Abs(liveViewportHeight - expectedViewportHeight) > SceneViewViewportTolerancePixels)
            {
                message = "Active Scene View viewport size no longer matches the screenshot pickView.";
                return false;
            }

            return true;
        }

        private static JObject NormalizePickView(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null)
                return null;
            if (token is JObject obj)
                return obj;
            if (token.Type == JTokenType.String)
            {
                string raw = token.ToString();
                if (string.IsNullOrWhiteSpace(raw))
                    return null;
                return JObject.Parse(raw);
            }
            throw new JsonException("pick_view must be a JSON object.");
        }

        private static Camera CreateCameraFromPickView(JObject view, float viewportWidth, float viewportHeight, out GameObject tempGo)
        {
            Vector3? position = ReadVector3(view["position"]);
            Vector3? rotation = ReadVector3(view["rotation"] ?? view["eulerAngles"] ?? view["euler_angles"]);
            if (!position.HasValue)
                throw new ArgumentException("pick_view.position is required.");
            if (!rotation.HasValue)
                throw new ArgumentException("pick_view.rotation is required.");

            tempGo = new GameObject("__MCP_PickViewCamera_Temp__");
            tempGo.hideFlags = HideFlags.HideAndDontSave;
            var camera = tempGo.AddComponent<Camera>();
            camera.enabled = false;
            camera.transform.position = position.Value;
            camera.transform.rotation = Quaternion.Euler(rotation.Value);

            string projection = ParamCoercion.CoerceString(view["projection"], null)?.ToLowerInvariant();
            bool orthographic = ParamCoercion.CoerceBool(view["orthographic"], projection == "orthographic");
            camera.orthographic = orthographic;

            if (orthographic)
            {
                float? size = ParamCoercion.CoerceFloatNullable(view["orthographicSize"] ?? view["orthographic_size"]);
                if (!IsFinite(size) || size.Value <= 0f)
                    throw new ArgumentException("pick_view.orthographicSize is required for orthographic screenshots.");
                camera.orthographicSize = size.Value;
            }
            else
            {
                float? fov = ParamCoercion.CoerceFloatNullable(view["fieldOfView"] ?? view["field_of_view"]);
                if (!IsFinite(fov) || fov.Value <= 0f || fov.Value >= 180f)
                    throw new ArgumentException("pick_view.fieldOfView is required for perspective screenshots.");
                camera.fieldOfView = fov.Value;
            }

            camera.nearClipPlane = ParamCoercion.CoerceFloatNullable(view["nearClipPlane"] ?? view["near_clip_plane"]) ?? 0.3f;
            camera.farClipPlane = ParamCoercion.CoerceFloatNullable(view["farClipPlane"] ?? view["far_clip_plane"]) ?? 1000f;
            int? cullingMask = ParamCoercion.CoerceIntNullable(view["cullingMask"] ?? view["culling_mask"]);
            if (cullingMask.HasValue)
                camera.cullingMask = cullingMask.Value;
            float aspect = ParamCoercion.CoerceFloatNullable(view["aspect"]) ?? (viewportWidth / viewportHeight);
            if (!IsFinite(aspect) || aspect <= 0f)
                aspect = viewportWidth / viewportHeight;
            camera.aspect = aspect;
            return camera;
        }

        private static Vector3? ReadVector3(JToken token)
        {
            if (token is JArray arr && arr.Count >= 3)
            {
                float? x = ParamCoercion.CoerceFloatNullable(arr[0]);
                float? y = ParamCoercion.CoerceFloatNullable(arr[1]);
                float? z = ParamCoercion.CoerceFloatNullable(arr[2]);
                if (IsFinite(x) && IsFinite(y) && IsFinite(z))
                    return new Vector3(x.Value, y.Value, z.Value);
            }

            if (token is JObject obj)
            {
                float? x = ParamCoercion.CoerceFloatNullable(obj["x"]);
                float? y = ParamCoercion.CoerceFloatNullable(obj["y"]);
                float? z = ParamCoercion.CoerceFloatNullable(obj["z"]);
                if (IsFinite(x) && IsFinite(y) && IsFinite(z))
                    return new Vector3(x.Value, y.Value, z.Value);
            }

            return null;
        }

        private static Camera ResolveCamera(string cameraRef)
        {
            if (string.IsNullOrWhiteSpace(cameraRef))
                return null;

            if (int.TryParse(cameraRef, out int id))
            {
                var obj = GameObjectLookup.ResolveInstanceID(id);
                if (obj is Camera cam)
                    return cam;
                if (obj is GameObject go)
                    return go.GetComponent<Camera>();
            }

            var allCameras = UnityFindObjectsCompat.FindAll<Camera>();
            foreach (var cam in allCameras)
            {
                if (cam == null)
                    continue;
                if (cam.name == cameraRef || cam.gameObject.name == cameraRef)
                    return cam;
            }

            if (cameraRef.Contains("/"))
            {
                var ids = GameObjectLookup.SearchGameObjects("by_path", cameraRef, includeInactive: false, maxResults: 1);
                if (ids.Count > 0)
                {
                    var go = GameObjectLookup.FindById(ids[0]);
                    if (go != null)
                        return go.GetComponent<Camera>();
                }
            }

            return null;
        }

        private static int ResolveLayerMask(JToken token, int defaultMask)
        {
            if (token == null || token.Type == JTokenType.Null)
                return defaultMask;

            string layerMaskStr = token.ToString();
            if (string.IsNullOrWhiteSpace(layerMaskStr))
                return defaultMask;

            if (int.TryParse(layerMaskStr, out int mask))
                return mask;

            int resolved = 0;
            foreach (var partRaw in layerMaskStr.Split(','))
            {
                string part = partRaw.Trim();
                if (part.Length == 0)
                    continue;

                int layer = LayerMask.NameToLayer(part);
                if (layer < 0)
                    throw new ArgumentException($"Unknown layer name: '{part}'. Use a valid layer name, comma-separated layer names, or integer mask.");
                resolved |= 1 << layer;
            }

            return resolved == 0 ? defaultMask : resolved;
        }

        private static int ResolveDefaultLayerMask(JObject pickView, Camera camera)
        {
            int? pickViewMask = ParamCoercion.CoerceIntNullable(pickView?["cullingMask"] ?? pickView?["culling_mask"]);
            if (pickViewMask.HasValue)
                return pickViewMask.Value;

            if (camera != null)
                return camera.cullingMask;

            return ~0;
        }

        private static bool IsFinite(float? value)
        {
            return value.HasValue && !float.IsNaN(value.Value) && !float.IsInfinity(value.Value);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
