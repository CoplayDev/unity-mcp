using AssetStoreTools.Utility;
using AssetStoreTools.Validator.UI.Data;
using UnityEditor;
using UnityEngine.UIElements;

namespace AssetStoreTools.Validator.UI.Elements
{
    internal class ValidatorPathsElement : VisualElement
    {
        // Data
        private IValidatorSettings _settings;

        // UI
        private ScrollView _pathBoxScrollView;

        public ValidatorPathsElement(IValidatorSettings settings)
        {
            AddToClassList("validator-paths");

            _settings = settings;
            _settings.OnValidationPathsChanged += ValidationPathsChanged;

            Create();
            Deserialize();
        }

        private void Create()
        {
            var pathSelectionRow = new VisualElement();
            pathSelectionRow.AddToClassList("validator-settings-selection-row");

            VisualElement labelHelpRow = new VisualElement();
            labelHelpRow.AddToClassList("validator-settings-selection-label-help-row");
            labelHelpRow.style.alignSelf = Align.FlexStart;

            Label pathLabel = new Label { text = "Validation paths" };
            Image pathLabelTooltip = new Image
            {
                tooltip = "Select the folder (or multiple folders) that your package consists of." +
                          "\n\nAll files and folders of your package should be contained within " +
                          "a single root folder that is named after your package " +
                          "(e.g. 'Assets/[MyPackageName]' or 'Packages/[MyPackageName]')" +
                          "\n\nIf your package includes special folders that cannot be nested within " +
                          "the root package folder (e.g. 'WebGLTemplates'), they should be added to this list " +
                          "together with the root package folder"
            };

            labelHelpRow.Add(pathLabel);
            labelHelpRow.Add(pathLabelTooltip);

            var fullPathBox = new VisualElement() { name = "ValidationPaths" };
            fullPathBox.AddToClassList("validator-paths-box");

            _pathBoxScrollView = new ScrollView { name = "ValidationPathsScrollView" };
            _pathBoxScrollView.AddToClassList("validator-paths-scroll-view");

            VisualElement scrollViewBottomRow = new VisualElement();
            scrollViewBottomRow.AddToClassList("validator-paths-scroll-view-bottom-row");

            var addExtraPathsButton = new Button(BrowsePath) { text = "Add a path" };
            addExtraPathsButton.AddToClassList("validator-paths-add-button");
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
            validationPath.AddToClassList("validator-paths-path-row");

            var folderPathLabel = new Label(path);
            folderPathLabel.AddToClassList("validator-paths-path-row-input-field");

            var removeButton = new Button(() =>
            {
                _settings.RemoveValidationPath(path);
            });
            removeButton.text = "X";
            removeButton.AddToClassList("validator-paths-path-row-remove-button");

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

            if (!_settings.IsValidationPathValid(relativePath, out var error))
            {
                EditorUtility.DisplayDialog("Invalid path", error, "OK");
                return;
            }

            _settings.AddValidationPath(relativePath);
        }

        private void ValidationPathsChanged()
        {
            var validationPaths = _settings.GetValidationPaths();

            _pathBoxScrollView.Clear();
            foreach (var path in validationPaths)
            {
                _pathBoxScrollView.Add(CreateSinglePathElement(path));
            }
        }

        private void Deserialize()
        {
            ValidationPathsChanged();
        }
    }
}