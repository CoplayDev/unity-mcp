using AssetStoreTools.Previews.Data;
using AssetStoreTools.Previews.Services;
using AssetStoreTools.Previews.UI.Data;
using AssetStoreTools.Previews.UI.Elements;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace AssetStoreTools.Previews.UI.Views
{
    internal class PreviewListView : VisualElement
    {
        //Data
        private PreviewDatabase _previewDatabase;
        private IPreviewGeneratorSettings _previewGeneratorSettings;
        private IAssetPreviewCollection _previewCollection;

        private ICachingService _cachingService;

        // UI
        private PreviewWindowDescriptionElement _descriptionElement;
        private PreviewGeneratorSettingsElement _settingsElement;
        private PreviewGenerateButtonElement _generateButtonElement;
        private PreviewCollectionElement _previewCollectionElement;

        public PreviewListView(ICachingService cachingService)
        {
            _cachingService = cachingService;

            _previewGeneratorSettings = new PreviewGeneratorSettings();
            _previewCollection = new AssetPreviewCollection();

            _previewGeneratorSettings.OnGenerationTypeChanged += RefreshPreviewList;
            _previewGeneratorSettings.OnGenerationPathsChanged += RefreshPreviewList;

            Create();
            RefreshPreviewList();
        }

        private void Create()
        {
            CreateDescription();
            CreateSettings();
            CreateGenerateButton();
            CreatePreviewList();
        }

        private void CreateDescription()
        {
            _descriptionElement = new PreviewWindowDescriptionElement();
            Add(_descriptionElement);
        }

        private void CreateSettings()
        {
            _settingsElement = new PreviewGeneratorSettingsElement(_previewGeneratorSettings);
            Add(_settingsElement);
        }

        private void CreateGenerateButton()
        {
            _generateButtonElement = new PreviewGenerateButtonElement(_previewGeneratorSettings);
            _generateButtonElement.OnGenerate += GeneratePreviews;
            Add(_generateButtonElement);
        }

        private void CreatePreviewList()
        {
            _previewCollectionElement = new PreviewCollectionElement(_previewCollection);
            Add(_previewCollectionElement);
        }

        private async void GeneratePreviews()
        {
            try
            {
                _settingsElement.SetEnabled(false);
                _generateButtonElement.SetEnabled(false);
                _previewCollectionElement.SetEnabled(false);

                var generator = _previewGeneratorSettings.CreateGenerator();
                generator.OnProgressChanged += DisplayProgress;
                var result = await generator.Generate();
                generator.OnProgressChanged -= DisplayProgress;

                if (!result.Success)
                {
                    EditorUtility.DisplayDialog("Error", result.Exception.Message, "OK");
                    Debug.LogException(result.Exception);
                    return;
                }

                RefreshPreviewList();
            }
            finally
            {
                _settingsElement.SetEnabled(true);
                _generateButtonElement.SetEnabled(true);
                _previewCollectionElement.SetEnabled(true);
                EditorUtility.ClearProgressBar();
            }
        }

        private void DisplayProgress(float progress)
        {
            EditorUtility.DisplayProgressBar("Generating", "Generating previews...", progress);
        }

        public void LoadSettings(PreviewGenerationSettings settings)
        {
            _previewGeneratorSettings.LoadSettings(settings);
        }

        private void RefreshPreviewList()
        {
            if (!_cachingService.GetCachedMetadata(out _previewDatabase))
                _previewDatabase = new PreviewDatabase();

            var paths = _previewGeneratorSettings.GetGenerationPaths();
            var guids = AssetDatabase.FindAssets("", paths.ToArray());
            var displayedPreviews = new List<PreviewMetadata>();

            foreach (var entry in _previewDatabase.Previews)
            {
                if (!entry.Exists())
                    continue;

                if (entry.Type != _previewGeneratorSettings.GetGenerationType())
                    continue;

                if (!guids.Any(x => x == entry.Guid))
                    continue;

                displayedPreviews.Add(entry);
            }

            _previewCollection.Refresh(_previewGeneratorSettings.GetGenerationType(), displayedPreviews);
        }
    }
}