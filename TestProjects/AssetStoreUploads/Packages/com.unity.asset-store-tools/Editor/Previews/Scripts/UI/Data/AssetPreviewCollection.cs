using AssetStoreTools.Previews.Data;
using System;
using System.Collections.Generic;

namespace AssetStoreTools.Previews.UI.Data
{
    internal class AssetPreviewCollection : IAssetPreviewCollection
    {
        private GenerationType _generationType;
        private List<IAssetPreview> _images;

        public event Action OnCollectionChanged;

        public AssetPreviewCollection()
        {
            _images = new List<IAssetPreview>();
        }

        public GenerationType GetGenerationType()
        {
            return _generationType;
        }

        public IEnumerable<IAssetPreview> GetPreviews()
        {
            return _images;
        }

        public void Refresh(GenerationType generationType, IEnumerable<PreviewMetadata> previews)
        {
            _images.Clear();

            _generationType = generationType;

            foreach (var entry in previews)
            {
                if (!entry.Exists())
                    continue;

                _images.Add(new AssetPreview(entry));
            }

            OnCollectionChanged?.Invoke();
        }
    }
}