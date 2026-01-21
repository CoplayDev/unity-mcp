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
            : this(fullPath, assetsRelativePath, superSize, isAsync: false)
        {
        }

        public ScreenshotCaptureResult(string fullPath, string assetsRelativePath, int superSize, bool isAsync)
        {
            FullPath = fullPath;
            AssetsRelativePath = assetsRelativePath;
            SuperSize = superSize;
            IsAsync = isAsync;
        }

        public string FullPath { get; }
        public string AssetsRelativePath { get; }
        public int SuperSize { get; }
        public bool IsAsync { get; }
    }

    public static class ScreenshotUtility
    {
        private const string ScreenshotsFolderName = "Screenshots";
        private static bool s_loggedLegacyScreenCaptureFallback;

        private static Camera FindBestCamera()
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
            ScreenshotCaptureResult result = PrepareCaptureResult(fileName, superSize, ensureUniqueFileName, isAsync: true);
            ScreenCapture.CaptureScreenshot(result.AssetsRelativePath, result.SuperSize);
            return result;
#else
            if (!s_loggedLegacyScreenCaptureFallback)
            {
                Debug.Log("ScreenCapture is supported after Unity 2022.1. Using camera capture as fallback.");
                s_loggedLegacyScreenCaptureFallback = true;
            }

            var cam = FindBestCamera();
            if (cam == null)
            {
                throw new InvalidOperationException("No camera found to capture screenshot.");
            }

            return CaptureFromCameraToAssetsFolder(cam, fileName, superSize, ensureUniqueFileName);
#endif
        }

        /// <summary>
        /// Captures a screenshot from a specific camera by rendering into a temporary RenderTexture (works in Edit Mode).
        /// </summary>
        public static ScreenshotCaptureResult CaptureFromCameraToAssetsFolder(Camera camera, string fileName = null, int superSize = 1, bool ensureUniqueFileName = true)
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
            }
            finally
            {
                camera.targetTexture = prevRT;
                RenderTexture.active = prevActive;
                RenderTexture.ReleaseTemporary(rt);
                if (tex != null)
                {
                    if (Application.isPlaying)
                    {
                        UnityEngine.Object.Destroy(tex);
                    }
                    else
                    {
                        UnityEngine.Object.DestroyImmediate(tex);
                    }
                }
            }

            return result;
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
