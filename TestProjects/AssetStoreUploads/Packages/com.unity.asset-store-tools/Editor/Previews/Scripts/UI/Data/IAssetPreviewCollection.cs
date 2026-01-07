using AssetStoreTools.Previews.Data;
using System;
using System.Collections.Generic;

namespace AssetStoreTools.Previews.UI.Data
{
    internal interface IAssetPreviewCollection
    {
        event Action OnCollectionChanged;

        GenerationType GetGenerationType();
        IEnumerable<IAssetPreview> GetPreviews();
        void Refresh(GenerationType generationType, IEnumerable<PreviewMetadata> previews);
    }
}