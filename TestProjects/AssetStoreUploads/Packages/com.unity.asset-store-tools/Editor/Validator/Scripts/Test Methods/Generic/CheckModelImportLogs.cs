using AssetStoreTools.Validator.Data;
using AssetStoreTools.Validator.Services.Validation;
using AssetStoreTools.Validator.TestDefinitions;
using System.Collections.Generic;
using System.Linq;
using UnityObject = UnityEngine.Object;

namespace AssetStoreTools.Validator.TestMethods
{
    internal class CheckModelImportLogs : ITestScript
    {
        private GenericTestConfig _config;
        private IAssetUtilityService _assetUtility;
        private IModelUtilityService _modelUtility;

        public CheckModelImportLogs(GenericTestConfig config, IAssetUtilityService assetUtility, IModelUtilityService modelUtility)
        {
            _config = config;
            _assetUtility = assetUtility;
            _modelUtility = modelUtility;
        }

        public TestResult Run()
        {
            var result = new TestResult() { Status = TestResultStatus.Undefined };

            var models = _assetUtility.GetObjectsFromAssets<UnityObject>(_config.ValidationPaths, AssetType.Model);
            var importLogs = _modelUtility.GetImportLogs(models.ToArray());

            var warningModels = new List<UnityObject>();
            var errorModels = new List<UnityObject>();

            foreach (var kvp in importLogs)
            {
                if (kvp.Value.Any(x => x.Severity == UnityEngine.LogType.Error))
                    errorModels.Add(kvp.Key);
                if (kvp.Value.Any(x => x.Severity == UnityEngine.LogType.Warning))
                    warningModels.Add(kvp.Key);
            }

            if (warningModels.Count > 0 || errorModels.Count > 0)
            {
                if (warningModels.Count > 0)
                {
                    result.Status = TestResultStatus.Warning;
                    result.AddMessage("The following models contain import warnings:", null, warningModels.ToArray());
                }

                if (errorModels.Count > 0)
                {
                    result.Status = TestResultStatus.Warning;
                    result.AddMessage("The following models contain import errors:", null, errorModels.ToArray());
                }
            }
            else
            {
                result.Status = TestResultStatus.Pass;
                result.AddMessage("No issues were detected when importing your models!");
            }

            return result;
        }
    }
}
