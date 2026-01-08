using AssetStoreTools.Utility;
using AssetStoreTools.Validator.Services.Validation;

namespace AssetStoreTools.Validator.Services
{
    internal class ValidatorServiceProvider : ServiceProvider<IValidatorService>
    {
        public static ValidatorServiceProvider Instance => _instance ?? (_instance = new ValidatorServiceProvider());
        private static ValidatorServiceProvider _instance;

        private ValidatorServiceProvider() { }

        protected override void RegisterServices()
        {
            Register<ICachingService, CachingService>();
            Register<IAssetUtilityService, AssetUtilityService>();
            Register<IFileSignatureUtilityService, FileSignatureUtilityService>();
            Register<IMeshUtilityService, MeshUtilityService>();
            Register<IModelUtilityService, ModelUtilityService>();
            Register<ISceneUtilityService, SceneUtilityService>();
            Register<IScriptUtilityService, ScriptUtilityService>();
        }
    }
}