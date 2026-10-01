using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Tools;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace MCPForUnityTests.Editor.Tools
{
    public class ScreenshotColorWindow : EditorWindow
    {
        public bool paintEdgeMarkers;
        protected void OnGUI()
        {
            float halfWidth = position.width / 2;
            float halfHeight = position.height / 2;
            EditorGUI.DrawRect(new Rect(0, 0, halfWidth, halfHeight), Color.blue);
            EditorGUI.DrawRect(new Rect(halfWidth, 0, halfWidth, halfHeight), Color.green);
            EditorGUI.DrawRect(new Rect(0, halfHeight, halfWidth, halfHeight), Color.red);
            EditorGUI.DrawRect(new Rect(halfWidth, halfHeight, halfWidth, halfHeight), new Color(1, 1, 0, 1));
            if (paintEdgeMarkers)
            {
                float edge = 4 / EditorGUIUtility.pixelsPerPoint;
                EditorGUI.DrawRect(new Rect(edge, edge, position.width - 2 * edge,
                    position.height - 2 * edge), Color.black);
            }
        }
    }

    public class ScreenshotDockWindow : ScreenshotColorWindow { }

    public class ScreenshotDarkWindow : EditorWindow
    {
        private void OnGUI() => EditorGUI.DrawRect(new Rect(0, 0, position.width, position.height),
            new Color(0.12f, 0.18f, 0.24f, 1));
    }

    [Category("EditorWindowScreenshots")]
    public class ManageEditorWindowsTests
    {
        private ScreenshotColorWindow first;
        private ScreenshotColorWindow second;
        private ScreenshotDockWindow docked;
        private EditorWindow previous;

        [SetUp]
        public void SetUp()
        {
            previous = EditorWindow.focusedWindow;
            first = ScriptableObject.CreateInstance<ScreenshotColorWindow>();
            second = ScriptableObject.CreateInstance<ScreenshotColorWindow>();
            first.titleContent = new GUIContent("MCP first fixture");
            second.titleContent = new GUIContent("MCP second fixture");
        }

        [TearDown]
        public void TearDown()
        {
            ManageEditorWindows.CancelPendingCapture();
            if (first != null) Object.DestroyImmediate(first);
            if (second != null) Object.DestroyImmediate(second);
            if (docked != null) docked.Close();
            if (previous != null) previous.Focus();
        }

        [UnityTearDown]
        public IEnumerator LeavePlayMode()
        {
            if (EditorApplication.isPlaying) yield return new ExitPlayMode();
        }

        [Test]
        public void IdSelectsOneWindowWithDuplicateTitles()
        {
            second.titleContent = first.titleContent;
            Assert.That(Resolve(new ManageEditorWindows.Parameters { window_id = second.GetInstanceID() }, out var error), Is.SameAs(second));
            Assert.That(error, Is.Null);
        }

        [Test]
        public void TitleIgnoresCaseAndOuterSpaces()
        {
            Assert.That(Resolve(new ManageEditorWindows.Parameters { window_title = " MCP FIRST fixture " }, out _), Is.SameAs(first));
        }

        [Test]
        public void DuplicateTitlesAndTypesRequireIds()
        {
            second.titleContent = first.titleContent;
            Assert.That(Resolve(new ManageEditorWindows.Parameters { window_title = "MCP first fixture" }, out var error), Is.Null);
            Assert.That(error, Does.Contain("More than one"));
            Assert.That(Resolve(new ManageEditorWindows.Parameters { window_type = typeof(ScreenshotColorWindow).FullName }, out _), Is.Null);
        }

        [Test]
        public void MultipleSelectorsAreRejected()
        {
            Assert.That(Resolve(new ManageEditorWindows.Parameters { window_id = first.GetInstanceID(), window_title = "other" }, out var error), Is.Null);
            Assert.That(error, Does.Contain("only one"));
        }

        [Test]
        public void ClosedIdCannotSelectAnotherWindow()
        {
            int id = second.GetInstanceID();
            Object.DestroyImmediate(second);
            Assert.That(ManageEditorWindows.Resolve(new EditorWindow[] { first },
                new ManageEditorWindows.Parameters { window_id = id }, out var error), Is.Null);
            Assert.That(error, Does.Contain("No matching"));
        }

        [Test]
        public void InvalidActionDoesNotChangeFocus()
        {
            var focused = EditorWindow.focusedWindow;
            Assert.That(ManageEditorWindows.HandleCommand(new JObject { ["action"] = "close" }).Result, Is.TypeOf<ErrorResponse>());
            Assert.That(EditorWindow.focusedWindow, Is.SameAs(focused));
        }

        [Test]
        public void BatchCaptureHasAnExplicitError()
        {
            if (!Application.isBatchMode) Assert.Ignore("Batch-mode guard requires a batch Editor.");
            var result = (ErrorResponse)ManageEditorWindows.HandleCommand(new JObject { ["action"] = "screenshot" }).Result;
            Assert.That(result.Error, Does.Contain("Batch mode"));
        }

        [Test]
        public void UnsupportedWindowHostFailsWithoutDesktopFallback()
        {
            Assert.Throws<InvalidOperationException>(() => EditorWindowScreenshotUtility.CaptureWindowPixels(first, 64, 64));
        }

        [TestCase(0, 64)]
        [TestCase(4097, 4097)]
        public void EmptyAndExcessiveBufferSizesAreRejected(int width, int height)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => EditorWindowScreenshotUtility.CaptureWindowPixels(first, width, height));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ParameterAccessorsPreserveEveryOptionAndDefault(bool camelCase)
        {
            string Key(string snake, string camel) => camelCase ? camel : snake;
            var args = new JObject {
                ["action"] = "screenshot", [Key("window_id", "windowId")] = second.GetInstanceID(),
                [Key("window_title", "windowTitle")] = "Inspector",
                [Key("window_type", "windowType")] = "UnityEditor.InspectorWindow",
                ["focus"] = false, [Key("restore_focus", "restoreFocus")] = false,
                [Key("include_image", "includeImage")] = false,
                [Key("save_file", "saveFile")] = true, [Key("max_resolution", "maxResolution")] = 128
            };
            var p = ManageEditorWindows.ParseParameters(args);
            Assert.That(p.action, Is.EqualTo("screenshot"));
            Assert.That(p.window_id, Is.EqualTo(second.GetInstanceID()));
            Assert.That(p.window_title, Is.EqualTo("Inspector"));
            Assert.That(p.window_type, Is.EqualTo("UnityEditor.InspectorWindow"));
            Assert.That(p.focus || p.restore_focus || p.include_image, Is.False);
            Assert.That(p.save_file, Is.True);
            Assert.That(p.max_resolution, Is.EqualTo(128));
            p = ManageEditorWindows.ParseParameters(null);
            Assert.That(p.action, Is.EqualTo("list"));
            Assert.That(p.window_id, Is.Null);
            Assert.That(p.focus && p.restore_focus && p.include_image, Is.True);
            Assert.That(p.save_file, Is.False);
            Assert.That(p.max_resolution, Is.EqualTo(1600));
        }

        [TestCase("window_id", "not-an-id")]
        [TestCase("windowId", "not-an-id")]
        [TestCase("windowId", null)]
        [TestCase("windowTitle", "  ")]
        [TestCase("window_type", "")]
        [TestCase("windowType", null)]
        [TestCase("maxResolution", "bad")]
        public void InvalidExplicitParametersNeverBecomeFocusedWindowRequests(string key, string value)
        {
            var args = new JObject { ["action"] = "screenshot",
                [key] = value == null ? JValue.CreateNull() : new JValue(value) };
            var focused = EditorWindow.focusedWindow;
            var result = ManageEditorWindows.HandleCommand(args).Result;
            Assert.That(result, Is.TypeOf<ErrorResponse>());
            Assert.That(((ErrorResponse)result).Error, Does.Contain("must be"));
            Assert.That(EditorWindow.focusedWindow, Is.SameAs(focused));
        }

        [UnityTest]
        public IEnumerator SavedAndInlineFullSizePngBytesMatch()
        {
            RequireGraphics();
            yield return ShowFixtures();
            var task = ManageEditorWindows.HandleCommand(new JObject {
                ["action"] = "screenshot", ["windowId"] = second.GetInstanceID(),
                ["saveFile"] = true, ["includeImage"] = true, ["maxResolution"] = 4096
            });
            yield return Await(task);
            Assert.That(task.Result, Is.TypeOf<SuccessResponse>());
            var data = (JObject)((SuccessResponse)task.Result).Data;
            string saved = (string)data["path"];
            try
            {
                Assert.That(File.ReadAllBytes(saved), Is.EqualTo(Convert.FromBase64String((string)data["imageBase64"])));
                Assert.That((int)data["imageWidth"], Is.EqualTo((int)data["width"]));
                Assert.That((int)data["imageHeight"], Is.EqualTo((int)data["height"]));
            }
            finally { if (File.Exists(saved)) File.Delete(saved); }
        }

        [UnityTest]
        public IEnumerator DockedBufferPreservesFourCornerColorsAndPhysicalDimensions()
        {
            RequireGraphics();
            var scene = EditorWindow.GetWindow<SceneView>();
            docked = EditorWindow.GetWindow<ScreenshotDockWindow>("MCP corner dock fixture", false, typeof(SceneView));
            docked.paintEdgeMarkers = true;
            docked.ShowTab();
            yield return null;
            yield return null;
            Assert.That(docked.docked, Is.True);
            var task = Screenshot(docked, 4096);
            yield return Await(task);
            Assert.That(task.Result, Is.TypeOf<SuccessResponse>());
            var data = (JObject)((SuccessResponse)task.Result).Data;
            var image = new Texture2D(2, 2);
            try
            {
                Assert.That(image.LoadImage(Convert.FromBase64String((string)data["imageBase64"])), Is.True);
                float scale = (float)data["pixels_per_point"];
                TestContext.CurrentContext.Test.Properties.Set("capture_backing_scale", scale);
                Assert.That(image.width, Is.EqualTo(Mathf.RoundToInt(docked.position.width * scale)));
                Assert.That(image.height, Is.EqualTo(Mathf.RoundToInt(docked.position.height * scale)));
                AssertContentEdgeMarkers(image);
            }
            finally { Object.DestroyImmediate(image); scene.ShowTab(); }
        }

        [UnityTest]
        public IEnumerator FloatingBufferCapturePreservesOrientationAndFocus()
        {
            RequireGraphics();
            yield return ShowFixtures();
            var task = Screenshot(second, 160);
            yield return Await(task);
            var data = ((SuccessResponse)task.Result).Data as JObject;
            Assert.That(data, Is.Not.Null);
            Assert.That((string)data["path"], Is.Null);
            Assert.That(EditorWindow.focusedWindow, Is.SameAs(first));
            var image = new Texture2D(2, 2);
            try
            {
                Assert.That(image.LoadImage(Convert.FromBase64String((string)data["imageBase64"])), Is.True);
                Assert.That(Mathf.Max(image.width, image.height), Is.LessThanOrEqualTo(160));
                AssertCornerColors(image);
            }
            finally { Object.DestroyImmediate(image); }
        }

        [UnityTest]
        public IEnumerator FloatingFullSizeBufferMatchesContentEdges()
        {
            RequireGraphics();
            second.paintEdgeMarkers = true;
            yield return ShowFixtures();
            var task = Screenshot(second, 4096);
            yield return Await(task);
            Assert.That(task.Result, Is.TypeOf<SuccessResponse>());
            var data = (JObject)((SuccessResponse)task.Result).Data;
            var image = new Texture2D(2, 2);
            try
            {
                Assert.That(image.LoadImage(Convert.FromBase64String((string)data["imageBase64"])), Is.True);
                float scale = (float)data["pixels_per_point"];
                TestContext.CurrentContext.Test.Properties.Set("capture_backing_scale", scale);
                Assert.That(image.width, Is.EqualTo(Mathf.RoundToInt(second.position.width * scale)));
                Assert.That(image.height, Is.EqualTo(Mathf.RoundToInt(second.position.height * scale)));
                AssertContentEdgeMarkers(image);
            }
            finally { Object.DestroyImmediate(image); }
        }

        [UnityTest]
        public IEnumerator SceneViewViewportPreservesContentEdgesAndOrientation() =>
            CaptureSceneViewFixture(false);

        [UnityTest]
        public IEnumerator DockedSceneViewViewportPreservesContentEdgesAndOrientation() =>
            CaptureSceneViewFixture(true);

        private static IEnumerator CaptureSceneViewFixture(bool dockedScene)
        {
            RequireGraphics();
            var scene = dockedScene ? EditorWindow.GetWindow<SceneView>() : ScriptableObject.CreateInstance<SceneView>();
            bool previousGizmos = scene.drawGizmos;
            if (!dockedScene)
            {
                scene.titleContent = new GUIContent("MCP Scene viewport fixture");
                scene.position = new Rect(200, 150, 400, 300);
            }
            scene.drawGizmos = false;
            // Controls are outside this fixture's painted viewport. Disable
            // overlays only on the owned Scene View so they cannot cover markers.
            var canvas = typeof(SceneView).GetProperty("overlayCanvas",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(scene);
            var overlaysProperty = canvas?.GetType().GetProperty("overlaysEnabled",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            object previousOverlays = overlaysProperty?.GetValue(canvas);
            void SetOverlays(bool enabled)
            {
                if (canvas == null) return;
                if (overlaysProperty?.CanWrite == true) overlaysProperty.SetValue(canvas, enabled);
                else
                {
                    var setter = canvas.GetType().GetMethod("SetOverlaysEnabled",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    Assert.That(setter, Is.Not.Null, "The Scene View fixture cannot hide its own overlays.");
                    setter.Invoke(canvas, new object[] { enabled });
                }
            }
            var image = new Texture2D(2, 2);
            string folder = Path.GetFullPath(Path.Combine(Application.dataPath,
                "../Library/McpSceneViewportTests", Guid.NewGuid().ToString("N")));
            int repaints = 0;
            void PaintViewport(SceneView view)
            {
                if (view != scene || Event.current.type != EventType.Repaint) return;
                var viewport = (Rect)typeof(SceneView).GetProperty("cameraViewport",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(scene);
                Handles.BeginGUI();
                try
                {
                    float halfWidth = viewport.width / 2;
                    float halfHeight = viewport.height / 2;
                    EditorGUI.DrawRect(new Rect(0, 0, halfWidth, halfHeight), Color.blue);
                    EditorGUI.DrawRect(new Rect(halfWidth, 0, halfWidth, halfHeight), Color.green);
                    EditorGUI.DrawRect(new Rect(0, halfHeight, halfWidth, halfHeight), Color.red);
                    EditorGUI.DrawRect(new Rect(halfWidth, halfHeight, halfWidth, halfHeight), new Color(1, 1, 0, 1));
                    float edge = 4 / EditorGUIUtility.pixelsPerPoint;
                    EditorGUI.DrawRect(new Rect(edge, edge, viewport.width - 2 * edge,
                        viewport.height - 2 * edge), Color.black);
                    repaints++;
                }
                finally { Handles.EndGUI(); }
            }
            SceneView.duringSceneGui += PaintViewport;
            try
            {
                if (dockedScene) scene.ShowTab(); else scene.ShowUtility();
                Assert.That(scene.docked, Is.EqualTo(dockedScene));
                SetOverlays(false);
                scene.Focus();
                double deadline = EditorApplication.timeSinceStartup + 5;
                while (repaints < 2 && EditorApplication.timeSinceStartup < deadline)
                {
                    scene.Repaint();
                    yield return null;
                }
                Assert.That(repaints, Is.GreaterThanOrEqualTo(2), "Scene View fixture never painted its viewport.");
                var result = EditorWindowScreenshotUtility.CaptureSceneViewViewportToProject(scene,
                    "viewport.png", 1, true, true, 4096, out int width, out int height, folder);
                Assert.That(image.LoadImage(Convert.FromBase64String(result.ImageBase64)), Is.True);
                TestContext.CurrentContext.Test.Properties.Set("capture_backing_scale",
                    EditorWindowScreenshotUtility.GetWindowPixelsPerPoint(scene));
                Assert.That(image.width, Is.EqualTo(width));
                Assert.That(image.height, Is.EqualTo(height));
                AssertContentEdgeMarkers(image);
            }
            finally
            {
                SceneView.duringSceneGui -= PaintViewport;
                scene.drawGizmos = previousGizmos;
                if (previousOverlays is bool enabled) SetOverlays(enabled);
                if (!dockedScene) scene.Close();
                Object.DestroyImmediate(image);
                string file = Path.Combine(folder, "viewport.png");
                if (File.Exists(file)) File.Delete(file);
                if (Directory.Exists(folder)) Directory.Delete(folder);
            }
        }

        [UnityTest]
        public IEnumerator CamelCaseRequestSelectsTheUnfocusedWindow()
        {
            RequireGraphics();
            yield return ShowFixtures();
            var task = ManageEditorWindows.HandleCommand(new JObject {
                ["action"] = "screenshot", ["windowId"] = second.GetInstanceID(),
                ["maxResolution"] = 128, ["restoreFocus"] = true
            });
            yield return Await(task);
            Assert.That(task.Result, Is.TypeOf<SuccessResponse>());
            var data = (JObject)((SuccessResponse)task.Result).Data;
            Assert.That((int)data["window"]["window_id"], Is.EqualTo(second.GetInstanceID()));
            Assert.That(Math.Max((int)data["imageWidth"], (int)data["imageHeight"]), Is.EqualTo(128));
            Assert.That(EditorWindow.focusedWindow, Is.SameAs(first));
        }

        [UnityTest]
        public IEnumerator BatchRouteSelectsTheRequestedUnfocusedWindow()
        {
            RequireGraphics();
            yield return ShowFixtures();
            var task = BatchExecute.HandleCommand(new JObject {
                ["commands"] = new JArray(new JObject {
                    ["tool"] = "manage_editor_windows", ["params"] = new JObject {
                        ["action"] = "screenshot", ["window_id"] = second.GetInstanceID(),
                        ["max_resolution"] = 128
                    }
                })
            });
            yield return Await(task);
            Assert.That(task.Result, Is.TypeOf<SuccessResponse>());
            var data = JObject.FromObject(((SuccessResponse)task.Result).Data);
            Assert.That((int)data["results"][0]["result"]["data"]["window"]["window_id"],
                Is.EqualTo(second.GetInstanceID()));
            Assert.That(EditorWindow.focusedWindow, Is.SameAs(first));
        }

        [UnityTest]
        public IEnumerator InvalidBatchSelectorFailsWithoutCapturingTheFocusedWindow()
        {
            RequireGraphics();
            yield return ShowFixtures();
            var task = BatchExecute.HandleCommand(new JObject {
                ["commands"] = new JArray(new JObject {
                    ["tool"] = "manage_editor_windows", ["params"] = new JObject {
                        ["action"] = "screenshot", ["window_title"] = "  "
                    }
                })
            });
            yield return Await(task);
            Assert.That(task.Result, Is.TypeOf<ErrorResponse>());
            var data = JObject.FromObject(((ErrorResponse)task.Result).Data);
            Assert.That((bool)data["results"][0]["callSucceeded"], Is.False);
            Assert.That(data["results"][0]["result"]["data"], Is.Null);
            Assert.That(EditorWindow.focusedWindow, Is.SameAs(first));
        }

        [UnityTest]
        public IEnumerator ResizedDockRestoresItsSelectedTabWhenAnotherWindowHadKeyboardFocus()
        {
            RequireGraphics();
            yield return ShowFixtures();
            var scene = EditorWindow.GetWindow<SceneView>();
            docked = EditorWindow.GetWindow<ScreenshotDockWindow>("MCP resized dock fixture", false, typeof(SceneView));
            docked.ShowTab();
            yield return null;
            yield return null;
            scene.ShowTab();
            yield return null;
            var parentField = typeof(EditorWindow).GetField("m_Parent", BindingFlags.Instance | BindingFlags.NonPublic);
            var host = parentField.GetValue(scene);
            Assert.That(parentField.GetValue(docked), Is.SameAs(host));
            var positionProperty = host.GetType().GetProperty("position", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Rect original = (Rect)positionProperty.GetValue(host);
            try
            {
                positionProperty.SetValue(host, new Rect(original.x, original.y, original.width + 73, original.height + 41));
                scene.Repaint();
                yield return null;
                yield return null;
                Assert.That(docked.position, Is.Not.EqualTo(scene.position), "Background-tab rectangle must be stale to exercise the regression.");
                first.Focus();
                var task = Screenshot(docked);
                yield return Await(task);
                Assert.That(task.Result, Is.TypeOf<SuccessResponse>());
                Assert.That(scene.hasFocus, Is.True, "The previous selected tab must be restored independently of keyboard focus.");
                Assert.That(EditorWindow.focusedWindow, Is.SameAs(first));
            }
            finally { if (host != null) positionProperty.SetValue(host, original); }
        }

        [UnityTest]
        public IEnumerator FileOnlyCaptureIsUniqueAndInsideLibrary()
        {
            RequireGraphics();
            yield return ShowFixtures();
            var firstTask = ManageEditorWindows.HandleCommand(new JObject {
                ["action"] = "screenshot", ["window_id"] = second.GetInstanceID(),
                ["include_image"] = false, ["save_file"] = true
            });
            yield return Await(firstTask);
            var firstData = (JObject)((SuccessResponse)firstTask.Result).Data;
            string firstPath = (string)firstData["path"];
            string secondPath = null;
            try
            {
                Assert.That(firstPath, Does.StartWith(Path.GetFullPath(Path.Combine(Application.dataPath, "../Library/McpEditorScreenshots"))));
                Assert.That(File.Exists(firstPath), Is.True);
                Assert.That(firstData["imageBase64"], Is.Null);
                var secondTask = ManageEditorWindows.HandleCommand(new JObject {
                    ["action"] = "screenshot", ["window_id"] = second.GetInstanceID(),
                    ["include_image"] = false, ["save_file"] = true
                });
                yield return Await(secondTask);
                secondPath = (string)((JObject)((SuccessResponse)secondTask.Result).Data)["path"];
                Assert.That(secondPath, Is.Not.EqualTo(firstPath));
                Assert.That(File.Exists(firstPath), Is.True);
            }
            finally
            {
                if (File.Exists(firstPath)) File.Delete(firstPath);
                if (secondPath != null && File.Exists(secondPath)) File.Delete(secondPath);
            }
        }

        [UnityTest]
        public IEnumerator CancellationRestoresFocusAndAllowsTheNextCapture()
        {
            RequireGraphics();
            yield return ShowFixtures();
            var cancelled = Screenshot(second);
            var concurrent = Screenshot(second);
            Assert.That(concurrent.Result, Is.TypeOf<ErrorResponse>());
            ManageEditorWindows.CancelPendingCapture();
            Assert.That(cancelled.IsCompleted, Is.True);
            Assert.That(((ErrorResponse)cancelled.Result).Error, Does.Contain("interrupted"));
            Assert.That(EditorWindow.focusedWindow, Is.SameAs(first));
            var retry = Screenshot(second);
            yield return Await(retry);
            Assert.That(retry.Result, Is.TypeOf<SuccessResponse>());
        }

        [UnityTest]
        public IEnumerator ClosingTargetCompletesWithErrorAndReleasesTheCaptureGate()
        {
            RequireGraphics();
            yield return ShowFixtures();
            var task = Screenshot(second);
            second.Close();
            yield return Await(task);
            Assert.That(((ErrorResponse)task.Result).Error, Does.Contain("closed"));
            var retry = Screenshot(first);
            yield return Await(retry);
            Assert.That(retry.Result, Is.TypeOf<SuccessResponse>());
        }

        [UnityTest]
        public IEnumerator InactiveDockedTabIsListedCapturedAndRestored()
        {
            RequireGraphics();
            var scene = EditorWindow.GetWindow<SceneView>();
            docked = EditorWindow.GetWindow<ScreenshotDockWindow>("MCP dock fixture", false, typeof(SceneView));
            scene.ShowTab();
            scene.Focus();
            yield return null;
            if (!docked.docked) Assert.Fail("Fixture was not docked; tab restoration was not exercised.");
            Assert.That(docked.hasFocus, Is.False);
            var listing = JObject.FromObject(((SuccessResponse)ManageEditorWindows.HandleCommand(new JObject { ["action"] = "list" }).Result).Data);
            Assert.That(listing["windows"].ToString(), Does.Contain("MCP dock fixture"));
            var noFocus = ManageEditorWindows.HandleCommand(new JObject {
                ["action"] = "screenshot", ["window_id"] = docked.GetInstanceID(), ["focus"] = false
            });
            Assert.That(noFocus.Result, Is.TypeOf<ErrorResponse>());
            var task = Screenshot(docked);
            yield return Await(task);
            Assert.That(task.Result, Is.TypeOf<SuccessResponse>());
            Assert.That(scene.hasFocus, Is.True);
            Assert.That(EditorWindow.focusedWindow, Is.SameAs(scene));
        }

        [UnityTest]
        public IEnumerator CaptureDoesNotOverrideUserFocusChanges()
        {
            RequireGraphics();
            yield return ShowFixtures();
            var task = Screenshot(second);
            var third = ScriptableObject.CreateInstance<ScreenshotColorWindow>();
            try
            {
                third.Show();
                third.Focus();
                yield return Await(task);
                Assert.That(task.Result, Is.TypeOf<SuccessResponse>());
                Assert.That(EditorWindow.focusedWindow, Is.SameAs(third));
            }
            finally { third.Close(); }
        }

        [UnityTest]
        public IEnumerator DownscaledDarkPixelsMatchTheFullSizeCapture()
        {
            RequireGraphics();
            var dark = ScriptableObject.CreateInstance<ScreenshotDarkWindow>();
            dark.position = new Rect(450, 100, 320, 240);
            dark.Show();
            var full = new Texture2D(2, 2);
            var small = new Texture2D(2, 2);
            try
            {
                yield return null;
                var fullTask = Screenshot(dark, 4096);
                yield return Await(fullTask);
                var smallTask = Screenshot(dark, 64);
                yield return Await(smallTask);
                Assert.That(fullTask.Result, Is.TypeOf<SuccessResponse>());
                Assert.That(smallTask.Result, Is.TypeOf<SuccessResponse>());
                Assert.That(full.LoadImage(Convert.FromBase64String((string)((JObject)((SuccessResponse)fullTask.Result).Data)["imageBase64"])), Is.True);
                Assert.That(small.LoadImage(Convert.FromBase64String((string)((JObject)((SuccessResponse)smallTask.Result).Data)["imageBase64"])), Is.True);
                Assert.That(Mathf.Max(small.width, small.height), Is.EqualTo(64));
                Color before = full.GetPixel(full.width / 2, full.height / 2);
                Color after = small.GetPixel(small.width / 2, small.height / 2);
                Assert.That(before.r, Is.GreaterThan(0.02f));
                Assert.That(after.r, Is.EqualTo(before.r).Within(0.02f));
                Assert.That(after.g, Is.EqualTo(before.g).Within(0.02f));
                Assert.That(after.b, Is.EqualTo(before.b).Within(0.02f));
            }
            finally
            {
                dark.Close();
                Object.DestroyImmediate(full);
                Object.DestroyImmediate(small);
            }
        }

        [UnityTest]
        public IEnumerator GameViewBufferCaptureInPlayModeDoesNotStepOrPauseThePlayer()
        {
            RequireGraphics();
            yield return new EnterPlayMode();
            var gameType = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView", true);
            var game = EditorWindow.GetWindow(gameType);
            game.ShowTab();
            yield return null;
            int before = Time.frameCount;
            for (int i = 0; i < 3; i++)
            {
                var task = Screenshot(game, 512);
                yield return Await(task);
                Assert.That(task.Result, Is.TypeOf<SuccessResponse>());
                Assert.That(EditorApplication.isPlaying, Is.True);
                Assert.That(EditorApplication.isPaused, Is.False);
            }
            Assert.That(Time.frameCount, Is.GreaterThan(before));
            LogAssert.NoUnexpectedReceived();
            yield return new ExitPlayMode();
        }

        [UnityTest]
        public IEnumerator AssemblyReloadCancelsThePendingCaptureAndAllowsRetry()
        {
            RequireGraphics();
            yield return ShowFixtures();
            var task = Screenshot(second);
            const string resultKey = "MCP.ScreenshotTests.ReloadResult";
            const string focusKey = "MCP.ScreenshotTests.ReloadFocus";
            SessionState.EraseString(resultKey);
            SessionState.EraseBool(focusKey);
            void ObserveCancellation()
            {
                SessionState.SetString(resultKey, task.IsCompleted && task.Result is ErrorResponse error ? error.Error : "not cancelled");
                SessionState.SetBool(focusKey, EditorWindow.focusedWindow == first);
            }
            // Production's callback was subscribed first. Observe its result
            // before domain state is discarded, then verify persisted evidence.
            AssemblyReloadEvents.beforeAssemblyReload += ObserveCancellation;
            EditorUtility.RequestScriptReload();
            yield return new WaitForDomainReload();
            Assert.That(SessionState.GetString(resultKey, "missing"), Does.Contain("interrupted"));
            Assert.That(SessionState.GetBool(focusKey, false), Is.True);
            SessionState.EraseString(resultKey);
            SessionState.EraseBool(focusKey);
            var target = EditorWindow.GetWindow<SceneView>();
            var retry = Screenshot(target);
            yield return Await(retry);
            Assert.That(retry.Result, Is.TypeOf<SuccessResponse>());
        }

        [UnityTest]
        public IEnumerator MinimizedWindowCompletesAndSubsequentCaptureRecovers()
        {
            RequireGraphics();
            string title = "MCP minimize fixture " + Guid.NewGuid().ToString("N");
            second.titleContent = new GUIContent(title);
            yield return ShowFixtures();
#if UNITY_EDITOR_WIN
                // Minimize only this uniquely named fixture, after verifying its
                // process ID. Confirm the native minimized state on every version.
                IntPtr handle = IntPtr.Zero;
                uint thisProcess = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
                EnumWindows((candidate, _) => {
                    GetWindowThreadProcessId(candidate, out uint candidateOwner);
                    if (candidateOwner != thisProcess) return true;
                    var caption = new System.Text.StringBuilder(512);
                    GetWindowText(candidate, caption, caption.Capacity);
                    if (!caption.ToString().Contains(title)) return true;
                    handle = candidate;
                    return false;
                }, IntPtr.Zero);
                Assert.That(handle, Is.Not.EqualTo(IntPtr.Zero), "Fixture native window was not found.");
                GetWindowThreadProcessId(handle, out uint owner);
                Assert.That(owner, Is.EqualTo((uint)System.Diagnostics.Process.GetCurrentProcess().Id));
                ShowWindow(handle, 6);
                Assert.That(IsIconic(handle), Is.True, "Fixture was not minimized.");
#else
                var host = typeof(EditorWindow).GetField("m_Parent", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(second);
                var container = host?.GetType().GetProperty("window", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(host);
                var minimize = container?.GetType().GetMethod("Minimize", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (minimize == null) Assert.Ignore("This Editor has no available minimize API on this platform.");
                minimize.Invoke(container, null);
#endif
            first.Focus();
            yield return null;
            var task = ManageEditorWindows.HandleCommand(new JObject {
                ["action"] = "screenshot", ["window_id"] = second.GetInstanceID(), ["focus"] = false
            });
            yield return Await(task);
            Assert.That(task.Result, Is.AssignableTo<IMcpResponse>());
            second.Close();
            var retry = Screenshot(first);
            yield return Await(retry);
            Assert.That(retry.Result, Is.TypeOf<SuccessResponse>());
        }

#if UNITY_EDITOR_WIN
        private delegate bool WindowCallback(IntPtr handle, IntPtr state);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool EnumWindows(WindowCallback callback, IntPtr state);
        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr handle, System.Text.StringBuilder text, int length);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr handle, out uint processId);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr handle, int command);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr handle);
#endif

        private IEnumerator ShowFixtures()
        {
            first.position = new Rect(100, 100, 320, 240);
            second.position = new Rect(450, 100, 320, 240);
            first.ShowUtility();
            second.ShowUtility();
            Assert.That(first.docked, Is.False);
            Assert.That(second.docked, Is.False);
            // Let both native utility-window creations finish before focusing.
            // A fixed two-tick wait after Focus can leave delayed activation queued.
            yield return null;
            yield return null;
            first.Focus();
            double deadline = EditorApplication.timeSinceStartup + 5;
            while (EditorWindow.focusedWindow != first && EditorApplication.timeSinceStartup < deadline)
                yield return null;
            Assert.That(EditorWindow.focusedWindow, Is.SameAs(first),
                "The fixture must establish keyboard focus before requesting capture.");
        }

        private static void AssertCornerColors(Texture2D image)
        {
            // Coordinates are actual output pixels, not a percentage of the buffer.
            // The fixture paints from content (0, 0) to position.size: a tab strip,
            // host border or vertical flip must fail even on a large docked pane.
            TestContext.CurrentContext.Test.Properties.Set("capture_graphics_api", SystemInfo.graphicsDeviceType.ToString());
            TestContext.CurrentContext.Test.Properties.Set("capture_uv_starts_at_top", SystemInfo.graphicsUVStartsAtTop);
            const int inset = 2;
            AssertPixel(image, inset, image.height - 1 - inset, Color.blue, "top left content edge");
            AssertPixel(image, image.width - 1 - inset, image.height - 1 - inset, Color.green, "top right content edge");
            AssertPixel(image, inset, inset, Color.red, "bottom left content edge");
            AssertPixel(image, image.width - 1 - inset, inset, new Color(1, 1, 0, 1), "bottom right content edge");
        }

        private static void AssertContentEdgeMarkers(Texture2D image)
        {
            AssertCornerColors(image);
            // Four physical pixels of colored edge surround a black interior.
            // Also sample just inside it: a shifted/cropped rectangle cannot pass
            // merely because a large quadrant still has the expected color.
            const int inner = 6;
            AssertPixel(image, inner, image.height - 1 - inner, Color.black, "top left interior");
            AssertPixel(image, image.width - 1 - inner, image.height - 1 - inner, Color.black, "top right interior");
            AssertPixel(image, inner, inner, Color.black, "bottom left interior");
            AssertPixel(image, image.width - 1 - inner, inner, Color.black, "bottom right interior");
        }

        private static void AssertPixel(Texture2D image, int x, int y, Color expected, string edge)
        {
            Color actual = image.GetPixel(x, y);
            AssertColor(actual, expected, $"{edge} at ({x}, {y}) in {image.width}x{image.height}; "
                + $"actual={actual}, expected={expected}, graphics={SystemInfo.graphicsDeviceType}, "
                + $"uv_top={SystemInfo.graphicsUVStartsAtTop}");
        }

        private static void AssertColor(Color actual, Color expected, string corner)
        {
            Assert.That(actual.r, Is.EqualTo(expected.r).Within(0.04f), corner + " red");
            Assert.That(actual.g, Is.EqualTo(expected.g).Within(0.04f), corner + " green");
            Assert.That(actual.b, Is.EqualTo(expected.b).Within(0.04f), corner + " blue");
        }

        private static void RequireGraphics()
        {
            if (Application.isBatchMode || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                Assert.Ignore("Pixel capture requires a graphical Editor; run this fixture without -batchmode/-nographics.");
        }

        private static Task<object> Screenshot(EditorWindow window, int resolution = 1600) =>
            ManageEditorWindows.HandleCommand(new JObject { ["action"] = "screenshot", ["window_id"] = window.GetInstanceID(), ["max_resolution"] = resolution });

        private static IEnumerator Await(Task<object> task)
        {
            double deadline = EditorApplication.timeSinceStartup + 10;
            while (!task.IsCompleted && EditorApplication.timeSinceStartup < deadline) yield return null;
            Assert.That(task.IsCompleted, Is.True, "Capture did not complete before the test deadline.");
            Assert.That(task.IsFaulted, Is.False, task.Exception?.ToString());
        }

        private EditorWindow Resolve(ManageEditorWindows.Parameters p, out string error) =>
            ManageEditorWindows.Resolve(new EditorWindow[] { first, second }, p, out error);
    }
}
