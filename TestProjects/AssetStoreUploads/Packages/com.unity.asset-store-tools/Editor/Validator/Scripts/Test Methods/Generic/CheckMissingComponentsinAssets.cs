using AssetStoreTools.Validator.Data;
using AssetStoreTools.Validator.Services.Validation;
using AssetStoreTools.Validator.TestDefinitions;
using System.Collections.Generic;
using UnityEngine;

namespace AssetStoreTools.Validator.TestMethods
{
    internal class CheckMissingComponentsinAssets : ITestScript
    {
        private GenericTestConfig _config;
        private IAssetUtilityService _assetUtility;

        public CheckMissingComponentsinAssets(GenericTestConfig config, IAssetUtilityService assetUtility)
        {
            _config = config;
            _assetUtility = assetUtility;
        }

        public TestResult Run()
        {
            var result = new TestResult() { Status = TestResultStatus.Undefined };

            var assets = GetAllAssetsWithMissingComponents(_config.ValidationPaths);

            if (assets.Length == 0)
            {
                result.Status = TestResultStatus.Pass;
                result.AddMessage("No assets have missing components!");
                return result;
            }

            result.Status = TestResultStatus.VariableSeverityIssue;
            result.AddMessage("The following assets contain missing components", null, assets);

            return result;
        }

        private GameObject[] GetAllAssetsWithMissingComponents(string[] validationPaths)
        {
            var missingReferenceAssets = new List<GameObject>();
            var prefabObjects = _assetUtility.GetObjectsFromAssets<GameObject>(validationPaths, AssetType.Prefab);

            foreach (var p in prefabObjects)
            {
                if (p != null && IsMissingReference(p))
                    missingReferenceAssets.Add(p);
            }

            return missingReferenceAssets.ToArray();
        }

        private bool IsMissingReference(GameObject asset)
        {
            var components = asset.GetComponentsInChildren<Component>();

            foreach (var c in components)
            {
                if (!c)
                    return true;
            }

            return false;
        }
    }
}
