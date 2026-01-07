using AssetStoreTools.Validator.Data;
using AssetStoreTools.Validator.Services.Validation;
using AssetStoreTools.Validator.TestDefinitions;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace AssetStoreTools.Validator.TestMethods
{
    internal class CheckPrefabTransforms : ITestScript
    {
        private GenericTestConfig _config;
        private IAssetUtilityService _assetUtility;
        private IMeshUtilityService _meshUtility;

        public CheckPrefabTransforms(GenericTestConfig config, IAssetUtilityService assetUtility, IMeshUtilityService meshUtility)
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
            var badPrefabsLowOffset = new List<GameObject>();

            foreach (var p in prefabs)
            {
                var hasRectTransform = p.TryGetComponent(out RectTransform _);
                if (hasRectTransform || !_meshUtility.GetCustomMeshesInObject(p).Any())
                    continue;

                var positionString = p.transform.position.ToString("F12");
                var rotationString = p.transform.rotation.eulerAngles.ToString("F12");
                var localScaleString = p.transform.localScale.ToString("F12");

                var vectorZeroString = Vector3.zero.ToString("F12");
                var vectorOneString = Vector3.one.ToString("F12");

                if (positionString != vectorZeroString || rotationString != vectorZeroString || localScaleString != vectorOneString)
                {
                    if (p.transform.position == Vector3.zero && p.transform.rotation.eulerAngles == Vector3.zero && p.transform.localScale == Vector3.one)
                        badPrefabsLowOffset.Add(p);
                    else
                        badPrefabs.Add(p);
                }
            }

            if (badPrefabs.Count == 0 && badPrefabsLowOffset.Count == 0)
            {
                result.Status = TestResultStatus.Pass;
                result.AddMessage("All found prefabs were reset!");
                return result;
            }

            result.Status = TestResultStatus.VariableSeverityIssue;
            if (badPrefabs.Count > 0)
                result.AddMessage("The following prefabs' transforms do not fit the requirements", null, badPrefabs.ToArray());
            if (badPrefabsLowOffset.Count > 0)
                result.AddMessage("The following prefabs have unusually low transform values, which might not be accurately displayed " +
                    "in the Inspector window. Please use the 'Debug' Inspector mode to review the Transform component of these prefabs " +
                    "or reset the Transform components using the right-click context menu", null, badPrefabsLowOffset.ToArray());

            return result;
        }
    }
}
