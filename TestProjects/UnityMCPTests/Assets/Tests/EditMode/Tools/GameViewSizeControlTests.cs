using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Tools;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace MCPForUnity.Tests.EditMode.Tools
{
    public class GameViewSizeValidationTests
    {
        [TestCase("null")]
        [TestCase("0")]
        [TestCase("9")]
        [TestCase("8193")]
        [TestCase("9223372036854775808")]
        [TestCase("1080.5")]
        [TestCase("true")]
        [TestCase("\"1080\"")]
        public void InvalidDimensionsFailBeforeEditorAccess(string invalid)
        {
            foreach (string dimension in new[] { "width", "height" })
            {
                var args = new JObject { ["action"] = "set_game_view_size", ["width"] = 2400, ["height"] = 1080 };
                args[dimension] = JToken.Parse(invalid);
                var result = JObject.FromObject(ManageEditor.HandleCommand(args));
                Assert.IsFalse(result.Value<bool>("success"));
                StringAssert.Contains(dimension, result.Value<string>("error"));
            }
        }

        [Test]
        public void MissingDimensionsFailBeforeEditorAccess()
        {
            var result = JObject.FromObject(ManageEditor.HandleCommand(new JObject { ["action"] = "set_game_view_size" }));
            Assert.IsFalse(result.Value<bool>("success"));
            StringAssert.Contains("width", result.Value<string>("error"));
        }

        [Test]
        public void DimensionsOnReadAreRejected()
        {
            var result = JObject.FromObject(ManageEditor.HandleCommand(new JObject { ["action"] = "get_game_view_size", ["width"] = 2400 }));
            Assert.IsFalse(result.Value<bool>("success"));
        }

        [Test]
        public void MissingInternalMemberFailsExplicitly()
        {
            var error = Assert.Throws<MissingMemberException>(() =>
                GameViewSizeControl.GameViewApi.RequireProperty(typeof(EditorWindow), "NoSuchGameViewProperty"));
            StringAssert.Contains("NoSuchGameViewProperty", error.Message);
        }
    }

    /// <summary>
    /// Real internal-API tests. Restore both the in-memory singleton and its exact
    /// preference file, since a disposable project alone does not isolate presets.
    /// </summary>
    public class GameViewSizeControlTests
    {
        private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private GameViewSizeControl.GameViewApi _api;
        private UnityEngine.Object _sizes;
        private string _sizesJson, _filePath;
        private byte[] _fileBytes;
        private readonly Dictionary<string, string> _preferences = new Dictionary<string, string>();
        private readonly Dictionary<EditorWindow, int[]> _selections = new Dictionary<EditorWindow, int[]>();
        private readonly Dictionary<EditorWindow, string> _zoomStates = new Dictionary<EditorWindow, string>();
        private EditorWindow _createdView, _previousFocus;
        private GameObject _camera;
        private Type _sizesType, _viewType;

        [SetUp]
        public void SnapshotPreferences()
        {
            _api = new GameViewSizeControl.GameViewApi();
            var assembly = typeof(EditorWindow).Assembly;
            _sizesType = assembly.GetType("UnityEditor.GameViewSizes", true);
            _viewType = assembly.GetType("UnityEditor.GameView", true);
            _sizes = (UnityEngine.Object)_sizesType.BaseType.GetProperty("instance", BindingFlags.Public | BindingFlags.Static).GetValue(null);
            _sizesJson = EditorJsonUtility.ToJson(_sizes);
            _filePath = (string)_sizesType.BaseType.GetMethod("GetFilePath", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null);
            _fileBytes = File.Exists(_filePath) ? File.ReadAllBytes(_filePath) : null;
            foreach (string group in Enum.GetNames(_api.CurrentGroupType.GetType()))
            {
                string key = GameViewSizeControl.OwnershipKeyPrefix + group;
                _preferences[key] = EditorPrefs.HasKey(key) ? EditorPrefs.GetString(key) : null;
            }
            foreach (var view in Resources.FindObjectsOfTypeAll(_viewType).Cast<EditorWindow>())
            {
                _api.SelectedIndex(view);
                _selections[view] = (int[])((int[])_viewType.GetField("m_SelectedSizes", Instance).GetValue(view)).Clone();
                _zoomStates[view] = JsonUtility.ToJson(_viewType.GetField("m_ZoomArea", Instance).GetValue(view));
            }
            _previousFocus = EditorWindow.focusedWindow;
        }

        [TearDown]
        public void RestorePreferences()
        {
            if (_createdView != null) _createdView.Close();
            if (_camera != null) UnityEngine.Object.DestroyImmediate(_camera);
            if (_sizes != null)
            {
                EditorJsonUtility.FromJsonOverwrite(_sizesJson, _sizes);
                _sizesType.GetMethod("Changed", Instance).Invoke(_sizes, null);
                foreach (var selection in _selections)
                {
                    if (selection.Key != null)
                    {
                        _viewType.GetField("m_SelectedSizes", Instance).SetValue(selection.Key, selection.Value);
                        _api.Select(selection.Key, _api.SelectedIndex(selection.Key));
                        JsonUtility.FromJsonOverwrite(_zoomStates[selection.Key], _viewType.GetField("m_ZoomArea", Instance).GetValue(selection.Key));
                    }
                }
                if (_fileBytes != null) File.WriteAllBytes(_filePath, _fileBytes);
                else if (File.Exists(_filePath)) File.Delete(_filePath);
            }
            foreach (var preference in _preferences)
            {
                if (preference.Value == null) EditorPrefs.DeleteKey(preference.Key);
                else EditorPrefs.SetString(preference.Key, preference.Value);
            }
            _preferences.Clear();
            _selections.Clear();
            _zoomStates.Clear();
            if (_previousFocus != null) _previousFocus.Focus();
        }

        private object Group(object groupType) => _sizesType.GetMethod("GetGroup", Instance).Invoke(_sizes, new[] { groupType });
        private static int Count(object group) => (int)group.GetType().GetMethod("GetTotalCount", Instance).Invoke(group, null);
        private static object Size(object group, int index) => group.GetType().GetMethod("GetGameViewSize", Instance).Invoke(group, new object[] { index });
        private static string SizeJson(object size) => Newtonsoft.Json.JsonConvert.SerializeObject(size);

        [Test]
        public void RepeatedAndChangedDimensionsReuseOnePresetAndPreserveOtherPresets()
        {
            object groupType = _api.CurrentGroupType;
            object group = Group(groupType);
            EditorPrefs.DeleteKey(GameViewSizeControl.OwnershipKeyPrefix + groupType);
            int before = Count(group);
            string[] originals = Enumerable.Range(0, before).Select(i => SizeJson(Size(group, i))).ToArray();
            int index = _api.SetOwnedPreset(groupType, 2400, 1080);
            Assert.AreEqual(before, index);
            Assert.AreEqual(before + 1, Count(group));
            Assert.AreEqual(index, _api.SetOwnedPreset(groupType, 2400, 1080));
            Assert.AreEqual(index, _api.SetOwnedPreset(groupType, 1080, 2400));
            Assert.AreEqual(before + 1, Count(group));
            CollectionAssert.AreEqual(originals, Enumerable.Range(0, before).Select(i => SizeJson(Size(group, i))).ToArray());
            var size = Size(group, index);
            Assert.AreEqual(1080, size.GetType().GetProperty("width").GetValue(size));
            StringAssert.StartsWith("MCP 1080x2400 [", (string)size.GetType().GetProperty("baseText").GetValue(size));
        }

        [Test]
        public void UserEditedOwnedPresetIsPreserved()
        {
            object groupType = _api.CurrentGroupType;
            object group = Group(groupType);
            int index = _api.SetOwnedPreset(groupType, 2400, 1080);
            object size = Size(group, index);
            size.GetType().GetProperty("baseText").SetValue(size, "MCP user preset");
            string edited = SizeJson(size);
            int count = Count(group);
            int replacement = _api.SetOwnedPreset(groupType, 1080, 2400);
            Assert.AreEqual(count, replacement);
            Assert.AreEqual(count + 1, Count(group));
            Assert.AreEqual(edited, SizeJson(Size(group, index)));
        }

        [Test]
        public void PlatformGroupsHaveIndependentOwnership()
        {
            var kind = _api.CurrentGroupType.GetType();
            var standalone = Enum.Parse(kind, "Standalone");
            var android = Enum.Parse(kind, "Android");
            int first = _api.SetOwnedPreset(standalone, 2400, 1080);
            string standaloneState = SizeJson(Size(Group(standalone), first));
            _api.SetOwnedPreset(android, 1080, 2400);
            Assert.AreEqual(standaloneState, SizeJson(Size(Group(standalone), first)));
            Assert.IsTrue(EditorPrefs.HasKey(GameViewSizeControl.OwnershipKeyPrefix + "Standalone"));
            Assert.IsTrue(EditorPrefs.HasKey(GameViewSizeControl.OwnershipKeyPrefix + "Android"));
        }

        [Test]
        public void CorruptOwnershipDoesNotAuthorizeOverwriting()
        {
            object groupType = _api.CurrentGroupType;
            object group = Group(groupType);
            EditorPrefs.SetString(GameViewSizeControl.OwnershipKeyPrefix + groupType, "not-json");
            int before = Count(group);
            string[] originals = Enumerable.Range(0, before).Select(i => SizeJson(Size(group, i))).ToArray();
            _api.SetOwnedPreset(groupType, 2400, 1080);
            CollectionAssert.AreEqual(originals, Enumerable.Range(0, before).Select(i => SizeJson(Size(group, i))).ToArray());
        }

        [UnityTest]
        public IEnumerator LandscapeThenPortraitReadsActualAllocatedTexture()
        {
            if (Application.isBatchMode) Assert.Ignore("Requires a visible graphical Game View and GPU-backed render texture.");
            _createdView = (EditorWindow)ScriptableObject.CreateInstance(_viewType);
            _createdView.Show();
            _createdView.Focus();
            _camera = new GameObject("MCP Game View size test camera", typeof(Camera));
            yield return null;
            foreach (var dimensions in new[] { new Vector2Int(2400, 1080), new Vector2Int(1080, 2400) })
            {
                _createdView.Focus();
                var task = ManageEditor.HandleCommand(new JObject
                {
                    ["action"] = "set_game_view_size", ["width"] = dimensions.x, ["height"] = dimensions.y
                }) as Task<object>;
                Assert.IsNotNull(task, "Valid set action must wait for render-target readback.");
                while (!task.IsCompleted) yield return null;
                var response = JObject.FromObject(task.Result);
                Assert.IsTrue(response.Value<bool>("success"), response.ToString());
                var texture = (RenderTexture)_viewType.BaseType.GetField("m_TargetTexture", Instance).GetValue(_createdView);
                Assert.IsNotNull(texture);
                Assert.IsTrue(texture.IsCreated());
                Assert.AreEqual(texture.width, (int)response["data"]["render_size"]["width"]);
                Assert.AreEqual(texture.height, (int)response["data"]["render_size"]["height"]);
                Assert.AreEqual(dimensions.x, texture.width, "Fixture GPU should support the requested landscape/portrait dimensions.");
                Assert.AreEqual(dimensions.y, texture.height);
                Assert.IsTrue((bool)response["data"]["settled"]);
                Assert.IsTrue((bool)response["data"]["matches_requested"]);
                Assert.AreSame(_createdView, EditorWindow.focusedWindow, "The operation must not take focus away.");
                var read = JObject.FromObject(ManageEditor.HandleCommand(new JObject { ["action"] = "get_game_view_size" }));
                Assert.AreEqual(texture.width, (int)read["data"]["render_size"]["width"]);
            }
        }

        [UnityTest]
        public IEnumerator ConcurrentPresetEditIsNotReportedAsGpuClamping()
        {
            if (Application.isBatchMode) Assert.Ignore("Requires a graphical Editor window.");
            _createdView = (EditorWindow)ScriptableObject.CreateInstance(_viewType);
            _createdView.Show();
            _createdView.Focus();
            var task = ManageEditor.HandleCommand(new JObject
            {
                ["action"] = "set_game_view_size", ["width"] = 2400, ["height"] = 1080
            }) as Task<object>;
            Assert.IsNotNull(task);
            object size = Size(Group(_api.CurrentGroupType), _api.SelectedIndex(_createdView));
            size.GetType().GetProperty("width").SetValue(size, 1024);
            while (!task.IsCompleted) yield return null;
            var response = JObject.FromObject(task.Result);
            Assert.AreEqual("game_view_size_changed", (string)response["code"]);
            Assert.IsTrue((bool)response["data"]["preset_may_have_changed"]);
            Assert.AreEqual(1024, (int)response["data"]["selected_size"]["width"]);
            Assert.IsFalse(response.Value<bool>("success"));
        }

        [UnityTest]
        public IEnumerator ClosedViewInterruptsReadbackAndReleasesBusyState()
        {
            if (Application.isBatchMode) Assert.Ignore("Requires a graphical Editor window.");
            _createdView = (EditorWindow)ScriptableObject.CreateInstance(_viewType);
            _createdView.Show();
            _createdView.Focus();
            var task = ManageEditor.HandleCommand(new JObject
            {
                ["action"] = "set_game_view_size", ["width"] = 2400, ["height"] = 1080
            }) as Task<object>;
            Assert.IsNotNull(task);
            var busy = JObject.FromObject(ManageEditor.HandleCommand(new JObject
            {
                ["action"] = "set_game_view_size", ["width"] = 1080, ["height"] = 2400
            }));
            Assert.AreEqual("game_view_size_busy", (string)busy["code"]);
            _createdView.Close();
            while (!task.IsCompleted) yield return null;
            var response = JObject.FromObject(task.Result);
            Assert.AreEqual("game_view_size_changed", (string)response["code"]);
            Assert.IsTrue((bool)response["data"]["preset_may_have_changed"]);
        }
    }
}
