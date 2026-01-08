using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;

namespace AssetStoreTools.Api.Responses
{
    internal class PackageUploadedUnityVersionDataResponse : AssetStoreResponse
    {
        public List<string> UnityVersions { get; set; }

        public PackageUploadedUnityVersionDataResponse() : base() { }
        public PackageUploadedUnityVersionDataResponse(Exception e) : base(e) { }

        public PackageUploadedUnityVersionDataResponse(string json)
        {
            try
            {
                ValidateAssetStoreResponse(json);
                ParseVersionData(json);
                Success = true;
            }
            catch (Exception e)
            {
                Success = false;
                Exception = e;
            }
        }

        private void ParseVersionData(string json)
        {
            var data = JsonConvert.DeserializeObject<JObject>(json);
            try
            {
                var content = data.GetValue("content").ToObject<JObject>();
                UnityVersions = content.GetValue("unity_versions").ToObject<List<string>>();
            }
            catch
            {
                throw new Exception("Could not parse the unity versions array");
            }
        }
    }
}