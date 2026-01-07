using Newtonsoft.Json.Serialization;
using System.Collections.Generic;

namespace AssetStoreTools.Api.Models
{
    internal class Package
    {
        public string PackageId { get; set; }
        public string VersionId { get; set; }
        public string Name { get; set; }
        public string Status { get; set; }
        public string Category { get; set; }
        public bool IsCompleteProject { get; set; }
        public string RootGuid { get; set; }
        public string RootPath { get; set; }
        public string ProjectPath { get; set; }
        public string Modified { get; set; }
        public string Size { get; set; }
        public string IconUrl { get; set; }

        public class AssetStorePackageResolver : DefaultContractResolver
        {
            private static AssetStorePackageResolver _instance;
            public static AssetStorePackageResolver Instance => _instance ?? (_instance = new AssetStorePackageResolver());

            private Dictionary<string, string> _propertyConversions;

            private AssetStorePackageResolver()
            {
                _propertyConversions = new Dictionary<string, string>()
                {
                    { nameof(Package.VersionId), "id" },
                    { nameof(Package.IsCompleteProject), "is_complete_project" },
                    { nameof(Package.RootGuid), "root_guid" },
                    { nameof(Package.RootPath), "root_path" },
                    { nameof(Package.ProjectPath), "project_path" },
                    { nameof(Package.IconUrl), "icon_url" }
                };
            }

            protected override string ResolvePropertyName(string propertyName)
            {
                if (_propertyConversions.ContainsKey(propertyName))
                    return _propertyConversions[propertyName];

                return base.ResolvePropertyName(propertyName);
            }
        }

        public class CachedPackageResolver : DefaultContractResolver
        {
            private static CachedPackageResolver _instance;
            public static CachedPackageResolver Instance => _instance ?? (_instance = new CachedPackageResolver());

            private Dictionary<string, string> _propertyConversion;

            private CachedPackageResolver()
            {
                this.NamingStrategy = new SnakeCaseNamingStrategy();
                _propertyConversion = new Dictionary<string, string>()
                {
                    { nameof(Package.PackageId), "package_id" },
                    { nameof(Package.VersionId), "version_id" },
                    { nameof(Package.IsCompleteProject), "is_complete_project" },
                    { nameof(Package.RootGuid), "root_guid" },
                    { nameof(Package.RootPath), "root_path" },
                    { nameof(Package.IconUrl), "icon_url" }
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