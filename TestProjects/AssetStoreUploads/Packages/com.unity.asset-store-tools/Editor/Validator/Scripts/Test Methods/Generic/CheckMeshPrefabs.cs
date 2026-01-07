using AssetStoreTools.Validator.Data;
using AssetStoreTools.Validator.Services.Validation;
using AssetStoreTools.Validator.TestDefinitions;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace AssetStoreTools.Validator.TestMethods
{
    internal class CheckMeshPrefabs : ITestScript
    {
        private GenericTestConfig _config;
        private IAssetUtilityService _assetUtility;
        private IMeshUtilityService _meshUtility;

        public CheckMeshPrefabs(GenericTestConfig config, IAssetUtilityService assetUtility, IMeshUtilityService meshUtility)
        {
            _config = config;
            _assetUtility = assetUtility;
            _meshUtility = meshUtility;
        }

        public TestResult Run()
        {
            var result = new TestResult() { Status = TestResultStatus.Undefined };

            var usedModelPaths = new List<string>();
            var prefabs = _assetUtility.GetObjectsFromAssets<GameObject>(_config.ValidationPaths, AssetType.Prefab);
            var missingMeshReferencePrefabs = new List<GameObject>();

            // Get all meshes in existing prefabs and check if prefab has missing mesh references
            foreach (var p in prefabs)
            {
                var meshes = _meshUtility.GetCustomMeshesInObject(p);
                foreach (var mesh in meshes)
                {
                    string meshPath = _assetUtility.ObjectToAssetPath(mesh);
                    usedModelPaths.Add(meshPath);
                }

                if (HasMissingMeshReferences(p))
                    missingMeshReferencePrefabs.Add(p);
            }

            // Get all meshes in existing models
            var allModelPaths = GetAllModelMeshPaths(_config.ValidationPaths);

            // Get the list of meshes without prefabs
            List<string> unusedModels = allModelPaths.Except(usedModelPaths).ToList();

            if (unusedModels.Count == 0)
            {
                result.Status = TestResultStatus.Pass;
                result.AddMessage("All found prefabs have meshes!");
                return result;
            }

            result.Status = TestResultStatus.VariableSeverityIssue;
            var models = unusedModels.Select(_assetUtility.AssetPathToObject).ToArray();
            result.AddMessage("The following models do not have associated prefabs", null, models);

            if (missingMeshReferencePrefabs.Count > 0)
                result.AddMessage("The following prefabs have missing mesh references", null, missingMeshReferencePrefabs.ToArray());

            return result;
        }

        private IEnumerable<string> GetAllModelMeshPaths(string[] validationPaths)
        {
            var models = _assetUtility.GetObjectsFromAssets(validationPaths, AssetType.Model);
            var paths = new List<string>();

            foreach (var o in models)
            {
                var m = (GameObject)o;
                var modelPath = _assetUtility.ObjectToAssetPath(m);
                var assetImporter = _assetUtility.GetAssetImporter(modelPath);
                if (assetImporter is UnityEditor.ModelImporter modelImporter)
                {
                    var clips = modelImporter.clipAnimations.Count();
                    var meshes = _meshUtility.GetCustomMeshesInObject(m);

                    // Only add if the model has meshes and no clips
                    if (meshes.Any() && clips == 0)
                        paths.Add(modelPath);
                }
            }

            return paths;
        }

        private bool HasMissingMeshReferences(GameObject go)
        {
            var meshes = go.GetComponentsInChildren<MeshFilter>(true);
            var skinnedMeshes = go.GetComponentsInChildren<SkinnedMeshRenderer>(true);

            if (meshes.Length == 0 && skinnedMeshes.Length == 0)
                return false;

            if (meshes.Any(x => x.sharedMesh == null) || skinnedMeshes.Any(x => x.sharedMesh == null))
                return true;

            return false;
        }
    }
}
