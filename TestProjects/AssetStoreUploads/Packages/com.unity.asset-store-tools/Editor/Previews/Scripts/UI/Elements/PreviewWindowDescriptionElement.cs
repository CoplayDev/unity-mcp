using UnityEngine.UIElements;

namespace AssetStoreTools.Previews.UI.Elements
{
    internal class PreviewWindowDescriptionElement : VisualElement
    {
        private const string DescriptionFoldoutText = "Generate and inspect asset preview images to be displayed in your package listing page on the Asset Store.";
        private const string DescriptionFoldoutContentText = "Images generated in this window will be reused when exporting a package. Any missing images generated during the package export process will also appear here.\n\n" +
            "Preview images are displayed on the Asset Store under the 'Package Content' section of the package listing. " +
            "They are also displayed in the package importer window that appears during the package import process. " +
            "Note that these images will not replace the images used for the assets in the Project window after the package gets imported.";

        private VisualElement _descriptionSimpleContainer;
        private Button _showMoreButton;

        private VisualElement _descriptionFullContainer;
        private Button _showLessButton;

        public PreviewWindowDescriptionElement()
        {
            AddToClassList("asset-preview-description");
            Create();
        }

        private void Create()
        {
            CreateSimpleDescription();
            CreateFullDescription();
        }

        private void CreateSimpleDescription()
        {
            _descriptionSimpleContainer = new VisualElement();
            _descriptionSimpleContainer.AddToClassList("asset-preview-description-simple-container");

            var simpleDescription = new Label(DescriptionFoldoutText);
            simpleDescription.AddToClassList("asset-preview-description-simple-label");

            _showMoreButton = new Button(ToggleFullDescription) { text = "Show more..." };
            _showMoreButton.AddToClassList("asset-preview-description-hyperlink-button");
            _showMoreButton.AddToClassList("asset-preview-description-show-button");

            _descriptionSimpleContainer.Add(simpleDescription);
            _descriptionSimpleContainer.Add(_showMoreButton);

            Add(_descriptionSimpleContainer);
        }

        private void CreateFullDescription()
        {
            _descriptionFullContainer = new VisualElement();
            _descriptionFullContainer.AddToClassList("asset-preview-description-full-container");

            var validatorDescription = new Label()
            {
                text = DescriptionFoldoutContentText
            };
            validatorDescription.AddToClassList("asset-preview-description-full-label");

            _showLessButton = new Button(ToggleFullDescription) { text = "Show less..." };
            _showLessButton.AddToClassList("asset-preview-description-hide-button");
            _showLessButton.AddToClassList("asset-preview-description-hyperlink-button");

            _descriptionFullContainer.Add(validatorDescription);
            _descriptionFullContainer.Add(_showLessButton);

            _descriptionFullContainer.style.display = DisplayStyle.None;
            Add(_descriptionFullContainer);
        }

        private void ToggleFullDescription()
        {
            var displayFullDescription = _descriptionFullContainer.style.display == DisplayStyle.None;

            if (displayFullDescription)
            {
                _showMoreButton.style.display = DisplayStyle.None;
                _descriptionFullContainer.style.display = DisplayStyle.Flex;
            }
            else
            {
                _showMoreButton.style.display = DisplayStyle.Flex;
                _descriptionFullContainer.style.display = DisplayStyle.None;
            }
        }
    }
}