using AssetStoreTools.Previews.Data;
using System.Collections.Generic;

namespace AssetStoreTools.Previews.Services
{
    internal interface ICachingService : IPreviewService
    {
        void CacheMetadata(IEnumerable<PreviewMetadata> previews);
        bool GetCachedMetadata(out PreviewDatabase previewDatabase);
    }
}