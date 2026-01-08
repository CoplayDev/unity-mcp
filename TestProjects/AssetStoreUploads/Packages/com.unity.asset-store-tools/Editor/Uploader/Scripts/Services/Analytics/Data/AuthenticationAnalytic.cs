using AssetStoreTools.Api;
using System;
#if UNITY_2023_2_OR_NEWER
using UnityEngine.Analytics;
#endif
using AnalyticsConstants = AssetStoreTools.Constants.Uploader.Analytics;

namespace AssetStoreTools.Uploader.Services.Analytics.Data
{
#if UNITY_2023_2_OR_NEWER
    [AnalyticInfo
    (eventName: AnalyticsConstants.AuthenticationAnalytics.EventName,
    vendorKey: AnalyticsConstants.VendorKey,
    version: AnalyticsConstants.AuthenticationAnalytics.EventVersion,
    maxEventsPerHour: AnalyticsConstants.MaxEventsPerHour,
    maxNumberOfElements: AnalyticsConstants.MaxNumberOfElements)]
#endif
    internal class AuthenticationAnalytic : BaseAnalytic
    {
        [Serializable]
        public class AuthenticationAnalyticData : BaseAnalyticData
        {
            public string AuthenticationType;
            public string PublisherId;
        }

        public override string EventName => AnalyticsConstants.AuthenticationAnalytics.EventName;
        public override int EventVersion => AnalyticsConstants.AuthenticationAnalytics.EventVersion;

        private AuthenticationAnalyticData _data;

        public AuthenticationAnalytic(IAuthenticationType authenticationType, string publisherId)
        {
            _data = new AuthenticationAnalyticData
            {
                AuthenticationType = authenticationType.GetType().Name,
                PublisherId = publisherId
            };
        }

        protected override BaseAnalyticData GetData()
        {
            return _data;
        }
    }
}