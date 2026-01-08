using AssetStoreTools.Previews.Data;
using AssetStoreTools.Previews.Services;
using System;
using System.Threading.Tasks;

namespace AssetStoreTools.Previews.Generators
{
    internal abstract class PreviewGeneratorBase : IPreviewGenerator
    {
        public PreviewGenerationSettings Settings { get; }
        protected ICachingService CachingService;

        public abstract event Action<float> OnProgressChanged;

        public PreviewGeneratorBase(PreviewGenerationSettings settings)
        {
            Settings = settings;
            CachingService = PreviewServiceProvider.Instance.GetService<ICachingService>();
        }

        public async Task<PreviewGenerationResult> Generate()
        {
            Validate();

            var result = await GenerateImpl();
            if (result.Success)
            {
                CachingService.CacheMetadata(result.GeneratedPreviews);
            }

            return result;
        }

        protected virtual void Validate()
        {
            if (Settings.InputPaths == null || Settings.InputPaths.Length == 0)
                throw new ArgumentException("Input paths cannot be null");

            if (string.IsNullOrEmpty(Settings.OutputPath))
                throw new ArgumentException("Output path cannot be null");
        }

        protected abstract Task<PreviewGenerationResult> GenerateImpl();
    }
}