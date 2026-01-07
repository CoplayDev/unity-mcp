using AssetStoreTools.Api.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;

namespace AssetStoreTools.Api.Responses
{
    internal class PackagesDataResponse : AssetStoreResponse
    {
        public List<Package> Packages { get; set; }

        public PackagesDataResponse() : base() { }
        public PackagesDataResponse(Exception e) : base(e) { }

        public PackagesDataResponse(string json)
        {
            try
            {
                ValidateAssetStoreResponse(json);
                ParseMainData(json);
                Success = true;
            }
            catch (Exception e)
            {
                Success = false;
                Exception = e;
            }
        }

        private void ParseMainData(string json)
        {
            var packageDict = JsonConvert.DeserializeObject<JObject>(json);
            if (!packageDict.ContainsKey("packages"))
                throw new Exception("Response did not not contain the list of packages");

            Packages = new List<Package>();
            var serializer = new JsonSerializer()
            {
                ContractResolver = Package.AssetStorePackageResolver.Instance
            };

            foreach (var packageToken in packageDict["packages"])
            {
                var property = (JProperty)packageToken;
                var packageData = property.Value.ToObject<Package>(serializer);

                // Package Id is the key of the package object
                packageData.PackageId = property.Name;

                // Package Icon Url is returned without the https: prefix
                if (!string.IsNullOrEmpty(packageData.IconUrl))
                    packageData.IconUrl = $"https:{packageData.IconUrl}";

                Packages.Add(packageData);
            }
        }
    }
}