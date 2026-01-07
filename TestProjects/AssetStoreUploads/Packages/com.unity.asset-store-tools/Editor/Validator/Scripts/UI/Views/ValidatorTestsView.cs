using AssetStoreTools.Validator.Data;
using AssetStoreTools.Validator.Services;
using AssetStoreTools.Validator.UI.Data;
using AssetStoreTools.Validator.UI.Data.Serialization;
using AssetStoreTools.Validator.UI.Elements;
using UnityEditor;
using UnityEngine.UIElements;

namespace AssetStoreTools.Validator.UI.Views
{
    internal class ValidatorTestsView : VisualElement
    {
        // Data
        private ValidatorStateData _stateData;
        private IValidatorSettings _settings;
        private IValidatorResults _results;

        private ICachingService _cachingService;

        // UI
        private ValidatorSettingsElement _validatorSettingsElement;
        private ValidatorButtonElement _validatorButtonElement;
        private ValidatorResultsElement _validationTestListElement;

        public ValidatorTestsView(ICachingService cachingService)
        {
            _cachingService = cachingService;

            if (!_cachingService.GetCachedValidatorStateData(out _stateData))
                _stateData = new ValidatorStateData();

            _settings = new ValidatorSettings(_stateData.GetSettings());
            _settings.OnRequireSerialize += Serialize;

            _results = new ValidatorResults(_settings, _stateData.GetResults());
            _results.OnRequireSerialize += Serialize;

            Create();
        }

        private void Create()
        {
            CreateValidatorDescription();
            CreateValidationSettings();
            CreateValidationButton();
            CreateValidatorResults();
        }

        private void CreateValidatorDescription()
        {
            var validationInfoElement = new ValidatorDescriptionElement();
            Add(validationInfoElement);
        }

        private void CreateValidationSettings()
        {
            _validatorSettingsElement = new ValidatorSettingsElement(_settings);
            Add(_validatorSettingsElement);
        }

        private void CreateValidationButton()
        {
            _validatorButtonElement = new ValidatorButtonElement(_settings);
            _validatorButtonElement.OnValidate += Validate;
            Add(_validatorButtonElement);
        }

        private void CreateValidatorResults()
        {
            _validationTestListElement = new ValidatorResultsElement(_results);
            Add(_validationTestListElement);
        }

        private void Validate()
        {
            var validator = _settings.CreateValidator();
            var result = validator.Validate();

            if (result.Status == ValidationStatus.Failed)
            {
                EditorUtility.DisplayDialog("Validation failed", result.Exception.Message, "OK");
                return;
            }

            LoadResult(result);
        }

        public void LoadSettings(ValidationSettings settings)
        {
            _settings.LoadSettings(settings);
        }

        public void LoadResult(ValidationResult result)
        {
            _results.LoadResult(result);
        }

        private void Serialize()
        {
            _cachingService.CacheValidatorStateData(_stateData);
        }
    }
}