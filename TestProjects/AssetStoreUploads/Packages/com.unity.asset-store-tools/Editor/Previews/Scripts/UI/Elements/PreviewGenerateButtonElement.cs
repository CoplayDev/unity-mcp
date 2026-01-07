using AssetStoreTools.Previews.UI.Data;
using System;
using UnityEngine.UIElements;

namespace AssetStoreTools.Previews.UI.Elements
{
    internal class PreviewGenerateButtonElement : VisualElement
    {
        // Data
        private IPreviewGeneratorSettings _settings;

        // UI
        private Button _generateButton;

        public event Action OnGenerate;

        public PreviewGenerateButtonElement(IPreviewGeneratorSettings settings)
        {
            _settings = settings;
            _settings.OnGenerationPathsChanged += GenerationPathsChanged;

            Create();
            Deserialize();
        }

        private void Create()
        {
            _generateButton = new Button(Validate) { text = "Generate" };
            _generateButton.AddToClassList("preview-generate-button");

            Add(_generateButton);
        }

        private void Validate()
        {
            OnGenerate?.Invoke();
        }

        private void GenerationPathsChanged()
        {
            var inputPathsPresent = _settings.GetGenerationPaths().Count > 0;
            _generateButton.SetEnabled(inputPathsPresent);
        }

        private void Deserialize()
        {
            GenerationPathsChanged();
        }
    }
}