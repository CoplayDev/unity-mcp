using AssetStoreTools.Previews.UI.Data;
using AssetStoreTools.Utility;
using UnityEditor;
using UnityEngine.UIElements;

namespace AssetStoreTools.Validator.UI.Elements
{
    internal class PreviewGeneratorPathsElement : VisualElement
    {
        // Data
        private IPreviewGeneratorSettings _settings;

        // UI
        private ScrollView _pathBoxScrollView;

        public PreviewGeneratorPathsElement(IPreviewGeneratorSettings settings)
        {
            AddToClassList("preview-paths");

            _settings = settings;
            _settings.OnGenerationPathsChanged += InputPathsChanged;

            Create();
            Deserialize();
        }

        private void Create()
        {
            var pathSelectionRow = new VisualElement();
            pathSelectionRow.AddToClassList("preview-settings-selection-row");

            VisualElement labelHelpRow = new VisualElement();
            labelHelpRow.AddToClassList("preview-settings-selection-label-help-row");
            labelHelpRow.style.alignSelf = Align.FlexStart;

            Label pathLabel = new Label { text = "Input paths" };
            Image pathLabelTooltip = new Image
            {
                tooltip = "Select the folder (or multiple folders) to generate asset previews for."
            };

            labelHelpRow.Add(pathLabel);
            labelHelpRow.Add(pathLabelTooltip);

            var fullPathBox = new VisualElement() { name = "PreviewPaths" };
            fullPathBox.AddToClassList("preview-paths-box");

            _pathBoxScrollView = new ScrollView { name = "PreviewPathsScrollView" };
            _pathBoxScrollView.AddToClassList("preview-paths-scroll-view");

            VisualElement scrollViewBottomRow = new VisualElement();
            scrollViewBottomRow.AddToClassList("preview-paths-scroll-view-bottom-row");

            var addExtraPathsButton = new Button(BrowsePath) { text = "Add a path" };
            addExtraPathsButton.AddToClassList("preview-paths-add-button");
            scrollViewBottomRow.Add(addExtraPathsButton);

            fullPathBox.Add(_pathBoxScrollView);
            fullPathBox.Add(scrollViewBottomRow);

            pathSelectionRow.Add(labelHelpRow);
            pathSelectionRow.Add(fullPathBox);

            Add(pathSelectionRow);
        }

        private VisualElement CreateSinglePathElement(string path)
        {
            var validationPath = new VisualElement();
            validationPath.AddToClassList("preview-paths-path-row");

            var folderPathLabel = new Label(path);
            folderPathLabel.AddToClassList("preview-paths-path-row-input-field");

            var removeButton = new Button(() =>
            {
                _settings.RemoveGenerationPath(path);
            });
            removeButton.text = "X";
            removeButton.AddToClassList("preview-paths-path-row-remove-button");

            validationPath.Add(folderPathLabel);
            validationPath.Add(removeButton);

            return validationPath;
        }

        private void BrowsePath()
        {
            string absolutePath = EditorUtility.OpenFolderPanel("Select a directory", "Assets", "");

            if (string.IsNullOrEmpty(absolutePath))
                return;

            var relativePath = FileUtility.AbsolutePathToRelativePath(absolutePath, ASToolsPreferences.Instance.EnableSymlinkSupport);

            if (!_settings.IsGenerationPathValid(relativePath, out var error))
            {
                EditorUtility.DisplayDialog("Invalid path", error, "OK");
                return;
            }

            _settings.AddGenerationPath(relativePath);
        }

        private void InputPathsChanged()
        {
            var inputPaths = _settings.GetGenerationPaths();

            _pathBoxScrollView.Clear();
            foreach (var path in inputPaths)
            {
                _pathBoxScrollView.Add(CreateSinglePathElement(path));
            }
        }

        private void Deserialize()
        {
            InputPathsChanged();
        }
    }
}