using AssetStoreTools.Validator.Data;
using AssetStoreTools.Validator.Services.Validation;
using AssetStoreTools.Validator.TestDefinitions;
using System.Linq;

namespace AssetStoreTools.Validator.TestMethods
{
    internal class RemoveJavaScriptFiles : ITestScript
    {
        private GenericTestConfig _config;
        private IAssetUtilityService _assetUtility;

        public RemoveJavaScriptFiles(GenericTestConfig config, IAssetUtilityService assetUtility)
        {
            _config = config;
            _assetUtility = assetUtility;
        }

        public TestResult Run()
        {
            var result = new TestResult() { Status = TestResultStatus.Undefined };

            var javascriptObjects = _assetUtility.GetObjectsFromAssets(_config.ValidationPaths, AssetType.JavaScript).ToArray();

            if (javascriptObjects.Length == 0)
            {
                result.Status = TestResultStatus.Pass;
                result.AddMessage("No UnityScript / JS files were found!");
                return result;
            }

            result.Status = TestResultStatus.VariableSeverityIssue;
            result.AddMessage("The following assets are UnityScript / JS files", null, javascriptObjects);

            return result;
        }
    }
}
