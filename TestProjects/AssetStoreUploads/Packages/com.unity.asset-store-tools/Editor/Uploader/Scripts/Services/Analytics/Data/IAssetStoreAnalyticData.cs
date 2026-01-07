namespace AssetStoreTools.Uploader.Services.Analytics.Data
{
    interface IAssetStoreAnalyticData
#if UNITY_2023_2_OR_NEWER
            : UnityEngine.Analytics.IAnalytic.IData
#endif
    { }
}