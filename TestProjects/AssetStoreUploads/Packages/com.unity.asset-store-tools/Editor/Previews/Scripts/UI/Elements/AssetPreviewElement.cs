using AssetStoreTools.Previews.UI.Data;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace AssetStoreTools.Previews.UI.Elements
{
    internal class AssetPreviewElement : VisualElement
    {
        // Data
        private IAssetPreview _assetPreview;

        // UI
        private Image _image;
        private Label _label;

        public AssetPreviewElement()
        {
            AddToClassList("preview-list-image");

            Create();

            RegisterCallback<MouseDownEvent>(OnImageClicked);
        }

        private void Create()
        {
            CreateFiller();
            CreateImage();
            CreateLabel();
        }

        private void CreateImage()
        {
            _image = new Image();
            Add(_image);
        }

        private void CreateFiller()
        {
            var filler = new VisualElement() { name = "Filler" };
            Add(filler);
        }

        private void CreateLabel()
        {
            _label = new Label();
            Add(_label);
        }

        private void SetImage(Texture2D texture)
        {
            _image.style.width = texture.width < 128 ? texture.width : 128;
            _image.style.height = texture.height < 128 ? texture.height : 128;
            _image.style.backgroundImage = texture;
        }

        private void OnImageClicked(MouseDownEvent _)
        {
            EditorGUIUtility.PingObject(_assetPreview.Asset);
        }

        public void SetSource(IAssetPreview assetPreview)
        {
            _assetPreview = assetPreview;
            _assetPreview.LoadImage(SetImage);

            var assetPath = _assetPreview.GetAssetPath();

            if (string.IsNullOrEmpty(assetPath))
            {
                _label.text = "[Missing]";
                tooltip = "This asset has been deleted";
                return;
            }

            var assetNameWithExtension = assetPath.Split('/').Last();
            _label.text = assetNameWithExtension;
            tooltip = assetPath;
        }
    }
}