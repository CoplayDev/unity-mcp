using System;
using System.IO;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Services;
using MCPForUnity.Editor.Tools;
using static MCPForUnityTests.Editor.TestUtilities;

namespace MCPForUnityTests.Editor.Helpers
{
    /// <summary>
    /// The guard reconciles an open scene changed on disk before an asset refresh, so Unity's modal
    /// "Scene(s) Have Been Modified" prompt has no reason to appear. These pin what it must not do:
    /// treat an unknown mode as "reload" (which replaces the open scene with the disk version), or
    /// act on a script-only refresh that never touches assets.
    /// </summary>
    [TestFixture]
    public class SceneExternalChangeGuardTests
    {
        // RefreshUnity.HandleCommand short-circuits with "tests_running" while a run the MCP bridge started
        // (its run_tests tool) is in progress, so these command tests can't exercise their path there and
        // are reported as ignored rather than passing vacuously. CI and the Test Runner window don't set
        // TestRunStatus, so they run them for real.
        private const string BridgeRunReason =
            "Started through the MCP bridge's run_tests, where RefreshUnity answers tests_running; run from CI or the Test Runner window.";

        [TestCase(null, "auto")]
        [TestCase("", "auto")]
        [TestCase("  ", "auto")]
        [TestCase("auto", "auto")]
        [TestCase("Reload", "reload")]
        [TestCase(" keep_editor ", "keep_editor")]
        public void TryNormalizeMode_AcceptsTheThreeModesAndBlank(string input, string expected)
        {
            Assert.IsTrue(SceneExternalChangeGuard.TryNormalizeMode(input, out string normalized));
            Assert.AreEqual(expected, normalized);
        }

        [TestCase("keep")]
        [TestCase("reload_all")]
        [TestCase("discard")]
        public void TryNormalizeMode_RejectsUnknownValues(string input)
        {
            Assert.IsFalse(SceneExternalChangeGuard.TryNormalizeMode(input, out _));
        }

        [Test]
        public void Reconcile_UnknownMode_ThrowsInsteadOfReloading()
        {
            // Before the fix an unknown value fell through to the reload branch.
            Assert.Throws<ArgumentException>(() => SceneExternalChangeGuard.Reconcile("keep"));
        }

        [Test]
        public void HandleCommand_UnknownSceneMode_IsRejected()
        {
            var task = RefreshUnity.HandleCommand(new JObject
            {
                ["mode"] = "if_dirty",
                ["scope"] = "all",
                ["compile"] = "none",
                ["wait_for_ready"] = false,
                ["on_external_scene_change"] = "keep",
            });

            Assert.IsTrue(task.IsCompleted, "the rejection must come before any refresh or wait");
            var result = ToJObject(task.Result);
            if (TestRunStatus.IsRunning)
            {
                Assert.Ignore(BridgeRunReason);
            }

            Assert.IsFalse(result.Value<bool>("success"), result.ToString());
            Assert.AreEqual("invalid_parameter", result["data"]?["reason"]?.ToString(), result.ToString());
        }

        [Test]
        public void Reconcile_KeepEditor_FailedSave_BlocksAndLeavesTheChangeUnresolved()
        {
            // A failed save (read-only or locked file) leaves the disk copy different. Reporting it as
            // overwritten and refreshing anyway would raise the prompt the guard exists to prevent.
            string path = $"Assets/McpGuardTest_{Guid.NewGuid():N}.unity";
            Scene scratch = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            var realSave = SceneExternalChangeGuard.SaveScene;
            try
            {
                Assert.IsTrue(EditorSceneManager.SaveScene(scratch, path), "sanity: scratch scene saved");
                var edited = File.GetLastWriteTimeUtc(path).AddMinutes(1);
                File.SetLastWriteTimeUtc(path, edited);
                SceneExternalChangeGuard.SaveScene = _ => false;

                var outcome = SceneExternalChangeGuard.Reconcile(SceneExternalChangeGuard.ModeKeepEditor);

                Assert.IsTrue(outcome.Blocked, "a failed save must block the refresh");
                StringAssert.Contains(path, outcome.Error);
                // Two scenes are open, where reload is refused: the advice must say to close the others first.
                StringAssert.Contains("close the other open scenes", outcome.Error);
                CollectionAssert.IsEmpty(outcome.OverwrittenScenes, "nothing was overwritten");
                Assert.AreEqual(edited, File.GetLastWriteTimeUtc(path), "the scene file must be untouched");

                // The baseline must not have advanced past the unresolved edit: the next check still sees it.
                var again = SceneExternalChangeGuard.Reconcile(SceneExternalChangeGuard.ModeKeepEditor);
                Assert.IsTrue(again.Blocked, "the unresolved change must still be detected");
            }
            finally
            {
                SceneExternalChangeGuard.SaveScene = realSave;
                if (scratch.IsValid())
                {
                    EditorSceneManager.CloseScene(scratch, removeScene: true);
                }
                AssetDatabase.DeleteAsset(path);
            }
        }

        [Test]
        public void HandleCommand_ScriptsScope_LeavesAChangedSceneAlone()
        {
            // Two scenes open plus an external edit is what makes "auto" block. A script-only request
            // never refreshes assets, so it can't raise the prompt and must not be blocked by, or
            // write to, a scene it doesn't touch.
            string path = $"Assets/McpGuardTest_{Guid.NewGuid():N}.unity";
            Scene scratch = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                Assert.IsTrue(EditorSceneManager.SaveScene(scratch, path), "sanity: scratch scene saved");
                // The save recorded the baseline; move the file's mtime past it, as an external edit would.
                var edited = File.GetLastWriteTimeUtc(path).AddMinutes(1);
                File.SetLastWriteTimeUtc(path, edited);

                var task = RefreshUnity.HandleCommand(new JObject
                {
                    ["mode"] = "if_dirty",
                    ["scope"] = "scripts",
                    ["compile"] = "none",
                    ["wait_for_ready"] = false,
                    ["on_external_scene_change"] = "auto",
                });
                var result = ToJObject(task.Result);
                if (TestRunStatus.IsRunning)
                {
                    Assert.Ignore(BridgeRunReason);
                }

                Assert.IsTrue(result.Value<bool>("success"),
                    "a script-only refresh must not be blocked by a changed scene: " + result);
                Assert.AreEqual(edited, File.GetLastWriteTimeUtc(path), "the scene file must not be rewritten");
            }
            finally
            {
                if (scratch.IsValid())
                {
                    EditorSceneManager.CloseScene(scratch, removeScene: true);
                }
                AssetDatabase.DeleteAsset(path);
            }
        }
    }
}
