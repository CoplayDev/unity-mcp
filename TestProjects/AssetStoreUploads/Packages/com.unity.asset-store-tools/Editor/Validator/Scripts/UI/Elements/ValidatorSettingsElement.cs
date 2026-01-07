using AssetStoreTools.Validator.UI.Data;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace AssetStoreTools.Validator.UI.Elements
{
    internal class ValidatorSettingsElement : VisualElement
    {
        // Data
        private IValidatorSettings _settings;

        // UI
        private ToolbarMenu _categoryMenu;
        private ValidatorPathsElement _validationPathsElement;

        public ValidatorSettingsElement(IValidatorSettings settings)
        {
            AddToClassList("validator-settings");

            _settings = settings;
            _settings.OnCategoryChanged += CategoryChanged;

            Create();
            Deserialize();
        }

        public void Create()
        {
            CreateCategorySelection();
            CreateValidationPathSelection();
        }

        private void CreateCategorySelection()
        {
            var categorySelectionBox = new VisualElement();
            categorySelectionBox.AddToClassList("validator-settings-selection-row");

            VisualElement labelHelpRow = new VisualElement();
            labelHelpRow.AddToClassList("validator-settings-selection-label-help-row");

            Label categoryLabel = new Label { text = "Category" };
            Image categoryLabelTooltip = new Image
            {
                tooltip = "Choose a base category of your package" +
                          "\n\nThis can be found in the Publishing Portal when creating the package listing or just " +
                          "selecting a planned one." +
                          "\n\nNote: Different categories could have different severities of several test cases."
            };

            labelHelpRow.Add(categoryLabel);
            labelHelpRow.Add(categoryLabelTooltip);

            _categoryMenu = new ToolbarMenu { name = "CategoryMenu" };
            _categoryMenu.AddToClassList("validator-settings-selection-dropdown");

            categorySelectionBox.Add(labelHelpRow);
            categorySelectionBox.Add(_categoryMenu);

            // Append available categories
            var categories = _settings.GetAvailableCategories();
            foreach (var category in categories)
            {
                _categoryMenu.menu.AppendAction(category, _ => _settings.SetActiveCategory(category));
            }

            // Append misc category
            _categoryMenu.menu.AppendAction("Other", _ => _settings.SetActiveCategory(string.Empty));

            Add(categorySelectionBox);
        }

        private void CreateValidationPathSelection()
        {
            _validationPathsElement = new ValidatorPathsElement(_settings);
            Add(_validationPathsElement);
        }

        private void CategoryChanged()
        {
            var category = _settings.GetActiveCategory();
            if (!string.IsNullOrEmpty(category))
                _categoryMenu.text = category;
            else
                _categoryMenu.text = "Other";
        }

        private void Deserialize()
        {
            if (_settings == null)
                return;

            // Set initial category
            CategoryChanged();
        }
    }
}