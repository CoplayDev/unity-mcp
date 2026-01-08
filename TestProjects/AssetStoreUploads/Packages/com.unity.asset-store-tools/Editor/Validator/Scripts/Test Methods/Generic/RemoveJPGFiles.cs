using AssetStoreTools.Validator.Data;
using AssetStoreTools.Validator.Services.Validation;
using AssetStoreTools.Validator.TestDefinitions;
using System.Linq;

namespace AssetStoreTools.Validator.TestMethods
{
    internal class RemoveJPGFiles : ITestScript
    {
        private GenericTestConfig _config;
        private IAssetUtilityService _assetUtility;

        public RemoveJPGFiles(GenericTestConfig config, IAssetUtilityService assetUtility)
        {
            _config = config;
            _assetUtility = assetUtility;
        }

        public TestResult Run()
        {
            var result = new TestResult() { Status = TestResultStatus.Undefined };

            var jpgs = _assetUtility.GetObjectsFromAssets(_config.ValidationPaths, AssetType.JPG).ToArray();

            if (jpgs.Length == 0)
            {
                result.Status = TestResultStatus.Pass;
                result.AddMessage("No JPG/JPEG textures were found!");
                return result;
            }

            result.Status = TestResultStatus.VariableSeverityIssue;
            result.AddMessage("The following textures are compressed as JPG/JPEG", null, jpgs);

            return result;
        }
    }
}
