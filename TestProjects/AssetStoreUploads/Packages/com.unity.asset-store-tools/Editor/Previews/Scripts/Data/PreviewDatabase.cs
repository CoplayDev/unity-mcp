using System.Collections.Generic;

namespace AssetStoreTools.Previews.Data
{
    internal class PreviewDatabase
    {
        public List<PreviewMetadata> Previews;

        public PreviewDatabase()
        {
            Previews = new List<PreviewMetadata>();
        }
    }
}