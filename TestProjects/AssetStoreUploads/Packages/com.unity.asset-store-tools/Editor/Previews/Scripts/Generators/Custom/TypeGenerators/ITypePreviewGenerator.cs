using AssetStoreTools.Previews.Data;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AssetStoreTools.Previews.Generators.Custom.TypeGenerators
{
    internal interface ITypePreviewGenerator
    {
        TypeGeneratorSettings Settings { get; }

        event Action<int, int> OnAssetProcessed;

        Task<List<PreviewMetadata>> Generate();
    }
}