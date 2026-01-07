using AssetStoreTools.Api.Models;
using AssetStoreTools.Utility;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditorInternal;

namespace AssetStoreTools.Api
{
    internal class ApiUtility
    {
        public static Uri CreateUri(string url, bool includeDefaultAssetStoreQuery) => CreateUri(url, null, includeDefaultAssetStoreQuery);
        public static Uri CreateUri(string url, IDictionary<string, string> queryParameters, bool includeDefaultAssetStoreQuery)
        {
            IDictionary<string, string> fullQueryParameters = includeDefaultAssetStoreQuery ?
                    Constants.Api.DefaultAssetStoreQuery() : new Dictionary<string, string>();

            if (queryParameters != null && queryParameters.Count > 0)
            {
                foreach (var kvp in queryParameters)
                    fullQueryParameters.Add(kvp);
            }

            var builder = new UriBuilder(url);
            if (fullQueryParameters.Count == 0)
                return builder.Uri;

            var fullQueryParameterString = string.Empty;
            foreach (var queryParam in fullQueryParameters)
            {
                var escapedValue = queryParam.Value != null ? Uri.EscapeDataString(queryParam.Value) : string.Empty;
                fullQueryParameterString += $"{queryParam.Key}={escapedValue}&";
            }
            fullQueryParameterString = fullQueryParameterString.Remove(fullQueryParameterString.Length - 1);

            builder.Query = fullQueryParameterString;
            return builder.Uri;
        }

        public static List<Package> CombinePackageData(List<Package> mainPackageData, List<PackageAdditionalData> extraPackageData, List<Category> categoryData)
        {
            foreach (var package in mainPackageData)
            {
                var extraData = extraPackageData.FirstOrDefault(x => package.PackageId == x.PackageId);

                if (extraData == null)
                {
                    ASDebug.LogWarning($"Could not find extra data for Package {package.PackageId}");
                    continue;
                }

                var categoryId = extraData.CategoryId;
                var category = categoryData.FirstOrDefault(x => x.Id.ToString() == categoryId);
                if (category != null)
                    package.Category = category.Name;
                else
                    package.Category = "Unknown";

                package.Modified = extraData.Modified;
                package.Size = extraData.Size;
            }

            return mainPackageData;
        }

        public static string GetLicenseHash()
        {
            return InternalEditorUtility.GetAuthToken().Substring(0, 40);
        }

        public static string GetHardwareHash()
        {
            return InternalEditorUtility.GetAuthToken().Substring(40, 40);
        }
    }
}