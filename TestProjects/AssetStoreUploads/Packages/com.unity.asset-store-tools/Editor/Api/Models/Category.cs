using Newtonsoft.Json.Serialization;
using System.Collections.Generic;

namespace AssetStoreTools.Api.Models
{
    internal class Category
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Status { get; set; }

        public class AssetStoreCategoryResolver : DefaultContractResolver
        {
            private Dictionary<string, string> _propertyConversions;

            public AssetStoreCategoryResolver()
            {
                _propertyConversions = new Dictionary<string, string>()
                {
                    { nameof(Category.Name), "assetstore_name" }
                };
            }

            protected override string ResolvePropertyName(string propertyName)
            {
                if (_propertyConversions.ContainsKey(propertyName))
                    return _propertyConversions[propertyName];

                return base.ResolvePropertyName(propertyName);
            }
        }

        public class CachedCategoryResolver : DefaultContractResolver
        {
            private static CachedCategoryResolver _instance;
            public static CachedCategoryResolver Instance => _instance ?? (_instance = new CachedCategoryResolver());

            private Dictionary<string, string> _propertyConversion;

            private CachedCategoryResolver()
            {
                this.NamingStrategy = new SnakeCaseNamingStrategy();
                _propertyConversion = new Dictionary<string, string>()
                {
                    { nameof(Category.Name), "name" }
                };
            }

            protected override string ResolvePropertyName(string propertyName)
            {
                if (_propertyConversion.ContainsKey(propertyName))
                    return _propertyConversion[propertyName];

                return base.ResolvePropertyName(propertyName);
            }
        }
    }
}