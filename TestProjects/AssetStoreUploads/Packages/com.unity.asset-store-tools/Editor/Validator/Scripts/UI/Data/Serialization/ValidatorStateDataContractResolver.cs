using Newtonsoft.Json.Serialization;

namespace AssetStoreTools.Validator.UI.Data.Serialization
{
    internal class ValidatorStateDataContractResolver : DefaultContractResolver
    {
        private static ValidatorStateDataContractResolver _instance;
        public static ValidatorStateDataContractResolver Instance => _instance ?? (_instance = new ValidatorStateDataContractResolver());

        private NamingStrategy _namingStrategy;

        private ValidatorStateDataContractResolver()
        {
            _namingStrategy = new SnakeCaseNamingStrategy();
        }

        protected override string ResolvePropertyName(string propertyName)
        {
            var resolvedName = _namingStrategy.GetPropertyName(propertyName, false);
            if (resolvedName.StartsWith("_"))
                return resolvedName.Substring(1);

            return resolvedName;
        }
    }
}