using AssetStoreTools.Validator.UI.Data;
using System;
using UnityEngine.UIElements;

namespace AssetStoreTools.Validator.UI.Elements
{
    internal class ValidatorButtonElement : VisualElement
    {
        // Data
        private IValidatorSettings _settings;

        // UI
        private Button _validateButton;

        public event Action OnValidate;

        public ValidatorButtonElement(IValidatorSettings settings)
        {
            _settings = settings;
            _settings.OnValidationPathsChanged += ValidationPathsChanged;

            Create();
            Deserialize();
        }

        private void Create()
        {
            _validateButton = new Button(Validate) { text = "Validate" };
            _validateButton.AddToClassList("validator-validate-button");

            Add(_validateButton);
        }

        private void Validate()
        {
            OnValidate?.Invoke();
        }

        private void ValidationPathsChanged()
        {
            var validationPathsPresent = _settings.GetValidationPaths().Count > 0;
            _validateButton.SetEnabled(validationPathsPresent);
        }

        private void Deserialize()
        {
            ValidationPathsChanged();
        }
    }
}