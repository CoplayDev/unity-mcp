using AssetStoreTools.Validator.Data;
using AssetStoreTools.Validator.Services.Validation;
using AssetStoreTools.Validator.TestDefinitions;
using System.Linq;

namespace AssetStoreTools.Validator.TestMethods
{
    internal class RemoveMixamoFiles : ITestScript
    {
        private GenericTestConfig _config;
        private IAssetUtilityService _assetUtility;

        public RemoveMixamoFiles(GenericTestConfig config, IAssetUtilityService assetUtility)
        {
            _config = config;
            _assetUtility = assetUtility;
        }

        public TestResult Run()
        {
            var result = new TestResult() { Status = TestResultStatus.Undefined };

            var mixamoFiles = _assetUtility.GetObjectsFromAssets(_config.ValidationPaths, AssetType.Mixamo).ToArray();

            if (mixamoFiles.Length == 0)
            {
                result.Status = TestResultStatus.Pass;
                result.AddMessage("No Mixamo files were found!");
                return result;
            }

            result.Status = TestResultStatus.VariableSeverityIssue;
            result.AddMessage("The following Mixamo files were found", null, mixamoFiles);

            return result;
        }
    }
}
