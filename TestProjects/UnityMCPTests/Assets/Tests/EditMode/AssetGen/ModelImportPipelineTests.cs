using MCPForUnity.Editor.Services.AssetGen;
using MCPForUnity.Editor.Services.AssetGen.Import;
using NUnit.Framework;
using UnityEditor;

namespace MCPForUnityTests.Editor.AssetGen
{
    /// <summary>
    /// Validates the import pipeline's guard paths without fabricating model bytes. Real FBX/GLB
    /// import is left to the user's licensed editor (manual verification checklist).
    /// Assumes glTFast is NOT installed in the test project (it is an optional dependency).
    /// </summary>
    public class ModelImportPipelineTests
    {
        private static AssetGenJob Job(string format) => new AssetGenJob
        {
            JobId = "test",
            Kind = "model",
            Provider = "tripo",
            State = AssetGenJobState.Importing,
            Format = format,
            TargetSize = 1f,
        };

        [Test]
        public void Glb_WithoutGltfast_FailsWithActionableMessage()
        {
            AssetGenJob result = ModelImportPipeline.ImportInto(Job("glb"), "Assets/Generated/Models/__fake_nonexistent.glb");
            Assert.AreEqual(AssetGenJobState.Failed, result.State);
            StringAssert.Contains("glTFast", result.Error);
        }

        [Test]
        public void PathOutsideAssets_Fails()
        {
            AssetGenJob result = ModelImportPipeline.ImportInto(Job("fbx"), "/tmp/somewhere/model.fbx");
            Assert.AreEqual(AssetGenJobState.Failed, result.State);
            StringAssert.Contains("Assets", result.Error);
        }

        [Test]
        public void NullPath_Fails()
        {
            AssetGenJob result = ModelImportPipeline.ImportInto(Job("fbx"), null);
            Assert.AreEqual(AssetGenJobState.Failed, result.State);
        }

        [TestCase("generic", ModelImporterAnimationType.Generic)]
        [TestCase("Generic", ModelImporterAnimationType.Generic)]
        [TestCase("humanoid", ModelImporterAnimationType.Human)]
        [TestCase("human", ModelImporterAnimationType.Human)]
        [TestCase(" LEGACY ", ModelImporterAnimationType.Legacy)]
        [TestCase("none", ModelImporterAnimationType.None)]
        [TestCase("", ModelImporterAnimationType.None)]
        [TestCase(null, ModelImporterAnimationType.None)]
        [TestCase("nonsense", ModelImporterAnimationType.None)]
        public void ParseAnimationType_MapsRigMode(string input, ModelImporterAnimationType expected)
        {
            Assert.AreEqual(expected, ModelImportPipeline.ParseAnimationType(input));
        }
    }
}
