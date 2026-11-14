using System.Collections.Generic;
using System.IO;
using MCPForUnity.Editor.Services;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MCPForUnityTests.Editor.Services
{
    /// <summary>
    /// Batch-mode test runs replace a dirty Untitled scene; only a scene with nothing but the
    /// default camera/light may be dropped, anything else is first saved as a copy.
    /// </summary>
    public class UntitledSceneBackupTests
    {
        private readonly List<GameObject> _created = new();
        private string _backup;

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _created) if (go != null) Object.DestroyImmediate(go);
            _created.Clear();
            if (_backup != null && File.Exists(_backup)) File.Delete(_backup);
        }

        private GameObject Make(string name, params System.Type[] components)
        {
            var go = new GameObject(name, components);
            _created.Add(go);
            return go;
        }

        private (GameObject camera, GameObject light) Defaults() =>
            (Make("Main Camera", typeof(Camera), typeof(AudioListener)), Make("Directional Light", typeof(Light)));

        [Test]
        public void DefaultCameraAndLight_AreDisposable()
        {
            var (camera, light) = Defaults();
            Assert.IsTrue(TestRunnerService.IsDisposable(new[] { camera, light }));
            Assert.IsTrue(TestRunnerService.IsDisposable(new GameObject[0]), "an empty scene is disposable");
        }

        [Test]
        public void AnyOtherObject_IsContent()
        {
            var (camera, light) = Defaults();
            var cube = Make("Cube", typeof(MeshFilter));
            Assert.IsFalse(TestRunnerService.IsDisposable(new[] { camera, light, cube }));
            Assert.IsFalse(TestRunnerService.IsDisposable(new[] { Make("Main Camera", typeof(Camera), typeof(BoxCollider)) }),
                "a default-named object with extra components is content");
        }

        [Test]
        public void ChildUnderTheDefaultCamera_IsContent()
        {
            var (camera, light) = Defaults();
            Make("Rig").transform.SetParent(camera.transform);
            Assert.IsFalse(TestRunnerService.IsDisposable(new[] { camera, light }));
        }

        [Test]
        public void Backup_SavesACopyAndKeepsTheSceneUntitled()
        {
            Scene scene = SceneManager.GetActiveScene();
            Assume.That(string.IsNullOrEmpty(scene.path), "needs the test runner's untitled scene");
            Make("UntitledBackupProbe_Cube", typeof(MeshFilter));

            _backup = TestRunnerService.BackupUntitledScene(scene);

            Assert.IsNotNull(_backup);
            StringAssert.StartsWith("Temp/MCPForUnity/UntitledBackup-", _backup);
            StringAssert.Contains("UntitledBackupProbe_Cube", File.ReadAllText(_backup));
            Assert.IsTrue(string.IsNullOrEmpty(SceneManager.GetActiveScene().path), "saved as a copy: the open scene stays untitled");
        }
    }
}
