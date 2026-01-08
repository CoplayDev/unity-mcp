using AssetStoreTools.Validator.Data;
using System;
using System.Collections.Generic;

namespace AssetStoreTools.Validator.UI.Data
{
    internal interface IValidatorSettings
    {
        event Action OnCategoryChanged;
        event Action OnValidationTypeChanged;
        event Action OnValidationPathsChanged;
        event Action OnRequireSerialize;

        void LoadSettings(ValidationSettings settings);

        string GetActiveCategory();
        void SetActiveCategory(string category);
        List<string> GetAvailableCategories();

        ValidationType GetValidationType();
        void SetValidationType(ValidationType validationType);

        List<string> GetValidationPaths();
        void AddValidationPath(string path);
        void RemoveValidationPath(string path);
        void ClearValidationPaths();
        bool IsValidationPathValid(string path, out string error);

        IValidator CreateValidator();
    }
}