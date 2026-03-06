using System;
using System.IO;
using System.Reflection;
using System.Threading;
using MCPForUnity.Runtime.Helpers;
using UnityEditor;
using UnityEngine;

namespace MCPForUnity.Editor.Helpers
{
    /// <summary>
    /// Captures the pixels currently displayed in an editor window viewport.
    /// Uses the editor view's own pixel grab path instead of re-rendering through a Camera,
    /// which is required for SceneView overlays, prefab stages, and UI editing views.
    /// </summary>
    internal static class EditorWindowScreenshotUtility
    {
        private const string ScreenshotsFolderName = "Screenshots";
        private const int RepaintSettlingDelayMs = 75;

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
            if (sceneView == null)
                throw new ArgumentNullException(nameof(sceneView));

            FocusAndRepaint(sceneView);

            Rect viewportRectPixels = GetSceneViewViewportPixelRect(sceneView);
            viewportWidth = Mathf.RoundToInt(viewportRectPixels.width);
            viewportHeight = Mathf.RoundToInt(viewportRectPixels.height);

            if (viewportWidth <= 0 || viewportHeight <= 0)
                throw new InvalidOperationException("Captured Scene view viewport is empty.");

            Texture2D captured = null;
            Texture2D downscaled = null;
            try
            {
                captured = CaptureViewRect(sceneView, viewportRectPixels);

                var result = PrepareCaptureResult(fileName, superSize, ensureUniqueFileName);
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
                        isAsync: false,
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

        private static void FocusAndRepaint(SceneView sceneView)
        {
            try
            {
                sceneView.Focus();
            }
            catch (Exception ex)
            {
                McpLog.Debug($"[EditorWindowScreenshotUtility] SceneView focus failed: {ex.Message}");
            }

            try
            {
                sceneView.Repaint();
                InvokeMethodIfExists(sceneView, "RepaintImmediately");
                SceneView.RepaintAll();
                UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
                EditorApplication.QueuePlayerLoopUpdate();
                Thread.Sleep(RepaintSettlingDelayMs);
            }
            catch (Exception ex)
            {
                McpLog.Debug($"[EditorWindowScreenshotUtility] SceneView repaint failed: {ex.Message}");
            }
        }

        private static Rect GetSceneViewViewportPixelRect(SceneView sceneView)
        {
            float pixelsPerPoint = EditorGUIUtility.pixelsPerPoint;
            Rect viewportLocalPoints = GetViewportLocalRectPoints(sceneView, pixelsPerPoint);
            if (viewportLocalPoints.width <= 0f || viewportLocalPoints.height <= 0f)
                throw new InvalidOperationException("Failed to resolve Scene view viewport rect.");

            return new Rect(
                Mathf.Round(viewportLocalPoints.x * pixelsPerPoint),
                Mathf.Round(viewportLocalPoints.y * pixelsPerPoint),
                Mathf.Round(viewportLocalPoints.width * pixelsPerPoint),
                Mathf.Round(viewportLocalPoints.height * pixelsPerPoint));
        }

        private static Rect GetViewportLocalRectPoints(SceneView sceneView, float pixelsPerPoint)
        {
            Rect? cameraViewport = GetRectProperty(sceneView, "cameraViewport");
            if (cameraViewport.HasValue && cameraViewport.Value.width > 0f && cameraViewport.Value.height > 0f)
            {
                return cameraViewport.Value;
            }

            Camera camera = sceneView.camera;
            if (camera == null)
                throw new InvalidOperationException("Active Scene View has no camera to derive viewport size from.");

            float viewportWidth = camera.pixelWidth / Mathf.Max(0.0001f, pixelsPerPoint);
            float viewportHeight = camera.pixelHeight / Mathf.Max(0.0001f, pixelsPerPoint);
            Rect windowRect = sceneView.position;

            return new Rect(
                0f,
                Mathf.Max(0f, windowRect.height - viewportHeight),
                Mathf.Min(windowRect.width, viewportWidth),
                Mathf.Min(windowRect.height, viewportHeight));
        }

        private static Texture2D CaptureViewRect(SceneView sceneView, Rect viewportRectPixels)
        {
            object hostView = GetHostView(sceneView);
            if (hostView == null)
                throw new InvalidOperationException("Failed to resolve Scene view host view.");

            MethodInfo grabPixels = hostView.GetType().GetMethod(
                "GrabPixels",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[] { typeof(RenderTexture), typeof(Rect) },
                null);

            if (grabPixels == null)
                throw new MissingMethodException($"{hostView.GetType().FullName}.GrabPixels(RenderTexture, Rect)");

            int viewportWidth = Mathf.RoundToInt(viewportRectPixels.width);
            int viewportHeight = Mathf.RoundToInt(viewportRectPixels.height);

            RenderTexture rt = null;
            RenderTexture previousActive = RenderTexture.active;
            try
            {
                rt = new RenderTexture(viewportWidth, viewportHeight, 0, RenderTextureFormat.ARGB32)
                {
                    antiAliasing = 1,
                    filterMode = FilterMode.Bilinear,
                    hideFlags = HideFlags.HideAndDontSave,
                };
                rt.Create();

                grabPixels.Invoke(hostView, new object[] { rt, viewportRectPixels });

                RenderTexture.active = rt;
                var texture = new Texture2D(viewportWidth, viewportHeight, TextureFormat.RGBA32, false);
                texture.ReadPixels(new Rect(0, 0, viewportWidth, viewportHeight), 0, 0);
                texture.Apply();
                FlipTextureVertically(texture);
                return texture;
            }
            catch (TargetInvocationException ex)
            {
                throw ex.InnerException ?? ex;
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
            catch
            {
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
            catch
            {
                // Best-effort only.
            }
        }

        private static void FlipTextureVertically(Texture2D texture)
        {
            if (texture == null)
                return;

            int width = texture.width;
            int height = texture.height;
            Color32[] source = texture.GetPixels32();
            Color32[] flipped = new Color32[source.Length];

            for (int y = 0; y < height; y++)
            {
                int srcRow = y * width;
                int dstRow = (height - 1 - y) * width;
                Array.Copy(source, srcRow, flipped, dstRow, width);
            }

            texture.SetPixels32(flipped);
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
            return new ScreenshotCaptureResult(normalizedFullPath, assetsRelativePath, size, isAsync: false);
        }

        private static string BuildFileName(string fileName)
        {
            string baseName = string.IsNullOrWhiteSpace(fileName)
                ? $"screenshot-{DateTime.Now:yyyyMMdd-HHmmss}.png"
                : fileName.Trim();

            if (!baseName.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                baseName += ".png";

            return baseName;
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
