using AssetStoreTools.Validator.Data;
using AssetStoreTools.Validator.Services.Validation;
using AssetStoreTools.Validator.TestDefinitions;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityObject = UnityEngine.Object;

namespace AssetStoreTools.Validator.TestMethods
{
    internal class CheckModelTypes : ITestScript
    {
        private GenericTestConfig _config;
        private IAssetUtilityService _assetUtility;

        public CheckModelTypes(GenericTestConfig config, IAssetUtilityService assetUtility)
        {
            _config = config;
            _assetUtility = assetUtility;
        }

        public TestResult Run()
        {
            var result = new TestResult() { Status = TestResultStatus.Undefined };

            var allowedExtensions = new string[] { ".fbx", ".dae", ".abc", ".obj" };
            // Should retrieve All assets and not models here since ADB will not recognize certain
            // types if appropriate software is not installed (e.g. .blend without Blender)
            var allAssetPaths = _assetUtility.GetAssetPathsFromAssets(_config.ValidationPaths, AssetType.All);
            var badModels = new List<UnityObject>();

            foreach (var assetPath in allAssetPaths)
            {
                var importer = _assetUtility.GetAssetImporter(assetPath);
                if (importer == null || !(importer is ModelImporter))
                    continue;

                if (allowedExtensions.Any(x => importer.assetPath.ToLower().EndsWith(x)))
                    continue;

                badModels.Add(_assetUtility.AssetPathToObject(assetPath));
            }

            if (badModels.Count == 0)
            {
                result.Status = TestResultStatus.Pass;
                result.AddMessage("All models are of allowed formats!");
            }
            else
            {
                result.Status = TestResultStatus.VariableSeverityIssue;
                result.AddMessage("The following models are of formats that should not be used for Asset Store packages:", null, badModels.ToArray());
            }

            return result;
        }
    }
}
