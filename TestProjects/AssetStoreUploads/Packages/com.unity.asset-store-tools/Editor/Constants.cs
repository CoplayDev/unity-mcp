using AssetStoreTools.Previews.Data;
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace AssetStoreTools
{
    internal class Constants
    {
#if UNITY_EDITOR_OSX
        public static readonly string UnityPath = System.IO.Path.Combine(EditorApplication.applicationPath, "Contents", "MacOS", "Unity");
#else
        public static readonly string UnityPath = EditorApplication.applicationPath;
#endif
        public static readonly string RootProjectPath = Application.dataPath.Substring(0, Application.dataPath.Length - "Assets".Length);

        private static bool GetArgument(string argumentName, out string argumentValue)
        {
            argumentValue = string.Empty;
            var args = Environment.GetCommandLineArgs();

            for (int i = 0; i < args.Length; i++)
            {
                if (!args[i].Equals(argumentName, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (i + 1 >= args.Length)
                    return false;

                argumentValue = args[i + 1];
                break;
            }

            return !string.IsNullOrEmpty(argumentValue);
        }

        public class Api
        {
            public static readonly string ApiVersion = $"V{PackageInfo.FindForAssetPath("Packages/com.unity.asset-store-tools").version}";
            public const string AssetStoreToolsLatestVersionUrl = "https://api.assetstore.unity3d.com/package/latest-version/115";

            private const string AssetStoreBaseUrlDefault = "https://kharma.unity3d.com";
            private const string AssetStoreBaseUrlOverrideArgument = "-assetStoreUrl";
            public static readonly string AssetStoreBaseUrl = !GetArgument(AssetStoreBaseUrlOverrideArgument, out var overriddenUrl)
                ? AssetStoreBaseUrlDefault
                : overriddenUrl;

            public static readonly string AuthenticateUrl = $"{AssetStoreBaseUrl}/login";
            public static readonly string GetPackagesUrl = $"{AssetStoreBaseUrl}/api/asset-store-tools/metadata/0.json";
            public static readonly string GetPackagesAdditionalDataUrl = $"{AssetStoreBaseUrl}/api/management/packages.json";
            public static readonly string GetCategoriesUrl = $"{AssetStoreBaseUrl}/api/management/categories.json";

            public static string GetPackageUploadedVersionsUrl(string packageId, string versionId) =>
                $"{AssetStoreBaseUrl}/api/content/preview/{packageId}/{versionId}.json";
            public static string UploadUnityPackageUrl(string versionId) =>
                $"{AssetStoreBaseUrl}/api/asset-store-tools/package/{versionId}/unitypackage.json";

            public static IDictionary<string, string> DefaultAssetStoreQuery()
            {
                var dict = new Dictionary<string, string>()
                {
                    { "unityversion", Application.unityVersion },
                    { "toolversion", ApiVersion }
                };
                return dict;
            }
        }

        public class Updater
        {
            public const string AssetStoreToolsUrl = "https://assetstore.unity.com/packages/tools/utilities/asset-store-publishing-tools-115";
        }

        public class Cache
        {
            public const string SessionTokenKey = "kharma.sessionid";
            public const string TempCachePath = "Temp/AssetStoreToolsCache";
            public const string PersistentCachePath = "Library/AssetStoreToolsCache";

            public const string PackageDataFileName = "PackageMetadata.json";
            public const string CategoryDataFile = "Categories.json";
            public const string ValidationResultFile = "ValidationStateData.asset";

            public static string PackageThumbnailFileName(string packageId) => $"{packageId}.png";
            public static string WorkflowStateDataFileName(string packageId) => $"{packageId}-workflowStateData.asset";
        }

        public class Uploader
        {
            public const string MinRequiredUnitySupportVersion = "2021.3";
            public const long MaxPackageSizeBytes = 6576668672; // 6 GB + 128MB of headroom
            public const string AccountRegistrationUrl = "https://publisher.unity.com/access";
            public const string AccountForgottenPasswordUrl = "https://id.unity.com/password/new";

            public class Analytics
            {
                public const string VendorKey = "unity.assetStoreTools";
                public const int MaxEventsPerHour = 20;
                public const int MaxNumberOfElements = 1000;

                public class AuthenticationAnalytics
                {
                    public const string EventName = "assetStoreToolsLogin";
                    public const int EventVersion = 1;
                }

                public class PackageUploadAnalytics
                {
                    public const string EventName = "assetStoreTools";
                    public const int EventVersion = 3;
                }
            }
        }

        public class Validator
        {
            public const string SubmissionGuidelinesUrl = "https://assetstore.unity.com/publishing/submission-guidelines#Overview";
            public const string SupportTicketUrl = "https://support.unity.com/hc/en-us/requests/new?ticket_form_id=65905";

            public class Tests
            {
                public const string TestDefinitionsPath = "Packages/com.unity.asset-store-tools/Editor/Validator/Tests";
                public const string TestMethodsPath = "Packages/com.unity.asset-store-tools/Editor/Validator/Scripts/Test Methods";

                public static readonly string GenericTestMethodsPath = $"{TestMethodsPath}/Generic";
                public static readonly string UnityPackageTestMethodsPath = $"{TestMethodsPath}/UnityPackage";
            }
        }

        public class Previews
        {
            public const string PreviewDatabaseFile = "PreviewDatabase.json";

            public static readonly string DefaultOutputPath = $"{Cache.TempCachePath}/AssetPreviews";
            public const FileNameFormat DefaultFileNameFormat = FileNameFormat.Guid;

            public class Native
            {
                public static readonly string DefaultOutputPath = $"{Previews.DefaultOutputPath}/Native";
                public const PreviewFormat DefaultFormat = PreviewFormat.PNG;
                public const bool DefaultWaitForPreviews = true;
                public const bool DefaultChunkedPreviewLoading = true;
                public const int DefaultChunkSize = 100;
            }

            public class Custom
            {
                public static readonly string DefaultOutputPath = $"{Previews.DefaultOutputPath}/Custom";
                public const PreviewFormat DefaultFormat = PreviewFormat.JPG;
                public const int DefaultWidth = 300;
                public const int DefaultHeight = 300;
                public const int DefaultDepth = 32;

                public const int DefaultNativeWidth = 900;
                public const int DefaultNativeHeight = 900;

                public static readonly Color DefaultAudioSampleColor = new Color(1f, 0.55f, 0);
                public static readonly Color DefaultAudioBackgroundColor = new Color(0.32f, 0.32f, 0.32f);
            }
        }

        public class WindowStyles
        {
            public const string UploaderStylesPath = "Packages/com.unity.asset-store-tools/Editor/Uploader/Styles";
            public const string ValidatorStylesPath = "Packages/com.unity.asset-store-tools/Editor/Validator/Styles";
            public const string ValidatorIconsPath = "Packages/com.unity.asset-store-tools/Editor/Validator/Icons";
            public const string PreviewGeneratorStylesPath = "Packages/com.unity.asset-store-tools/Editor/Previews/Styles";
            public const string UpdaterStylesPath = "Packages/com.unity.asset-store-tools/Editor/Utility/Styles/Updater";
        }

        public class Debug
        {
            public const string DebugModeKey = "ASTDebugMode";
        }
    }
}