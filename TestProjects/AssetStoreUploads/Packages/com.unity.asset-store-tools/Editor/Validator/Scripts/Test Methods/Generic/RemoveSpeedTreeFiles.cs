using AssetStoreTools.Validator.Data;
using AssetStoreTools.Validator.Services.Validation;
using AssetStoreTools.Validator.TestDefinitions;
using System.Linq;

namespace AssetStoreTools.Validator.TestMethods
{
    internal class RemoveSpeedTreeFiles : ITestScript
    {
        private GenericTestConfig _config;
        private IAssetUtilityService _assetUtility;

        public RemoveSpeedTreeFiles(GenericTestConfig config, IAssetUtilityService assetUtility)
        {
            _config = config;
            _assetUtility = assetUtility;
        }

        public TestResult Run()
        {
            var result = new TestResult() { Status = TestResultStatus.Undefined };

            var speedtreeObjects = _assetUtility.GetObjectsFromAssets(_config.ValidationPaths, AssetType.SpeedTree).ToArray();

            if (speedtreeObjects.Length == 0)
            {
                result.Status = TestResultStatus.Pass;
                result.AddMessage("No SpeedTree assets have been found!");
                return result;
            }

            result.Status = TestResultStatus.VariableSeverityIssue;
            result.AddMessage("The following SpeedTree assets have been found", null, speedtreeObjects);

            return result;
        }
    }
}
