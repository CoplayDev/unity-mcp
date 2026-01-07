using AssetStoreTools.Previews.Data;
using System;
using System.IO;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace AssetStoreTools.Previews.UI.Data
{
    internal class AssetPreview : IAssetPreview
    {
        private PreviewMetadata _metadata;

        private UnityEngine.Object _cachedAsset;
        private string _cachedAssetPath;
        private Texture2D _cachedTexture;

        public UnityEngine.Object Asset => _cachedAsset ?? (_cachedAsset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(AssetPath));
        public string AssetPath => _cachedAssetPath ?? (_cachedAssetPath = AssetDatabase.GUIDToAssetPath(_metadata.Guid));

        public AssetPreview(PreviewMetadata metadata)
        {
            _metadata = metadata;
        }

        public string GetAssetPath()
        {
            var assetPath = AssetDatabase.GUIDToAssetPath(_metadata.Guid);
            return assetPath;
        }

        public async Task LoadImage(Action<Texture2D> onSuccess)
        {
            if (_cachedTexture == null)
            {
                if (!_metadata.Exists())
                    return;

                await Task.Yield();

                try
                {
                    _cachedTexture = new Texture2D(1, 1);
                    _cachedTexture.LoadImage(File.ReadAllBytes(_metadata.Path));
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                    return;
                }
            }

            onSuccess?.Invoke(_cachedTexture);
        }
    }
}