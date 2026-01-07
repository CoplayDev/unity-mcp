using AssetStoreTools.Api;
using AssetStoreTools.Validator.Data;
using System;
#if UNITY_2023_2_OR_NEWER
using UnityEngine.Analytics;
#endif
using AnalyticsConstants = AssetStoreTools.Constants.Uploader.Analytics;

namespace AssetStoreTools.Uploader.Services.Analytics.Data
{
#if UNITY_2023_2_OR_NEWER
    [AnalyticInfo
    (eventName: AnalyticsConstants.PackageUploadAnalytics.EventName,
    vendorKey: AnalyticsConstants.VendorKey,
    version: AnalyticsConstants.PackageUploadAnalytics.EventVersion,
    maxEventsPerHour: AnalyticsConstants.MaxEventsPerHour,
    maxNumberOfElements: AnalyticsConstants.MaxNumberOfElements)]
#endif
    internal class PackageUploadAnalytic : BaseAnalytic
    {
        [Serializable]
        public class PackageUploadAnalyticData : BaseAnalyticData
        {
            public string PackageId;
            public string Category;
            public bool UsedValidator;
            public string ValidatorResults;
            public string UploadFinishedReason;
            public double TimeTaken;
            public long PackageSize;
            public string Workflow;
            public string EndpointUrl;
        }

        public override string EventName => AnalyticsConstants.PackageUploadAnalytics.EventName;
        public override int EventVersion => AnalyticsConstants.PackageUploadAnalytics.EventVersion;

        private PackageUploadAnalyticData _data;

        public PackageUploadAnalytic(
            string packageId,
            string category,
            bool usedValidator,
            ValidationSettings validationSettings,
            ValidationResult validationResult,
            UploadStatus uploadFinishedReason,
            double timeTaken,
            long packageSize,
            string workflow
            )
        {
            _data = new PackageUploadAnalyticData()
            {
                PackageId = packageId,
                Category = category,
                UsedValidator = usedValidator,
                ValidatorResults = usedValidator ?
                    ValidationResultsSerializer.ConstructValidationResultsJson(validationSettings, validationResult) : null,
                UploadFinishedReason = uploadFinishedReason.ToString(),
                TimeTaken = timeTaken,
                PackageSize = packageSize,
                Workflow = workflow,
                EndpointUrl = Constants.Api.AssetStoreBaseUrl
            };
        }

        protected override BaseAnalyticData GetData()
        {
            return _data;
        }
    }
}