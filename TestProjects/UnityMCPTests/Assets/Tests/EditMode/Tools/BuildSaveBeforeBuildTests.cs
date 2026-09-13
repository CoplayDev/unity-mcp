using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using MCPForUnity.Editor.Tools.Build;

namespace MCPForUnityTests.Editor.Tools
{
    /// <summary>
    /// SaveBeforeBuild originally called EditorSceneManager.SaveOpenScenes() unconditionally.
    /// A scene that has never been saved carries an empty path, and handing one to Unity's save
    /// API opens the modal "Save Scene" file panel — the exact block manage_build exists to
    /// avoid (issue #1341). These tests pin the guard so the modal cannot come back.
    /// </summary>
    [TestFixture]
    public class BuildSaveBeforeBuildTests
    {
        [Test]
        public void SaveBeforeBuild_LeavesUntitledSceneUnsaved()
        {
            // In batchmode the active scene is already untitled; in the interactive Test Runner
            // it usually is not, so fall back to an additive scratch scene there.
            Scene target = SceneManager.GetActiveScene();
            bool createdAdditive = false;
            if (!string.IsNullOrEmpty(target.path))
            {
                target = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                createdAdditive = true;
            }

            try
            {
                Assert.IsTrue(string.IsNullOrEmpty(target.path),
                    "sanity: the target scene must have never been saved");

                EditorSceneManager.MarkSceneDirty(target);
                Assert.IsTrue(target.isDirty, "sanity: scene must be dirty to reach the save path");

                // Before the guard this reached SaveOpenScenes(), which opens a modal file panel
                // for a pathless scene.
                Assert.DoesNotThrow(() => BuildRunner.SaveBeforeBuild());

                // The sharp assertion: the scene was skipped, not saved. A saved scene would have
                // been given a path and would no longer be dirty.
                Assert.IsTrue(string.IsNullOrEmpty(target.path),
                    "an untitled scene must not be written anywhere");
                Assert.IsTrue(target.isDirty,
                    "an untitled scene must be left dirty — saving it is what opens the modal");
            }
            finally
            {
                if (createdAdditive)
                {
                    EditorSceneManager.CloseScene(target, removeScene: true);
                }
            }
        }

        [Test]
        public void SaveBeforeBuild_WithNoDirtyScenes_IsANoOp()
        {
            Assert.DoesNotThrow(() => BuildRunner.SaveBeforeBuild());
        }
    }
}
