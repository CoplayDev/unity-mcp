using AssetStoreTools.Validator.Data;
using AssetStoreTools.Validator.Services.Validation;
using AssetStoreTools.Validator.TestDefinitions;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityObject = UnityEngine.Object;

namespace AssetStoreTools.Validator.TestMethods
{
    internal class CheckAnimationClips : ITestScript
    {
        private static readonly string[] InvalidNames = new[] { "Take 001" };

        private GenericTestConfig _config;
        private IAssetUtilityService _assetUtility;

        public CheckAnimationClips(GenericTestConfig config, IAssetUtilityService assetUtility)
        {
            _config = config;
            _assetUtility = assetUtility;
        }

        public TestResult Run()
        {
            var result = new TestResult() { Status = TestResultStatus.Undefined };
            var badModels = new Dictionary<UnityObject, List<UnityObject>>();
            var models = _assetUtility.GetObjectsFromAssets<UnityObject>(_config.ValidationPaths, AssetType.Model);

            foreach (var model in models)
            {
                var badClips = new List<UnityObject>();
                var clips = AssetDatabase.LoadAllAssetsAtPath(_assetUtility.ObjectToAssetPath(model));
                foreach (var clip in clips)
                {
                    if (InvalidNames.Any(x => x.ToLower().Equals(clip.name.ToLower())))
                    {
                        badClips.Add(clip);
                    }
                }

                if (badClips.Count > 0)
                    badModels.Add(model, badClips);
            }

            if (badModels.Count > 0)
            {
                result.Status = TestResultStatus.VariableSeverityIssue;
                result.AddMessage("The following models have animation clips with invalid names. Animation clip names should be unique and reflective of the animation itself");
                foreach (var kvp in badModels)
                {
                    result.AddMessage(_assetUtility.ObjectToAssetPath(kvp.Key), null, kvp.Value.ToArray());
                }
            }
            else
            {
                result.AddMessage("No animation clips with invalid names were found!");
                result.Status = TestResultStatus.Pass;
            }

            return result;
        }
    }
}
