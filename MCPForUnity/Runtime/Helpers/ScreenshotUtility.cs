using System;
using System.Collections.Generic;
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
        /// Renders a camera to a Texture2D without saving to disk.
        /// Caller owns the returned texture and must destroy it.
        /// </summary>
        public static Texture2D RenderCameraToTexture(Camera camera, int maxResolution = 640)
        {
            if (camera == null) throw new ArgumentNullException(nameof(camera));

            int width = Mathf.Max(1, camera.pixelWidth > 0 ? camera.pixelWidth : Screen.width);
            int height = Mathf.Max(1, camera.pixelHeight > 0 ? camera.pixelHeight : Screen.height);

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

                int targetMax = maxResolution > 0 ? maxResolution : 640;
                if (width > targetMax || height > targetMax)
                {
                    var downscaled = DownscaleTexture(tex, targetMax);
                    DestroyTexture(tex);
                    return downscaled;
                }
                return tex;
            }
            finally
            {
                camera.targetTexture = prevRT;
                RenderTexture.active = prevActive;
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        /// <summary>
        /// Composites a list of tile textures into a single contact-sheet grid image.
        /// Labels are drawn as white text on a dark banner at the bottom of each tile.
        /// Returns base64 PNG plus dimensions. Destroys all input tile textures.
        /// </summary>
        public static (string base64, int width, int height) ComposeContactSheet(
            List<Texture2D> tiles, List<string> labels, int padding = 4)
        {
            if (tiles == null || tiles.Count == 0)
                throw new ArgumentException("No tiles to compose.", nameof(tiles));

            int tileW = tiles[0].width;
            int tileH = tiles[0].height;
            int count = tiles.Count;

            // Calculate grid: prefer wider than tall (cols >= rows)
            int cols = Mathf.CeilToInt(Mathf.Sqrt(count));
            int rows = Mathf.CeilToInt((float)count / cols);

            int labelHeight = Mathf.Max(14, tileH / 12);
            int cellW = tileW + padding;
            int cellH = tileH + labelHeight + padding;

            int sheetW = cols * cellW + padding;
            int sheetH = rows * cellH + padding;

            Texture2D sheet = null;
            try
            {
                sheet = new Texture2D(sheetW, sheetH, TextureFormat.RGBA32, false);

                // Fill background with dark gray
                var bgColor = new Color32(30, 30, 30, 255);
                Color32[] bgPixels = new Color32[sheetW * sheetH];
                for (int i = 0; i < bgPixels.Length; i++) bgPixels[i] = bgColor;
                sheet.SetPixels32(bgPixels);

                for (int idx = 0; idx < count; idx++)
                {
                    int col = idx % cols;
                    int row = idx / cols;
                    // Place tiles top-left to bottom-right (Unity Texture2D y=0 is bottom)
                    int x = padding + col * cellW;
                    int y = sheetH - padding - (row + 1) * cellH + padding;

                    // Draw tile pixels
                    Color32[] tilePixels = tiles[idx].GetPixels32();
                    for (int ty = 0; ty < tileH; ty++)
                    {
                        for (int tx = 0; tx < tileW; tx++)
                        {
                            sheet.SetPixel(x + tx, y + labelHeight + ty, tiles[idx].GetPixel(tx, ty));
                        }
                    }

                    // Draw label banner (dark background strip below tile)
                    var bannerColor = new Color32(20, 20, 20, 220);
                    for (int ly = 0; ly < labelHeight; ly++)
                    {
                        for (int lx = 0; lx < tileW; lx++)
                        {
                            sheet.SetPixel(x + lx, y + ly, bannerColor);
                        }
                    }

                    // Draw label text using simple pixel font
                    if (labels != null && idx < labels.Count && !string.IsNullOrEmpty(labels[idx]))
                    {
                        DrawText(sheet, labels[idx], x + 3, y + 2, labelHeight - 4, Color.white);
                    }
                }

                sheet.Apply();

                byte[] png = sheet.EncodeToPNG();
                string b64 = System.Convert.ToBase64String(png);
                return (b64, sheetW, sheetH);
            }
            finally
            {
                foreach (var tile in tiles) DestroyTexture(tile);
                DestroyTexture(sheet);
            }
        }

        private static void DrawText(Texture2D tex, string text, int startX, int startY, int charHeight, Color color)
        {
            // Simple 5x7 bitmap font for basic ASCII characters
            int charWidth = Mathf.Max(4, charHeight * 5 / 7);
            int spacing = Mathf.Max(1, charWidth / 5);
            int x = startX;

            foreach (char c in text)
            {
                if (x + charWidth > tex.width) break;
                ulong glyph = GetGlyph(c);
                if (glyph != 0)
                {
                    for (int row = 0; row < 7; row++)
                    {
                        for (int col = 0; col < 5; col++)
                        {
                            bool on = ((glyph >> ((6 - row) * 5 + (4 - col))) & 1) == 1;
                            if (!on) continue;
                            // Scale the 5x7 glyph to charWidth x charHeight
                            int px0 = x + col * charWidth / 5;
                            int px1 = x + (col + 1) * charWidth / 5;
                            int py0 = startY + (6 - row) * charHeight / 7;
                            int py1 = startY + (7 - row) * charHeight / 7;
                            for (int py = py0; py < py1 && py < tex.height; py++)
                                for (int px = px0; px < px1 && px < tex.width; px++)
                                    tex.SetPixel(px, py, color);
                        }
                    }
                }
                x += charWidth + spacing;
            }
        }

        private static ulong GetGlyph(char c)
        {
            // 5x7 pixel font stored as 35-bit values (row0=bits34-30 ... row6=bits4-0)
            // Each row is 5 wide, MSB=left. Row 0 is top.
            switch (char.ToUpperInvariant(c))
            {
                case 'A': return 0b01110_10001_10001_11111_10001_10001_10001UL;
                case 'B': return 0b11110_10001_10001_11110_10001_10001_11110UL;
                case 'C': return 0b01110_10001_10000_10000_10000_10001_01110UL;
                case 'D': return 0b11100_10010_10001_10001_10001_10010_11100UL;
                case 'E': return 0b11111_10000_10000_11110_10000_10000_11111UL;
                case 'F': return 0b11111_10000_10000_11110_10000_10000_10000UL;
                case 'G': return 0b01110_10001_10000_10111_10001_10001_01110UL;
                case 'H': return 0b10001_10001_10001_11111_10001_10001_10001UL;
                case 'I': return 0b01110_00100_00100_00100_00100_00100_01110UL;
                case 'K': return 0b10001_10010_10100_11000_10100_10010_10001UL;
                case 'L': return 0b10000_10000_10000_10000_10000_10000_11111UL;
                case 'M': return 0b10001_11011_10101_10101_10001_10001_10001UL;
                case 'N': return 0b10001_11001_10101_10011_10001_10001_10001UL;
                case 'O': return 0b01110_10001_10001_10001_10001_10001_01110UL;
                case 'R': return 0b11110_10001_10001_11110_10100_10010_10001UL;
                case 'S': return 0b01110_10001_10000_01110_00001_10001_01110UL;
                case 'T': return 0b11111_00100_00100_00100_00100_00100_00100UL;
                case 'U': return 0b10001_10001_10001_10001_10001_10001_01110UL;
                case 'V': return 0b10001_10001_10001_10001_01010_01010_00100UL;
                case 'W': return 0b10001_10001_10001_10101_10101_11011_10001UL;
                case 'Y': return 0b10001_10001_01010_00100_00100_00100_00100UL;
                case '0': return 0b01110_10011_10101_10101_10101_11001_01110UL;
                case '1': return 0b00100_01100_00100_00100_00100_00100_01110UL;
                case '2': return 0b01110_10001_00001_00010_00100_01000_11111UL;
                case '3': return 0b01110_10001_00001_00110_00001_10001_01110UL;
                case '4': return 0b00010_00110_01010_10010_11111_00010_00010UL;
                case '5': return 0b11111_10000_11110_00001_00001_10001_01110UL;
                case '6': return 0b01110_10001_10000_11110_10001_10001_01110UL;
                case '7': return 0b11111_00001_00010_00100_01000_01000_01000UL;
                case '8': return 0b01110_10001_10001_01110_10001_10001_01110UL;
                case '9': return 0b01110_10001_10001_01111_00001_10001_01110UL;
                case 'J': return 0b00111_00010_00010_00010_00010_10010_01100UL;
                case 'P': return 0b11110_10001_10001_11110_10000_10000_10000UL;
                case 'Q': return 0b01110_10001_10001_10001_10101_10010_01101UL;
                case 'X': return 0b10001_01010_00100_00100_00100_01010_10001UL;
                case 'Z': return 0b11111_00001_00010_00100_01000_10000_11111UL;
                case '-': return 0b00000_00000_00000_11111_00000_00000_00000UL;
                case '_': return 0b00000_00000_00000_00000_00000_00000_11111UL;
                case ' ': return 0UL;
                case '+': return 0b00000_00100_00100_11111_00100_00100_00000UL;
                default:  return 0UL;
            }
        }

        /// <summary>
        /// Downscales a Texture2D so that its longest edge is at most <paramref name="maxEdge"/> pixels.
        /// Uses bilinear filtering via a temporary RenderTexture blit.
        /// Caller must destroy the returned Texture2D.
        /// </summary>
        public static Texture2D DownscaleTexture(Texture2D source, int maxEdge)
        {
            if (source == null)
                throw new System.ArgumentNullException(nameof(source));
            if (maxEdge <= 0)
                throw new System.ArgumentOutOfRangeException(nameof(maxEdge), maxEdge, "maxEdge must be > 0.");

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
