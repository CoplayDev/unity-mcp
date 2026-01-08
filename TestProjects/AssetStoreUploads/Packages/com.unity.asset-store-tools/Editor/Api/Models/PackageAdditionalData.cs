using Newtonsoft.Json.Serialization;
using System.Collections.Generic;

namespace AssetStoreTools.Api.Models
{
    internal class PackageAdditionalData
    {
        public string Name { get; set; }
        public string Status { get; set; }
        public string PackageId { get; set; }
        public string VersionId { get; set; }
        public string CategoryId { get; set; }
        public string Modified { get; set; }
        public string Size { get; set; }

        public class AssetStorePackageResolver : DefaultContractResolver
        {
            private static AssetStorePackageResolver _instance;
            public static AssetStorePackageResolver Instance => _instance ?? (_instance = new AssetStorePackageResolver());

            private Dictionary<string, string> _propertyConversions;

            private AssetStorePackageResolver()
            {
                _propertyConversions = new Dictionary<string, string>()
                {
                    { nameof(PackageAdditionalData.PackageId), "id" },
                    { nameof(PackageAdditionalData.CategoryId), "category_id" }
                };
            }

            protected override string ResolvePropertyName(string propertyName)
            {
                if (_propertyConversions.ContainsKey(propertyName))
                    return _propertyConversions[propertyName];

                return base.ResolvePropertyName(propertyName);
            }
        }
    }
}