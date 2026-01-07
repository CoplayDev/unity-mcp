#if UNITY_2023_2_OR_NEWER
using UnityEngine.Analytics;
#endif

namespace AssetStoreTools.Uploader.Services.Analytics.Data
{
    internal interface IAssetStoreAnalytic
#if UNITY_2023_2_OR_NEWER
        : IAnalytic
#endif
    {
        string EventName { get; }
        int EventVersion { get; }
        IAssetStoreAnalyticData Data { get; }
    }
}