using AssetStoreTools.Validator.Data;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Linq;

namespace AssetStoreTools.Validator.UI.Data.Serialization
{
    internal class ValidatorStateSettings
    {
        [JsonProperty("category")]
        private string _category;
        [JsonProperty("validation_type")]
        private ValidationType _validationType;
        [JsonProperty("validation_paths")]
        private List<string> _validationPaths;

        public ValidatorStateSettings()
        {
            _category = string.Empty;
            _validationType = ValidationType.UnityPackage;
            _validationPaths = new List<string>();
        }

        public string GetCategory()
        {
            return _category;
        }

        public void SetCategory(string category)
        {
            if (_category == category)
                return;

            _category = category;
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
        }

        public List<string> GetValidationPaths()
        {
            return _validationPaths;
        }

        public void SetValidationPaths(List<string> validationPaths)
        {
            if (_validationPaths.SequenceEqual(validationPaths))
                return;

            _validationPaths = validationPaths;
        }
    }
}