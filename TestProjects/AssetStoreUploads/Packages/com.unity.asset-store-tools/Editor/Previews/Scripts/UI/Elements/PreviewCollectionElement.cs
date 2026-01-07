using AssetStoreTools.Previews.Data;
using AssetStoreTools.Previews.UI.Data;
using System.Linq;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace AssetStoreTools.Previews.UI.Elements
{
    internal class PreviewCollectionElement : VisualElement
    {
        // Data
        private IAssetPreviewCollection _collection;

        // UI
        private Label _previewCountLabel;
        private GridListElement _gridListElement;

        public PreviewCollectionElement(IAssetPreviewCollection collection)
        {
            AddToClassList("preview-list");

            _collection = collection;
            _collection.OnCollectionChanged += RefreshList;

            Create();
            RefreshList();

            SubscribeToSceneChanges();
        }

        private void Create()
        {
            CreateLabel();
            CreateGridListElement();
        }

        private void CreateLabel()
        {
            _previewCountLabel = new Label();
            _previewCountLabel.style.display = DisplayStyle.None;
            Add(_previewCountLabel);
        }

        private void CreateGridListElement()
        {
            _gridListElement = new GridListElement();
            _gridListElement.MakeItem = CreatePreview;
            _gridListElement.BindItem = BindPreview;
            _gridListElement.ElementWidth = 140 + 10; // Accounting for margin style
            _gridListElement.ElementHeight = 160 + 10; // Accounting for margin style
            Add(_gridListElement);
        }

        private VisualElement CreatePreview()
        {
            var preview = new AssetPreviewElement();
            return preview;
        }

        private void BindPreview(VisualElement element, int index)
        {
            var previewElement = (AssetPreviewElement)element;
            var preview = _collection.GetPreviews().ToList()[index];
            previewElement.SetSource(preview);
        }

        private void RefreshList()
        {
            var type = _collection.GetGenerationType();
            var items = _collection.GetPreviews().ToList();
            _previewCountLabel.text = $"Displaying {items.Count} {ConvertGenerationTypeName(type)} previews";
            _previewCountLabel.style.display = DisplayStyle.Flex;
            _previewCountLabel.style.alignSelf = Align.Center;
            _previewCountLabel.style.marginBottom = 10;
            _previewCountLabel.style.unityFontStyleAndWeight = FontStyle.Bold;

            _gridListElement.ItemSource = items;
            _gridListElement.Redraw();
        }

        private string ConvertGenerationTypeName(GenerationType type)
        {
            switch (type)
            {
                case GenerationType.Custom:
                    return "high resolution";
                default:
                    return type.ToString().ToLower();
            }
        }

        private void SubscribeToSceneChanges()
        {
            var windowToSubscribeTo = Resources.FindObjectsOfTypeAll<PreviewGeneratorWindow>().FirstOrDefault();
            UnityAction<Scene, Scene> sceneChanged = null;
            sceneChanged = new UnityAction<Scene, Scene>((_, __) => RefreshObjects(windowToSubscribeTo));
            EditorSceneManager.activeSceneChangedInEditMode += sceneChanged;

            void RefreshObjects(PreviewGeneratorWindow subscribedWindow)
            {
                // Remove callback if preview generator window instance changed
                var activeWindow = Resources.FindObjectsOfTypeAll<PreviewGeneratorWindow>().FirstOrDefault();
                if (subscribedWindow == null || subscribedWindow != activeWindow)
                {
                    EditorSceneManager.activeSceneChangedInEditMode -= sceneChanged;
                    return;
                }

                RefreshList();
            }
        }
    }
}