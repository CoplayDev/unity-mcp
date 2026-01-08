using AssetStoreTools.Validator.Data;
using AssetStoreTools.Validator.Services.Validation;
using AssetStoreTools.Validator.TestDefinitions;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace AssetStoreTools.Validator.TestMethods
{
    internal class CheckProjectTemplateAssets : ITestScript
    {
        private GenericTestConfig _config;
        private IAssetUtilityService _assetUtility;

        // Constructor also accepts dependency injection of registered IValidatorService types
        public CheckProjectTemplateAssets(GenericTestConfig config, IAssetUtilityService assetUtility)
        {
            _config = config;
            _assetUtility = assetUtility;
        }

        public TestResult Run()
        {
            var result = new TestResult() { Status = TestResultStatus.Undefined };

            var assets = _assetUtility.GetObjectsFromAssets<Object>(_config.ValidationPaths, AssetType.All);
            var invalidAssetsByGuid = CheckGuids(assets);
            var invalidAssetsByPath = CheckPaths(assets);

            var hasIssues = invalidAssetsByGuid.Length > 0
                || invalidAssetsByPath.Length > 0;

            if (hasIssues)
            {
                result.Status = TestResultStatus.VariableSeverityIssue;

                if (invalidAssetsByPath.Length > 0)
                {
                    result.AddMessage("The following assets were found to have an asset path which is common to project template asset paths. They should be renamed or moved:", null, invalidAssetsByPath);
                }

                if (invalidAssetsByGuid.Length > 0)
                {
                    result.AddMessage("The following assets were found to be using a GUID which is common to project template asset GUIDs. They should be assigned a new GUID:", null, invalidAssetsByGuid);
                }
            }
            else
            {
                result.Status = TestResultStatus.Pass;
                result.AddMessage("No common assets that might cause asset clashing were found!");
            }

            return result;
        }

        private Object[] CheckGuids(IEnumerable<Object> assets)
        {
            var clashingAssets = new List<Object>();
            foreach (var asset in assets)
            {
                if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset, out var guid, out long _))
                    continue;

                if (CommonTemplateAssets.Any(x => x.Key.Equals(guid, System.StringComparison.OrdinalIgnoreCase)))
                    clashingAssets.Add(asset);
            }

            return clashingAssets.ToArray();
        }

        private Object[] CheckPaths(IEnumerable<Object> assets)
        {
            var clashingAssets = new List<Object>();
            foreach (var asset in assets)
            {
                var assetPath = AssetDatabase.GetAssetPath(asset);
                if (CommonTemplateAssets.Any(x => x.Value.Equals(assetPath, System.StringComparison.OrdinalIgnoreCase)))
                    clashingAssets.Add(asset);
            }

            return clashingAssets.ToArray();
        }

        private Dictionary<string, string> CommonTemplateAssets = new Dictionary<string, string>()
        {
            {"3f9215ea0144899419cfbc0957140d3f", "Assets/DefaultVolumeProfile.asset"},
            {"3d4c13846a3e9bd4c8ccfbd0657ed847", "Assets/DefaultVolumeProfile.asset"},
            {"4cee8bca36f2ab74b8feb832747fa6f4", "Assets/Editor/com.unity.mobile.notifications/NotificationSettings.asset"},
            {"45a04f37e0f48c744acc0874c4a8918a", "Assets/Editor/com.unity.mobile.notifications/NotificationSettings.asset"},
            {"54a3a0570aebe8949bec4966f1376581", "Assets/HDRPDefaultResources/DefaultHDRISky.exr"},
            {"e93c35b24eb03c74284e7dc0b755bfcc", "Assets/HDRPDefaultResources/DefaultHDRPAsset.asset"},
            {"254320a857a30444da2c99496a186368", "Assets/HDRPDefaultResources/DefaultLookDevProfile.asset"},
            {"2bfa7b9d63fa79e4abdc033f54a868d2", "Assets/HDRPDefaultResources/DefaultSceneRoot.prefab"},
            {"f9e3ff5a1b8f49c4fa8686e68d2dadae", "Assets/HDRPDefaultResources/DefaultSceneRoot.prefab"},
            {"d87f7d7815073e840834a16a518c1237", "Assets/HDRPDefaultResources/DefaultSettingsVolumeProfile.asset"},
            {"145290c901d58b343bdeb3b4362c9ff2", "Assets/HDRPDefaultResources/DefaultVFXResources.asset"},
            {"acc11144f57719542b5fa25f02e74afb", "Assets/HDRPDefaultResources/HDRenderPipelineGlobalSettings.asset"},
            {"582adbd84082fdb4faf7cd4beb1ccd14", "Assets/HDRPDefaultResources/HDRPDefaultSettings.asset"},
            {"2801c2ff7303a7543a8727f862f6c236", "Assets/HDRPDefaultResources/Sky and Fog Settings Profile.asset"},
            {"ea5c25297f0c0a04da0eabb1c26a7509", "Assets/HDRPDefaultResources/SkyFogSettingsProfile.asset"},
            {"3590b91b4603b465dbb4216d601bff33", "Assets/InputSystem_Actions.inputactions"},
            {"289c1b55c9541489481df5cc06664110", "Assets/InputSystem_Actions.inputactions"},
            {"dc70d2c4f369241dd99afd7c451b813e", "Assets/InputSystem_Actions.inputactions"},
            {"2bcd2660ca9b64942af0de543d8d7100", "Assets/InputSystem_Actions.inputactions"},
            {"052faaac586de48259a63d0c4782560b", "Assets/InputSystem_Actions.inputactions"},
            {"35845fe01580c41289b024647b1d1c53", "Assets/InputSystem_Actions.inputactions"},
            {"8124e5870f4fd4c779e7a5f994e84ad1", "Assets/OutdoorsScene.unity"},
            {"2dd802e4d37c65149922028d3e973832", "Assets/Presets/AudioCompressedInMemory.preset"},
            {"e18fd6ecd9cdb524ca99844f39b9d9ac", "Assets/Presets/AudioCompressedInMemory.preset"},
            {"86bcce7f5575b54408aa0f3a7d321039", "Assets/Presets/AudioStreaming.preset"},
            {"460e573eb8466884baaa0b8475505f83", "Assets/Presets/AudioStreaming.preset"},
            {"e8537455c6c08bd4e8bf0be3707da685", "Assets/Presets/Defaults/AlbedoTexture_Default.preset"},
            {"7a99f8aa944efe94cb9bd74562b7d5f9", "Assets/Presets/Defaults/AlbedoTexture_Default.preset"},
            {"0cd792cc87e492d43b4e95b205fc5cc6", "Assets/Presets/Defaults/AudioDecompressOnLoad_Default.preset"},
            {"e7689051185d12f4298e1ebb2693a29f", "Assets/Presets/Defaults/AudioDecompressOnLoad.preset"},
            {"463065d4f17d1d94d848aa127b94dd43", "Assets/Presets/Defaults/DirectionalLight_Default.preset"},
            {"c1cf8506f04ef2c4a88b64b6c4202eea", "Assets/Presets/Defaults/DirectionalLight_Default.preset"},
            {"8fa3055e2a1363246838debd20206d37", "Assets/Presets/Defaults/SSSSettings_Default.preset"},
            {"78830bb1431cab940b74be615e2a739f", "Assets/Presets/HDRTexture.preset"},
            {"14a57cf3b9fa1c74b884aa7e0dcf1faa", "Assets/Presets/NormalTexture.preset"},
            {"1d826a4c23450f946b19c20560595a1f", "Assets/Presets/NormalTexture.preset"},
            {"45f7b2e3c78185248b3adbb14429c2ab", "Assets/Presets/UtilityTexture.preset"},
            {"78fae3569c6c66c46afc3d9d4fb0b8d4", "Assets/Presets/UtilityTexture.preset"},
            {"9303d565bd8aa6948ba775e843320e4d", "Assets/Presets/UtilityTexture.preset"},
            {"34f54ff1ff9415249a847506b6f2fec5", "Assets/Scenes/PrefabEditingScene.unity"},
            {"cbfe36cfddfde964d9dfce63a355d5dd", "Assets/Scenes/samplescene.unity"},
            {"2cda990e2423bbf4892e6590ba056729", "Assets/Scenes/SampleScene.unity"},
            {"9fc0d4010bbf28b4594072e72b8655ab", "Assets/Scenes/SampleScene.unity"},
            {"3db1837cc97a95e4c98610966fac2b0b", "Assets/Scenes/SampleScene.unity"},
            {"3fc8acdd13e6c734bafef6554d6fdbcd", "Assets/Scenes/SampleScene.unity"},
            {"8c9cfa26abfee488c85f1582747f6a02", "Assets/Scenes/SampleScene.unity"},
            {"c850ee8c3b14cc8459e7e186857cf567", "Assets/Scenes/SampleScene.unity"},
            {"99c9720ab356a0642a771bea13969a05", "Assets/Scenes/SampleScene.unity"},
            {"d1c3109bdb54ad54c8a2b2838528e640", "Assets/Scenes/SampleScene.unity"},
            {"477cc4148fad3449482a3bc3178594e2", "Assets/Scenes/SampleSceneLightingSettings.lighting"},
            {"4eb578550bc4f824e97f0a72eac1f3a5", "Assets/Scripts/LookWithMouse.cs"},
            {"87f6dfceb3e39a947a312f7eeaa2a113", "Assets/Scripts/PlayerMovement.cs"},
            {"be76e5f14cfee674cb30b491fb72b09b", "Assets/Scripts/SimpleCameraController.cs"},
            {"6547d18b2bc62c94aa5ec1e87434da4e", "Assets/Scripts/SimpleCameraController.cs"},
            {"e8a636f62116c0a40bbfefdf876d4608", "Assets/Scripts/SimpleCameraController.cs"},
            {"14e519c409be4a1428028347410f5677", "Assets/Scripts/SimpleCameraController.cs"},
            {"a04c28107d77d5e42b7155783b8475b6", "Assets/Settings/Cockpit_Renderer.asset"},
            {"ab09877e2e707104187f6f83e2f62510", "Assets/Settings/DefaultVolumeProfile.asset"},
            {"238cd62f6b58cb04e9c94749c4a015a7", "Assets/Settings/DefaultVolumeProfile.asset"},
            {"5a9a2dc462c7bde4f86d0615a19c2c72", "Assets/Settings/DiffusionProfiles/BambooLeaves.asset"},
            {"78322c7f82657514ebe48203160e3f39", "Assets/Settings/Foliage.asset"},
            {"3e2e6bfc59709614ab90c0cd7d755e48", "Assets/Settings/HDRP Balanced.asset"},
            {"36dd385e759c96147b6463dcd1149c11", "Assets/Settings/HDRP High Fidelity.asset"},
            {"168a2336534e4e043b2a210b6f8d379a", "Assets/Settings/HDRP Performant.asset"},
            {"4594f4a3fb14247e192bcca6dc23c8ed", "Assets/Settings/HDRPDefaultResources/DefaultLookDevProfile.asset"},
            {"14b392ee213d25a48b1feddbd9f5a9be", "Assets/Settings/HDRPDefaultResources/DefaultSettingsVolumeProfile.asset"},
            {"879ffae44eefa4412bb327928f1a96dd", "Assets/Settings/HDRPDefaultResources/FoliageDiffusionProfile.asset"},
            {"b9f3086da92434da0bc1518f19f0ce86", "Assets/Settings/HDRPDefaultResources/HDRenderPipelineAsset.asset"},
            {"ac0316ca287ba459492b669ff1317a6f", "Assets/Settings/HDRPDefaultResources/HDRenderPipelineGlobalSettings.asset"},
            {"48e911a1e337b44e2b85dbc65b47a594", "Assets/Settings/HDRPDefaultResources/SkinDiffusionProfile.asset"},
            {"d03ed43fc9d8a4f2e9fa70c1c7916eb9", "Assets/Settings/Lit2DSceneTemplate.scenetemplate"},
            {"65bc7dbf4170f435aa868c779acfb082", "Assets/Settings/Mobile_Renderer.asset"},
            {"5e6cbd92db86f4b18aec3ed561671858", "Assets/Settings/Mobile_RPAsset.asset"},
            {"23cccccf13c3d4170a9b21e52a9bc86b", "Assets/Settings/Mobile/Mobile_High_ScreenRenderer.asset"},
            {"6e8f76111115f44e0a76c2bff3cec258", "Assets/Settings/Mobile/Mobile_Low_Renderer.asset"},
            {"aed30aee6a3ceae4090dadd1934d2ad0", "Assets/Settings/Mobile/Mobile_Low_ScreenRenderer.asset"},
            {"d7686b11d09df481bac3c76ecc5ea626", "Assets/Settings/Mobile/Mobile_Low.asset"},
            {"f288ae1f4751b564a96ac7587541f7a2", "Assets/Settings/PC_Renderer.asset"},
            {"4b83569d67af61e458304325a23e5dfd", "Assets/Settings/PC_RPAsset.asset"},
            {"42b230d443c6d6c4b89c47f97db59121", "Assets/Settings/PC/PC_High_ScreenRenderer.asset"},
            {"13ba41cd2fa191f43890b271bd110ed9", "Assets/Settings/PC/PC_Low_Renderer.asset"},
            {"a73f6fa069dd14a42b40cbb01bae63b4", "Assets/Settings/PC/PC_Low_ScreenRenderer.asset"},
            {"4eb9ff6b5314098428cfa0be7e36ccda", "Assets/Settings/PC/PC_Low.asset"},
            {"573ac53c334415945bf239de2c2f0511", "Assets/Settings/PlayerControllerFPS.prefab"},
            {"7ba2b06fb32e5274aad88925a5b8d3f5", "Assets/Settings/PostProcessVolumeProfile.asset"},
            {"424799608f7334c24bf367e4bbfa7f9a", "Assets/Settings/Renderer2D.asset"},
            {"183cbd347d25080429f42b520742bbd8", "Assets/Settings/SampleScenePostProcessingSettings.asset"},
            {"10fc4df2da32a41aaa32d77bc913491c", "Assets/Settings/SampleSceneProfile.asset"},
            {"a6560a915ef98420e9faacc1c7438823", "Assets/Settings/SampleSceneProfile.asset"},
            {"a123fc0ac58cb774e8592c925f167e7c", "Assets/Settings/SampleSceneSkyandFogSettings.asset"},
            {"26bdddf49760c61438938733f07fa2a2", "Assets/Settings/Skin.asset"},
            {"8ba92e2dd7f884a0f88b98fa2d235fe7", "Assets/Settings/SkyandFogSettingsProfile.asset"},
            {"4a8e21d5c33334b11b34a596161b9360", "Assets/Settings/UniversalRenderer.asset"},
            {"18dc0cd2c080841dea60987a38ce93fa", "Assets/Settings/UniversalRenderPipelineGlobalSettings.asset"},
            {"bdede76083021864d8ff8bf23b2f37f1", "Assets/Settings/UniversalRenderPipelineGlobalSettings.asset"},
            {"19ba41d7c0026c3459d37c2fe90c55a0", "Assets/Settings/UniversalRP-HighQuality.asset"},
            {"a31e9f9f9c9d4b9429ed0d1234e22103", "Assets/Settings/UniversalRP-LowQuality.asset"},
            {"d847b876476d3d6468f5dfcd34266f96", "Assets/Settings/UniversalRP-MediumQuality.asset"},
            {"681886c5eb7344803b6206f758bf0b1c", "Assets/Settings/UniversalRP.asset"},
            {"e634585d5c4544dd297acaee93dc2beb", "Assets/Settings/URP-Balanced-Renderer.asset"},
            {"e1260c1148f6143b28bae5ace5e9c5d1", "Assets/Settings/URP-Balanced.asset"},
            {"c40be3174f62c4acf8c1216858c64956", "Assets/Settings/URP-HighFidelity-Renderer.asset"},
            {"7b7fd9122c28c4d15b667c7040e3b3fd", "Assets/Settings/URP-HighFidelity.asset"},
            {"707360a9c581a4bd7aa53bfeb1429f71", "Assets/Settings/URP-Performant-Renderer.asset"},
            {"d0e2fc18fe036412f8223b3b3d9ad574", "Assets/Settings/URP-Performant.asset"},
            {"b62413aeefabaaa41a4b5a71dd7ae1ac", "Assets/Settings/VolumeProfiles/CinematicProfile.asset"},
            {"ac0c2cad5778d4544b6a690963e02fe3", "Assets/Settings/VolumeProfiles/DefaultVolumeProfile.asset"},
            {"f2d4d916a6612574cad220d125febbf2", "Assets/Settings/VolumeProfiles/LowQualityVolumeProfile.asset"},
            {"cef078630d63d0442a070f84d4f13735", "Assets/Settings/VolumeProfiles/MarketProfile.asset"},
            {"7ede9c9f109e5c442b7d29e54b4996fc", "Assets/Settings/VolumeProfiles/MediaOverrides.asset"},
            {"3532e98caae428047bcefe69a344f72c", "Assets/Settings/VolumeProfiles/OutlineEnabled.asset"},
            {"bfc08ba7e35de1a44bb84a32f1a693e1", "Assets/Settings/VolumeProfiles/ZenGardenProfile.asset"},
            {"59a34a3881431c246b3564a0f0ca5bb0", "Assets/Settings/Volumes/CinematicPhysicalCamera.asset"},
            {"03bc34b71695890468eb021c73b228db", "Assets/Settings/Volumes/ScreenshotsTimelineProfile.asset"},
            {"7f342610b85f4164f808a1f380dcc668", "Assets/Settings/Volumes/VolumeGlobal.asset"},
            {"bd6d234073408c44ca3828113aac655e", "Assets/Settings/Volumes/VolumeRoom1.asset"},
            {"d78a1b031ab26034eb6ec3cbc9fbcec3", "Assets/Settings/Volumes/VolumeRoom2.asset"},
            {"5727d3e07f75c3744b6cc8a1e55850a9", "Assets/Settings/Volumes/VolumeRoom2Skylight.asset"},
            {"06114ad16a0bc0a41957375ac3bf472e", "Assets/Settings/Volumes/VolumeRoom3.asset"},
            {"1584bf21cf81d5147aa00e8a2deaf2fb", "Assets/Settings/Volumes/VolumeRoom3Corridor.asset"},
            {"7bca3a07cdd522c4c8020832c20b3eae", "Assets/Settings/Volumes/VolumeRoom3Sitting.asset"},
            {"2872d90954412244a8b4a477b939c3ca", "Assets/Settings/XR/Loaders/Mock_HMD_Loader.asset"},
            {"f25758a0f79593d4a9b3ee30a17b4c2e", "Assets/Settings/XR/Loaders/Oculus_Loader.asset"},
            {"9e9f2958d1b4b4642ace1d0c7770650b", "Assets/Settings/XR/Settings/Mock_HMD_Build_Settings.asset"},
            {"290a6e6411d135049940bec2237b8938", "Assets/Settings/XR/Settings/Oculus_Settings.asset"},
            {"4c1640683c539c14080cfd43fbeffbda", "Assets/Settings/XR/XRGeneralSettings.asset"},
            {"93b439a37f63240aca3dd4e01d978a9f", "Assets/UniversalRenderPipelineGlobalSettings.asset"},
            {"38b35347542e5af4c9b140950c5b18db", "Assets/UniversalRenderPipelineGlobalSettings.asset"}
        };
    }
}
