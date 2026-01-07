using AssetStoreTools.Api.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;

namespace AssetStoreTools.Api.Responses
{
    internal class PackagesAdditionalDataResponse : AssetStoreResponse
    {
        public List<PackageAdditionalData> Packages { get; set; }

        public PackagesAdditionalDataResponse() : base() { }
        public PackagesAdditionalDataResponse(Exception e) : base(e) { }

        public PackagesAdditionalDataResponse(string json)
        {
            try
            {
                ValidateAssetStoreResponse(json);
                ParseExtraData(json);
                Success = true;
            }
            catch (Exception e)
            {
                Success = false;
                Exception = e;
            }
        }

        private void ParseExtraData(string json)
        {
            var packageDict = JsonConvert.DeserializeObject<JObject>(json);
            if (!packageDict.ContainsKey("packages"))
                throw new Exception("Response did not not contain the list of packages");

            Packages = new List<PackageAdditionalData>();
            var serializer = new JsonSerializer()
            {
                ContractResolver = PackageAdditionalData.AssetStorePackageResolver.Instance
            };

            var packageArray = packageDict.GetValue("packages").ToObject<JArray>();
            foreach (var packageData in packageArray)
            {
                var package = packageData.ToObject<PackageAdditionalData>(serializer);

                // Some fields are based on the latest version in the json
                var latestVersion = packageData["versions"].ToObject<JArray>().Last;

                package.VersionId = latestVersion["id"].ToString();
                package.Modified = latestVersion["modified"].ToString();
                package.Size = latestVersion["size"].ToString();

                Packages.Add(package);
            }
        }
    }
}