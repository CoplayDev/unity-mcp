using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
using MCPForUnity.Runtime.Helpers;
using UnityEditor;
using UnityEngine;

namespace MCPForUnity.Editor.Helpers
{
    /// <summary>
    /// Captures the pixels currently displayed in an editor window viewport.
    /// Uses the editor view's own pixel grab path instead of re-rendering through a Camera.
    /// </summary>
    internal static class EditorWindowScreenshotUtility
    {
        private const string ScreenshotsFolderName = "Screenshots";
        // Keep capture synchronous so callers can immediately return the screenshot payload.
        // The short sleep gives Unity a chance to flush repaint work before GrabPixels reads the viewport.
        private const int RepaintSettlingDelayMs = 75;
        private static readonly HashSet<string> WindowsReservedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
        };

        /// <summary>
        /// Captures the active Scene View viewport to a PNG asset.
        /// </summary>
        /// <param name="sceneView">Scene View window to capture.</param>
        /// <param name="fileName">Optional file name, defaulting to a timestamped PNG.</param>
        /// <param name="superSize">
        /// Preserved in the result for API consistency, but Scene View capture always uses the current viewport resolution.
        /// </param>
        /// <param name="ensureUniqueFileName">If true, appends a suffix instead of overwriting an existing file.</param>
        /// <param name="includeImage">If true, includes a base64 PNG in the returned result.</param>
        /// <param name="maxResolution">Maximum edge length for the inline image payload.</param>
        /// <param name="viewportWidth">Captured viewport width in pixels.</param>
        /// <param name="viewportHeight">Captured viewport height in pixels.</param>
        public static ScreenshotCaptureResult CaptureSceneViewViewportToAssets(
            SceneView sceneView,
            string fileName,
            int superSize,
            bool ensureUniqueFileName,
            bool includeImage,
            int maxResolution,
            out int viewportWidth,
            out int viewportHeight)
        {
            return CaptureWindowViewportToAssets(sceneView, fileName, superSize, ensureUniqueFileName, includeImage, maxResolution, out viewportWidth, out viewportHeight);
        }

        public static ScreenshotCaptureResult CaptureGameViewToAssets(
            string fileName,
            bool ensureUniqueFileName,
            bool includeImage,
            int maxResolution,
            out int viewportWidth,
            out int viewportHeight)
        {
            System.Reflection.Assembly assembly = typeof(UnityEditor.EditorWindow).Assembly;
            System.Type type = assembly.GetType("UnityEditor.GameView");
            EditorWindow gameView = EditorWindow.GetWindow(type);

            if (gameView == null)
            {
                viewportWidth = 0;
                viewportHeight = 0;
                throw new InvalidOperationException("Could not find Game View window.");
            }

            return CaptureWindowViewportToAssets(gameView, fileName, 1, ensureUniqueFileName, includeImage, maxResolution, out viewportWidth, out viewportHeight);
        }

        private static ScreenshotCaptureResult CaptureWindowViewportToAssets(
            EditorWindow window,
            string fileName,
            int superSize,
            bool ensureUniqueFileName,
            bool includeImage,
            int maxResolution,
            out int viewportWidth,
            out int viewportHeight)
        {
            if (window == null)
                throw new ArgumentNullException(nameof(window));

            int effectiveSuperSize = (window is SceneView) ? NormalizeSceneViewSuperSize(superSize) : 1;

            FocusAndRepaint(window);

            Rect viewportRectPixels = GetWindowViewportPixelRect(window);
            viewportWidth = Mathf.RoundToInt(viewportRectPixels.width);
            viewportHeight = Mathf.RoundToInt(viewportRectPixels.height);

            if (viewportWidth <= 0 || viewportHeight <= 0)
                throw new InvalidOperationException($"Captured {window.GetType().Name} viewport is empty.");

            Texture2D captured = null;
            Texture2D downscaled = null;
            try
            {
                captured = CaptureViewRect(window, viewportRectPixels);

                var result = PrepareCaptureResult(fileName, effectiveSuperSize, ensureUniqueFileName);
                byte[] png = captured.EncodeToPNG();
                File.WriteAllBytes(result.FullPath, png);

                if (includeImage)
                {
                    int targetMax = maxResolution > 0 ? maxResolution : 640;
                    string imageBase64;
                    int imageWidth;
                    int imageHeight;

                    if (captured.width > targetMax || captured.height > targetMax)
                    {
                        downscaled = ScreenshotUtility.DownscaleTexture(captured, targetMax);
                        imageBase64 = Convert.ToBase64String(downscaled.EncodeToPNG());
                        imageWidth = downscaled.width;
                        imageHeight = downscaled.height;
                    }
                    else
                    {
                        imageBase64 = Convert.ToBase64String(png);
                        imageWidth = captured.width;
                        imageHeight = captured.height;
                    }

                    return new ScreenshotCaptureResult(
                        result.FullPath,
                        result.AssetsRelativePath,
                        result.SuperSize,
                        false,
                        imageBase64,
                        imageWidth,
                        imageHeight);
                }

                return result;
            }
            finally
            {
                DestroyTexture(captured);
                DestroyTexture(downscaled);
            }
        }

        private static void FocusAndRepaint(EditorWindow window)
        {
            try
            {
                window.Focus();
            }
            catch (Exception ex)
            {
                McpLog.Debug($"[EditorWindowScreenshotUtility] Window focus failed: {ex.Message}");
            }

            try
            {
                window.Repaint();
                InvokeMethodIfExists(window, "RepaintImmediately");
                if (window is SceneView) SceneView.RepaintAll();
                UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
                EditorApplication.QueuePlayerLoopUpdate();
                Thread.Sleep(RepaintSettlingDelayMs);
            }
            catch (Exception ex)
            {
                McpLog.Debug($"[EditorWindowScreenshotUtility] Window repaint failed: {ex.Message}");
            }
        }

        private static Rect GetWindowViewportPixelRect(EditorWindow window)
        {
            float pixelsPerPoint = EditorGUIUtility.pixelsPerPoint;
            Rect viewportLocalPoints = GetViewportLocalRectPoints(window, pixelsPerPoint);
            if (viewportLocalPoints.width <= 0f || viewportLocalPoints.height <= 0f)
                throw new InvalidOperationException($"Failed to resolve {window.GetType().Name} viewport rect.");

            return new Rect(
                Mathf.Round(viewportLocalPoints.x * pixelsPerPoint),
                Mathf.Round(viewportLocalPoints.y * pixelsPerPoint),
                Mathf.Round(viewportLocalPoints.width * pixelsPerPoint),
                Mathf.Round(viewportLocalPoints.height * pixelsPerPoint));
        }

        private static Rect GetViewportLocalRectPoints(EditorWindow window, float pixelsPerPoint)
        {
            Rect? cameraViewport = GetRectProperty(window, "cameraViewport");
            if (cameraViewport.HasValue && cameraViewport.Value.width > 0f && cameraViewport.Value.height > 0f)
            {
                return cameraViewport.Value;
            }

            if (window is SceneView sceneView)
            {
                Camera camera = sceneView.camera;
                if (camera != null)
                {
                    float viewportWidth = camera.pixelWidth / Mathf.Max(0.0001f, pixelsPerPoint);
                    float viewportHeight = camera.pixelHeight / Mathf.Max(0.0001f, pixelsPerPoint);
                    Rect windowRect = sceneView.position;

                    return new Rect(
                        0f,
                        Mathf.Max(0f, windowRect.height - viewportHeight),
                        Mathf.Min(windowRect.width, viewportWidth),
                        Mathf.Min(windowRect.height, viewportHeight));
                }
            }

            // Generic fallback for other windows
            return new Rect(0, 0, window.position.width, window.position.height);
        }

        private static Texture2D CaptureViewRect(EditorWindow window, Rect viewportRectPixels)
        {
            object hostView = GetHostView(window);
            if (hostView == null)
                throw new InvalidOperationException("Failed to resolve Scene view host view.");

            // GrabPixels is an internal extern on GUIView (parent of HostView), present since at least Unity 2021.1.
            // See: UnityCsReference/Editor/Mono/GUIView.bindings.cs — `internal extern void GrabPixels(RenderTexture, Rect)`
            // If Unity removes this, the MissingMethodException below keeps the failure explicit.
            MethodInfo grabPixels = hostView.GetType().GetMethod(
                "GrabPixels",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[] { typeof(RenderTexture), typeof(Rect) },
                null);

            if (grabPixels == null)
                throw new MissingMethodException($"{hostView.GetType().FullName}.GrabPixels(RenderTexture, Rect)");

            int width = Mathf.RoundToInt(viewportRectPixels.width);
            int height = Mathf.RoundToInt(viewportRectPixels.height);

            RenderTexture rt = null;
            RenderTexture previousActive = RenderTexture.active;
            try
            {
                rt = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32)
                {
                    antiAliasing = 1,
                    filterMode = FilterMode.Bilinear,
                    hideFlags = HideFlags.HideAndDontSave,
                };
                rt.Create();

                grabPixels.Invoke(hostView, new object[] { rt, viewportRectPixels });

                RenderTexture.active = rt;
                var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
                texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                texture.Apply();
                FlipTextureVertically(texture);
                return texture;
            }
            catch (TargetInvocationException ex)
            {
                ExceptionDispatchInfo.Capture(ex.InnerException ?? ex).Throw();
                throw;
            }
            finally
            {
                RenderTexture.active = previousActive;
                if (rt != null)
                {
                    rt.Release();
                    UnityEngine.Object.DestroyImmediate(rt);
                }
            }
        }

        private static object GetHostView(EditorWindow window)
        {
            if (window == null)
                return null;

            Type windowType = typeof(EditorWindow);
            FieldInfo parentField = windowType.GetField("m_Parent", BindingFlags.Instance | BindingFlags.NonPublic);
            if (parentField != null)
            {
                object parent = parentField.GetValue(window);
                if (parent != null)
                    return parent;
            }

            PropertyInfo hostViewProperty = windowType.GetProperty("hostView", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return hostViewProperty?.GetValue(window, null);
        }

        private static Rect? GetRectProperty(object instance, string propertyName)
        {
            if (instance == null)
                return null;

            Type type = instance.GetType();
            PropertyInfo property = type.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property == null || property.PropertyType != typeof(Rect))
                return null;

            try
            {
                return (Rect)property.GetValue(instance, null);
            }
            catch (Exception ex)
            {
                McpLog.Debug($"[EditorWindowScreenshotUtility] Failed to read rect property '{propertyName}': {ex.Message}");
                return null;
            }
        }

        private static void InvokeMethodIfExists(object instance, string methodName)
        {
            if (instance == null)
                return;

            MethodInfo method = instance.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (method == null || method.GetParameters().Length != 0)
                return;

            try
            {
                method.Invoke(instance, null);
            }
            catch (Exception ex)
            {
                McpLog.Debug($"[EditorWindowScreenshotUtility] Best-effort invoke of '{methodName}' failed: {ex.Message}");
            }
        }

        private static void FlipTextureVertically(Texture2D texture)
        {
            if (texture == null)
                return;

            int width = texture.width;
            int height = texture.height;
            Color32[] pixels = texture.GetPixels32();
            var temp = new Color32[width];

            for (int y = 0; y < height / 2; y++)
            {
                int topRow = y * width;
                int bottomRow = (height - 1 - y) * width;
                Array.Copy(pixels, topRow, temp, 0, width);
                Array.Copy(pixels, bottomRow, pixels, topRow, width);
                Array.Copy(temp, 0, pixels, bottomRow, width);
            }

            texture.SetPixels32(pixels);
            texture.Apply();
        }

        private static ScreenshotCaptureResult PrepareCaptureResult(string fileName, int superSize, bool ensureUniqueFileName)
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
            string assetsRelativePath = "Assets/" + normalizedFullPath.Substring(Application.dataPath.Length).TrimStart('/');
            return new ScreenshotCaptureResult(normalizedFullPath, assetsRelativePath, size, false);
        }

        private static string BuildFileName(string fileName)
        {
            string baseName = string.IsNullOrWhiteSpace(fileName)
                ? $"screenshot-{DateTime.Now:yyyyMMdd-HHmmss}.png"
                : SanitizeFileName(fileName);

            if (!baseName.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                baseName += ".png";

            return baseName;
        }

        private static int NormalizeSceneViewSuperSize(int superSize)
        {
            if (superSize > 1)
            {
                McpLog.Warn("[EditorWindowScreenshotUtility] Scene View capture ignores superSize and uses the displayed viewport resolution.");
                return 1;
            }

            return Mathf.Max(1, superSize);
        }

        private static string SanitizeFileName(string fileName)
        {
            string trimmed = (fileName ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(trimmed))
                return $"screenshot-{DateTime.Now:yyyyMMdd-HHmmss}.png";

            string candidate = trimmed;
            string normalizedSeparators = candidate.Replace('\\', '/');
            if (Path.IsPathRooted(candidate) || normalizedSeparators.Contains("/") || normalizedSeparators.Contains(".."))
            {
                string[] pathParts = normalizedSeparators.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                candidate = pathParts.Length > 0 ? pathParts[pathParts.Length - 1] : string.Empty;
            }

            if (string.IsNullOrWhiteSpace(candidate) || candidate == "." || candidate == "..")
                candidate = $"screenshot-{DateTime.Now:yyyyMMdd-HHmmss}.png";

            char[] invalidChars = Path.GetInvalidFileNameChars();
            foreach (char invalidChar in invalidChars)
            {
                candidate = candidate.Replace(invalidChar, '_');
            }

            string extension = Path.GetExtension(candidate);
            string stem = Path.GetFileNameWithoutExtension(candidate);
            extension = extension.TrimEnd(' ', '.');
            stem = stem.TrimEnd(' ', '.');
            if (WindowsReservedNames.Contains(stem))
            {
                candidate = $"_{stem}{extension}";
            }

            return candidate;
        }

        private static string EnsureUnique(string fullPath)
        {
            if (!File.Exists(fullPath))
                return fullPath;

            string directory = Path.GetDirectoryName(fullPath) ?? string.Empty;
            string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(fullPath);
            string extension = Path.GetExtension(fullPath);

            for (int i = 1; i < 10000; i++)
            {
                string candidate = Path.Combine(directory, $"{fileNameWithoutExtension}-{i}{extension}");
                if (!File.Exists(candidate))
                    return candidate;
            }

            throw new IOException($"Could not generate a unique screenshot filename for '{fullPath}'.");
        }

        private static void DestroyTexture(Texture2D texture)
        {
            if (texture == null)
                return;

            UnityEngine.Object.DestroyImmediate(texture);
        }
    }
}
