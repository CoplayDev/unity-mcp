using AssetStoreTools.Validator.Data;
using AssetStoreTools.Validator.Data.MessageActions;
using AssetStoreTools.Validator.Services.Validation;
using AssetStoreTools.Validator.TestDefinitions;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace AssetStoreTools.Validator.TestMethods
{
    internal class CheckParticleSystems : ITestScript
    {
        private GenericTestConfig _config;
        private IAssetUtilityService _assetUtility;
        private ISceneUtilityService _sceneUtility;

        public CheckParticleSystems(GenericTestConfig config, IAssetUtilityService assetUtility, ISceneUtilityService sceneUtility)
        {
            _config = config;
            _assetUtility = assetUtility;
            _sceneUtility = sceneUtility;
        }

        public TestResult Run()
        {
            var result = new TestResult() { Status = TestResultStatus.Undefined };

            var originalScenePath = _sceneUtility.CurrentScenePath;

            var scenePaths = _assetUtility.GetAssetPathsFromAssets(_config.ValidationPaths, AssetType.Scene);

            foreach (var path in scenePaths)
            {
                var badParticleSystems = new List<ParticleSystem>();

                var scene = _sceneUtility.OpenScene(path);

                if (!scene.IsValid())
                {
                    Debug.LogWarning("Unable to get Scene in " + path);
                    continue;
                }

#if UNITY_2023_1_OR_NEWER
                var particleSystems = GameObject.FindObjectsByType<ParticleSystem>(FindObjectsInactive.Include, FindObjectsSortMode.None);
#else
                var particleSystems = GameObject.FindObjectsOfType<ParticleSystem>();
#endif

                foreach (var ps in particleSystems)
                {
                    if (PrefabUtility.IsPartOfAnyPrefab(ps.gameObject))
                        continue;
                    badParticleSystems.Add(ps);
                }

                if (badParticleSystems.Count == 0)
                    continue;

                result.Status = TestResultStatus.VariableSeverityIssue;
                var message = $"Particle Systems not belonging to any Prefab were found in {path}.\n\nClick this message to open the Scene and see the affected Particle Systems:";
                result.AddMessage(message, new OpenAssetAction(AssetDatabase.LoadAssetAtPath<SceneAsset>(path)), badParticleSystems.ToArray());
            }

            _sceneUtility.OpenScene(originalScenePath);

            if (result.Status == TestResultStatus.Undefined)
            {
                result.Status = TestResultStatus.Pass;
                result.AddMessage("No Particle Systems without Prefabs were found!");
            }

            return result;
        }
    }
}
