using System;
using UnityEngine.UIElements;

namespace AssetStoreTools.Uploader.UI.Elements
{
    internal class PathSelectionElement : VisualElement
    {
        // Data
        private string _labelText;
        private string _labelTooltip;

        public event Action OnBrowse;

        // UI
        private TextField _pathSelectionTextField;

        public PathSelectionElement(string labelText, string labelTooltip)
        {
            AddToClassList("package-content-option-box");

            _labelText = labelText;
            _labelTooltip = labelTooltip;

            Create();
        }

        private void Create()
        {
            VisualElement labelHelpRow = new VisualElement();
            labelHelpRow.AddToClassList("package-content-option-label-help-row");

            Label folderPathLabel = new Label { text = _labelText };
            Image folderPathLabelTooltip = new Image
            {
                tooltip = _labelTooltip
            };

            labelHelpRow.Add(folderPathLabel);
            labelHelpRow.Add(folderPathLabelTooltip);

            _pathSelectionTextField = new TextField();
            _pathSelectionTextField.AddToClassList("package-content-option-textfield");
            _pathSelectionTextField.isReadOnly = true;

            Button browsePathButton = new Button(Browse) { name = "BrowsePathButton", text = "Browse" };
            browsePathButton.AddToClassList("package-content-option-button");

            Add(labelHelpRow);
            Add(_pathSelectionTextField);
            Add(browsePathButton);
        }

        private void Browse()
        {
            OnBrowse?.Invoke();
        }

        public void SetPath(string path)
        {
            _pathSelectionTextField.value = path;
        }
    }
}