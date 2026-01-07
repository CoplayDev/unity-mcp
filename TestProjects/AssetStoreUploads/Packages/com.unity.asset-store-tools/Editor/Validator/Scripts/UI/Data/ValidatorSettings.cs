using AssetStoreTools.Utility;
using AssetStoreTools.Validator.Data;
using AssetStoreTools.Validator.UI.Data.Serialization;
using AssetStoreTools.Validator.Utility;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace AssetStoreTools.Validator.UI.Data
{
    internal class ValidatorSettings : IValidatorSettings
    {
        private ValidatorStateSettings _stateData;

        private string _category;
        private ValidationType _validationType;
        private List<string> _validationPaths;

        public event Action OnCategoryChanged;
        public event Action OnValidationTypeChanged;
        public event Action OnValidationPathsChanged;
        public event Action OnRequireSerialize;

        public ValidatorSettings(ValidatorStateSettings stateData)
        {
            _stateData = stateData;

            _category = string.Empty;
            _validationType = ValidationType.UnityPackage;
            _validationPaths = new List<string>();

            Deserialize();
        }

        public void LoadSettings(ValidationSettings settings)
        {
            if (settings == null)
                return;

            var currentProjectValidationSettings = settings as CurrentProjectValidationSettings;
            if (currentProjectValidationSettings == null)
                throw new ArgumentException($"Only {nameof(CurrentProjectValidationSettings)} can be loaded");

            _category = currentProjectValidationSettings.Category;
            OnCategoryChanged?.Invoke();

            _validationType = currentProjectValidationSettings.ValidationType;
            OnValidationTypeChanged?.Invoke();

            _validationPaths = currentProjectValidationSettings.ValidationPaths.ToList();
            OnValidationPathsChanged?.Invoke();

            Serialize();
        }

        public string GetActiveCategory()
        {
            return _category;
        }

        public void SetActiveCategory(string category)
        {
            if (category == _category)
                return;

            _category = category;
            Serialize();
            OnCategoryChanged?.Invoke();
        }

        public List<string> GetAvailableCategories()
        {
            var categories = new HashSet<string>();

            var testData = ValidatorUtility.GetAutomatedTestCases();
            foreach (var test in testData)
            {
                if (test.CategoryInfo == null)
                    continue;

                foreach (var filter in test.CategoryInfo.Filter)
                    categories.Add(ConvertSlashToUnicodeSlash(filter));
            }

            return categories.OrderBy(x => x).ToList();
        }

        private string ConvertSlashToUnicodeSlash(string text)
        {
            return text.Replace('/', '\u2215');
        }

        public ValidationType GetValidationType()
        {
            return _validationType;
        }

        public void SetValidationType(ValidationType validationType)
        {
            if (validationType == _validationType)
                return;

            _validationType = validationType;

            Serialize();
            OnValidationTypeChanged?.Invoke();
        }

        public List<string> GetValidationPaths()
        {
            return _validationPaths;
        }

        public void AddValidationPath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return;

            if (_validationPaths.Contains(path))
                return;

            // Prevent redundancy for new paths
            var existingPath = _validationPaths.FirstOrDefault(x => path.StartsWith(x + "/"));
            if (existingPath != null)
            {
                Debug.LogWarning($"Path '{path}' is already included with existing path: '{existingPath}'");
                return;
            }

            // Prevent redundancy for already added paths
            var redundantPaths = _validationPaths.Where(x => x.StartsWith(path + "/")).ToArray();
            foreach (var redundantPath in redundantPaths)
            {
                Debug.LogWarning($"Existing validation path '{redundantPath}' has been made redundant by the inclusion of new validation path: '{path}'");
                _validationPaths.Remove(redundantPath);
            }

            _validationPaths.Add(path);

            Serialize();
            OnValidationPathsChanged?.Invoke();
        }

        public void RemoveValidationPath(string path)
        {
            if (!_validationPaths.Contains(path))
                return;

            _validationPaths.Remove(path);

            Serialize();
            OnValidationPathsChanged?.Invoke();
        }

        public void ClearValidationPaths()
        {
            if (_validationPaths.Count == 0)
                return;

            _validationPaths.Clear();

            Serialize();
            OnValidationPathsChanged?.Invoke();
        }

        public bool IsValidationPathValid(string path, out string error)
        {
            error = string.Empty;

            if (string.IsNullOrEmpty(path))
            {
                error = "Path cannot be empty";
                return false;
            }

            var isAssetsPath = path.StartsWith("Assets/")
                || path.Equals("Assets");
            var isPackagePath = PackageUtility.GetPackageByManifestPath($"{path}/package.json", out _);

            if (!isAssetsPath && !isPackagePath)
            {
                error = "Selected path must be within the Assets folder or point to a root path of a package";
                return false;
            }

            if (!Directory.Exists(path))
            {
                error = "Path does not exist";
                return false;
            }

            if (path.Split('/').Any(x => x.StartsWith(".") || x.EndsWith("~")))
            {
                error = $"Path '{path}' cannot be validated as it is a hidden folder and not part of the Asset Database";
                return false;
            }

            return true;
        }

        public IValidator CreateValidator()
        {
            var settings = new CurrentProjectValidationSettings()
            {
                Category = _category,
                ValidationType = _validationType,
                ValidationPaths = _validationPaths
            };

            var validator = new CurrentProjectValidator(settings);
            return validator;
        }

        private void Serialize()
        {
            _stateData.SetCategory(_category);
            _stateData.SetValidationType(_validationType);
            _stateData.SetValidationPaths(_validationPaths);

            OnRequireSerialize?.Invoke();
        }

        private void Deserialize()
        {
            if (_stateData == null)
                return;

            _category = _stateData.GetCategory();
            _validationType = _stateData.GetValidationType();
            foreach (var path in _stateData.GetValidationPaths())
                _validationPaths.Add(path);
        }
    }
}