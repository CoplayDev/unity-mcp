using System;
using System.IO;
using System.Linq;
using UnityEngine;

namespace MCPForUnity.Runtime.Helpers
//The reason for having another Runtime Utilities in additional to Editor Utilities is to avoid Editor-only dependencies in this runtime code.
{
    public readonly struct ScreenshotCaptureResult
    {
        public ScreenshotCaptureResult(string fullPath, string assetsRelativePath, int superSize)
            : this(fullPath, assetsRelativePath, superSize, isAsync: false, imageBase64: null, imageWidth: 0, imageHeight: 0)
        {
        }

        public ScreenshotCaptureResult(string fullPath, string assetsRelativePath, int superSize, bool isAsync)
            : this(fullPath, assetsRelativePath, superSize, isAsync, imageBase64: null, imageWidth: 0, imageHeight: 0)
        {
        }

        public ScreenshotCaptureResult(string fullPath, string assetsRelativePath, int superSize, bool isAsync,
            string imageBase64, int imageWidth, int imageHeight)
        {
            FullPath = fullPath;
            AssetsRelativePath = assetsRelativePath;
            SuperSize = superSize;
            IsAsync = isAsync;
            ImageBase64 = imageBase64;
            ImageWidth = imageWidth;
            ImageHeight = imageHeight;
        }

        public string FullPath { get; }
        public string AssetsRelativePath { get; }
        public int SuperSize { get; }
        public bool IsAsync { get; }
        /// <summary>Base64-encoded PNG image data. Only populated when include_image is true.</summary>
        public string ImageBase64 { get; }
        public int ImageWidth { get; }
        public int ImageHeight { get; }
    }

    public static class ScreenshotUtility
    {
        private const string ScreenshotsFolderName = "Screenshots";
        private static bool s_loggedLegacyScreenCaptureFallback;
        private static bool? s_screenCaptureModuleAvailable;

        /// <summary>
        /// Checks if the Screen Capture module (com.unity.modules.screencapture) is enabled.
        /// This module can be disabled in Package Manager > Built-in, which removes the ScreenCapture class.
        /// </summary>
        public static bool IsScreenCaptureModuleAvailable
        {
            get
            {
                if (!s_screenCaptureModuleAvailable.HasValue)
                {
                    // Check if ScreenCapture type exists (module might be disabled)
                    s_screenCaptureModuleAvailable = Type.GetType("UnityEngine.ScreenCapture, UnityEngine.ScreenCaptureModule") != null
                        || Type.GetType("UnityEngine.ScreenCapture, UnityEngine.CoreModule") != null;
                }
                return s_screenCaptureModuleAvailable.Value;
            }
        }

        /// <summary>
        /// Error message to display when Screen Capture module is not available.
        /// </summary>
        public const string ScreenCaptureModuleNotAvailableError =
            "The Screen Capture module (com.unity.modules.screencapture) is not enabled. " +
            "To use screenshot capture with ScreenCapture API, please enable it in Unity: " +
            "Window > Package Manager > Built-in > Screen Capture > Enable. " +
            "Alternatively, MCP for Unity will use camera-based capture as a fallback if a Camera exists in the scene.";

        private static Camera FindAvailableCamera()
        {
            var main = Camera.main;
            if (main != null)
            {
                return main;
            }

            try
            {
                // Use FindObjectsOfType for Unity 2021 compatibility.
                var cams = UnityEngine.Object.FindObjectsOfType<Camera>();
                return cams.FirstOrDefault();
            }
            catch
            {
                return null;
            }
        }

        public static ScreenshotCaptureResult CaptureToAssetsFolder(string fileName = null, int superSize = 1, bool ensureUniqueFileName = true)
        {
#if UNITY_2022_1_OR_NEWER
            // Check if Screen Capture module is available (can be disabled in Package Manager > Built-in)
            if (IsScreenCaptureModuleAvailable)
            {
                ScreenshotCaptureResult result = PrepareCaptureResult(fileName, superSize, ensureUniqueFileName, isAsync: true);
                ScreenCapture.CaptureScreenshot(result.AssetsRelativePath, result.SuperSize);
                return result;
            }
            else
            {
                // Module disabled - try camera fallback
                Debug.LogWarning("[MCP for Unity] " + ScreenCaptureModuleNotAvailableError);
                return CaptureWithCameraFallback(fileName, superSize, ensureUniqueFileName);
            }
#else
            // Unity < 2022.1 - always use camera fallback
            return CaptureWithCameraFallback(fileName, superSize, ensureUniqueFileName);
#endif
        }

        private static ScreenshotCaptureResult CaptureWithCameraFallback(string fileName, int superSize, bool ensureUniqueFileName)
        {
            if (!s_loggedLegacyScreenCaptureFallback)
            {
                Debug.Log("[MCP for Unity] Using camera-based screenshot capture. " +
                    "This requires a Camera in the scene. For best results on Unity 2022.1+, ensure the Screen Capture module is enabled: " +
                    "Window > Package Manager > Built-in > Screen Capture > Enable.");
                s_loggedLegacyScreenCaptureFallback = true;
            }

            var cam = FindAvailableCamera();
            if (cam == null)
            {
                throw new InvalidOperationException(
                    "No camera found to capture screenshot. Camera-based capture requires a Camera in the scene. " +
                    "Either add a Camera to your scene, or enable the Screen Capture module: " +
                    "Window > Package Manager > Built-in > Screen Capture > Enable."
                );
            }

            return CaptureFromCameraToAssetsFolder(cam, fileName, superSize, ensureUniqueFileName);
        }

        /// <summary>
        /// Captures a screenshot from a specific camera by rendering into a temporary RenderTexture (works in Edit Mode).
        /// When <paramref name="includeImage"/> is true, the result includes a base64-encoded PNG (optionally
        /// downscaled so the longest edge is at most <paramref name="maxResolution"/>).
        /// </summary>
        public static ScreenshotCaptureResult CaptureFromCameraToAssetsFolder(
            Camera camera,
            string fileName = null,
            int superSize = 1,
            bool ensureUniqueFileName = true,
            bool includeImage = false,
            int maxResolution = 0)
        {
            if (camera == null)
            {
                throw new ArgumentNullException(nameof(camera));
            }

            ScreenshotCaptureResult result = PrepareCaptureResult(fileName, superSize, ensureUniqueFileName, isAsync: false);
            int size = result.SuperSize;

            int width = Mathf.Max(1, camera.pixelWidth > 0 ? camera.pixelWidth : Screen.width);
            int height = Mathf.Max(1, camera.pixelHeight > 0 ? camera.pixelHeight : Screen.height);
            width *= size;
            height *= size;

            RenderTexture prevRT = camera.targetTexture;
            RenderTexture prevActive = RenderTexture.active;
            var rt = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
            Texture2D tex = null;
            Texture2D downscaled = null;
            string imageBase64 = null;
            int imgW = 0, imgH = 0;
            try
            {
                camera.targetTexture = rt;
                camera.Render();

                RenderTexture.active = rt;
                tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
                tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                tex.Apply();

                byte[] png = tex.EncodeToPNG();
                File.WriteAllBytes(result.FullPath, png);

                if (includeImage)
                {
                    int targetMax = maxResolution > 0 ? maxResolution : 640;
                    if (width > targetMax || height > targetMax)
                    {
                        downscaled = DownscaleTexture(tex, targetMax);
                        byte[] smallPng = downscaled.EncodeToPNG();
                        imageBase64 = System.Convert.ToBase64String(smallPng);
                        imgW = downscaled.width;
                        imgH = downscaled.height;
                    }
                    else
                    {
                        imageBase64 = System.Convert.ToBase64String(png);
                        imgW = width;
                        imgH = height;
                    }
                }
            }
            finally
            {
                camera.targetTexture = prevRT;
                RenderTexture.active = prevActive;
                RenderTexture.ReleaseTemporary(rt);
                DestroyTexture(tex);
                DestroyTexture(downscaled);
            }

            if (includeImage && imageBase64 != null)
            {
                return new ScreenshotCaptureResult(
                    result.FullPath, result.AssetsRelativePath, result.SuperSize, false,
                    imageBase64, imgW, imgH);
            }
            return result;
        }

        /// <summary>
        /// Renders a camera to a Texture2D without saving to disk. Used for multi-angle captures.
        /// Returns the base64-encoded PNG, downscaled to fit within <paramref name="maxResolution"/>.
        /// </summary>
        public static (string base64, int width, int height) RenderCameraToBase64(Camera camera, int maxResolution = 640)
        {
            if (camera == null) throw new ArgumentNullException(nameof(camera));

            int width = Mathf.Max(1, camera.pixelWidth > 0 ? camera.pixelWidth : Screen.width);
            int height = Mathf.Max(1, camera.pixelHeight > 0 ? camera.pixelHeight : Screen.height);

            RenderTexture prevRT = camera.targetTexture;
            RenderTexture prevActive = RenderTexture.active;
            var rt = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
            Texture2D tex = null;
            Texture2D downscaled = null;
            try
            {
                camera.targetTexture = rt;
                camera.Render();

                RenderTexture.active = rt;
                tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
                tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                tex.Apply();

                int targetMax = maxResolution > 0 ? maxResolution : 640;
                if (width > targetMax || height > targetMax)
                {
                    downscaled = DownscaleTexture(tex, targetMax);
                    string b64 = System.Convert.ToBase64String(downscaled.EncodeToPNG());
                    return (b64, downscaled.width, downscaled.height);
                }
                else
                {
                    string b64 = System.Convert.ToBase64String(tex.EncodeToPNG());
                    return (b64, width, height);
                }
            }
            finally
            {
                camera.targetTexture = prevRT;
                RenderTexture.active = prevActive;
                RenderTexture.ReleaseTemporary(rt);
                DestroyTexture(tex);
                DestroyTexture(downscaled);
            }
        }

        /// <summary>
        /// Downscales a Texture2D so that its longest edge is at most <paramref name="maxEdge"/> pixels.
        /// Uses bilinear filtering via a temporary RenderTexture blit.
        /// Caller must destroy the returned Texture2D.
        /// </summary>
        public static Texture2D DownscaleTexture(Texture2D source, int maxEdge)
        {
            int srcW = source.width;
            int srcH = source.height;
            float scale = Mathf.Min((float)maxEdge / srcW, (float)maxEdge / srcH);
            scale = Mathf.Min(scale, 1f); // never upscale
            int dstW = Mathf.Max(1, Mathf.RoundToInt(srcW * scale));
            int dstH = Mathf.Max(1, Mathf.RoundToInt(srcH * scale));

            RenderTexture prevActive = RenderTexture.active;
            var rt = RenderTexture.GetTemporary(dstW, dstH, 0, RenderTextureFormat.ARGB32);
            rt.filterMode = FilterMode.Bilinear;
            try
            {
                Graphics.Blit(source, rt);
                RenderTexture.active = rt;
                var dst = new Texture2D(dstW, dstH, TextureFormat.RGBA32, false);
                dst.ReadPixels(new Rect(0, 0, dstW, dstH), 0, 0);
                dst.Apply();
                return dst;
            }
            finally
            {
                RenderTexture.active = prevActive;
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        private static void DestroyTexture(Texture2D tex)
        {
            if (tex == null) return;
            if (Application.isPlaying)
                UnityEngine.Object.Destroy(tex);
            else
                UnityEngine.Object.DestroyImmediate(tex);
        }

        private static ScreenshotCaptureResult PrepareCaptureResult(string fileName, int superSize, bool ensureUniqueFileName, bool isAsync)
        {
            int size = Mathf.Max(1, superSize);
            string resolvedName = BuildFileName(fileName);
            string folder = Path.Combine(Application.dataPath, ScreenshotsFolderName);
            Directory.CreateDirectory(folder);

            string fullPath = Path.Combine(folder, resolvedName);
            if (ensureUniqueFileName)
            {
                fullPath = EnsureUnique(fullPath);
            }

            string normalizedFullPath = fullPath.Replace('\\', '/');
            string assetsRelativePath = ToAssetsRelativePath(normalizedFullPath);

            return new ScreenshotCaptureResult(normalizedFullPath, assetsRelativePath, size, isAsync);
        }

        private static string ToAssetsRelativePath(string normalizedFullPath)
        {
            string projectRoot = GetProjectRootPath();
            string assetsRelativePath = normalizedFullPath;
            if (assetsRelativePath.StartsWith(projectRoot, StringComparison.OrdinalIgnoreCase))
            {
                assetsRelativePath = assetsRelativePath.Substring(projectRoot.Length).TrimStart('/');
            }
            return assetsRelativePath;
        }

        private static string BuildFileName(string fileName)
        {
            string name = string.IsNullOrWhiteSpace(fileName)
                ? $"screenshot-{DateTime.Now:yyyyMMdd-HHmmss}"
                : fileName.Trim();

            name = SanitizeFileName(name);

            if (!name.EndsWith(".png", StringComparison.OrdinalIgnoreCase) &&
                !name.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) &&
                !name.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase))
            {
                name += ".png";
            }

            return name;
        }

        private static string SanitizeFileName(string fileName)
        {
            var invalidChars = Path.GetInvalidFileNameChars();
            string cleaned = new string(fileName.Select(ch => invalidChars.Contains(ch) ? '_' : ch).ToArray());

            return string.IsNullOrWhiteSpace(cleaned) ? "screenshot" : cleaned;
        }

        private static string EnsureUnique(string path)
        {
            if (!File.Exists(path))
            {
                return path;
            }

            string directory = Path.GetDirectoryName(path) ?? string.Empty;
            string baseName = Path.GetFileNameWithoutExtension(path);
            string extension = Path.GetExtension(path);
            int counter = 1;

            string candidate;
            do
            {
                candidate = Path.Combine(directory, $"{baseName}-{counter}{extension}");
                counter++;
            } while (File.Exists(candidate));

            return candidate;
        }

        private static string GetProjectRootPath()
        {
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            root = root.Replace('\\', '/');
            if (!root.EndsWith("/", StringComparison.Ordinal))
            {
                root += "/";
            }
            return root;
        }
    }
}
