using MCPForUnity.Editor.Tools;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using static MCPForUnityTests.Editor.TestUtilities;

namespace MCPForUnityTests.Editor.Tools
{
    public class PickGameObjectFromImageTests
    {
        private const string TempRoot = "Assets/Temp/PickGameObjectFromImageTests";

        [SetUp]
        public void SetUp()
        {
            EnsureFolder(TempRoot);
        }

        [TearDown]
        public void TearDown()
        {
            PickGameObjectFromImage.SceneViewPickOverrideForTests = null;

#if UNITY_2022_2_OR_NEWER
            foreach (var go in UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None))
#else
            foreach (var go in UnityEngine.Object.FindObjectsOfType<GameObject>())
#endif
            {
                if (go.name.StartsWith("PickTest_"))
                    UnityEngine.Object.DestroyImmediate(go);
            }

            if (AssetDatabase.IsValidFolder(TempRoot))
                AssetDatabase.DeleteAsset(TempRoot);
            CleanupEmptyParentFolders(TempRoot);
        }

        [Test]
        public void Pick3D_CameraCenterPixelHitsBoxCollider()
        {
            var camera = CreatePerspectiveCamera("PickTest_3DCamera", new Vector3(500, 500, -10));
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = "PickTest_3DTarget";
            cube.transform.position = new Vector3(500, 500, 0);
            Physics.SyncTransforms();

            var result = ToJObject(PickGameObjectFromImage.HandleCommand(new JObject
            {
                ["imageX"] = 50,
                ["imageY"] = 50,
                ["imageWidth"] = 100,
                ["imageHeight"] = 100,
                ["camera"] = camera.gameObject.name,
                ["dimension"] = "3d"
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            Assert.IsTrue(result["data"].Value<bool>("hit"), result.ToString());
            Assert.AreEqual("PickTest_3DTarget", result["data"]["gameObject"]["name"].ToString());
            Assert.AreEqual("BoxCollider", result["data"]["colliderType"].ToString());
        }

        [Test]
        public void Pick2D_OrthographicCameraCenterPixelHitsCollider2D()
        {
            var camera = CreateOrthographicCamera("PickTest_2DCamera", new Vector3(900, 900, -10));
            var target = new GameObject("PickTest_2DTarget");
            target.transform.position = new Vector3(900, 900, 0);
            target.AddComponent<BoxCollider2D>();
            Physics2D.SyncTransforms();

            var result = ToJObject(PickGameObjectFromImage.HandleCommand(new JObject
            {
                ["imageX"] = 50,
                ["imageY"] = 50,
                ["imageWidth"] = 100,
                ["imageHeight"] = 100,
                ["camera"] = camera.gameObject.name,
                ["dimension"] = "2d"
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            Assert.IsTrue(result["data"].Value<bool>("hit"), result.ToString());
            Assert.AreEqual("PickTest_2DTarget", result["data"]["gameObject"]["name"].ToString());
            Assert.AreEqual("BoxCollider2D", result["data"]["colliderType"].ToString());
        }

        [Test]
        public void PickView_TakesPriorityOverCurrentCameraState()
        {
            var camera = CreatePerspectiveCamera("PickTest_BadCamera", new Vector3(0, 0, -10));
            var target = GameObject.CreatePrimitive(PrimitiveType.Cube);
            target.name = "PickTest_PickViewTarget";
            target.transform.position = new Vector3(1300, 1300, 0);
            Physics.SyncTransforms();

            var result = ToJObject(PickGameObjectFromImage.HandleCommand(new JObject
            {
                ["imageX"] = 50,
                ["imageY"] = 50,
                ["imageWidth"] = 100,
                ["imageHeight"] = 100,
                ["camera"] = camera.gameObject.name,
                ["pickView"] = PerspectivePickView(new Vector3(1300, 1300, -10)),
                ["dimension"] = "3d"
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            Assert.IsTrue(result["data"].Value<bool>("hit"), result.ToString());
            Assert.AreEqual("pick_view", result["data"]["viewSource"].ToString());
            Assert.AreEqual("PickTest_PickViewTarget", result["data"]["gameObject"]["name"].ToString());
        }

        [Test]
        public void PickView_CullingMaskExcludesTargetWhenLayerMaskOmitted()
        {
            var target = GameObject.CreatePrimitive(PrimitiveType.Cube);
            target.name = "PickTest_CullingMaskTarget";
            target.transform.position = new Vector3(1400, 1400, 0);
            target.layer = LayerMask.NameToLayer("Ignore Raycast");
            Physics.SyncTransforms();

            var pickView = PerspectivePickView(new Vector3(1400, 1400, -10));
            pickView["cullingMask"] = 1 << LayerMask.NameToLayer("Default");

            var result = ToJObject(PickGameObjectFromImage.HandleCommand(new JObject
            {
                ["imageX"] = 50,
                ["imageY"] = 50,
                ["imageWidth"] = 100,
                ["imageHeight"] = 100,
                ["pickView"] = pickView,
                ["dimension"] = "3d"
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            Assert.IsFalse(result["data"].Value<bool>("hit"), result.ToString());
        }

        [Test]
        public void Pick3D_ScaleCoordinatesMapBackToViewport()
        {
            var camera = CreatePerspectiveCamera("PickTest_ScaleCamera", new Vector3(1500, 1500, -10));
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = "PickTest_ScaleTarget";
            cube.transform.position = new Vector3(1500, 1500, 0);
            Physics.SyncTransforms();

            var result = ToJObject(PickGameObjectFromImage.HandleCommand(new JObject
            {
                ["imageX"] = 25,
                ["imageY"] = 25,
                ["imageWidth"] = 50,
                ["imageHeight"] = 50,
                ["scaleX"] = 2,
                ["scaleY"] = 2,
                ["viewportWidth"] = 100,
                ["viewportHeight"] = 100,
                ["camera"] = camera.gameObject.name,
                ["dimension"] = "3d"
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            Assert.IsTrue(result["data"].Value<bool>("hit"), result.ToString());
            Assert.AreEqual("PickTest_ScaleTarget", result["data"]["gameObject"]["name"].ToString());
        }

        [Test]
        public void SceneViewPickView_EditorPickHitsRendererWithoutCollider()
        {
            var target = GameObject.CreatePrimitive(PrimitiveType.Cube);
            target.name = "PickTest_SceneViewRendererOnly";
            UnityEngine.Object.DestroyImmediate(target.GetComponent<Collider>());
            Vector2 observedPoint = Vector2.zero;
            PickGameObjectFromImage.SceneViewPickOverrideForTests = point =>
            {
                observedPoint = point;
                return new PickGameObjectFromImage.SceneViewPickResult(target, 0);
            };

            var result = ToJObject(PickGameObjectFromImage.HandleCommand(new JObject
            {
                ["imageX"] = 50,
                ["imageY"] = 50,
                ["imageWidth"] = 100,
                ["imageHeight"] = 100,
                ["pickView"] = PerspectivePickView(new Vector3(3000, 3000, -10), "scene_view"),
                ["dimension"] = "3d"
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            Assert.IsTrue(result["data"].Value<bool>("hit"), result.ToString());
            Assert.AreEqual("PickTest_SceneViewRendererOnly", result["data"]["gameObject"]["name"].ToString());
            Assert.AreEqual("scene_view_editor_pick", result["data"]["pickMode"].ToString());
            Assert.IsFalse(result["data"].Value<bool>("requiresCollider"));
            Assert.AreEqual(0, result["data"].Value<int>("materialIndex"));
            Assert.That(observedPoint.x, Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(observedPoint.y, Is.EqualTo(0.5f).Within(0.0001f));
        }

        [Test]
        public void SceneViewPickView_MeshIntersectionHitsRendererWithoutCollider()
        {
            var target = GameObject.CreatePrimitive(PrimitiveType.Cube);
            target.name = "PickTest_SceneViewMeshRendererOnly";
            target.transform.position = new Vector3(3050, 3050, 0);
            UnityEngine.Object.DestroyImmediate(target.GetComponent<Collider>());

            var result = ToJObject(PickGameObjectFromImage.HandleCommand(new JObject
            {
                ["imageX"] = 50,
                ["imageY"] = 50,
                ["imageWidth"] = 100,
                ["imageHeight"] = 100,
                ["pickView"] = PerspectivePickView(new Vector3(3050, 3050, -10), "scene_view"),
                ["dimension"] = "3d"
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            Assert.IsTrue(result["data"].Value<bool>("hit"), result.ToString());
            Assert.AreEqual("PickTest_SceneViewMeshRendererOnly", result["data"]["gameObject"]["name"].ToString());
            Assert.AreEqual("scene_view_editor_pick", result["data"]["pickMode"].ToString());
            Assert.AreEqual("mesh_intersection", result["data"]["sceneViewPickBackend"].ToString());
            Assert.IsFalse(result["data"].Value<bool>("requiresCollider"));
            Assert.AreEqual(-1, result["data"].Value<int>("materialIndex"));
            Assert.IsNotNull(result["data"]["point"], result.ToString());
            Assert.IsNotNull(result["data"]["normal"], result.ToString());
            Assert.Greater(result["data"].Value<float>("distance"), 0f);
        }

        [Test]
        public void SceneViewPickView_MeshIntersectionHitsSkinnedRendererWithoutCollider()
        {
            Mesh mesh = null;
            try
            {
                var target = new GameObject("PickTest_SceneViewSkinnedRendererOnly");
                target.transform.position = new Vector3(3060, 3060, 0);
                mesh = new Mesh { hideFlags = HideFlags.HideAndDontSave };
                mesh.vertices = new[]
                {
                    new Vector3(-1, -1, 0),
                    new Vector3(1, -1, 0),
                    new Vector3(-1, 1, 0),
                    new Vector3(1, 1, 0),
                };
                mesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();

                var renderer = target.AddComponent<SkinnedMeshRenderer>();
                renderer.sharedMesh = mesh;
                renderer.localBounds = new Bounds(Vector3.zero, new Vector3(2, 2, 0.1f));
                renderer.updateWhenOffscreen = true;

                var result = ToJObject(PickGameObjectFromImage.HandleCommand(new JObject
                {
                    ["imageX"] = 50,
                    ["imageY"] = 50,
                    ["imageWidth"] = 100,
                    ["imageHeight"] = 100,
                    ["pickView"] = PerspectivePickView(new Vector3(3060, 3060, -10), "scene_view"),
                    ["dimension"] = "3d"
                }));

                Assert.IsTrue(result.Value<bool>("success"), result.ToString());
                Assert.IsTrue(result["data"].Value<bool>("hit"), result.ToString());
                Assert.AreEqual("PickTest_SceneViewSkinnedRendererOnly", result["data"]["gameObject"]["name"].ToString());
                Assert.AreEqual("mesh_intersection", result["data"]["sceneViewPickBackend"].ToString());
                Assert.IsFalse(result["data"].Value<bool>("requiresCollider"));
            }
            finally
            {
                if (mesh != null)
                    UnityEngine.Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void SceneViewPickView_LayerMaskExcludesEditorPickAndFallsBackToNoHit()
        {
            var target = GameObject.CreatePrimitive(PrimitiveType.Cube);
            target.name = "PickTest_SceneViewExcludedRendererOnly";
            target.layer = LayerMask.NameToLayer("Default");
            UnityEngine.Object.DestroyImmediate(target.GetComponent<Collider>());
            PickGameObjectFromImage.SceneViewPickOverrideForTests = _ =>
                new PickGameObjectFromImage.SceneViewPickResult(target, 0);

            var result = ToJObject(PickGameObjectFromImage.HandleCommand(new JObject
            {
                ["imageX"] = 50,
                ["imageY"] = 50,
                ["imageWidth"] = 100,
                ["imageHeight"] = 100,
                ["pickView"] = PerspectivePickView(new Vector3(3100, 3100, -10), "scene_view"),
                ["dimension"] = "3d",
                ["layerMask"] = "Ignore Raycast",
                ["maxDistance"] = 20
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            Assert.IsFalse(result["data"].Value<bool>("hit"), result.ToString());
            Assert.AreEqual("physics_3d", result["data"]["pickMode"].ToString());
            Assert.IsTrue(result["data"].Value<bool>("requiresCollider"));
        }

        [Test]
        public void SceneViewPickView_EditorPickMissFallsBackTo3DRaycast()
        {
            var target = GameObject.CreatePrimitive(PrimitiveType.Cube);
            target.name = "PickTest_SceneViewFallbackCollider";
            target.transform.position = new Vector3(3200, 3200, 0);
            Physics.SyncTransforms();
            PickGameObjectFromImage.SceneViewPickOverrideForTests = _ =>
                new PickGameObjectFromImage.SceneViewPickResult(null, -1);

            var result = ToJObject(PickGameObjectFromImage.HandleCommand(new JObject
            {
                ["imageX"] = 50,
                ["imageY"] = 50,
                ["imageWidth"] = 100,
                ["imageHeight"] = 100,
                ["pickView"] = PerspectivePickView(new Vector3(3200, 3200, -10), "scene_view"),
                ["dimension"] = "3d"
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            Assert.IsTrue(result["data"].Value<bool>("hit"), result.ToString());
            Assert.AreEqual("PickTest_SceneViewFallbackCollider", result["data"]["gameObject"]["name"].ToString());
            Assert.AreEqual("physics_3d", result["data"]["pickMode"].ToString());
            Assert.IsTrue(result["data"].Value<bool>("requiresCollider"));
            Assert.AreEqual("BoxCollider", result["data"]["colliderType"].ToString());
        }

        [Test]
        public void Pick3D_NoHitReturnsSuccessfulFalseHit()
        {
            var camera = CreatePerspectiveCamera("PickTest_MissCamera", new Vector3(1800, 1800, -10));

            var result = ToJObject(PickGameObjectFromImage.HandleCommand(new JObject
            {
                ["imageX"] = 50,
                ["imageY"] = 50,
                ["imageWidth"] = 100,
                ["imageHeight"] = 100,
                ["camera"] = camera.gameObject.name,
                ["dimension"] = "3d",
                ["maxDistance"] = 20
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            Assert.IsFalse(result["data"].Value<bool>("hit"), result.ToString());
        }

        [Test]
        public void Pick3D_LayerMaskExcludesTarget()
        {
            var camera = CreatePerspectiveCamera("PickTest_LayerCamera", new Vector3(2100, 2100, -10));
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = "PickTest_LayerTarget";
            cube.transform.position = new Vector3(2100, 2100, 0);
            cube.layer = LayerMask.NameToLayer("Default");
            Physics.SyncTransforms();

            var result = ToJObject(PickGameObjectFromImage.HandleCommand(new JObject
            {
                ["imageX"] = 50,
                ["imageY"] = 50,
                ["imageWidth"] = 100,
                ["imageHeight"] = 100,
                ["camera"] = camera.gameObject.name,
                ["dimension"] = "3d",
                ["layerMask"] = "Ignore Raycast"
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            Assert.IsFalse(result["data"].Value<bool>("hit"), result.ToString());
        }

        [Test]
        public void MissingPickViewAndCameraReturnsError()
        {
            var result = ToJObject(PickGameObjectFromImage.HandleCommand(new JObject
            {
                ["imageX"] = 50,
                ["imageY"] = 50,
                ["imageWidth"] = 100,
                ["imageHeight"] = 100
            }));

            Assert.IsFalse(result.Value<bool>("success"));
            Assert.That(result["error"].ToString(), Does.Contain("pick_view"));
        }

        [Test]
        public void Screenshot_WithExplicitCamera_ReturnsPickView()
        {
            var camera = CreatePerspectiveCamera("PickTest_ScreenshotCamera", new Vector3(2400, 2400, -10));
            camera.cullingMask = 1 << LayerMask.NameToLayer("Default");

            var result = ToJObject(ManageScene.HandleCommand(new JObject
            {
                ["action"] = "screenshot",
                ["camera"] = camera.gameObject.name,
                ["fileName"] = "explicit-camera-pick-view",
                ["outputFolder"] = TempRoot
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            var pickView = result["data"]?["pickView"] as JObject;
            Assert.IsNotNull(pickView, result.ToString());
            Assert.AreEqual("game_view", pickView.Value<string>("captureSource"));
            Assert.AreEqual("perspective", pickView.Value<string>("projection"));
            Assert.AreEqual(camera.cullingMask, pickView.Value<int>("cullingMask"));
            Assert.Greater(pickView.Value<int>("viewportWidth"), 0);
            Assert.Greater(pickView.Value<int>("viewportHeight"), 0);
        }

        [Test]
        public void Screenshot_PositionedCapture_ReturnsPickView()
        {
            var result = ToJObject(ManageScene.HandleCommand(new JObject
            {
                ["action"] = "screenshot",
                ["viewPosition"] = new JArray(2600, 2600, -10),
                ["viewRotation"] = new JArray(0, 0, 0),
                ["maxResolution"] = 64,
                ["fileName"] = "positioned-pick-view",
                ["outputFolder"] = TempRoot
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            var pickView = result["data"]?["pickView"] as JObject;
            Assert.IsNotNull(pickView, result.ToString());
            Assert.AreEqual("game_view", pickView.Value<string>("captureSource"));
            Assert.AreEqual("perspective", pickView.Value<string>("projection"));
            Assert.Greater(pickView.Value<int>("viewportWidth"), 0);
            Assert.Greater(pickView.Value<int>("viewportHeight"), 0);
        }

        [Test]
        public void Screenshot_SceneView_ReturnsSceneViewPickView()
        {
            if (Application.isBatchMode)
                Assert.Ignore("Scene View screenshot requires a non-batch Unity Editor window.");

            var sceneView = SceneView.lastActiveSceneView ?? EditorWindow.GetWindow<SceneView>();
            if (sceneView == null)
                Assert.Ignore("No Scene View window is available.");

            var result = ToJObject(ManageScene.HandleCommand(new JObject
            {
                ["action"] = "screenshot",
                ["captureSource"] = "scene_view",
                ["fileName"] = "scene-view-pick-view",
                ["outputFolder"] = TempRoot
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            Assert.AreEqual("scene_view", result["data"].Value<string>("captureSource"));
            var pickView = result["data"]?["pickView"] as JObject;
            Assert.IsNotNull(pickView, result.ToString());
            Assert.AreEqual("scene_view", pickView.Value<string>("captureSource"));
            Assert.Greater(pickView.Value<int>("viewportWidth"), 0);
            Assert.Greater(pickView.Value<int>("viewportHeight"), 0);
        }

        [Test]
        public void Screenshot_MultiviewDoesNotReturnSinglePickView()
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = "PickTest_MultiviewRenderer";
            cube.transform.position = new Vector3(2800, 2800, 0);

            var result = ToJObject(ManageScene.HandleCommand(new JObject
            {
                ["action"] = "screenshot",
                ["batch"] = "surround",
                ["maxResolution"] = 32,
                ["outputFolder"] = TempRoot
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            Assert.IsNull(result["data"]?["pickView"], result.ToString());
            Assert.IsNotNull(result["data"]?["shots"], result.ToString());
        }

        private static Camera CreatePerspectiveCamera(string name, Vector3 position)
        {
            var go = new GameObject(name);
            var camera = go.AddComponent<Camera>();
            camera.transform.position = position;
            camera.transform.rotation = Quaternion.identity;
            camera.fieldOfView = 60f;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 1000f;
            camera.aspect = 1f;
            return camera;
        }

        private static Camera CreateOrthographicCamera(string name, Vector3 position)
        {
            var camera = CreatePerspectiveCamera(name, position);
            camera.orthographic = true;
            camera.orthographicSize = 5f;
            return camera;
        }

        private static JObject PerspectivePickView(Vector3 position, string captureSource = "game_view")
        {
            return new JObject
            {
                ["captureSource"] = captureSource,
                ["position"] = new JArray(position.x, position.y, position.z),
                ["rotation"] = new JArray(0, 0, 0),
                ["projection"] = "perspective",
                ["fieldOfView"] = 60,
                ["nearClipPlane"] = 0.1,
                ["farClipPlane"] = 1000,
                ["aspect"] = 1,
                ["viewportWidth"] = 100,
                ["viewportHeight"] = 100
            };
        }
    }
}
