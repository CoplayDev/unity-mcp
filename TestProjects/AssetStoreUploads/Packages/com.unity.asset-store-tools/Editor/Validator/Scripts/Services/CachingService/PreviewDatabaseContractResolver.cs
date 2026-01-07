using Newtonsoft.Json.Serialization;

namespace AssetStoreTools.Previews.Services
{
    internal class PreviewDatabaseContractResolver : DefaultContractResolver
    {
        private static PreviewDatabaseContractResolver _instance;
        public static PreviewDatabaseContractResolver Instance => _instance ?? (_instance = new PreviewDatabaseContractResolver());

        private NamingStrategy _namingStrategy;

        private PreviewDatabaseContractResolver()
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