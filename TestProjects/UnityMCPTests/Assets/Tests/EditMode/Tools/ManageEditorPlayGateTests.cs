using NUnit.Framework;
using Newtonsoft.Json.Linq;
using MCPForUnity.Editor.Constants;
using MCPForUnity.Editor.Tools;
using UnityEditor;

namespace MCPForUnity.Editor.Tests.EditMode.Tools
{
    /// <summary>
    /// harden/security (R6): entering play mode is opt-in. Verifies the negative case
    /// (default pref => blocked) without actually entering play mode. The positive case
    /// (pref true => enters play) is intentionally not automated here because flipping
    /// into play mode during an EditMode run is disruptive; it is covered by manual smoke.
    /// </summary>
    [TestFixture]
    public class ManageEditorPlayGateTests
    {
        private static JObject ToJO(object o) => JObject.FromObject(o);

        private bool _hadKey;
        private bool _origValue;

        [SetUp]
        public void SetUp()
        {
            _hadKey = EditorPrefs.HasKey(EditorPrefKeys.AllowPlayMode);
            _origValue = _hadKey && EditorPrefs.GetBool(EditorPrefKeys.AllowPlayMode, false);
        }

        [TearDown]
        public void TearDown()
        {
            if (_hadKey) EditorPrefs.SetBool(EditorPrefKeys.AllowPlayMode, _origValue);
            else EditorPrefs.DeleteKey(EditorPrefKeys.AllowPlayMode);
        }

        [Test]
        public void Play_Blocked_WhenAllowPlayModeDisabled()
        {
            // Default posture: pref absent/false => entering play mode is refused.
            EditorPrefs.DeleteKey(EditorPrefKeys.AllowPlayMode);

            var result = ManageEditor.HandleCommand(new JObject { ["action"] = "play" });
            var jo = ToJO(result);

            Assert.IsFalse((bool)jo["success"], "Play must be blocked when AllowPlayMode is false.");
            StringAssert.Contains("disabled", (jo["error"]?.ToString() ?? "").ToLower(),
                "Error should explain play mode is disabled.");
            Assert.IsFalse(EditorApplication.isPlaying, "Editor must not have entered play mode.");
        }
    }
}
