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
        {
            FullPath = fullPath;
            AssetsRelativePath = assetsRelativePath;
            SuperSize = superSize;
        }

        public string FullPath { get; }
        public string AssetsRelativePath { get; }
        public int SuperSize { get; }
    }

    public static class ScreenshotUtility
    {
        private const string ScreenshotsFolderName = "Screenshots";

        public static ScreenshotCaptureResult CaptureToAssetsFolder(string fileName = null, int superSize = 1, bool ensureUniqueFileName = true)
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

            // Use only the file name to let Unity decide the final location (per CaptureScreenshot docs).
            string captureName = Path.GetFileName(normalizedFullPath);


#if UNITY_2022_1_OR_NEWER
            ScreenCapture.CaptureScreenshot(captureName, size);
#else
            Debug.LogWarning("ScreenCapture is supported after Unity 2022.1. Using main camera capture as fallback.");
            CaptureFromCameraToAssetsFolder(Camera.main, captureName, size, false);
#endif

            string projectRoot = GetProjectRootPath();
            string assetsRelativePath = normalizedFullPath;
            if (assetsRelativePath.StartsWith(projectRoot, StringComparison.OrdinalIgnoreCase))
            {
                assetsRelativePath = assetsRelativePath.Substring(projectRoot.Length).TrimStart('/');
            }

            return new ScreenshotCaptureResult(
                normalizedFullPath,
                assetsRelativePath,
                size);
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

            int width = Mathf.Max(1, camera.pixelWidth > 0 ? camera.pixelWidth : Screen.width);
            int height = Mathf.Max(1, camera.pixelHeight > 0 ? camera.pixelHeight : Screen.height);
            width *= size;
            height *= size;

            RenderTexture prevRT = camera.targetTexture;
            RenderTexture prevActive = RenderTexture.active;
            var rt = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
            try
            {
                camera.targetTexture = rt;
                camera.Render();

                RenderTexture.active = rt;
                var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
                tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                tex.Apply();

                byte[] png = tex.EncodeToPNG();
                File.WriteAllBytes(normalizedFullPath, png);
            }
            finally
            {
                camera.targetTexture = prevRT;
                RenderTexture.active = prevActive;
                RenderTexture.ReleaseTemporary(rt);
            }

            string projectRoot = GetProjectRootPath();
            string assetsRelativePath = normalizedFullPath;
            if (assetsRelativePath.StartsWith(projectRoot, StringComparison.OrdinalIgnoreCase))
            {
                assetsRelativePath = assetsRelativePath.Substring(projectRoot.Length).TrimStart('/');
            }

            return new ScreenshotCaptureResult(normalizedFullPath, assetsRelativePath, size);
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
