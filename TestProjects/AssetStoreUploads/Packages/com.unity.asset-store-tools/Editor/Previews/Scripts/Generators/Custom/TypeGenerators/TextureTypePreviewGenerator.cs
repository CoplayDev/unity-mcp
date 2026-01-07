using AssetStoreTools.Previews.Data;
using AssetStoreTools.Previews.Utility;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace AssetStoreTools.Previews.Generators.Custom.TypeGenerators
{
    internal class TextureTypePreviewGenerator : TypePreviewGeneratorBase
    {
        private TextureTypeGeneratorSettings _settings;

        public override event Action<int, int> OnAssetProcessed;

        public TextureTypePreviewGenerator(TextureTypeGeneratorSettings settings) : base(settings)
        {
            _settings = settings;
        }

        public override void ValidateSettings()
        {
            base.ValidateSettings();

            if (_settings.MaxWidth <= 0)
                throw new ArgumentException("Max width should be larger than 0");

            if (_settings.MaxHeight <= 0)
                throw new ArgumentException("Max height should be larger than 0");
        }

        protected override IEnumerable<UnityEngine.Object> CollectAssets()
        {
            var textures = new List<UnityEngine.Object>();
            var textureGuids = AssetDatabase.FindAssets("t:texture", Settings.InputPaths);

            foreach (var guid in textureGuids)
            {
                var texture = AssetDatabase.LoadAssetAtPath<Texture>(AssetDatabase.GUIDToAssetPath(guid));

                // Skip nested textures
                if (!AssetDatabase.IsMainAsset(texture))
                    continue;

                textures.Add(texture);
            }

            return textures;
        }

        protected override async Task<List<PreviewMetadata>> GenerateImpl(IEnumerable<UnityEngine.Object> assets)
        {
            var generatedPreviews = new List<PreviewMetadata>();
            var textures = assets.ToList();

            for (int i = 0; i < textures.Count; i++)
            {
                var texture = textures[i] as Texture2D;

                if (texture != null)
                {
                    Texture2D resizedTexture;
                    CalculateTextureSize(texture, out var resizeWidth, out var resizeHeight);

                    var importer = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(texture)) as TextureImporter;
                    if (importer != null && importer.textureType == TextureImporterType.NormalMap)
                        resizedTexture = GraphicsUtility.ResizeTextureNormalMap(texture, resizeWidth, resizeHeight);
                    else
                        resizedTexture = GraphicsUtility.ResizeTexture(texture, resizeWidth, resizeHeight);

                    var previewPath = GenerateOutputPathWithExtension(texture, _settings.PreviewFileNamingFormat, _settings.Format);

                    // Some textures may be transparent and need to be encoded as PNG to look correctly
                    var targetFormat = texture.alphaIsTransparency ? PreviewFormat.PNG : _settings.Format;
                    var bytes = PreviewConvertUtility.ConvertTexture(resizedTexture, targetFormat);

                    File.WriteAllBytes(previewPath, bytes);
                    generatedPreviews.Add(ObjectToMetadata(texture, previewPath));
                }

                OnAssetProcessed?.Invoke(i, textures.Count);
                await Task.Yield();
            }

            return generatedPreviews;
        }

        private void CalculateTextureSize(Texture2D texture, out int width, out int height)
        {
            if (texture.width <= _settings.MaxWidth && texture.height <= _settings.MaxHeight)
            {
                width = texture.width;
                height = texture.height;
                return;
            }

            var widthLongerThanHeight = texture.width > texture.height;

            if (widthLongerThanHeight)
            {
                var ratio = (float)texture.width / texture.height;
                width = _settings.MaxWidth;
                height = Mathf.RoundToInt(width / ratio);
            }
            else
            {
                var ratio = (float)texture.height / texture.width;
                height = _settings.MaxHeight;
                width = Mathf.RoundToInt(height / ratio);
            }
        }
    }
}