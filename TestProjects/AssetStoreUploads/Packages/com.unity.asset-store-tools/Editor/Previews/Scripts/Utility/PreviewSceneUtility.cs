using System;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
#if AST_URP_AVAILABLE
using UnityEngine.Rendering.Universal;
#endif
#if AST_HDRP_AVAILABLE
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
#endif

namespace AssetStoreTools.Previews.Utility
{
    internal static class PreviewSceneUtility
    {
        private const string PreviewSceneName = "Preview Generation In Progress";
        private static readonly Color BackgroundColor = new Color(82f / 255, 82f / 255, 82f / 255);
        private static readonly Color BackgroundColorHDRP = new Color(38f / 255, 38f / 255, 38f / 255);

        public static async Task OpenPreviewSceneForCurrentPipeline()
        {
            // Wait for an Editor frame to avoid recursive player loop internal errors
            await WaitForEditorUpdate();

            switch (RenderPipelineUtility.GetCurrentPipeline())
            {
                case RenderPipeline.BiRP:
                    await OpenPreviewSceneBiRP();
                    break;
#if AST_URP_AVAILABLE
                case RenderPipeline.URP:
                    await OpenPreviewSceneURP();
                    break;
#endif
#if AST_HDRP_AVAILABLE
                case RenderPipeline.HDRP:
                    await OpenPreviewSceneHDRP();
                    break;
#endif
                default:
                    throw new NotImplementedException("Undefined Render Pipeline");
            }
        }

        private static async Task WaitForEditorUpdate()
        {
            var updateCalled = false;
            var delayCalled = false;

            void Update()
            {
                EditorApplication.update -= Update;
                updateCalled = true;
            }

            EditorApplication.update += Update;
            while (!updateCalled)
                await Task.Delay(10);

            void DelayCall()
            {
                EditorApplication.delayCall -= DelayCall;
                delayCalled = true;
            }

            EditorApplication.delayCall += DelayCall;
            while (!delayCalled)
                await Task.Delay(10);
        }

        public static async Task OpenPreviewSceneBiRP()
        {
            OpenNewScene();

            CreateSceneCamera();
            CreateSceneLighting();

            await WaitForLighting();
        }

        private static void OpenNewScene()
        {
            EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            scene.name = PreviewSceneName;
        }

        private static Camera CreateSceneCamera()
        {
            var cameraGO = new GameObject() { name = "Camera" };
            var camera = cameraGO.AddComponent<Camera>();
            camera.enabled = false;
            camera.tag = "MainCamera";

            camera.nearClipPlane = 0.01f;
            camera.farClipPlane = 100000;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = BackgroundColor;

            return camera;
        }

        private static Light CreateSceneLighting()
        {
            var lightGO = new GameObject() { name = "Lights" };
            lightGO.transform.rotation = Quaternion.Euler(45, 225, 0);
            var light = lightGO.AddComponent<Light>();
            light.intensity = 0.75f;
            light.type = LightType.Directional;
            light.shadows = LightShadows.None;

            return light;
        }

        private static async Task WaitForLighting()
        {
            while (!DynamicGI.isConverged)
                await Task.Delay(100);

            await Task.Yield();
        }

#if AST_URP_AVAILABLE
        public static async Task OpenPreviewSceneURP()
        {
            OpenNewScene();

            var camera = CreateSceneCamera();
            camera.gameObject.AddComponent<UniversalAdditionalCameraData>();

            var lighting = CreateSceneLighting();
            lighting.intensity = 0.5f;
            lighting.gameObject.AddComponent<UniversalAdditionalLightData>();

            await WaitForLighting();
        }
#endif

#if AST_HDRP_AVAILABLE
        public static async Task OpenPreviewSceneHDRP()
        {
            OpenNewScene();

            var camera = CreateSceneCamera();
            var cameraData = camera.gameObject.AddComponent<HDAdditionalCameraData>();
            cameraData.clearColorMode = HDAdditionalCameraData.ClearColorMode.Color;
            cameraData.backgroundColorHDR = BackgroundColorHDRP;

            var light = CreateSceneLighting();
            var lightData = light.gameObject.AddComponent<HDAdditionalLightData>();
            lightData.SetIntensity(5000, LightUnit.Lux);

            CreateHDRPVolumeProfile();

            await WaitForLighting();
        }

        private static Volume CreateHDRPVolumeProfile()
        {
            var volumeGO = new GameObject() { name = "Volume" };
            var volume = volumeGO.gameObject.AddComponent<Volume>();

            var profile = VolumeProfile.CreateInstance<VolumeProfile>();
            volume.profile = profile;
            volume.isGlobal = true;

            var exposure = profile.Add<Exposure>();
            exposure.active = true;

            exposure.mode.overrideState = true;
            exposure.mode.value = ExposureMode.Fixed;

            exposure.fixedExposure.overrideState = true;
            exposure.fixedExposure.value = 11;

            var fog = profile.Add<Fog>();
            fog.active = true;

            fog.enabled.overrideState = true;
            fog.enabled.value = false;

#if AST_HDRP_AVAILABLE_V12
            var volumetricClouds = profile.Add<VolumetricClouds>();
            volumetricClouds.active = true;

            volumetricClouds.enable.overrideState = true;
            volumetricClouds.enable.value = false;
#endif

            return volume;
        }
#endif
        }
}