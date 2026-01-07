using AssetStoreTools.Validator.Data;
using AssetStoreTools.Validator.Services.Validation;
using AssetStoreTools.Validator.TestDefinitions;
using System.Linq;

namespace AssetStoreTools.Validator.TestMethods
{
    internal class RemoveExecutableFiles : ITestScript
    {
        private GenericTestConfig _config;
        private IAssetUtilityService _assetUtility;

        public RemoveExecutableFiles(GenericTestConfig config, IAssetUtilityService assetUtility)
        {
            _config = config;
            _assetUtility = assetUtility;
        }

        public TestResult Run()
        {
            var result = new TestResult() { Status = TestResultStatus.Undefined };

            var executables = _assetUtility.GetObjectsFromAssets(_config.ValidationPaths, AssetType.Executable).ToArray();

            if (executables.Length == 0)
            {
                result.Status = TestResultStatus.Pass;
                result.AddMessage("No executable files were found!");
                return result;
            }

            result.Status = TestResultStatus.VariableSeverityIssue;
            result.AddMessage("The following executable files were found", null, executables);

            return result;
        }
    }
}
