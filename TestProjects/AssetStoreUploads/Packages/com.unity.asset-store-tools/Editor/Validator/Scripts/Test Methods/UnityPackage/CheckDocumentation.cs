using AssetStoreTools.Validator.Data;
using AssetStoreTools.Validator.Services.Validation;
using AssetStoreTools.Validator.TestDefinitions;
using System;
using System.IO;
using System.Linq;

namespace AssetStoreTools.Validator.TestMethods
{
    internal class CheckDocumentation : ITestScript
    {
        private GenericTestConfig _config;
        private IAssetUtilityService _assetUtility;

        public CheckDocumentation(GenericTestConfig config, IAssetUtilityService assetUtility)
        {
            _config = config;
            _assetUtility = assetUtility;
        }

        public TestResult Run()
        {
            var result = new TestResult() { Status = TestResultStatus.Undefined };

            var textFilePaths = _assetUtility.GetAssetPathsFromAssets(_config.ValidationPaths, AssetType.Documentation).ToArray();
            var documentationFilePaths = textFilePaths.Where(CouldBeDocumentation).ToArray();

            if (textFilePaths.Length == 0)
            {
                result.Status = TestResultStatus.VariableSeverityIssue;
                result.AddMessage("No potential documentation files ('.txt', '.pdf', " +
                                  "'.html', '.rtf', '.md') found within the given path.");
            }
            else if (documentationFilePaths.Length == 0)
            {
                result.Status = TestResultStatus.Warning;
                var textFileObjects = textFilePaths.Select(_assetUtility.AssetPathToObject).ToArray();
                result.AddMessage("The following files have been found to match the documentation file format," +
                    " but may not be documentation in content",
                    null, textFileObjects);
            }
            else
            {
                result.Status = TestResultStatus.Pass;
                var documentationFileObjects = documentationFilePaths.Select(_assetUtility.AssetPathToObject).ToArray();
                result.AddMessage("Found documentation files", null, documentationFileObjects);
            }

            return result;
        }

        private bool CouldBeDocumentation(string filePath)
        {
            if (filePath.EndsWith(".pdf"))
                return true;

            using (var fs = File.Open(filePath, FileMode.Open))
            using (var bs = new BufferedStream(fs))
            using (var sr = new StreamReader(bs))
            {
                string line;
                while ((line = sr.ReadLine()) != null)
                {
                    var mentionsDocumentation = line.IndexOf("documentation", StringComparison.OrdinalIgnoreCase) >= 0;
                    if (mentionsDocumentation)
                        return true;
                }
            }

            return false;
        }
    }
}
