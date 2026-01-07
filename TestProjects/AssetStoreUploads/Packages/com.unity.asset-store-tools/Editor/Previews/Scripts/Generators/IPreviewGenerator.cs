using AssetStoreTools.Previews.Data;
using System;
using System.Threading.Tasks;

namespace AssetStoreTools.Previews.Generators
{
    internal interface IPreviewGenerator
    {
        PreviewGenerationSettings Settings { get; }

        event Action<float> OnProgressChanged;

        Task<PreviewGenerationResult> Generate();
    }
}