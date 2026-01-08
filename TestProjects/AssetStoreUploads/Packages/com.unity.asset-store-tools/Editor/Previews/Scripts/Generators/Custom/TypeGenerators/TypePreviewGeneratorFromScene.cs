using AssetStoreTools.Previews.Data;
using AssetStoreTools.Previews.Utility;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AssetStoreTools.Previews.Generators.Custom.TypeGenerators
{
    internal abstract class TypePreviewGeneratorFromScene : TypePreviewGeneratorBase
    {
        protected new TypePreviewGeneratorFromSceneSettings Settings;

        private CancellationTokenSource _cancellationTokenSource;

        public TypePreviewGeneratorFromScene(TypePreviewGeneratorFromSceneSettings settings) : base(settings)
        {
            Settings = settings;
        }

        public override void ValidateSettings()
        {
            base.ValidateSettings();

            if (Settings.Screenshotter == null)
                throw new ArgumentException("Screenshotter cannot be null");
        }

        protected sealed override async Task<List<PreviewMetadata>> GenerateImpl(IEnumerable<UnityEngine.Object> assets)
        {
            var originalScenePath = EditorSceneManager.GetActiveScene().path;
            await PreviewSceneUtility.OpenPreviewSceneForCurrentPipeline();

            try
            {
                _cancellationTokenSource = new CancellationTokenSource();
                EditorSceneManager.sceneOpened += SceneOpenedDuringGeneration;
                return await GeneratePreviewsInScene(assets);
            }
            finally
            {
                EditorSceneManager.sceneOpened -= SceneOpenedDuringGeneration;
                _cancellationTokenSource.Dispose();
                if (!string.IsNullOrEmpty(originalScenePath))
                    EditorSceneManager.OpenScene(originalScenePath);
            }
        }

        protected abstract Task<List<PreviewMetadata>> GeneratePreviewsInScene(IEnumerable<UnityEngine.Object> assets);

        private void SceneOpenedDuringGeneration(Scene _, OpenSceneMode __)
        {
            if (!_cancellationTokenSource.IsCancellationRequested)
                _cancellationTokenSource.Cancel();
        }

        protected void ThrowIfSceneChanged()
        {
            if (_cancellationTokenSource.Token.IsCancellationRequested)
                throw new Exception("Preview generation was aborted due to a change of the scene");
        }

        protected Shader GetDefaultObjectShader()
        {
            switch (RenderPipelineUtility.GetCurrentPipeline())
            {
                case RenderPipeline.BiRP:
                    return Shader.Find("Standard");
                case RenderPipeline.URP:
                    return Shader.Find("Universal Render Pipeline/Lit");
                case RenderPipeline.HDRP:
                    return Shader.Find("HDRP/Lit");
                default:
                    throw new NotImplementedException("Undefined Render Pipeline");
            }
        }

        protected Shader GetDefaultParticleShader()
        {
            switch (RenderPipelineUtility.GetCurrentPipeline())
            {
                case RenderPipeline.BiRP:
                    return Shader.Find("Particles/Standard Unlit");
                case RenderPipeline.URP:
                    return Shader.Find("Universal Render Pipeline/Particles/Unlit");
                case RenderPipeline.HDRP:
                    return Shader.Find("HDRP/Unlit");
                default:
                    throw new NotImplementedException("Undefined Render Pipeline");
            }
        }

        protected Shader GetDefaultTextureShader()
        {
            switch (RenderPipelineUtility.GetCurrentPipeline())
            {
                case RenderPipeline.BiRP:
                    return Shader.Find("Unlit/Texture");
                case RenderPipeline.URP:
                    return Shader.Find("Universal Render Pipeline/Unlit");
                case RenderPipeline.HDRP:
                    return Shader.Find("HDRP/Unlit");
                default:
                    throw new NotImplementedException("Undefined Render Pipeline");
            }
        }
    }
}