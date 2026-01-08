using System;
using System.Collections.Generic;

namespace AssetStoreTools.Previews.Data
{
    internal class PreviewGenerationResult
    {
        public GenerationType GenerationType;
        public bool Success;
        public IEnumerable<PreviewMetadata> GeneratedPreviews;
        public IEnumerable<PreviewMetadata> Previews;
        public Exception Exception;
    }
}