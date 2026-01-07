using AssetStoreTools.Validator.Data;
using AssetStoreTools.Validator.Services.Validation;
using AssetStoreTools.Validator.TestDefinitions;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace AssetStoreTools.Validator.TestMethods
{
    internal class CheckColliders : ITestScript
    {
        private GenericTestConfig _config;
        private IAssetUtilityService _assetUtility;
        private IMeshUtilityService _meshUtility;

        public CheckColliders(GenericTestConfig config, IAssetUtilityService assetUtility, IMeshUtilityService meshUtility)
        {
            _config = config;
            _assetUtility = assetUtility;
            _meshUtility = meshUtility;
        }

        public TestResult Run()
        {
            var result = new TestResult() { Status = TestResultStatus.Undefined };

            var prefabs = _assetUtility.GetObjectsFromAssets<GameObject>(_config.ValidationPaths, AssetType.Prefab);
            var badPrefabs = new List<GameObject>();

            foreach (var p in prefabs)
            {
                var meshes = _meshUtility.GetCustomMeshesInObject(p);

                if (!p.isStatic || !meshes.Any())
                    continue;

                var colliders = p.GetComponentsInChildren<Collider>(true);
                if (!colliders.Any())
                    badPrefabs.Add(p);
            }

            if (badPrefabs.Count == 0)
            {
                result.Status = TestResultStatus.Pass;
                result.AddMessage("All found prefabs have colliders!");
                return result;
            }

            result.Status = TestResultStatus.VariableSeverityIssue;
            result.AddMessage("The following prefabs contain meshes, but colliders were not found", null, badPrefabs.ToArray());

            return result;
        }
    }
}
