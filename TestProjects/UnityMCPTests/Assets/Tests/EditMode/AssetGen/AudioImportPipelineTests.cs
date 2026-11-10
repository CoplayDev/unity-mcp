using MCPForUnity.Editor.Services.AssetGen;
using MCPForUnity.Editor.Services.AssetGen.Import;
using NUnit.Framework;

namespace MCPForUnityTests.Editor.AssetGen
{
    /// <summary>
    /// Validates the audio import pipeline's guard paths without fabricating audio bytes. Real
    /// AudioClip import + AudioImporter settings are left to the licensed editor (manual checklist /
    /// live smoke test), matching ModelImportPipelineTests' guard-only philosophy.
    /// </summary>
    public class AudioImportPipelineTests
    {
        private static AssetGenJob Job() => new AssetGenJob
        {
            JobId = "test",
            Kind = "audio",
            Provider = "fal",
            State = AssetGenJobState.Importing,
            Format = "wav",
        };

        [Test]
        public void ImportInto_NullPath_Fails()
        {
            AssetGenJob result = AudioImportPipeline.ImportInto(Job(), null);
            Assert.AreEqual(AssetGenJobState.Failed, result.State);
        }

        [Test]
        public void ImportInto_PathOutsideAssets_Fails()
        {
            AssetGenJob result = AudioImportPipeline.ImportInto(Job(), "/tmp/somewhere/sound.wav");
            Assert.AreEqual(AssetGenJobState.Failed, result.State);
            StringAssert.Contains("Assets", result.Error);
        }

        [Test]
        public void ImportInto_NonAudioExtension_UnderAssets_Rejected()
        {
            // Defense-in-depth: even a path under Assets is refused if it isn't an audio type, so a
            // .cs payload can never be handed to AssetDatabase.ImportAsset.
            AssetGenJob result = AudioImportPipeline.ImportInto(Job(), "Assets/Generated/payload.cs");
            Assert.AreEqual(AssetGenJobState.Failed, result.State);
            StringAssert.Contains("non-audio", result.Error.ToLowerInvariant());
        }
    }
}
