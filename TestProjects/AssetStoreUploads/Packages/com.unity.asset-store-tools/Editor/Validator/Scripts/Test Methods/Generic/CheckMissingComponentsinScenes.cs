using AssetStoreTools.Validator.Data;
using AssetStoreTools.Validator.Data.MessageActions;
using AssetStoreTools.Validator.Services.Validation;
using AssetStoreTools.Validator.TestDefinitions;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using SceneAsset = UnityEditor.SceneAsset;

namespace AssetStoreTools.Validator.TestMethods
{
    internal class CheckMissingComponentsinScenes : ITestScript
    {
        private GenericTestConfig _config;
        private IAssetUtilityService _assetUtility;
        private ISceneUtilityService _sceneUtility;

        public CheckMissingComponentsinScenes(GenericTestConfig config, IAssetUtilityService assetUtility, ISceneUtilityService sceneUtility)
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
            foreach (var scenePath in scenePaths)
            {
                var missingComponentGOs = GetMissingComponentGOsInScene(scenePath);

                if (missingComponentGOs.Count == 0)
                    continue;

                result.Status = TestResultStatus.VariableSeverityIssue;
                var message = $"GameObjects with missing components or prefab references found in {scenePath}.\n\nClick this message to open the Scene and see the affected GameObjects:";
                result.AddMessage(message, new OpenAssetAction(_assetUtility.AssetPathToObject<SceneAsset>(scenePath)), missingComponentGOs.ToArray());
            }

            _sceneUtility.OpenScene(originalScenePath);

            if (result.Status == TestResultStatus.Undefined)
            {
                result.Status = TestResultStatus.Pass;
                result.AddMessage("No missing components were found!");
            }

            return result;
        }

        private List<GameObject> GetMissingComponentGOsInScene(string path)
        {
            var missingComponentGOs = new List<GameObject>();

            var scene = _sceneUtility.OpenScene(path);

            if (!scene.IsValid())
            {
                Debug.LogWarning("Unable to get Scene in " + path);
                return new List<GameObject>();
            }

            var rootObjects = scene.GetRootGameObjects();

            foreach (var obj in rootObjects)
            {
                missingComponentGOs.AddRange(GetMissingComponentGOs(obj));
            }

            return missingComponentGOs;
        }

        private List<GameObject> GetMissingComponentGOs(GameObject root)
        {
            var missingComponentGOs = new List<GameObject>();
            var rootComponents = root.GetComponents<Component>();

            if (UnityEditor.PrefabUtility.GetPrefabInstanceStatus(root) == UnityEditor.PrefabInstanceStatus.MissingAsset || rootComponents.Any(c => !c))
            {
                missingComponentGOs.Add(root);
            }

            foreach (Transform child in root.transform)
                missingComponentGOs.AddRange(GetMissingComponentGOs(child.gameObject));

            return missingComponentGOs;
        }
    }
}
