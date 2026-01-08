using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;

namespace AssetStoreTools.Api.Responses
{
    internal class AssetStoreToolsVersionResponse : AssetStoreResponse
    {
        public string Version { get; set; }

        public AssetStoreToolsVersionResponse() : base() { }
        public AssetStoreToolsVersionResponse(Exception e) : base(e) { }

        public AssetStoreToolsVersionResponse(string json)
        {
            try
            {
                ValidateAssetStoreResponse(json);
                ParseVersion(json);
                Success = true;
            }
            catch (Exception e)
            {
                Success = false;
                Exception = e;
            }
        }

        private void ParseVersion(string json)
        {
            var dict = JsonConvert.DeserializeObject<JObject>(json);
            if (!dict.ContainsKey("version"))
                throw new Exception("Version was not found");

            Version = dict.GetValue("version").ToString();
        }
    }
}