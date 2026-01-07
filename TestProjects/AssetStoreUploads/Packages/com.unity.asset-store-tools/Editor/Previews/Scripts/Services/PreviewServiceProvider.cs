using AssetStoreTools.Utility;

namespace AssetStoreTools.Previews.Services
{
    internal class PreviewServiceProvider : ServiceProvider<IPreviewService>
    {
        public static PreviewServiceProvider Instance => _instance ?? (_instance = new PreviewServiceProvider());
        private static PreviewServiceProvider _instance;

        private PreviewServiceProvider() { }

        protected override void RegisterServices()
        {
            Register<ICachingService, CachingService>();
        }
    }
}