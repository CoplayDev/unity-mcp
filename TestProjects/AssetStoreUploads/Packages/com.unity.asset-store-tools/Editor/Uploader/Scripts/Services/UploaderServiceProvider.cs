using AssetStoreTools.Api;
using AssetStoreTools.Uploader.Services.Analytics;
using AssetStoreTools.Uploader.Services.Api;
using AssetStoreTools.Utility;

namespace AssetStoreTools.Uploader.Services
{
    internal class UploaderServiceProvider : ServiceProvider<IUploaderService>
    {
        public static UploaderServiceProvider Instance => _instance ?? (_instance = new UploaderServiceProvider());
        private static UploaderServiceProvider _instance;

        private UploaderServiceProvider() { }

        protected override void RegisterServices()
        {
            var api = new AssetStoreApi(new AssetStoreClient());
            Register<IAnalyticsService, AnalyticsService>();
            Register<ICachingService, CachingService>();
            Register<IAuthenticationService>(() => new AuthenticationService(api, GetService<ICachingService>(), GetService<IAnalyticsService>()));
            Register<IPackageDownloadingService>(() => new PackageDownloadingService(api, GetService<ICachingService>()));
            Register<IPackageUploadingService>(() => new PackageUploadingService(api));
            Register<IPackageFactoryService, PackageFactoryService>();
        }
    }
}