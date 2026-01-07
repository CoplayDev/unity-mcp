using AssetStoreTools.Validator.Data;
using AssetStoreTools.Validator.Data.MessageActions;
using AssetStoreTools.Validator.Services.Validation;
using AssetStoreTools.Validator.TestDefinitions;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace AssetStoreTools.Validator.TestMethods
{
    internal class CheckLODs : ITestScript
    {
        private GenericTestConfig _config;
        private IAssetUtilityService _assetUtility;

        public CheckLODs(GenericTestConfig config, IAssetUtilityService assetUtility)
        {
            _config = config;
            _assetUtility = assetUtility;
        }

        public TestResult Run()
        {
            var result = new TestResult() { Status = TestResultStatus.Undefined };

            var prefabs = _assetUtility.GetObjectsFromAssets<GameObject>(_config.ValidationPaths, AssetType.Prefab);
            var badPrefabs = new Dictionary<GameObject, List<MeshFilter>>();

            foreach (var p in prefabs)
            {
                var meshFilters = p.GetComponentsInChildren<MeshFilter>(true);
                var badMeshFilters = new List<MeshFilter>();
                var lodGroups = p.GetComponentsInChildren<LODGroup>(true);

                foreach (var mf in meshFilters)
                {
                    if (mf.name.Contains("LOD") && !IsPartOfLodGroup(mf, lodGroups))
                        badMeshFilters.Add(mf);
                }

                if (badMeshFilters.Count > 0)
                    badPrefabs.Add(p, badMeshFilters);
            }

            if (badPrefabs.Count == 0)
            {
                result.Status = TestResultStatus.Pass;
                result.AddMessage("All found prefabs are meeting the LOD requirements!");
                return result;
            }

            result.Status = TestResultStatus.VariableSeverityIssue;
            result.AddMessage("The following prefabs do not meet the LOD requirements");

            foreach (var p in badPrefabs)
            {
                var resultList = new List<Object>();
                resultList.Add(p.Key);
                resultList.AddRange(p.Value);
                result.AddMessage($"{p.Key.name}.prefab", new OpenAssetAction(p.Key), resultList.ToArray());
            }

            return result;
        }

        private bool IsPartOfLodGroup(MeshFilter mf, LODGroup[] lodGroups)
        {
            foreach (var lodGroup in lodGroups)
            {
                // If MeshFilter is a child/deep child of a LodGroup AND is referenced in this LOD group - it is valid
                if (mf.transform.IsChildOf(lodGroup.transform) &&
                    lodGroup.GetLODs().Any(lod => lod.renderers.Any(renderer => renderer != null && renderer.gameObject == mf.gameObject)))
                    return true;
            }

            return false;
        }
    }
}
