using AssetStoreTools.Utility;
using AssetStoreTools.Validator.UI.Data.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace AssetStoreTools.Validator.Services
{
    internal class CachingService : ICachingService
    {
        public bool GetCachedValidatorStateData(out ValidatorStateData stateData)
        {
            return GetCachedValidatorStateData(Constants.RootProjectPath, out stateData);
        }

        public bool GetCachedValidatorStateData(string projectPath, out ValidatorStateData stateData)
        {
            stateData = null;
            if (!CacheUtil.GetFileFromProjectPersistentCache(projectPath, Constants.Cache.ValidationResultFile, out var filePath))
                return false;

            try
            {
                var serializerSettings = new JsonSerializerSettings()
                {
                    ContractResolver = ValidatorStateDataContractResolver.Instance,
                    TypeNameHandling = TypeNameHandling.Auto,
                    Converters = new List<JsonConverter>() { new StringEnumConverter() }
                };

                stateData = JsonConvert.DeserializeObject<ValidatorStateData>(File.ReadAllText(filePath, Encoding.UTF8), serializerSettings);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public void CacheValidatorStateData(ValidatorStateData stateData)
        {
            var serializerSettings = new JsonSerializerSettings()
            {
                ContractResolver = ValidatorStateDataContractResolver.Instance,
                Formatting = Formatting.Indented,
                TypeNameHandling = TypeNameHandling.Auto,
                Converters = new List<JsonConverter>() { new StringEnumConverter() }
            };

            CacheUtil.CreateFileInPersistentCache(Constants.Cache.ValidationResultFile, JsonConvert.SerializeObject(stateData, serializerSettings), true);
        }
    }
}