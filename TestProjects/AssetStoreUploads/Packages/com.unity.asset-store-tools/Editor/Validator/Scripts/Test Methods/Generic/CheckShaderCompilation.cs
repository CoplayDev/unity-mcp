using AssetStoreTools.Validator.Data;
using AssetStoreTools.Validator.Services.Validation;
using AssetStoreTools.Validator.TestDefinitions;
using System.Linq;
using UnityEditor;
using UnityEngine;
#if !UNITY_2023_1_OR_NEWER
using UnityEngine.Experimental.Rendering;
#endif
#if UNITY_2023_1_OR_NEWER
using UnityEngine.Rendering;
#endif

namespace AssetStoreTools.Validator.TestMethods
{
    internal class CheckShaderCompilation : ITestScript
    {
        private GenericTestConfig _config;
        private IAssetUtilityService _assetUtility;

        public CheckShaderCompilation(GenericTestConfig config, IAssetUtilityService assetUtility)
        {
            _config = config;
            _assetUtility = assetUtility;
        }

        public TestResult Run()
        {
            var result = new TestResult() { Status = TestResultStatus.Undefined };

            var shaders = _assetUtility.GetObjectsFromAssets(_config.ValidationPaths, AssetType.Shader);
            var badShaders = shaders.Where(ShaderHasError).ToArray();

            if (badShaders.Length > 0)
            {
                result.Status = TestResultStatus.VariableSeverityIssue;
                result.AddMessage("The following shader files have errors", null, badShaders);
            }
            else
            {
                result.Status = TestResultStatus.Pass;
                result.AddMessage("All found Shaders have no compilation errors!");
            }

            return result;
        }

        private bool ShaderHasError(Object obj)
        {
            switch (obj)
            {
                case Shader shader:
                    return ShaderUtil.ShaderHasError(shader);
                case ComputeShader shader:
                    return ShaderUtil.GetComputeShaderMessageCount(shader) > 0;
                case RayTracingShader shader:
                    return ShaderUtil.GetRayTracingShaderMessageCount(shader) > 0;
                default:
                    return false;
            }
        }
    }
}
