using AssetStoreTools.Validator.Data;
using AssetStoreTools.Validator.TestDefinitions;
using System;
using System.IO;

namespace AssetStoreTools.Validator
{
    internal class CurrentProjectValidator : ValidatorBase
    {
        private CurrentProjectValidationSettings _settings;

        public CurrentProjectValidator(CurrentProjectValidationSettings settings) : base(settings)
        {
            _settings = settings;
        }

        protected override void ValidateSettings()
        {
            if (_settings == null)
                throw new Exception("Validation Settings is null");

            if (_settings.ValidationPaths == null
                || _settings.ValidationPaths.Count == 0)
                throw new Exception("No validation paths were set");

            switch (_settings.ValidationType)
            {
                case ValidationType.Generic:
                case ValidationType.UnityPackage:
                    ValidateUnityPackageSettings();
                    break;
                default:
                    throw new NotImplementedException("Undefined validation type");
            }
        }

        private void ValidateUnityPackageSettings()
        {
            var invalidPaths = string.Empty;
            foreach (var path in _settings.ValidationPaths)
            {
                if (!Directory.Exists(path))
                    invalidPaths += $"\n{path}";
            }

            if (!string.IsNullOrEmpty(invalidPaths))
                throw new Exception("The following directories do not exist:" + invalidPaths);
        }

        protected override ValidationResult GenerateValidationResult()
        {
            ITestConfig config;
            var applicableTests = GetApplicableTests(ValidationType.Generic);
            switch (_settings.ValidationType)
            {
                case ValidationType.Generic:
                    config = new GenericTestConfig() { ValidationPaths = _settings.ValidationPaths.ToArray() };
                    break;
                case ValidationType.UnityPackage:
                    applicableTests.AddRange(GetApplicableTests(ValidationType.UnityPackage));
                    config = new GenericTestConfig() { ValidationPaths = _settings.ValidationPaths.ToArray() };
                    break;
                default:
                    return new ValidationResult() { Status = ValidationStatus.Failed, Exception = new Exception("Undefined validation type") };
            }

            var validationResult = RunTests(applicableTests, config);
            return validationResult;
        }
    }
}