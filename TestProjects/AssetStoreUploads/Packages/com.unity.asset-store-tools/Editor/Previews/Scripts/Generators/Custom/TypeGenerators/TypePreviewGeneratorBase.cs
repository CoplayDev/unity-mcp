using AssetStoreTools.Previews.Data;
using AssetStoreTools.Previews.Utility;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;

namespace AssetStoreTools.Previews.Generators.Custom.TypeGenerators
{
    internal abstract class TypePreviewGeneratorBase : ITypePreviewGenerator
    {
        public TypeGeneratorSettings Settings { get; }

        public abstract event Action<int, int> OnAssetProcessed;

        public TypePreviewGeneratorBase(TypeGeneratorSettings settings)
        {
            Settings = settings;
        }

        public virtual void ValidateSettings()
        {
            if (Settings.InputPaths == null || Settings.InputPaths.Length == 0)
                throw new ArgumentException("Input path cannot be null");

            foreach (var path in Settings.InputPaths)
            {
                var inputPath = path.EndsWith("/") ? path.Remove(path.Length - 1) : path;
                if (!AssetDatabase.IsValidFolder(inputPath))
                    throw new ArgumentException($"Input path '{inputPath}' is not a valid ADB folder");
            }

            if (string.IsNullOrEmpty(Settings.OutputPath))
                throw new ArgumentException("Output path cannot be null");
        }

        public async Task<List<PreviewMetadata>> Generate()
        {
            var generatedPreviews = new List<PreviewMetadata>();
            ValidateSettings();

            var assets = CollectAssets();
            assets = FilterIgnoredAssets(assets);

            if (assets.Count() == 0)
                return generatedPreviews;

            return await GenerateImpl(assets);
        }

        protected abstract IEnumerable<UnityEngine.Object> CollectAssets();

        private IEnumerable<UnityEngine.Object> FilterIgnoredAssets(IEnumerable<UnityEngine.Object> assets)
        {
            if (Settings.IgnoredGuids == null || Settings.IgnoredGuids.Length == 0)
                return assets;

            var filteredAssets = new List<UnityEngine.Object>();
            foreach (var asset in assets)
            {
                if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset, out var guid, out long _))
                    continue;

                if (Settings.IgnoredGuids.Any(x => x == guid))
                    continue;

                filteredAssets.Add(asset);
            }

            return filteredAssets;
        }

        protected abstract Task<List<PreviewMetadata>> GenerateImpl(IEnumerable<UnityEngine.Object> assets);

        protected PreviewMetadata ObjectToMetadata(UnityEngine.Object obj, string previewPath)
        {
            if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(obj, out var guid, out long _))
                throw new Exception($"Could not retrieve guid for object {obj}");

            return new PreviewMetadata()
            {
                Type = GenerationType.Custom,
                Guid = guid,
                Name = obj.name,
                Path = previewPath
            };
        }

        protected string GenerateOutputPathWithoutExtension(UnityEngine.Object asset, FileNameFormat fileNameFormat)
        {
            PrepareOutputFolder(Settings.OutputPath, false);
            var directoryPath = Settings.OutputPath;
            var fileName = PreviewConvertUtility.ConvertFilename(asset, fileNameFormat);
            var fullPath = $"{directoryPath}/{fileName}";

            return fullPath;
        }

        protected string GenerateOutputPathWithExtension(UnityEngine.Object asset, FileNameFormat fileNameFormat, PreviewFormat previewFormat)
        {
            var partialOutputPath = GenerateOutputPathWithoutExtension(asset, fileNameFormat);
            var extension = PreviewConvertUtility.ConvertExtension(previewFormat);

            return $"{partialOutputPath}.{extension}";
        }

        private void PrepareOutputFolder(string outputPath, bool cleanup)
        {
            var dir = new DirectoryInfo(outputPath);

            if (!dir.Exists)
            {
                dir.Create();
                return;
            }

            if (!cleanup)
                return;

            dir.Delete(true);
            dir.Create();
        }
    }
}