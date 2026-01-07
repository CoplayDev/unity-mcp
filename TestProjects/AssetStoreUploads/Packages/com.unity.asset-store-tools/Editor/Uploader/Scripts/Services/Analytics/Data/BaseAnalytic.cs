using System;
#if UNITY_2023_2_OR_NEWER
using UnityEngine.Analytics;
#endif

namespace AssetStoreTools.Uploader.Services.Analytics.Data
{
    internal abstract class BaseAnalytic : IAssetStoreAnalytic
    {
        [Serializable]
        public class BaseAnalyticData : IAssetStoreAnalyticData
        {
            public string ToolVersion = Constants.Api.ApiVersion;
        }

        public abstract string EventName { get; }
        public abstract int EventVersion { get; }

        public IAssetStoreAnalyticData Data => GetData();
        protected abstract BaseAnalyticData GetData();

#if UNITY_2023_2_OR_NEWER
        public bool TryGatherData(out IAnalytic.IData data, [System.Diagnostics.CodeAnalysis.NotNullWhen(false)] out Exception error)
        {
            error = null;
            data = Data;

            if (data == null)
                error = new Exception("Analytic data is null");

            return error == null;
        }
#endif
    }
}