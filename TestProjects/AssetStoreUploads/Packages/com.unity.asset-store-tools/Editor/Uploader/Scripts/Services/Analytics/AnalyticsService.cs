using AssetStoreTools.Uploader.Services.Analytics.Data;
using UnityEditor;
using UnityEngine.Analytics;
#if !UNITY_2023_2_OR_NEWER
using AnalyticsConstants = AssetStoreTools.Constants.Uploader.Analytics;
#endif

namespace AssetStoreTools.Uploader.Services.Analytics
{
    internal class AnalyticsService : IAnalyticsService
    {
        public AnalyticsResult Send(IAssetStoreAnalytic analytic)
        {
            if (!EditorAnalytics.enabled)
                return AnalyticsResult.AnalyticsDisabled;

            if (!Register(analytic))
                return AnalyticsResult.AnalyticsDisabled;

#if UNITY_2023_2_OR_NEWER
            return EditorAnalytics.SendAnalytic(analytic);
#else
            return EditorAnalytics.SendEventWithLimit(analytic.EventName,
                analytic.Data,
                analytic.EventVersion);
#endif
        }

        private bool Register(IAssetStoreAnalytic analytic)
        {
#if UNITY_2023_2_OR_NEWER
            return true;
#else
            var result = EditorAnalytics.RegisterEventWithLimit(
                eventName: analytic.EventName,
                maxEventPerHour: AnalyticsConstants.MaxEventsPerHour,
                maxItems: AnalyticsConstants.MaxNumberOfElements,
                vendorKey: AnalyticsConstants.VendorKey,
                ver: analytic.EventVersion);

            return result == AnalyticsResult.Ok;
#endif
        }
    }
}