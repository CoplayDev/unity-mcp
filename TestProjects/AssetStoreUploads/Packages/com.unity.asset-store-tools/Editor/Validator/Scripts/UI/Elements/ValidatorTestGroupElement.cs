using AssetStoreTools.Validator.UI.Data;
using AssetStoreTools.Validator.Utility;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.UIElements;

namespace AssetStoreTools.Validator.UI.Elements
{
    internal class ValidatorTestGroupElement : VisualElement
    {
        // Data
        private IValidatorTestGroup _group;
        private bool _isExpanded;

        // UI
        private Button _groupFoldoutButton;
        private Label _groupExpandStateLabel;
        private Label _groupFoldoutLabel;
        private Image _groupStatusImage;

        private VisualElement _groupContent;
        private List<ValidatorTestElement> _testElements;

        public ValidatorTestGroupElement(IValidatorTestGroup group)
        {
            AddToClassList("validator-test-list-group");

            _group = group;

            Create();
        }

        private void Create()
        {
            CreateGroupFoldout();
            CreateGroupContent();
        }

        private void CreateGroupFoldout()
        {
            _groupFoldoutButton = new Button(ToggleExpand);
            _groupFoldoutButton.AddToClassList("validator-test-list-group-expander");

            _groupExpandStateLabel = new Label { name = "ExpanderLabel", text = "►" };
            _groupExpandStateLabel.AddToClassList("validator-test-list-group-expander-arrow");

            _groupStatusImage = new Image
            {
                name = "TestImage",
                image = ValidatorUtility.GetStatusTexture(_group.Status)
            };
            _groupStatusImage.AddToClassList("validator-test-list-group-expander-image");

            _groupFoldoutLabel = new Label() { text = $"{_group.Name} ({_group.Tests.Count()})" };
            _groupFoldoutLabel.AddToClassList("validator-test-list-group-expander-label");

            _groupFoldoutButton.Add(_groupExpandStateLabel);
            _groupFoldoutButton.Add(_groupStatusImage);
            _groupFoldoutButton.Add(_groupFoldoutLabel);

            Add(_groupFoldoutButton);
        }

        private void CreateGroupContent()
        {
            _groupContent = new VisualElement();
            _groupContent.AddToClassList("validator-test-list-group-content");

            Add(_groupContent);

            _testElements = new List<ValidatorTestElement>();
            foreach (var test in _group.Tests)
            {
                var testElement = new ValidatorTestElement(test);
                _testElements.Add(testElement);
                _groupContent.Add(testElement);
            }
        }

        private void ToggleExpand()
        {
            if (!_isExpanded)
                Expand();
            else
                Unexpand();
        }

        private void Expand()
        {
            _groupExpandStateLabel.text = "▼";
            _groupContent.style.display = DisplayStyle.Flex;
            _isExpanded = true;
        }

        private void Unexpand()
        {
            _groupExpandStateLabel.text = "►";
            _groupContent.style.display = DisplayStyle.None;
            _isExpanded = false;
        }
    }
}