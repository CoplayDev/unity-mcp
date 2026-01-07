using AssetStoreTools.Validator.UI.Data.Serialization;

namespace AssetStoreTools.Validator.Services
{
    internal interface ICachingService : IValidatorService
    {
        void CacheValidatorStateData(ValidatorStateData stateData);
        bool GetCachedValidatorStateData(out ValidatorStateData stateData);
        bool GetCachedValidatorStateData(string projectPath, out ValidatorStateData stateData);
    }
}