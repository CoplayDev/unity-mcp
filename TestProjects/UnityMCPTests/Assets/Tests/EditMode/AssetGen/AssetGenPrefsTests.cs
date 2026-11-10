using MCPForUnity.Editor.Constants;
using MCPForUnity.Editor.Helpers;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace MCPForUnityTests.Editor.AssetGen
{
    /// <summary>
    /// Phase 0 scaffold: non-secret AssetGen preferences round-trip through EditorPrefs
    /// with sensible defaults. (API keys are NOT covered here — they live in the secure store.)
    /// </summary>
    public class AssetGenPrefsTests
    {
        private static readonly string[] Keys =
        {
            EditorPrefKeys.AssetGenSelectedModelProvider,
            EditorPrefKeys.AssetGenSelectedImageProvider,
            EditorPrefKeys.AssetGenSelectedAudioProvider,
            EditorPrefKeys.AssetGenSelectedModelPrefix + "image.fal",
            EditorPrefKeys.AssetGenSelectedModelPrefix + "model.tripo",
            EditorPrefKeys.AssetGenSelectedModelPrefix + "model.meshy",
            EditorPrefKeys.AssetGenSelectedModelPrefix + "audio.fal",
            EditorPrefKeys.AssetGenDefaultFormat,
            EditorPrefKeys.AssetGenOutputRoot,
            EditorPrefKeys.AssetGenAutoNormalize,
            EditorPrefKeys.AssetGenProviderEnabledPrefix + "tripo",
        };

        private void Clear()
        {
            foreach (string k in Keys) EditorPrefs.DeleteKey(k);
        }

        [SetUp]
        public void SetUp() => Clear();

        [TearDown]
        public void TearDown() => Clear();

        [Test]
        public void Defaults_AreSensible_WhenUnset()
        {
            Assert.AreEqual("tripo", AssetGenPrefs.ModelProvider);
            Assert.AreEqual("fal", AssetGenPrefs.ImageProvider);
            Assert.AreEqual("Assets/Generated", AssetGenPrefs.OutputRoot);
            Assert.IsTrue(AssetGenPrefs.AutoNormalize);
            Assert.IsFalse(AssetGenPrefs.IsProviderEnabled("tripo"));
        }

        [Test]
        public void RoundTrip_PersistsValues()
        {
            AssetGenPrefs.ModelProvider = "meshy";
            AssetGenPrefs.OutputRoot = "Assets/Foo";
            AssetGenPrefs.AutoNormalize = false;
            AssetGenPrefs.SetProviderEnabled("tripo", true);

            Assert.AreEqual("meshy", AssetGenPrefs.ModelProvider);
            Assert.AreEqual("Assets/Foo", AssetGenPrefs.OutputRoot);
            Assert.IsFalse(AssetGenPrefs.AutoNormalize);
            Assert.IsTrue(AssetGenPrefs.IsProviderEnabled("tripo"));
        }

        [Test]
        public void Defaults_ModelSelectionsEmpty_WhenUnset()
        {
            Assert.AreEqual(string.Empty, AssetGenPrefs.GetSelectedModel("image", "fal"));
            Assert.AreEqual(string.Empty, AssetGenPrefs.GetSelectedModel("model", "tripo"));
            Assert.AreEqual(string.Empty, AssetGenPrefs.GetSelectedModel("audio", "fal"));
            Assert.AreEqual("fal", AssetGenPrefs.AudioProvider);
        }

        [Test]
        public void SelectedModels_RoundTrip_PerProvider_AndClearOnEmpty()
        {
            AssetGenPrefs.SetSelectedModel("model", "tripo", "P1-20260311");
            AssetGenPrefs.SetSelectedModel("model", "meshy", "meshy-6");
            AssetGenPrefs.SetSelectedModel("audio", "fal", "cassetteai/sound-effects-generator");

            // Per-provider: Tripo and Meshy selections are independent, no clobbering.
            Assert.AreEqual("P1-20260311", AssetGenPrefs.GetSelectedModel("model", "tripo"));
            Assert.AreEqual("meshy-6", AssetGenPrefs.GetSelectedModel("model", "meshy"));
            Assert.AreEqual("cassetteai/sound-effects-generator", AssetGenPrefs.GetSelectedModel("audio", "fal"));

            // Setting empty deletes the key (SetOrDelete) -> back to the empty default.
            AssetGenPrefs.SetSelectedModel("audio", "fal", "");
            Assert.AreEqual(string.Empty, AssetGenPrefs.GetSelectedModel("audio", "fal"));
        }

        [Test]
        public void OutputRoot_FallsBackToDefault_WhenClearedToEmpty()
        {
            AssetGenPrefs.OutputRoot = "Assets/Foo";
            AssetGenPrefs.OutputRoot = "";
            Assert.AreEqual("Assets/Generated", AssetGenPrefs.OutputRoot);
        }

        [Test]
        public void AssetPathNormalization_RejectsTraversalOutsideAssets()
        {
            Assert.IsFalse(AssetGenPaths.TryGetAssetsRelativePath("Assets/../ProjectSettings", out _));
        }

        [Test]
        public void AssetPathNormalization_AcceptsAbsolutePathInsideAssets()
        {
            string abs = System.IO.Path.Combine(Application.dataPath, "Refs/ref.png");

            Assert.IsTrue(AssetGenPaths.TryGetAssetsRelativePath(abs, out string rel));
            Assert.AreEqual("Assets/Refs/ref.png", rel);
        }
    }
}
