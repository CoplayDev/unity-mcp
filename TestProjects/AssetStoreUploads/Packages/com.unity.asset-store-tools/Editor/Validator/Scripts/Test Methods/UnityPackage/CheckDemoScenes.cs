using AssetStoreTools.Validator.Data;
using AssetStoreTools.Validator.Services.Validation;
using AssetStoreTools.Validator.TestDefinitions;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityObject = UnityEngine.Object;

namespace AssetStoreTools.Validator.TestMethods
{
    internal class CheckDemoScenes : ITestScript
    {
        private class DemoSceneScanResult
        {
            public List<UnityObject> ValidAdbScenes;
            public List<string> HybridScenePaths;
            public List<UnityObject> NestedUnityPackages;
        }

        private GenericTestConfig _config;
        private IAssetUtilityService _assetUtility;
        private ISceneUtilityService _sceneUtility;

        public CheckDemoScenes(GenericTestConfig config, IAssetUtilityService assetUtility, ISceneUtilityService sceneUtility)
        {
            _config = config;
            _assetUtility = assetUtility;
            _sceneUtility = sceneUtility;
        }

        public TestResult Run()
        {
            var result = new TestResult();
            var demoSceneScanResult = CheckForDemoScenes(_config);

            // Valid demo scenes were found in ADB
            if (demoSceneScanResult.ValidAdbScenes.Count > 0)
            {
                result.Status = TestResultStatus.Pass;
                result.AddMessage("Demo scenes found", null, demoSceneScanResult.ValidAdbScenes.ToArray());
                return result;
            }

            // Valid demo scenes found in UPM package.json
            if (demoSceneScanResult.HybridScenePaths.Count > 0)
            {
                result.Status = TestResultStatus.Pass;

                var upmSampleSceneList = string.Join("\n-", demoSceneScanResult.HybridScenePaths);
                upmSampleSceneList = upmSampleSceneList.Insert(0, "-");

                result.AddMessage($"Demo scenes found:\n{upmSampleSceneList}");
                return result;
            }

            // No valid scenes found, but package contains nested .unitypackages
            if (demoSceneScanResult.NestedUnityPackages.Count > 0)
            {
                result.Status = TestResultStatus.Warning;
                result.AddMessage("Could not find any valid Demo scenes in the selected validation paths.");
                result.AddMessage("The following nested .unitypackage files were found. " +
                    "If they contain any demo scenes, you can ignore this warning.", null, demoSceneScanResult.NestedUnityPackages.ToArray());
                return result;
            }

            // No valid scenes were found and there is nothing pointing to their inclusion in the package
            result.Status = TestResultStatus.VariableSeverityIssue;
            result.AddMessage("Could not find any valid Demo Scenes in the selected validation paths.");
            return result;
        }

        private DemoSceneScanResult CheckForDemoScenes(GenericTestConfig config)
        {
            var scanResult = new DemoSceneScanResult();
            scanResult.ValidAdbScenes = CheckForDemoScenesInAssetDatabase(config);
            scanResult.HybridScenePaths = CheckForDemoScenesInUpmSamples(config);
            scanResult.NestedUnityPackages = CheckForNestedUnityPackages(config);

            return scanResult;
        }

        private List<UnityObject> CheckForDemoScenesInAssetDatabase(GenericTestConfig config)
        {
            var scenePaths = _assetUtility.GetAssetPathsFromAssets(config.ValidationPaths, AssetType.Scene).ToArray();
            if (scenePaths.Length == 0)
                return new List<UnityObject>();

            var originalScenePath = _sceneUtility.CurrentScenePath;
            var validScenePaths = scenePaths.Where(CanBeDemoScene).ToArray();
            _sceneUtility.OpenScene(originalScenePath);

            if (validScenePaths.Length == 0)
                return new List<UnityObject>();

            return validScenePaths.Select(x => AssetDatabase.LoadAssetAtPath<UnityObject>(x)).ToList();
        }

        private bool CanBeDemoScene(string scenePath)
        {
            // Check skybox
            var sceneSkyboxPath = _assetUtility.ObjectToAssetPath(RenderSettings.skybox).Replace("\\", "").Replace("/", "");
            var defaultSkyboxPath = "Resources/unity_builtin_extra".Replace("\\", "").Replace("/", "");

            if (!sceneSkyboxPath.Equals(defaultSkyboxPath, StringComparison.OrdinalIgnoreCase))
                return true;

            // Check GameObjects
            _sceneUtility.OpenScene(scenePath);
            var rootObjects = _sceneUtility.GetRootGameObjects();
            var count = rootObjects.Length;

            if (count == 0)
                return false;

            if (count != 2)
                return true;

            var cameraGOUnchanged = rootObjects.Any(o => o.TryGetComponent<Camera>(out _) && o.GetComponents(typeof(Component)).Length == 3);
            var lightGOUnchanged = rootObjects.Any(o => o.TryGetComponent<Light>(out _) && o.GetComponents(typeof(Component)).Length == 2);

            return !cameraGOUnchanged || !lightGOUnchanged;
        }

        private List<string> CheckForDemoScenesInUpmSamples(GenericTestConfig config)
        {
            var scenePaths = new List<string>();

            foreach (var path in config.ValidationPaths)
            {
                if (!File.Exists($"{path}/package.json"))
                    continue;

                var packageJsonText = File.ReadAllText($"{path}/package.json");
                var json = JObject.Parse(packageJsonText);

                if (!json.ContainsKey("samples") || json["samples"].Type != JTokenType.Array || json["samples"].ToList().Count == 0)
                    continue;

                foreach (var sample in json["samples"].ToList())
                {
                    var samplePath = sample["path"].ToString();
                    samplePath = $"{path}/{samplePath}";
                    if (!Directory.Exists(samplePath))
                        continue;

                    var sampleScenePaths = Directory.GetFiles(samplePath, "*.unity", SearchOption.AllDirectories);
                    foreach (var scenePath in sampleScenePaths)
                    {
                        // If meta file is not found, the sample will not be included with the exported .unitypackage
                        if (!File.Exists($"{scenePath}.meta"))
                            continue;

                        if (!scenePaths.Contains(scenePath.Replace("\\", "/")))
                            scenePaths.Add(scenePath.Replace("\\", "/"));
                    }
                }
            }

            return scenePaths;
        }

        private List<UnityObject> CheckForNestedUnityPackages(GenericTestConfig config)
        {
            var unityPackages = _assetUtility.GetObjectsFromAssets(config.ValidationPaths, AssetType.UnityPackage).ToArray();
            return unityPackages.ToList();
        }
    }
}