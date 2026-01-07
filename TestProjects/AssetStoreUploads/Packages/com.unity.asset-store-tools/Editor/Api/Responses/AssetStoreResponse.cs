using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;

namespace AssetStoreTools.Api.Responses
{
    /// <summary>
    /// A structure used to return the success outcome and the result of Asset Store API calls
    /// </summary>
    internal class AssetStoreResponse
    {
        public bool Success { get; set; } = false;
        public bool Cancelled { get; set; } = false;
        public Exception Exception { get; set; }

        public AssetStoreResponse() { }

        public AssetStoreResponse(Exception e) : this()
        {
            Exception = e;
        }

        protected void ValidateAssetStoreResponse(string json)
        {
            var dict = JsonConvert.DeserializeObject<JObject>(json);
            if (dict == null)
                throw new Exception("Response is empty");

            // Some json responses return an error field on error
            if (dict.ContainsKey("error"))
            {
                // Server side error message
                // Do not write to console since this is an error that 
                // is "expected" ie. can be handled by the gui.
                throw new Exception(dict.GetValue("error").ToString());
            }
            // Some json responses return status+message fields instead of an error field. Go figure.
            else if (dict.ContainsKey("status") && dict.GetValue("status").ToString() != "ok"
                && dict.ContainsKey("message"))
            {
                throw new Exception(dict.GetValue("message").ToString());
            }
        }
    }
}