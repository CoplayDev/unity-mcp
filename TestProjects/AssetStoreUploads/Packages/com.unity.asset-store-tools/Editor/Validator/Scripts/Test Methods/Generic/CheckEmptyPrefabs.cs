using AssetStoreTools.Validator.Data;
using AssetStoreTools.Validator.Services.Validation;
using AssetStoreTools.Validator.TestDefinitions;
using System.Collections.Generic;
using UnityEngine;

namespace AssetStoreTools.Validator.TestMethods
{
    internal class CheckEmptyPrefabs : ITestScript
    {
        private GenericTestConfig _config;
        private IAssetUtilityService _assetUtility;

        public CheckEmptyPrefabs(GenericTestConfig config, IAssetUtilityService assetUtility)
        {
            _config = config;
            _assetUtility = assetUtility;
        }

        public TestResult Run()
        {
            var result = new TestResult() { Status = TestResultStatus.Undefined };

            var prefabs = _assetUtility.GetObjectsFromAssets<GameObject>(_config.ValidationPaths, AssetType.Prefab);
            var badPrefabs = new List<GameObject>();

            foreach (var p in prefabs)
            {
                if (p.GetComponents<Component>().Length == 1 && p.transform.childCount == 0)
                    badPrefabs.Add(p);
            }

            if (badPrefabs.Count == 0)
            {
                result.Status = TestResultStatus.Pass;
                result.AddMessage("No empty prefabs were found!");
                return result;
            }

            result.Status = TestResultStatus.VariableSeverityIssue;
            result.AddMessage("The following prefabs are empty", null, badPrefabs.ToArray());

            return result;
        }
    }
}
