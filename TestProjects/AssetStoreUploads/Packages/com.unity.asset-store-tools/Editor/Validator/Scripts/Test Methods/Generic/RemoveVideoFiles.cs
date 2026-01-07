using AssetStoreTools.Validator.Data;
using AssetStoreTools.Validator.Services.Validation;
using AssetStoreTools.Validator.TestDefinitions;
using System.Linq;

namespace AssetStoreTools.Validator.TestMethods
{
    internal class RemoveVideoFiles : ITestScript
    {
        private GenericTestConfig _config;
        private IAssetUtilityService _assetUtility;

        public RemoveVideoFiles(GenericTestConfig config, IAssetUtilityService assetUtility)
        {
            _config = config;
            _assetUtility = assetUtility;
        }

        public TestResult Run()
        {
            var result = new TestResult() { Status = TestResultStatus.Undefined };

            var videos = _assetUtility.GetObjectsFromAssets(_config.ValidationPaths, AssetType.Video).ToArray();

            if (videos.Length == 0)
            {
                result.Status = TestResultStatus.Pass;
                result.AddMessage("No video files were found, looking good!");
                return result;
            }

            result.Status = TestResultStatus.VariableSeverityIssue;
            result.AddMessage("The following video files were found", null, videos);

            return result;
        }
    }
}
