using AssetStoreTools.Validator.Data;
using AssetStoreTools.Validator.Services.Validation;
using AssetStoreTools.Validator.TestDefinitions;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace AssetStoreTools.Validator.TestMethods
{
    internal class CheckTextureDimensions : ITestScript
    {
        private GenericTestConfig _config;
        private IAssetUtilityService _assetUtility;

        public CheckTextureDimensions(GenericTestConfig config, IAssetUtilityService assetUtility)
        {
            _config = config;
            _assetUtility = assetUtility;
        }

        public TestResult Run()
        {
            var result = new TestResult() { Status = TestResultStatus.Undefined };

            var textures = _assetUtility.GetObjectsFromAssets<Texture>(_config.ValidationPaths, AssetType.Texture);
            var badTextures = new List<Texture>();

            foreach (var texture in textures)
            {
                if (Mathf.IsPowerOfTwo(texture.width) && Mathf.IsPowerOfTwo(texture.height))
                    continue;

                var importer = _assetUtility.GetAssetImporter(_assetUtility.ObjectToAssetPath(texture));

                if (importer == null || !(importer is TextureImporter textureImporter)
                    || textureImporter.textureType == TextureImporterType.Sprite
                    || textureImporter.textureType == TextureImporterType.GUI)
                    continue;

                badTextures.Add(texture);
            }

            if (badTextures.Count == 0)
            {
                result.Status = TestResultStatus.Pass;
                result.AddMessage("All texture dimensions are a power of 2!");
            }
            else
            {
                result.Status = TestResultStatus.VariableSeverityIssue;
                result.AddMessage("The following texture dimensions are not a power of 2:", null, badTextures.ToArray());
            }

            return result;
        }
    }
}
