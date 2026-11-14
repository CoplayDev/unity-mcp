using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Runtime.Helpers;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MCPForUnity.Editor.Tools
{
    [McpForUnityTool("manage_editor_windows", AutoRegister = false)]
    public static class ManageEditorWindows
    {
        private static Action cancelPendingCapture;

        internal sealed class Parameters
        {
            public string action = "list";
            public int? window_id;
            public string window_title;
            public string window_type;
            public bool focus = true;
            public bool restore_focus = true;
            public bool include_image = true;
            public bool save_file = false;
            public int max_resolution = 1600;
        }

        /// <summary>Lists eligible windows or schedules one validated capture; errors never select a replacement target.</summary>
        public static Task<object> HandleCommand(JObject args)
        {
            try
            {
                var p = ParseParameters(args);
                var windows = UnityEngine.Resources.FindObjectsOfTypeAll<EditorWindow>()
                    .Where(w => w != null && (w.docked || w.hasFocus))
                    .OrderBy(w => w.titleContent.text, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(w => w.GetInstanceID()).ToArray();
                if (string.Equals(p.action, "list", StringComparison.OrdinalIgnoreCase))
                    return Task.FromResult<object>(new SuccessResponse("Open Editor windows.", new
                    {
                        windows = windows.Select(Describe).ToArray(),
                        focused_window_id = EditorWindow.focusedWindow != null
                            ? (int?)EditorWindow.focusedWindow.GetInstanceID() : null,
                        unity_version = Application.unityVersion
                    }));
                if (!string.Equals(p.action, "screenshot", StringComparison.OrdinalIgnoreCase))
                    return Failure("action must be list or screenshot.");
                if (Application.isBatchMode)
                    return Failure("Window capture requires a visible Editor. Batch mode is not supported.");
                if (EditorApplication.isCompiling || EditorApplication.isUpdating)
                    return Failure("The Editor is busy. Wait for compilation and asset import to finish.");
                if (cancelPendingCapture != null)
                    return Failure("Another Editor window capture is in progress.");
                if (p.max_resolution < 64 || p.max_resolution > 4096)
                    return Failure("max_resolution must be from 64 to 4096.");
                if (!p.include_image && !p.save_file)
                    return Failure("Set include_image or save_file to true.");
                var target = Resolve(windows, p, out string error);
                if (target == null)
                    return Task.FromResult<object>(new ErrorResponse(error));
                if (!p.focus && !target.hasFocus)
                    return Failure("With focus=false, the target must be a selected tab.");
                return CaptureAfterRepaint(target, p);
            }
            catch (Exception ex)
            {
                return Failure("Window capture failed: " + ex.Message);
            }
        }

        /// <summary>Reads direct and batch-route aliases, retaining defaults while rejecting invalid explicit selectors.</summary>
        internal static Parameters ParseParameters(JObject args)
        {
            var values = new ToolParams(args ?? new JObject());
            var p = new Parameters
            {
                action = values.Get("action", "list"),
                window_id = values.GetInt("window_id"),
                window_title = values.Get("window_title"),
                window_type = values.Get("window_type"),
                focus = values.GetBool("focus", true),
                restore_focus = values.GetBool("restore_focus", true),
                include_image = values.GetBool("include_image", true),
                save_file = values.GetBool("save_file", false),
                max_resolution = values.GetInt("max_resolution", 1600).Value
            };
            // An explicit invalid selector must never become a focused-window request.
            if (values.Has("window_id") && !p.window_id.HasValue)
                throw new ArgumentException("window_id must be an integer.");
            foreach (string key in new[] { "window_title", "window_type" })
                if (values.Has(key) && (values.GetRaw(key).Type != JTokenType.String
                    || string.IsNullOrWhiteSpace(values.Get(key))))
                    throw new ArgumentException(key + " must be a non-empty string.");
            if (values.Has("max_resolution") && !values.GetInt("max_resolution").HasValue)
                throw new ArgumentException("max_resolution must be an integer from 64 to 4096.");
            return p;
        }

        /// <summary>Requires one unambiguous selector; only an omitted selector may use the currently focused window.</summary>
        internal static EditorWindow Resolve(EditorWindow[] windows, Parameters p, out string error)
        {
            error = null;
            int selectorCount = (p.window_id.HasValue ? 1 : 0)
                + (!string.IsNullOrWhiteSpace(p.window_title) ? 1 : 0)
                + (!string.IsNullOrWhiteSpace(p.window_type) ? 1 : 0);
            if (selectorCount > 1)
            {
                error = "Use only one of window_id, window_title, or window_type.";
                return null;
            }
            var matches = windows.Where(w => w != null && (p.window_id.HasValue
                ? w.GetInstanceID() == p.window_id.Value
                : !string.IsNullOrWhiteSpace(p.window_title)
                    ? string.Equals(w.titleContent.text, p.window_title.Trim(), StringComparison.OrdinalIgnoreCase)
                : !string.IsNullOrWhiteSpace(p.window_type)
                    ? string.Equals(w.GetType().Name, p.window_type.Trim(), StringComparison.Ordinal)
                        || string.Equals(w.GetType().FullName, p.window_type.Trim(), StringComparison.Ordinal)
                : w == EditorWindow.focusedWindow)).ToArray();
            if (matches.Length == 1)
                return matches[0];
            error = matches.Length == 0 ? "No matching open window. Call action=list to get current window IDs."
                : "More than one window matches. Call action=list, then use window_id.";
            return null;
        }

        /// <summary>Completes the current request through its normal cleanup path during reload, shutdown, or cancellation.</summary>
        internal static void CancelPendingCapture()
        {
            cancelPendingCapture?.Invoke();
        }

        /// <summary>Waits for the selected tab to repaint and restores its former host selection without overriding later user focus.</summary>
        private static Task<object> CaptureAfterRepaint(EditorWindow target, Parameters p)
        {
            var previous = EditorWindow.focusedWindow;
            var selectedTabs = UnityEngine.Resources.FindObjectsOfTypeAll<EditorWindow>()
                .Where(w => w != null && w != target && w.docked && w.hasFocus).ToArray();
            var targetHost = EditorWindowScreenshotUtility.GetHostView(target);
            EditorWindow previousTab = target.docked && targetHost != null
                ? selectedTabs.FirstOrDefault(w => ReferenceEquals(
                    EditorWindowScreenshotUtility.GetHostView(w), targetHost)) : null;
            var completion = new TaskCompletionSource<object>();
            double start = EditorApplication.timeSinceStartup;
            int ticks = 0;
            bool finished = false;

            /// <summary>Completes once, detaches lifecycle callbacks, releases the capture gate, and conditionally restores focus.</summary>
            void Finish(object result)
            {
                if (finished) return;
                finished = true;
                EditorApplication.update -= Tick;
                AssemblyReloadEvents.beforeAssemblyReload -= CancelPendingCapture;
                EditorApplication.quitting -= CancelPendingCapture;
                cancelPendingCapture = null;
                try
                {
                    if (p.restore_focus && p.focus && (target == null || target.hasFocus))
                    {
                        bool canRestoreKeyboardFocus = EditorWindow.focusedWindow == target;
                        if (previousTab != null) previousTab.ShowTab();
                        if (canRestoreKeyboardFocus && previous != null && previous != target) previous.Focus();
                    }
                }
                catch (Exception ex)
                {
                    if (result is SuccessResponse success && success.Data is JObject data)
                        data["focus_restore_error"] = ex.Message;
                }
                finally
                {
                    completion.TrySetResult(result);
                }
            }

            /// <summary>Bounds tab-selection waiting and handles a closed target before synchronous pixel capture.</summary>
            void Tick()
            {
                try
                {
                    if (target == null)
                    {
                        Finish(new ErrorResponse("The target window closed before capture."));
                        return;
                    }
                    double elapsed = EditorApplication.timeSinceStartup - start;
                    if (++ticks < 2 || elapsed < 0.2) return;
                    if (!target.hasFocus)
                    {
                        if (elapsed < 3) return;
                        Finish(new ErrorResponse("Unity did not select the target tab. Retry after the Editor is ready."));
                        return;
                    }
                    Finish(Capture(target, p));
                }
                catch (Exception ex)
                {
                    Finish(new ErrorResponse("Window capture failed: " + ex.Message));
                }
            }

            cancelPendingCapture = () => Finish(new ErrorResponse("Editor reload or shutdown interrupted capture. Retry after the Editor is ready."));
            EditorApplication.update += Tick;
            AssemblyReloadEvents.beforeAssemblyReload += CancelPendingCapture;
            EditorApplication.quitting += CancelPendingCapture;
            try
            {
                if (p.focus)
                {
                    target.ShowTab();
                }
                target.Repaint();
                EditorApplication.QueuePlayerLoopUpdate();
            }
            catch (Exception ex)
            {
                Finish(new ErrorResponse("Window focus failed: " + ex.Message));
            }
            return completion.Task;
        }

        /// <summary>Checks the physical pixel area and owns the full-size texture until response construction finishes.</summary>
        private static object Capture(EditorWindow target, Parameters p)
        {
            float scale = EditorWindowScreenshotUtility.GetWindowPixelsPerPoint(target);
            int width = Mathf.RoundToInt(target.position.width * scale);
            int height = Mathf.RoundToInt(target.position.height * scale);
            if (width <= 0 || height <= 0 || (long)width * height > 16777216)
                return new ErrorResponse("The target has an empty or excessive capture area.");
            Texture2D full = null;
            try
            {
                full = EditorWindowScreenshotUtility.CaptureWindowPixels(target, width, height);
                return BuildCaptureResponse(target, p, full, scale);
            }
            finally
            {
                if (full != null) Object.DestroyImmediate(full);
            }
        }

        /// <summary>
        /// Prepares metadata and inline pixels before persisting an optional full-size PNG.
        /// The caller owns full; this method releases only its downscaled texture.
        /// </summary>
        internal static object BuildCaptureResponse(EditorWindow target, Parameters p, Texture2D full,
            float scale, Func<Texture2D, int, Texture2D> downscale = null)
        {
            Texture2D image = null;
            try
            {
                string path = null;
                byte[] fullPng = null;
                if (p.save_file)
                {
                    string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "../Library/McpEditorScreenshots"));
                    path = Path.Combine(folder, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff")
                        + "-" + Guid.NewGuid().ToString("N") + ".png");
                    fullPng = full.EncodeToPNG();
                }
                var data = new JObject
                {
                    ["window"] = JObject.FromObject(Describe(target)),
                    ["capture_source"] = "editor_window_buffer",
                    ["captured_at_utc"] = DateTime.UtcNow.ToString("O"),
                    ["width"] = full.width, ["height"] = full.height,
                    ["pixels_per_point"] = scale, ["path"] = path,
                    ["mimeType"] = "image/png"
                };
                if (p.include_image)
                {
                    image = Mathf.Max(full.width, full.height) > p.max_resolution
                        ? (downscale ?? ScreenshotUtility.DownscaleTexture)(full, p.max_resolution) : full;
                    data["imageBase64"] = Convert.ToBase64String(image == full
                        ? fullPng ?? full.EncodeToPNG() : image.EncodeToPNG());
                    data["imageWidth"] = image.width;
                    data["imageHeight"] = image.height;
                }
                var response = new SuccessResponse("Editor window captured.", data);
                return path == null ? response : SaveCaptureFile(path, fullPng, response);
            }
            catch (Exception ex)
            {
                return new ErrorResponse("Window capture failed: " + ex.Message);
            }
            finally
            {
                if (image != null && image != full) Object.DestroyImmediate(image);
            }
        }

        /// <summary>
        /// Saves a prepared response's unique PNG, removing incomplete output on failure.
        /// If removal fails, the error exposes the retained path and cleanup outcome.
        /// </summary>
        internal static object SaveCaptureFile(string path, byte[] png, SuccessResponse response)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllBytes(path, png);
                return response;
            }
            catch (Exception ex)
            {
                try
                {
                    File.Delete(path);
                }
                catch (Exception cleanupError)
                {
                    return new ErrorResponse("Screenshot file write failed and its output could not be removed: " + ex.Message,
                        new { path, cleanup_failed = true, cleanup_error = cleanupError.Message });
                }
                return new ErrorResponse("Screenshot file write failed; incomplete output was removed: " + ex.Message);
            }
        }

        /// <summary>Reports current window identity, tab selection, keyboard focus, and content position in Editor points.</summary>
        private static object Describe(EditorWindow window)
        {
            Rect rect = window.position;
            return new
            {
                window_id = window.GetInstanceID(), title = window.titleContent.text,
                type = window.GetType().FullName, focused = EditorWindow.focusedWindow == window,
                selected_tab = window.hasFocus, docked = window.docked,
                position = new { x = rect.x, y = rect.y, width = rect.width, height = rect.height }
            };
        }

        /// <summary>Returns a completed command task containing a structured error instead of a transport exception.</summary>
        private static Task<object> Failure(string message) => Task.FromResult<object>(new ErrorResponse(message));
    }
}
