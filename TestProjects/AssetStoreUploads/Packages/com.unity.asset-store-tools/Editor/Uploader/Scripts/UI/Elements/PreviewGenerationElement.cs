using AssetStoreTools.Previews.Data;
using AssetStoreTools.Uploader.Data;
using System.Linq;
using UnityEditor;
using UnityEngine.UIElements;

namespace AssetStoreTools.Uploader.UI.Elements
{
    internal class PreviewGenerationElement : VisualElement
    {
        // Data
        private IWorkflow _workflow;

        // UI
        private VisualElement _toggleRow;
        private Toggle _previewToggle;

        private VisualElement _buttonRow;
        private VisualElement _viewButton;

        public PreviewGenerationElement(IWorkflow workflow)
        {
            _workflow = workflow;

            Create();
        }

        private void Create()
        {
            CreateInfoRow();
            CreateViewButton();
        }

        private void CreateInfoRow()
        {
            _toggleRow = new VisualElement();
            _toggleRow.AddToClassList("package-content-option-box");

            VisualElement toggleLabelHelpRow = new VisualElement();
            toggleLabelHelpRow.AddToClassList("package-content-option-label-help-row");

            Label toggleLabel = new Label { text = "Asset Previews" };
            Image toggleLabelTooltip = new Image
            {
                tooltip = "Select how the previews for your assets will be generated.\n\n" +
                "Unity generates asset preview images natively up to a size of 128x128. " +
                "You can try generating previews which are of higher resolution, up to 300x300.\n\n" +
                "Note: these asset preview images will only be displayed in the 'Package Content' section of the " +
                "Asset Store listing page once the package is published, and in the package importer window that appears during the package import process.\n" +
                "They will not replace the images used for the assets in the Project window after the package gets imported."
            };

            _previewToggle = new Toggle { name = "PreviewToggle", text = "Generate Hi-Res (experimental)" };
            _previewToggle.AddToClassList("package-content-option-toggle");
            _previewToggle.RegisterValueChangedCallback((_) => DependencyToggleValueChange());

            toggleLabelHelpRow.Add(toggleLabel);
            toggleLabelHelpRow.Add(toggleLabelTooltip);

            _toggleRow.Add(toggleLabelHelpRow);
            _toggleRow.Add(_previewToggle);

            Add(_toggleRow);
        }

        private void CreateViewButton()
        {
            _buttonRow = new VisualElement();
            _buttonRow.AddToClassList("package-content-option-box");
            _buttonRow.style.display = DisplayStyle.None;

            var spaceFiller = new VisualElement();
            spaceFiller.AddToClassList("package-content-option-label-help-row");

            _viewButton = new Button(ViewClicked) { text = "Inspect Previews" };

            _buttonRow.Add(spaceFiller);
            _buttonRow.Add(_viewButton);

            Add(_buttonRow);
        }

        private void DependencyToggleValueChange()
        {
            _workflow.GenerateHighQualityPreviews = _previewToggle.value;
            _buttonRow.style.display = _previewToggle.value ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void ViewClicked()
        {
            PreviewGenerationSettings settings;
            if (_workflow.GenerateHighQualityPreviews)
            {
                settings = new CustomPreviewGenerationSettings() { InputPaths = _workflow.GetAllPaths().ToArray() };
            }
            else
            {
                settings = new NativePreviewGenerationSettings() { InputPaths = _workflow.GetAllPaths().ToArray() };
            }

            AssetStoreTools.ShowAssetStoreToolsPreviewGenerator(settings);
        }

        private void DisplayProgress(float value)
        {
            EditorUtility.DisplayProgressBar("Generating", "Generating previews...", value);
        }
    }
}