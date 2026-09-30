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
        protected void OnGUI()
        {
            EditorGUI.DrawRect(new Rect(0, 0, position.width, position.height / 2), Color.blue);
            EditorGUI.DrawRect(new Rect(0, position.height / 2, position.width, position.height / 2), Color.red);
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
                Assert.That(image.GetPixel(image.width / 2, image.height * 3 / 4).b, Is.GreaterThan(0.9));
                Assert.That(image.GetPixel(image.width / 2, image.height / 4).r, Is.GreaterThan(0.9));
            }
            finally { Object.DestroyImmediate(image); }
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
            Debug.Log("[EditorWindowScreenshots] Unsupported minimized capture: " +
                (task.Result is ErrorResponse error ? error.Error : "buffer returned; freshness is not guaranteed"));
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
            first.Focus();
            yield return null;
            yield return null;
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
