using Newtonsoft.Json;

namespace AssetStoreTools.Validator.UI.Data.Serialization
{
    internal class ValidatorStateData
    {
        [JsonProperty("validation_settings")]
        private ValidatorStateSettings _settings;
        [JsonProperty("validation_results")]
        private ValidatorStateResults _results;

        public ValidatorStateData()
        {
            _settings = new ValidatorStateSettings();
            _results = new ValidatorStateResults();
        }

        public ValidatorStateSettings GetSettings()
        {
            return _settings;
        }

        public ValidatorStateResults GetResults()
        {
            return _results;
        }
    }
}