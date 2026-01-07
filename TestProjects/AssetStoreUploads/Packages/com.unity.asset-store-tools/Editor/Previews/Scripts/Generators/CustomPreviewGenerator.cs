using AssetStoreTools.Previews.Data;
using AssetStoreTools.Previews.Generators;
using AssetStoreTools.Previews.Generators.Custom.Screenshotters;
using AssetStoreTools.Previews.Generators.Custom.TypeGenerators;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;

namespace AssetStoreTools.Previews
{
    internal class CustomPreviewGenerator : PreviewGeneratorBase
    {
        private CustomPreviewGenerationSettings _customSettings;

        public override event Action<float> OnProgressChanged;

        public CustomPreviewGenerator(CustomPreviewGenerationSettings settings)
            : base(settings)
        {
            _customSettings = settings;
        }

        protected override void Validate()
        {
            base.Validate();

            if (_customSettings.Width <= 0)
                throw new ArgumentException("Width should be larger than 0");

            if (_customSettings.Height <= 0)
                throw new ArgumentException("Height should be larger than 0");

            if (_customSettings.Depth <= 0)
                throw new ArgumentException("Depth should be larger than 0");

            if (_customSettings.NativeWidth <= 0)
                throw new ArgumentException("Native width should be larger than 0");

            if (_customSettings.NativeHeight <= 0)
                throw new ArgumentException("Native height should be larger than 0");
        }

        protected override async Task<PreviewGenerationResult> GenerateImpl()
        {
            var result = new PreviewGenerationResult()
            {
                GenerationType = _customSettings.GenerationType
            };

            OnProgressChanged?.Invoke(0f);

            var generatedPreviews = new List<PreviewMetadata>();
            var existingPreviews = GetExistingPreviews();
            var generators = CreateGenerators(existingPreviews);

            var currentGenerator = 0;
            Action<int, int> generatorProgressCallback = null;
            generatorProgressCallback = (currentAsset, totalAssets) => ReportProgress(currentGenerator, generators.Count(), currentAsset, totalAssets);

            try
            {
                foreach (var generator in generators)
                {
                    generator.OnAssetProcessed += generatorProgressCallback;
                    var typeGeneratorPreviews = await generator.Generate();
                    generatedPreviews.AddRange(typeGeneratorPreviews);
                    currentGenerator++;
                }

                AssetDatabase.Refresh();

                var allPreviews = new List<PreviewMetadata>();
                allPreviews.AddRange(generatedPreviews);
                allPreviews.AddRange(existingPreviews);

                result.Success = true;
                result.GeneratedPreviews = generatedPreviews;
                result.Previews = allPreviews;
            }
            catch (Exception e)
            {
                result.Success = false;
                result.Exception = e;
            }
            finally
            {
                foreach (var generator in generators)
                    generator.OnAssetProcessed -= generatorProgressCallback;
            }

            return result;
        }

        private IEnumerable<PreviewMetadata> GetExistingPreviews()
        {
            var existingPreviews = new List<PreviewMetadata>();

            if (Settings.OverwriteExisting || !CachingService.GetCachedMetadata(out var database))
                return existingPreviews;

            var inputGuids = AssetDatabase.FindAssets("", _customSettings.InputPaths);
            existingPreviews = database.Previews.Where(x => x.Type == GenerationType.Custom && x.Exists() && inputGuids.Any(y => y.Equals(x.Guid))).ToList();
            return existingPreviews;
        }

        private IEnumerable<ITypePreviewGenerator> CreateGenerators(IEnumerable<PreviewMetadata> existingPreviews)
        {
            var ignoredGuids = existingPreviews.Select(x => x.Guid).ToArray();

            var generators = new ITypePreviewGenerator[]
            {
                CreateAudioPreviewGenerator(ignoredGuids),
                CreateMaterialPreviewGenerator(ignoredGuids),
                CreateModelPreviewGenerator(ignoredGuids),
                CreatePrefabPreviewGenerator(ignoredGuids),
                CreateTexturePreviewGenerator(ignoredGuids)
            };

            return generators;
        }

        private ITypePreviewGenerator CreateAudioPreviewGenerator(string[] ignoredGuids)
        {
            var settings = new AudioTypeGeneratorSettings()
            {
                Width = _customSettings.Width,
                Height = _customSettings.Height,
                InputPaths = _customSettings.InputPaths,
                OutputPath = _customSettings.OutputPath,
                PreviewFileNamingFormat = _customSettings.PreviewFileNamingFormat,
                Format = _customSettings.Format,
                SampleColor = _customSettings.AudioSampleColor,
                BackgroundColor = _customSettings.AudioBackgroundColor,
                IgnoredGuids = ignoredGuids
            };

            return new AudioTypePreviewGenerator(settings);
        }

        private ITypePreviewGenerator CreateMaterialPreviewGenerator(string[] ignoredGuids)
        {
            var settings = CreateSceneGeneratorSettings(new MaterialScreenshotter(CreateScreenshotterSettings()), ignoredGuids);
            return new MaterialTypePreviewGenerator(settings);
        }

        private ITypePreviewGenerator CreateModelPreviewGenerator(string[] ignoredGuids)
        {
            var settings = CreateSceneGeneratorSettings(new MeshScreenshotter(CreateScreenshotterSettings()), ignoredGuids);
            return new ModelTypePreviewGenerator(settings);
        }

        private ITypePreviewGenerator CreatePrefabPreviewGenerator(string[] ignoredGuids)
        {
            var settings = CreateSceneGeneratorSettings(new MeshScreenshotter(CreateScreenshotterSettings()), ignoredGuids);
            return new PrefabTypePreviewGenerator(settings);
        }

        private ITypePreviewGenerator CreateTexturePreviewGenerator(string[] ignoredGuids)
        {
            var settings = new TextureTypeGeneratorSettings()
            {
                MaxWidth = _customSettings.Width,
                MaxHeight = _customSettings.Height,
                InputPaths = _customSettings.InputPaths,
                OutputPath = _customSettings.OutputPath,
                Format = _customSettings.Format,
                PreviewFileNamingFormat = _customSettings.PreviewFileNamingFormat,
                IgnoredGuids = ignoredGuids
            };

            return new TextureTypePreviewGenerator(settings);
        }

        private TypePreviewGeneratorFromSceneSettings CreateSceneGeneratorSettings(ISceneScreenshotter screenshotter, string[] ignoredGuids)
        {
            var settings = new TypePreviewGeneratorFromSceneSettings()
            {
                InputPaths = _customSettings.InputPaths,
                OutputPath = _customSettings.OutputPath,
                PreviewFileNamingFormat = _customSettings.PreviewFileNamingFormat,
                Screenshotter = screenshotter,
                IgnoredGuids = ignoredGuids
            };

            return settings;
        }

        private SceneScreenshotterSettings CreateScreenshotterSettings()
        {
            var settings = new SceneScreenshotterSettings()
            {
                Width = _customSettings.Width,
                Height = _customSettings.Height,
                Depth = _customSettings.Depth,
                Format = _customSettings.Format,
                NativeWidth = _customSettings.NativeWidth,
                NativeHeight = _customSettings.NativeHeight,
            };

            return settings;
        }

        private void ReportProgress(int currentGenerator, int totalGenerators, int currentGeneratorAsset, int totalCurrentGeneratorAssets)
        {
            var completedGeneratorProgress = (float)currentGenerator / totalGenerators;
            var currentGeneratorProgress = ((float)currentGeneratorAsset / totalCurrentGeneratorAssets) / totalGenerators;
            var progressToReport = completedGeneratorProgress + currentGeneratorProgress;
            OnProgressChanged?.Invoke(progressToReport);
        }
    }
}