using AssetStoreTools.Validator.Data;
using AssetStoreTools.Validator.Services.Validation;
using AssetStoreTools.Validator.TestDefinitions;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace AssetStoreTools.Validator.TestMethods
{
    internal class CheckNormalMapTextures : ITestScript
    {
        public const int TextureCacheLimit = 8;

        private GenericTestConfig _config;
        private IAssetUtilityService _assetUtility;

        public CheckNormalMapTextures(GenericTestConfig config, IAssetUtilityService assetUtility)
        {
            _config = config;
            _assetUtility = assetUtility;
        }

        public TestResult Run()
        {
            var result = new TestResult() { Status = TestResultStatus.Undefined };

            var materials = _assetUtility.GetObjectsFromAssets<Material>(_config.ValidationPaths, AssetType.Material);
            var badTextures = new List<Texture>();
            var badPaths = new List<string>();

            foreach (var mat in materials)
            {
                for (int i = 0; i < mat.shader.GetPropertyCount(); i++)
                {
                    if ((mat.shader.GetPropertyFlags(i) & UnityEngine.Rendering.ShaderPropertyFlags.Normal) != 0)
                    {
                        var propertyName = mat.shader.GetPropertyName(i);
                        var assignedTexture = mat.GetTexture(propertyName);

                        if (assignedTexture == null)
                            continue;

                        var texturePath = _assetUtility.ObjectToAssetPath(assignedTexture);
                        var textureImporter = _assetUtility.GetAssetImporter(texturePath) as TextureImporter;
                        if (textureImporter == null)
                            continue;

                        if (textureImporter.textureType != TextureImporterType.NormalMap && !badTextures.Contains(assignedTexture))
                        {
                            if (badTextures.Count < TextureCacheLimit)
                            {
                                badTextures.Add(assignedTexture);
                            }
                            else
                            {
                                string path = AssetDatabase.GetAssetPath(assignedTexture);
                                badPaths.Add(path);
                            }
                        }
                    }
                }

                EditorUtility.UnloadUnusedAssetsImmediate();
            }

            if (badTextures.Count == 0)
            {
                result.Status = TestResultStatus.Pass;
                result.AddMessage("All normal map textures have the correct texture type!");
            }
            else if (badPaths.Count != 0)
            {
                foreach (Texture texture in badTextures)
                {
                    string path = AssetDatabase.GetAssetPath(texture);
                    badPaths.Add(path);
                }

                string paths = string.Join("\n", badPaths);

                result.Status = TestResultStatus.VariableSeverityIssue;
                result.AddMessage("The following textures are not set to type 'Normal Map'", null);
                result.AddMessage(paths);
            }
            else
            {
                result.Status = TestResultStatus.VariableSeverityIssue;
                result.AddMessage("The following textures are not set to type 'Normal Map'", null, badTextures.ToArray());
            }

            return result;
        }
    }
}
