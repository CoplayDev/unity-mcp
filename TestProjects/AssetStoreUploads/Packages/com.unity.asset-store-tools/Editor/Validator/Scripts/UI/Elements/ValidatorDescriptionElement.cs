using UnityEngine;
using UnityEngine.UIElements;

namespace AssetStoreTools.Validator.UI.Elements
{
    internal class ValidatorDescriptionElement : VisualElement
    {
        private const string DescriptionFoldoutText = "Validate your package to ensure your content follows the chosen submission guidelines.";
        private const string DescriptionFoldoutContentText = "The validations below do not cover all of the content standards, and passing all validations does not " +
                "guarantee that your package will be accepted to the Asset Store.\n\n" +
                "The tests are not obligatory for submitting your assets, but they can help avoid instant rejection by the " +
                "automated vetting system, or clarify reasons of rejection communicated by the vetting team.\n\n" +
                "For more information about the validations, view the message by expanding the tests or contact our support team.";

        private VisualElement _descriptionSimpleContainer;
        private Label _descriptionSimpleLabel;
        private Button _showMoreButton;

        private VisualElement _descriptionFullContainer;
        private Button _showLessButton;

        public ValidatorDescriptionElement()
        {
            AddToClassList("validator-description");
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
            _descriptionSimpleContainer.AddToClassList("validator-description-simple-container");

            _descriptionSimpleLabel = new Label(DescriptionFoldoutText);
            _descriptionSimpleLabel.AddToClassList("validator-description-simple-label");

            _showMoreButton = new Button(ToggleFullDescription) { text = "Show more..." };
            _showMoreButton.AddToClassList("validator-description-show-button");
            _showMoreButton.AddToClassList("validator-description-hyperlink-button");

            _descriptionSimpleContainer.Add(_descriptionSimpleLabel);
            _descriptionSimpleContainer.Add(_showMoreButton);

            Add(_descriptionSimpleContainer);
        }

        private void CreateFullDescription()
        {
            _descriptionFullContainer = new VisualElement();
            _descriptionFullContainer.AddToClassList("validator-description-full-container");

            var validatorDescription = new Label()
            {
                text = DescriptionFoldoutContentText
            };
            validatorDescription.AddToClassList("validator-description-full-label");

            var submissionGuidelinesButton = new Button(OpenSubmissionGuidelinesUrl)
            {
                text = "Submission Guidelines"
            };
            submissionGuidelinesButton.AddToClassList("validator-description-hyperlink-button");

            var supportTicketButton = new Button(OpenSupportTicketUrl)
            {
                text = "Contact our Support Team"
            };
            supportTicketButton.AddToClassList("validator-description-hyperlink-button");

            _showLessButton = new Button(ToggleFullDescription) { text = "Show less..." };
            _showLessButton.AddToClassList("validator-description-hide-button");
            _showLessButton.AddToClassList("validator-description-hyperlink-button");

            _descriptionFullContainer.Add(validatorDescription);
            _descriptionFullContainer.Add(submissionGuidelinesButton);
            _descriptionFullContainer.Add(supportTicketButton);
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

        private void OpenSubmissionGuidelinesUrl()
        {
            Application.OpenURL(Constants.Validator.SubmissionGuidelinesUrl);
        }

        private void OpenSupportTicketUrl()
        {
            Application.OpenURL(Constants.Validator.SupportTicketUrl);
        }
    }
}