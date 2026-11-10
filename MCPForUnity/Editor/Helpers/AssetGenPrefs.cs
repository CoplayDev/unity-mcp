using MCPForUnity.Editor.Constants;
using UnityEditor;

namespace MCPForUnity.Editor.Helpers
{
    /// <summary>
    /// Per-user, NON-SECRET configuration for the AI Asset Generation feature.
    ///
    /// Provider API keys are never stored here — they live in the OS secure store
    /// (<see cref="MCPForUnity.Editor.Security.SecureKeyStore"/>). Only UI/behavior
    /// preferences (selected provider, default format, output folder, normalize toggle,
    /// per-provider enable flags) are kept in EditorPrefs.
    /// </summary>
    public static class AssetGenPrefs
    {
        public const string DefaultOutputRoot = "Assets/Generated";
        public const string DefaultModelProvider = "tripo";
        public const string DefaultImageProvider = "fal";
        public const string DefaultAudioProvider = "fal";
        public const string DefaultFormatValue = "glb";

        public static string ModelProvider
        {
            get => EditorPrefs.GetString(EditorPrefKeys.AssetGenSelectedModelProvider, DefaultModelProvider);
            set => SetOrDelete(EditorPrefKeys.AssetGenSelectedModelProvider, value);
        }

        public static string ImageProvider
        {
            get => EditorPrefs.GetString(EditorPrefKeys.AssetGenSelectedImageProvider, DefaultImageProvider);
            set => SetOrDelete(EditorPrefKeys.AssetGenSelectedImageProvider, value);
        }

        public static string AudioProvider
        {
            get => EditorPrefs.GetString(EditorPrefKeys.AssetGenSelectedAudioProvider, DefaultAudioProvider);
            set => SetOrDelete(EditorPrefKeys.AssetGenSelectedAudioProvider, value);
        }

        /// <summary>
        /// Selected model id for a (kind, provider) pair — the GUI dropdown writes it and the
        /// generate_* tools read it when the caller omits `model`. Empty by design: the tool's
        /// empty -> catalog-default chain is the single source of the default, so a blank value
        /// means "use the catalog default for this provider". Per-provider (not per-type) so
        /// disjoint provider model lists (Tripo vs Meshy, fal vs OpenRouter) never clobber each other.
        /// </summary>
        public static string GetSelectedModel(string kind, string providerId)
            => string.IsNullOrEmpty(providerId)
                ? string.Empty
                : EditorPrefs.GetString(ModelKey(kind, providerId), string.Empty);

        public static void SetSelectedModel(string kind, string providerId, string value)
        {
            if (string.IsNullOrEmpty(providerId)) return;
            SetOrDelete(ModelKey(kind, providerId), value);
        }

        private static string ModelKey(string kind, string providerId)
            => EditorPrefKeys.AssetGenSelectedModelPrefix + kind + "." + providerId;

        public static string DefaultFormat
        {
            get => EditorPrefs.GetString(EditorPrefKeys.AssetGenDefaultFormat, DefaultFormatValue);
            set => SetOrDelete(EditorPrefKeys.AssetGenDefaultFormat, value);
        }

        /// <summary>Project-relative root under which generated assets are written. Defaults to Assets/Generated.</summary>
        public static string OutputRoot
        {
            get
            {
                string v = EditorPrefs.GetString(EditorPrefKeys.AssetGenOutputRoot, string.Empty);
                return string.IsNullOrWhiteSpace(v) ? DefaultOutputRoot : v;
            }
            set => SetOrDelete(EditorPrefKeys.AssetGenOutputRoot, value);
        }

        /// <summary>When true, imported models are uniformly scaled to a target size on import.</summary>
        public static bool AutoNormalize
        {
            get => EditorPrefs.GetBool(EditorPrefKeys.AssetGenAutoNormalize, true);
            set => EditorPrefs.SetBool(EditorPrefKeys.AssetGenAutoNormalize, value);
        }

        public static bool IsProviderEnabled(string providerId) =>
            !string.IsNullOrEmpty(providerId)
            && EditorPrefs.GetBool(EditorPrefKeys.AssetGenProviderEnabledPrefix + providerId, false);

        public static void SetProviderEnabled(string providerId, bool enabled)
        {
            if (string.IsNullOrEmpty(providerId)) return;
            EditorPrefs.SetBool(EditorPrefKeys.AssetGenProviderEnabledPrefix + providerId, enabled);
        }

        private static void SetOrDelete(string key, string value)
        {
            if (string.IsNullOrWhiteSpace(value)) EditorPrefs.DeleteKey(key);
            else EditorPrefs.SetString(key, value.Trim());
        }
    }
}
