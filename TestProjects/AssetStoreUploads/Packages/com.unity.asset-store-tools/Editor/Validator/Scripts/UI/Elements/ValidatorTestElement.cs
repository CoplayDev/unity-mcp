using AssetStoreTools.Validator.Data;
using AssetStoreTools.Validator.UI.Data;
using AssetStoreTools.Validator.Utility;
using System.Linq;
using UnityEditor.SceneManagement;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace AssetStoreTools.Validator.UI.Elements
{
    internal class ValidatorTestElement : VisualElement
    {
        // Data
        private IValidatorTest _test;
        private bool _isExpanded;

        // UI
        private Button _testFoldoutButton;
        private Label _testFoldoutExpandStateLabel;
        private Label _testFoldoutLabel;
        private Image _testStatusImage;

        private VisualElement _testContent;
        private VisualElement _resultMessagesBox;

        public ValidatorTestElement(IValidatorTest test)
        {
            AddToClassList("validator-test");

            _test = test;

            Create();
            Unexpand();

            SubscribeToSceneChanges();
        }

        private void Create()
        {
            CreateFoldoutButton();
            CreateTestContent();
            CreateTestDescription();
            CreateTestMessages();
        }

        private void CreateFoldoutButton()
        {
            _testFoldoutButton = new Button(ToggleExpand) { name = _test.Name };
            _testFoldoutButton.AddToClassList("validator-test-foldout");

            // Expander and Asset Label
            VisualElement labelExpanderRow = new VisualElement { name = "labelExpanderRow" };
            labelExpanderRow.AddToClassList("validator-test-expander");

            _testFoldoutExpandStateLabel = new Label { name = "ExpanderLabel", text = "►" };
            _testFoldoutExpandStateLabel.AddToClassList("validator-test-expander-arrow");

            _testFoldoutLabel = new Label { name = "TestLabel", text = _test.Name };
            _testFoldoutLabel.AddToClassList("validator-text-expander-label");

            labelExpanderRow.Add(_testFoldoutExpandStateLabel);
            labelExpanderRow.Add(_testFoldoutLabel);

            _testStatusImage = new Image
            {
                name = "TestImage",
                image = ValidatorUtility.GetStatusTexture(_test.Result.Status)
            };

            _testStatusImage.AddToClassList("validator-test-expander-image");

            _testFoldoutButton.Add(labelExpanderRow);
            _testFoldoutButton.Add(_testStatusImage);

            Add(_testFoldoutButton);
        }

        private void CreateTestContent()
        {
            _testContent = new VisualElement();
            _testContent.AddToClassList("validator-test-content");
            Add(_testContent);
        }

        private void CreateTestDescription()
        {
            var testCaseDescription = new TextField
            {
                name = "Description",
                value = _test.Description,
                isReadOnly = true,
                multiline = true,
                focusable = false,
                doubleClickSelectsWord = false,
                tripleClickSelectsLine = false
            };
            testCaseDescription.AddToClassList("validator-test-content-textfield");

#if UNITY_2022_1_OR_NEWER
        testCaseDescription.focusable = true;
        testCaseDescription.selectAllOnFocus = false;
        testCaseDescription.selectAllOnMouseUp = false;
#endif

            _testContent.Add(testCaseDescription);
        }

        private void CreateTestMessages()
        {
            if (_test.Result.MessageCount == 0)
                return;

            _resultMessagesBox = new VisualElement();
            _resultMessagesBox.AddToClassList("validator-test-content-result-messages");

            switch (_test.Result.Status)
            {
                case TestResultStatus.Pass:
                    _resultMessagesBox.AddToClassList("validator-test-content-result-messages-pass");
                    break;
                case TestResultStatus.Warning:
                    _resultMessagesBox.AddToClassList("validator-test-content-result-messages-warning");
                    break;
                case TestResultStatus.Fail:
                    _resultMessagesBox.AddToClassList("validator-test-content-result-messages-fail");
                    break;
            }

            for (int i = 0; i < _test.Result.MessageCount; i++)
            {
                _resultMessagesBox.Add(CreateMessage(_test.Result.GetMessage(i)));

                if (i == _test.Result.MessageCount - 1)
                    continue;

                var separator = new VisualElement() { name = "Separator" };
                separator.AddToClassList("message-separator");
                _resultMessagesBox.Add(separator);
            }

            _testContent.Add(_resultMessagesBox);
        }

        private VisualElement CreateMessage(TestResultMessage message)
        {
            var resultText = message.GetText();
            var clickAction = message.GetClickAction();

            var resultMessage = new VisualElement { name = "ResultMessageElement" };
            resultMessage.AddToClassList("validator-test-content-result-messages-content");

            var informationButton = new Button();
            informationButton.AddToClassList("validator-test-content-result-messages-content-button");

            if (clickAction != null)
            {
                informationButton.tooltip = clickAction.Tooltip;
                informationButton.clicked += clickAction.Execute;
                informationButton.SetEnabled(true);
            }

            var informationDescription = new Label { name = "InfoDesc", text = resultText };
            informationDescription.AddToClassList("validator-test-content-result-messages-content-label");

            informationButton.Add(informationDescription);
            resultMessage.Add(informationButton);

            for (int i = 0; i < message.MessageObjectCount; i++)
            {
                var obj = message.GetMessageObject(i);
                if (obj == null)
                    continue;

                if (obj.GetObject() == null)
                    continue;

                var objectField = new ObjectField() { objectType = obj.GetType(), value = obj.GetObject() };
                objectField.RegisterCallback<ChangeEvent<UnityEngine.Object>>((evt) =>
                    objectField.SetValueWithoutNotify(evt.previousValue));
                resultMessage.Add(objectField);
            }

            return resultMessage;
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
            _testFoldoutExpandStateLabel.text = "▼";
            _testFoldoutButton.AddToClassList("validator-test-foldout-expanded");
            _testContent.style.display = DisplayStyle.Flex;
            _isExpanded = true;
        }

        private void Unexpand()
        {
            _testFoldoutExpandStateLabel.text = "►";
            _testFoldoutButton.RemoveFromClassList("validator-test-foldout-expanded");
            _testContent.style.display = DisplayStyle.None;
            _isExpanded = false;
        }

        private void SubscribeToSceneChanges()
        {
            // Some result message objects only exist in specific scenes,
            // therefore the UI must be refreshed on scene change
            var windowToSubscribeTo = Resources.FindObjectsOfTypeAll<ValidatorWindow>().FirstOrDefault();
            UnityAction<Scene, Scene> sceneChanged = null;
            sceneChanged = new UnityAction<Scene, Scene>((_, __) => RefreshObjects(windowToSubscribeTo));
            EditorSceneManager.activeSceneChangedInEditMode += sceneChanged;

            void RefreshObjects(ValidatorWindow subscribedWindow)
            {
                // Remove callback if validator window instance changed
                var activeWindow = Resources.FindObjectsOfTypeAll<ValidatorWindow>().FirstOrDefault();
                if (subscribedWindow == null || subscribedWindow != activeWindow)
                {
                    EditorSceneManager.activeSceneChangedInEditMode -= sceneChanged;
                    return;
                }

                if (_resultMessagesBox != null)
                    _testContent.Remove(_resultMessagesBox);

                CreateTestMessages();
            }
        }
    }
}