using AssetStoreTools.Validator.Data;
using AssetStoreTools.Validator.Services.Validation;
using AssetStoreTools.Validator.TestDefinitions;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace AssetStoreTools.Validator.TestMethods
{
    internal class CheckModelOrientation : ITestScript
    {
        private GenericTestConfig _config;
        private IAssetUtilityService _assetUtility;
        private IMeshUtilityService _meshUtility;

        public CheckModelOrientation(GenericTestConfig config, IAssetUtilityService assetUtility, IMeshUtilityService meshUtility)
        {
            _config = config;
            _assetUtility = assetUtility;
            _meshUtility = meshUtility;
        }

        public TestResult Run()
        {
            var result = new TestResult() { Status = TestResultStatus.Undefined };

            var models = _assetUtility.GetObjectsFromAssets<GameObject>(_config.ValidationPaths, AssetType.Model);
            var badModels = new List<GameObject>();

            foreach (var m in models)
            {
                var meshes = _meshUtility.GetCustomMeshesInObject(m);
                var assetImporter = _assetUtility.GetAssetImporter(m);

                if (!(assetImporter is UnityEditor.ModelImporter modelImporter))
                    continue;

                var clips = modelImporter.clipAnimations.Length;

                // Only check if the model has meshes and no clips
                if (!meshes.Any() || clips != 0)
                    continue;

                Transform[] transforms = m.GetComponentsInChildren<Transform>(true);

                foreach (var t in transforms)
                {
                    var hasMeshComponent = t.TryGetComponent<MeshFilter>(out _) || t.TryGetComponent<SkinnedMeshRenderer>(out _);

                    if (t.localRotation == Quaternion.identity || !hasMeshComponent)
                        continue;

                    badModels.Add(m);
                    break;
                }
            }

            if (badModels.Count == 0)
            {
                result.Status = TestResultStatus.Pass;
                result.AddMessage("All found models are facing the right way!");
                return result;
            }

            result.Status = TestResultStatus.VariableSeverityIssue;
            result.AddMessage("The following models have incorrect rotation", null, badModels.ToArray());

            return result;
        }
    }
}
