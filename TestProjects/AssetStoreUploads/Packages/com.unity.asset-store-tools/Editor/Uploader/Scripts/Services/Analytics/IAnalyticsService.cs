using AssetStoreTools.Uploader.Services.Analytics.Data;
using UnityEngine.Analytics;

namespace AssetStoreTools.Uploader.Services.Analytics
{
    internal interface IAnalyticsService : IUploaderService
    {
        AnalyticsResult Send(IAssetStoreAnalytic analytic);
    }
}