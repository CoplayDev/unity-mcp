using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Runtime.Helpers;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace MCPForUnity.Editor.Tools
{
    [McpForUnityTool("manage_ui", AutoRegister = false)]
    public static class ManageUI
    {
        private static readonly HashSet<string> ValidExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".uxml", ".uss"
        };

        static ManageUI()
        {
            EditorApplication.quitting += CleanupRenderTextures;
            AssemblyReloadEvents.beforeAssemblyReload += CleanupRenderTextures;
        }

        private static void CleanupRenderTextures()
        {
            foreach (var kvp in s_panelRTs)
            {
                if (kvp.Value == null) continue;
                string assetPath = AssetDatabase.GetAssetPath(kvp.Value);
                kvp.Value.Release();
                if (!string.IsNullOrEmpty(assetPath))
                    AssetDatabase.DeleteAsset(assetPath);
                else
                    UnityEngine.Object.DestroyImmediate(kvp.Value);
            }
            s_panelRTs.Clear();
        }

        public static object HandleCommand(JObject @params)
        {
            string action = @params["action"]?.ToString()?.ToLowerInvariant();
            if (string.IsNullOrEmpty(action))
            {
                return new ErrorResponse("Action is required");
            }

            try
            {
                switch (action)
                {
                    case "ping":
                        return new SuccessResponse("pong", new { tool = "manage_ui" });

                    case "create":
                        return CreateFile(@params);

                    case "read":
                        return ReadFile(@params);

                    case "update":
                        return UpdateFile(@params);

                    case "attach_ui_document":
                        return AttachUIDocument(@params);

                    case "create_panel_settings":
                        return CreatePanelSettings(@params);

                    case "update_panel_settings":
                        return UpdatePanelSettings(@params);

                    case "get_visual_tree":
                        return GetVisualTree(@params);

                    case "render_ui":
                        return RenderUI(@params);

                    case "link_stylesheet":
                        return LinkStylesheet(@params);

                    case "delete":
                        return DeleteFile(@params);

                    case "list":
                        return ListUIAssets(@params);

                    case "detach_ui_document":
                        return DetachUIDocument(@params);

                    case "modify_visual_element":
                        return ModifyVisualElement(@params);

                    default:
                        return new ErrorResponse($"Unknown action: {action}");
                }
            }
            catch (Exception ex)
            {
                return new ErrorResponse(ex.Message, new { stackTrace = ex.StackTrace });
            }
        }

        private static string ValidatePath(string path, out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(path))
            {
                error = "'path' parameter is required.";
                return null;
            }

            path = AssetPathUtility.SanitizeAssetPath(path);
            if (path == null)
            {
                error = "Invalid path: contains traversal sequences.";
                return null;
            }

            string ext = Path.GetExtension(path);
            if (!ValidExtensions.Contains(ext))
            {
                error = $"Invalid file extension '{ext}'. Must be .uxml or .uss.";
                return null;
            }

            return path;
        }

        private static object CreateFile(JObject @params)
        {
            var p = new ToolParams(@params);
            string path = ValidatePath(p.Get("path"), out string pathError);
            if (pathError != null) return new ErrorResponse(pathError);

            string contents;
            try
            {
                contents = GetDecodedContents(p);
            }
            catch (ArgumentException ex)
            {
                return new ErrorResponse(ex.Message);
            }

            if (contents == null)
            {
                return new ErrorResponse("'contents' parameter is required for create.");
            }

            string fullPath = Path.Combine(Application.dataPath,
                path.Substring("Assets/".Length));
            fullPath = fullPath.Replace('/', Path.DirectorySeparatorChar);

            if (File.Exists(fullPath))
            {
                return new ErrorResponse($"File already exists at {path}. Use 'update' action to overwrite.");
            }

            string dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(fullPath, contents, Encoding.UTF8);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

            return new SuccessResponse($"Created {Path.GetExtension(path).TrimStart('.')} file at {path}",
                new { path });
        }

        private static object ReadFile(JObject @params)
        {
            var p = new ToolParams(@params);
            string path = ValidatePath(p.Get("path"), out string pathError);
            if (pathError != null) return new ErrorResponse(pathError);

            string fullPath = Path.Combine(Application.dataPath,
                path.Substring("Assets/".Length));
            fullPath = fullPath.Replace('/', Path.DirectorySeparatorChar);

            if (!File.Exists(fullPath))
            {
                return new ErrorResponse($"File not found: {path}");
            }

            string contents = File.ReadAllText(fullPath, Encoding.UTF8);
            string encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(contents));

            return new SuccessResponse($"Read {Path.GetExtension(path).TrimStart('.')} file at {path}",
                new
                {
                    path,
                    contents,
                    encodedContents = encoded,
                    contentsEncoded = true,
                    lengthBytes = Encoding.UTF8.GetByteCount(contents)
                });
        }

        private static object UpdateFile(JObject @params)
        {
            var p = new ToolParams(@params);
            string path = ValidatePath(p.Get("path"), out string pathError);
            if (pathError != null) return new ErrorResponse(pathError);

            string contents;
            try
            {
                contents = GetDecodedContents(p);
            }
            catch (ArgumentException ex)
            {
                return new ErrorResponse(ex.Message);
            }

            if (contents == null)
            {
                return new ErrorResponse("'contents' parameter is required for update.");
            }

            string fullPath = Path.Combine(Application.dataPath,
                path.Substring("Assets/".Length));
            fullPath = fullPath.Replace('/', Path.DirectorySeparatorChar);

            if (!File.Exists(fullPath))
            {
                return new ErrorResponse($"File not found: {path}. Use 'create' action for new files.");
            }

            File.WriteAllText(fullPath, contents, Encoding.UTF8);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

            return new SuccessResponse($"Updated {Path.GetExtension(path).TrimStart('.')} file at {path}",
                new { path });
        }

        private static object AttachUIDocument(JObject @params)
        {
            var p = new ToolParams(@params);

            var targetResult = p.GetRequired("target");
            var targetError = targetResult.GetOrError(out string target);
            if (targetError != null) return targetError;

            var sourceResult = p.GetRequired("source_asset");
            var sourceError = sourceResult.GetOrError(out string sourceAssetPath);
            if (sourceError != null) return sourceError;

            sourceAssetPath = AssetPathUtility.SanitizeAssetPath(sourceAssetPath);
            if (sourceAssetPath == null)
            {
                return new ErrorResponse("Invalid source_asset path.");
            }

            // Find the GameObject
            var goInstruction = new JObject { ["find"] = target };
            GameObject go = ObjectResolver.Resolve(goInstruction, typeof(GameObject)) as GameObject;
            if (go == null)
            {
                return new ErrorResponse($"Could not find target GameObject: {target}");
            }

            // Load the VisualTreeAsset
            var vta = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(sourceAssetPath);
            if (vta == null)
            {
                return new ErrorResponse($"Could not load VisualTreeAsset at: {sourceAssetPath}");
            }

            // Load or create PanelSettings
            string panelSettingsPath = p.Get("panel_settings") ?? p.Get("panelSettings");
            PanelSettings panelSettings = null;

            if (!string.IsNullOrEmpty(panelSettingsPath))
            {
                panelSettingsPath = AssetPathUtility.SanitizeAssetPath(panelSettingsPath);
                if (panelSettingsPath != null)
                {
                    panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(panelSettingsPath);
                }
                if (panelSettings == null)
                {
                    return new ErrorResponse($"Could not load PanelSettings at: {panelSettingsPath}");
                }
            }
            else
            {
                // Find existing or create default PanelSettings
                string[] guids = AssetDatabase.FindAssets("t:PanelSettings");
                if (guids.Length > 0)
                {
                    string existingPath = AssetDatabase.GUIDToAssetPath(guids[0]);
                    panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(existingPath);
                }

                if (panelSettings == null)
                {
                    panelSettings = CreateDefaultPanelSettings("Assets/UI/DefaultPanelSettings.asset");
                    if (panelSettings == null)
                    {
                        return new ErrorResponse("Failed to create default PanelSettings.");
                    }
                }
            }

            Undo.RecordObject(go, "Attach UIDocument");

            // Add or get UIDocument component
            var uiDoc = go.GetComponent<UIDocument>();
            if (uiDoc == null)
            {
                uiDoc = Undo.AddComponent<UIDocument>(go);
            }

            uiDoc.visualTreeAsset = vta;
            uiDoc.panelSettings = panelSettings;

            int sortOrder = p.GetInt("sort_order") ?? 0;
            uiDoc.sortingOrder = sortOrder;

            EditorUtility.SetDirty(go);

            return new SuccessResponse($"Attached UIDocument to {go.name}",
                new
                {
                    gameObject = go.name,
                    sourceAsset = sourceAssetPath,
                    panelSettings = AssetDatabase.GetAssetPath(panelSettings),
                    sortOrder
                });
        }

        private static object CreatePanelSettings(JObject @params)
        {
            var p = new ToolParams(@params);

            var pathResult = p.GetRequired("path");
            var pathError = pathResult.GetOrError(out string path);
            if (pathError != null) return pathError;

            path = AssetPathUtility.SanitizeAssetPath(path);
            if (path == null)
            {
                return new ErrorResponse("Invalid path: contains traversal sequences.");
            }

            if (!path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
            {
                path += ".asset";
            }

            if (AssetDatabase.LoadAssetAtPath<PanelSettings>(path) != null)
            {
                return new ErrorResponse($"PanelSettings already exists at {path}");
            }

            var ps = CreateDefaultPanelSettings(path);
            if (ps == null)
            {
                return new ErrorResponse("Failed to create PanelSettings asset.");
            }

            // Apply any settings passed as a flat dict
            JToken settingsToken = p.GetRaw("settings");
            var changes = new List<string>();
            if (settingsToken is JObject settingsObj)
            {
                ApplyPanelSettingsProperties(ps, settingsObj, changes);
            }
            else
            {
                // Legacy: support top-level scale_mode / reference_resolution
                string scaleMode = p.Get("scale_mode");
                if (!string.IsNullOrEmpty(scaleMode))
                {
                    if (Enum.TryParse<PanelScaleMode>(scaleMode, true, out var mode))
                    {
                        ps.scaleMode = mode;
                        changes.Add("scaleMode");
                    }
                }

                JToken refResToken = p.GetRaw("reference_resolution");
                if (refResToken is JObject refRes)
                {
                    int w = refRes["width"]?.ToObject<int>() ?? 1920;
                    int h = refRes["height"]?.ToObject<int>() ?? 1080;
                    ps.referenceResolution = new Vector2Int(w, h);
                    changes.Add("referenceResolution");
                }
            }

            EditorUtility.SetDirty(ps);
            AssetDatabase.SaveAssets();

            return new SuccessResponse($"Created PanelSettings at {path}",
                new { path, applied = changes });
        }

        private static object UpdatePanelSettings(JObject @params)
        {
            var p = new ToolParams(@params);

            var pathResult = p.GetRequired("path");
            var pathError = pathResult.GetOrError(out string path);
            if (pathError != null) return pathError;

            path = AssetPathUtility.SanitizeAssetPath(path);
            if (path == null)
                return new ErrorResponse("Invalid path: contains traversal sequences.");

            if (!path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
                path += ".asset";

            var ps = AssetDatabase.LoadAssetAtPath<PanelSettings>(path);
            if (ps == null)
                return new ErrorResponse($"No PanelSettings found at {path}");

            JToken settingsToken = p.GetRaw("settings");
            if (settingsToken is not JObject settingsObj || settingsObj.Count == 0)
                return new ErrorResponse("'settings' dict is required with at least one property to update.");

            var changes = new List<string>();
            ApplyPanelSettingsProperties(ps, settingsObj, changes);

            if (changes.Count == 0)
                return new ErrorResponse("No recognised properties were applied. Check the key names.");

            EditorUtility.SetDirty(ps);
            AssetDatabase.SaveAssets();

            return new SuccessResponse($"Updated PanelSettings at {path}",
                new { path, applied = changes });
        }

        private static PanelSettings CreateDefaultPanelSettings(string path)
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
            {
                EnsureFolderExists(dir);
            }

            var ps = ScriptableObject.CreateInstance<PanelSettings>();
            AssetDatabase.CreateAsset(ps, path);
            AssetDatabase.SaveAssets();
            return ps;
        }

        /// <summary>
        /// Generic, data-driven applicator for PanelSettings properties.
        /// Accepts a flat JObject where each key maps to a PanelSettings property.
        /// Recognised keys (case-insensitive matching via snake_case/camelCase):
        ///   scaleMode, referenceResolution, screenMatchMode, match,
        ///   referenceDpi, fallbackDpi, sortingOrder, targetDisplay,
        ///   clearColor, colorClearValue, clearDepthStencil,
        ///   themeStyleSheet, dynamicAtlasSettings.
        /// </summary>
        private static void ApplyPanelSettingsProperties(PanelSettings ps, JObject settings, List<string> changes)
        {
            foreach (var prop in settings)
            {
                string key = NormalizeKey(prop.Key);
                JToken val = prop.Value;

                switch (key)
                {
                    // ── Enum properties ─────────────────────────────────────
                    case "scalemode":
                        if (TryParseEnum<PanelScaleMode>(val, out var sm)) { ps.scaleMode = sm; changes.Add("scaleMode"); }
                        break;

                    case "screenmatchmode":
                        if (TryParseEnum<PanelScreenMatchMode>(val, out var smm)) { ps.screenMatchMode = smm; changes.Add("screenMatchMode"); }
                        break;

                    // ── Numeric properties ──────────────────────────────────
                    case "match":
                        if (TryFloat(val, out float matchVal)) { ps.match = Mathf.Clamp01(matchVal); changes.Add("match"); }
                        break;

                    case "referencedpi":
                        if (TryFloat(val, out float refDpi)) { ps.referenceDpi = refDpi; changes.Add("referenceDpi"); }
                        break;

                    case "fallbackdpi":
                        if (TryFloat(val, out float fbDpi)) { ps.fallbackDpi = fbDpi; changes.Add("fallbackDpi"); }
                        break;

                    case "sortingorder":
                        if (TryInt(val, out int so)) { ps.sortingOrder = so; changes.Add("sortingOrder"); }
                        break;

                    case "targetdisplay":
                        if (TryInt(val, out int td)) { ps.targetDisplay = td; changes.Add("targetDisplay"); }
                        break;

                    // ── Bool properties ──────────────────────────────────────
                    case "clearcolor":
                        ps.clearColor = ParamCoercion.CoerceBool(val, false);
                        changes.Add("clearColor");
                        break;

                    case "cleardepthstencil":
                        ps.clearDepthStencil = ParamCoercion.CoerceBool(val, false);
                        changes.Add("clearDepthStencil");
                        break;

                    // ── Composite properties ────────────────────────────────
                    case "referenceresolution":
                        if (val is JObject resObj)
                        {
                            int w = resObj["width"]?.ToObject<int>() ?? ps.referenceResolution.x;
                            int h = resObj["height"]?.ToObject<int>() ?? ps.referenceResolution.y;
                            ps.referenceResolution = new Vector2Int(w, h);
                            changes.Add("referenceResolution");
                        }
                        break;

                    case "colorclearvalue":
                        if (TryParseColor(val, out Color clr)) { ps.colorClearValue = clr; changes.Add("colorClearValue"); }
                        break;

                    case "dynamicatlassettings":
                        if (val is JObject daObj) { ApplyDynamicAtlasSettings(ps, daObj, changes); }
                        break;

                    // ── Asset reference properties ──────────────────────────
                    case "themestylesheet":
                    {
                        string tsPath = val?.ToString();
                        if (!string.IsNullOrEmpty(tsPath))
                        {
                            var ts = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(tsPath);
                            if (ts != null) { ps.themeStyleSheet = ts; changes.Add("themeStyleSheet"); }
                        }
                        break;
                    }

                    // unknown keys are silently ignored
                }
            }
        }

        private static void ApplyDynamicAtlasSettings(PanelSettings ps, JObject da, List<string> changes)
        {
            var daCopy = ps.dynamicAtlasSettings;

            if (da["minAtlasSize"] != null && TryInt(da["minAtlasSize"], out int minSize))
                daCopy.minAtlasSize = minSize;
            if (da["maxAtlasSize"] != null && TryInt(da["maxAtlasSize"], out int maxSize))
                daCopy.maxAtlasSize = maxSize;
            if (da["maxSubTextureSize"] != null && TryInt(da["maxSubTextureSize"], out int maxSub))
                daCopy.maxSubTextureSize = maxSub;
            if (da["activeFilters"] != null && TryParseEnum<DynamicAtlasFilters>(da["activeFilters"], out var af))
                daCopy.activeFilters = af;

            ps.dynamicAtlasSettings = daCopy;
            changes.Add("dynamicAtlasSettings");
        }

        // ── Tiny helpers to keep the switch compact ─────────────────────────

        private static string NormalizeKey(string key)
        {
            // Strip underscores and lowercase so "scale_mode", "scaleMode", "ScaleMode"
            // all match the same case label.
            return key.Replace("_", "").ToLowerInvariant();
        }

        private static bool TryParseEnum<T>(JToken token, out T result) where T : struct, Enum
        {
            result = default;
            string s = token?.ToString();
            return !string.IsNullOrEmpty(s) && Enum.TryParse(s, true, out result);
        }

        private static bool TryFloat(JToken token, out float result)
        {
            result = 0f;
            if (token == null) return false;
            if (token.Type == JTokenType.Float || token.Type == JTokenType.Integer)
            {
                result = token.ToObject<float>();
                return true;
            }
            return float.TryParse(token.ToString(), out result);
        }

        private static bool TryInt(JToken token, out int result)
        {
            result = 0;
            if (token == null) return false;
            if (token.Type == JTokenType.Integer)
            {
                result = token.ToObject<int>();
                return true;
            }
            return int.TryParse(token.ToString(), out result);
        }

        private static bool TryParseColor(JToken token, out Color color)
        {
            color = Color.clear;
            if (token == null) return false;

            // Accept "#RRGGBB", "#RRGGBBAA", or {r,g,b,a} object
            if (token.Type == JTokenType.String)
            {
                return ColorUtility.TryParseHtmlString(token.ToString(), out color);
            }

            if (token is JObject cObj)
            {
                color = new Color(
                    cObj["r"]?.ToObject<float>() ?? 0f,
                    cObj["g"]?.ToObject<float>() ?? 0f,
                    cObj["b"]?.ToObject<float>() ?? 0f,
                    cObj["a"]?.ToObject<float>() ?? 1f
                );
                return true;
            }

            return false;
        }

        private static void EnsureFolderExists(string assetFolderPath)
        {
            if (AssetDatabase.IsValidFolder(assetFolderPath))
                return;

            string[] parts = assetFolderPath.Replace('\\', '/').Split('/');
            string current = parts[0]; // "Assets"
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }
                current = next;
            }
        }

        private static object GetVisualTree(JObject @params)
        {
            var p = new ToolParams(@params);

            var targetResult = p.GetRequired("target");
            var targetError = targetResult.GetOrError(out string target);
            if (targetError != null) return targetError;

            int maxDepth = p.GetInt("max_depth") ?? 10;

            var goInstruction = new JObject { ["find"] = target };
            GameObject go = ObjectResolver.Resolve(goInstruction, typeof(GameObject)) as GameObject;
            if (go == null)
            {
                return new ErrorResponse($"Could not find target GameObject: {target}");
            }

            var uiDoc = go.GetComponent<UIDocument>();
            if (uiDoc == null)
            {
                return new ErrorResponse($"GameObject {go.name} has no UIDocument component.");
            }

            var root = uiDoc.rootVisualElement;
            if (root == null)
            {
                return new SuccessResponse($"UIDocument on {go.name} has no visual tree (not yet built).",
                    new
                    {
                        gameObject = go.name,
                        sourceAsset = uiDoc.visualTreeAsset != null
                            ? AssetDatabase.GetAssetPath(uiDoc.visualTreeAsset)
                            : null,
                        tree = (object)null
                    });
            }

            var tree = SerializeVisualElement(root, 0, maxDepth);

            return new SuccessResponse($"Visual tree for UIDocument on {go.name}",
                new
                {
                    gameObject = go.name,
                    sourceAsset = uiDoc.visualTreeAsset != null
                        ? AssetDatabase.GetAssetPath(uiDoc.visualTreeAsset)
                        : null,
                    tree
                });
        }

        private static object SerializeVisualElement(VisualElement element, int depth, int maxDepth)
        {
            var result = new Dictionary<string, object>
            {
                ["type"] = element.GetType().Name,
                ["name"] = element.name ?? "",
                ["classes"] = new List<string>(element.GetClasses()),
            };

            // Include basic computed style info
            var style = new Dictionary<string, object>();
            var resolved = element.resolvedStyle;

            if (resolved.width > 0) style["width"] = resolved.width;
            if (resolved.height > 0) style["height"] = resolved.height;
            if (resolved.color != Color.clear)
                style["color"] = ColorToHex(resolved.color);
            if (resolved.backgroundColor != Color.clear)
                style["backgroundColor"] = ColorToHex(resolved.backgroundColor);
            if (resolved.fontSize > 0) style["fontSize"] = resolved.fontSize;

            if (style.Count > 0)
                result["resolvedStyle"] = style;

            // Include text content for labels/buttons
            if (element is TextElement textEl && !string.IsNullOrEmpty(textEl.text))
            {
                result["text"] = textEl.text;
            }

            // Serialize children
            if (depth < maxDepth && element.childCount > 0)
            {
                var children = new List<object>();
                foreach (var child in element.Children())
                {
                    children.Add(SerializeVisualElement(child, depth + 1, maxDepth));
                }
                result["children"] = children;
            }
            else if (element.childCount > 0)
            {
                result["childCount"] = element.childCount;
                result["truncated"] = true;
            }

            return result;
        }

        // ---- Render UI ----

        // Persistent RenderTextures keyed by PanelSettings instance ID so the panel
        // renders into them automatically every frame.
        private static readonly Dictionary<int, RenderTexture> s_panelRTs = new();

        // Play-mode coroutine capture state.  Only one capture is in-flight at a
        // time; concurrent render_ui calls while a capture is pending are rejected
        // with an explicit error.
        private static Texture2D s_pendingCaptureTex;
        private static bool s_pendingCaptureDone;
        private static bool s_pendingCaptureStarted;

        // MonoBehaviour that captures a screenshot at end-of-frame in play mode.
        private sealed class MCP_ScreenCapturer : MonoBehaviour
        {
            private System.Collections.IEnumerator Start()
            {
                yield return new WaitForEndOfFrame();

                if (!ScreenshotUtility.IsScreenCaptureModuleAvailable)
                {
                    Debug.LogError("[MCP] " + ScreenshotUtility.ScreenCaptureModuleNotAvailableError);
                    ManageUI.s_pendingCaptureTex = null;
                    ManageUI.s_pendingCaptureDone = false;
                    ManageUI.s_pendingCaptureStarted = false;
                    Destroy(gameObject);
                    yield break;
                }

                try
                {
                    ManageUI.s_pendingCaptureTex = ScreenCapture.CaptureScreenshotAsTexture();
                    ManageUI.s_pendingCaptureDone = true;
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[MCP] ScreenCapture failed: {ex.Message}");
                    ManageUI.s_pendingCaptureTex = null;
                    ManageUI.s_pendingCaptureDone = false;
                }
                ManageUI.s_pendingCaptureStarted = false;
                Destroy(gameObject);
            }
        }

        private static object RenderUI(JObject @params)
        {
            var p = new ToolParams(@params);

            string target = p.Get("target");
            string uxmlPath = p.Get("path");
            int width = p.GetInt("width") ?? 1920;
            int height = p.GetInt("height") ?? 1080;
            bool includeImage = p.GetBool("include_image") || p.GetBool("includeImage");
            int maxResolution = p.GetInt("max_resolution") ?? p.GetInt("maxResolution") ?? 640;
            string fileName = p.Get("file_name") ?? p.Get("fileName");

            if (string.IsNullOrEmpty(target) && string.IsNullOrEmpty(uxmlPath))
            {
                return new ErrorResponse("Either 'target' (GameObject with UIDocument) or 'path' (UXML asset path) is required.");
            }

            // ── Play-mode capture via ScreenCapture coroutine ──────────────────────
            // PanelSettings.targetTexture is read in the same frame it is assigned,
            // so the RT is always blank in a synchronous tool call.  In play mode we
            // dispatch a WaitForEndOfFrame coroutine that uses ScreenCapture, which
            // captures the fully-composited game view (including UI Toolkit overlays).
            // First call: queues the capture and returns "pending".
            // Second call: result is ready – save PNG and return data.
            if (Application.isPlaying)
            {
                // Build the output paths (used by both the pending and ready branches)
                string resolvedPlayName = string.IsNullOrWhiteSpace(fileName)
                    ? $"ui-render-{DateTime.Now:yyyyMMdd-HHmmss}.png"
                    : fileName.Trim();
                if (!resolvedPlayName.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                    resolvedPlayName += ".png";

                string playFolder = Path.Combine(Application.dataPath, "Screenshots");
                Directory.CreateDirectory(playFolder);
                string playFullPath = Path.Combine(playFolder, resolvedPlayName).Replace('\\', '/');
                playFullPath = EnsureUniqueFilePath(playFullPath);
                string playAssetsRelPath = "Assets/Screenshots/" + Path.GetFileName(playFullPath);

                // ── Case 1: capture is ready ──────────────────────────────────────
                if (s_pendingCaptureDone && s_pendingCaptureTex != null)
                {
                    var captureTex = s_pendingCaptureTex;
                    s_pendingCaptureDone = false;
                    s_pendingCaptureTex = null;

                    int captureW = captureTex.width;
                    int captureH = captureTex.height;
                    byte[] capturePng = captureTex.EncodeToPNG();
                    UnityEngine.Object.DestroyImmediate(captureTex);

                    File.WriteAllBytes(playFullPath, capturePng);
                    AssetDatabase.ImportAsset(playAssetsRelPath, ImportAssetOptions.ForceSynchronousImport);

                    var playData = new Dictionary<string, object>
                    {
                        { "path", playAssetsRelPath },
                        { "fullPath", playFullPath },
                        { "width", captureW },
                        { "height", captureH },
                        { "hasContent", true },
                    };

                    if (!string.IsNullOrEmpty(target)) playData["gameObject"] = target;
                    if (!string.IsNullOrEmpty(uxmlPath)) playData["sourceAsset"] = uxmlPath;

                    if (includeImage)
                    {
                        int targetMax = maxResolution > 0 ? maxResolution : 640;
                        Texture2D downscaled = null;
                        try
                        {
                            var fullTex = new Texture2D(captureW, captureH, TextureFormat.RGBA32, false);
                            fullTex.LoadImage(capturePng);
                            if (captureW > targetMax || captureH > targetMax)
                            {
                                downscaled = ScreenshotUtility.DownscaleTexture(fullTex, targetMax);
                                playData["imageBase64"] = Convert.ToBase64String(downscaled.EncodeToPNG());
                                playData["imageWidth"] = downscaled.width;
                                playData["imageHeight"] = downscaled.height;
                            }
                            else
                            {
                                playData["imageBase64"] = Convert.ToBase64String(capturePng);
                                playData["imageWidth"] = captureW;
                                playData["imageHeight"] = captureH;
                            }
                            UnityEngine.Object.DestroyImmediate(fullTex);
                        }
                        finally
                        {
                            if (downscaled != null) UnityEngine.Object.DestroyImmediate(downscaled);
                        }
                    }

                    return new SuccessResponse($"UI render saved to '{playAssetsRelPath}'.", playData);
                }

                // ── Case 2: start a new capture ───────────────────────────────────
                // Verify the ScreenCapture module is enabled before attempting capture.
                if (!ScreenshotUtility.IsScreenCaptureModuleAvailable)
                {
                    return new ErrorResponse(ScreenshotUtility.ScreenCaptureModuleNotAvailableError);
                }

                // Only one capture in flight at a time.  If one is already pending,
                // reject rather than silently overwriting the state.
                if (s_pendingCaptureStarted)
                {
                    return new ErrorResponse(
                        "A play-mode screen capture is already in progress. "
                        + "Call render_ui again after the current capture completes.");
                }

                s_pendingCaptureDone = false;
                s_pendingCaptureTex = null;
                s_pendingCaptureStarted = true;
                var captureGo = new GameObject("__MCP_ScreenCapturer__")
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
                captureGo.AddComponent<MCP_ScreenCapturer>();

                return new SuccessResponse(
                    "Play-mode screenshot capture queued (WaitForEndOfFrame). Call render_ui again to retrieve the rendered image.",
                    new Dictionary<string, object>
                    {
                        { "pending", true },
                        { "gameObject", (object)target ?? uxmlPath },
                        { "note", "A screen capture was scheduled for the end of this frame. Call render_ui once more to get the result." }
                    });
            }
            // ── End play-mode branch ────────────────────────────────────────────────

            // Resolve UIDocument
            UIDocument uiDoc = null;
            GameObject tempGo = null;
            PanelSettings tempPs = null;

            try
            {
                if (!string.IsNullOrEmpty(target))
                {
                    var goInstruction = new JObject { ["find"] = target };
                    GameObject go = ObjectResolver.Resolve(goInstruction, typeof(GameObject)) as GameObject;
                    if (go == null)
                        return new ErrorResponse($"Could not find target GameObject: {target}");

                    uiDoc = go.GetComponent<UIDocument>();
                    if (uiDoc == null)
                        return new ErrorResponse($"GameObject '{go.name}' has no UIDocument component.");
                }
                else
                {
                    uxmlPath = AssetPathUtility.SanitizeAssetPath(uxmlPath);
                    if (uxmlPath == null)
                        return new ErrorResponse("Invalid UXML path.");

                    var vta = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(uxmlPath);
                    if (vta == null)
                        return new ErrorResponse($"Could not load VisualTreeAsset at: {uxmlPath}");

                    tempGo = new GameObject("__MCP_UI_Render_Temp__");
                    tempGo.hideFlags = HideFlags.HideAndDontSave;
                    uiDoc = tempGo.AddComponent<UIDocument>();

                    string[] guids = AssetDatabase.FindAssets("t:PanelSettings");
                    PanelSettings ps = null;
                    if (guids.Length > 0)
                        ps = AssetDatabase.LoadAssetAtPath<PanelSettings>(AssetDatabase.GUIDToAssetPath(guids[0]));
                    if (ps == null)
                    {
                        ps = CreateDefaultPanelSettings("Assets/UI/DefaultPanelSettings.asset");
                        tempPs = ps;
                    }

                    uiDoc.panelSettings = ps;
                    uiDoc.visualTreeAsset = vta;
                }

                if (uiDoc.panelSettings == null)
                    return new ErrorResponse("UIDocument has no PanelSettings assigned.");

                var panelSettings = uiDoc.panelSettings;
                int psId = panelSettings.GetInstanceID();

                // Check if we already have a persistent RT assigned to this PanelSettings.
                // If the RT exists and its size matches, the panel has been rendering into it.
                // If not, create one and assign it — content will be available on the next call.
                bool rtJustCreated = false;
                RenderTexture rt = panelSettings.targetTexture as RenderTexture;

                if (rt != null && s_panelRTs.ContainsKey(psId) && rt.width == width && rt.height == height)
                {
                    // RT already assigned and size matches — panel has been rendering into it.
                    // We will read it below, then restore targetTexture = null so the UI
                    // goes back to rendering on the actual display/camera.
                }
                else
                {
                    // Clean up old RT if size changed
                    if (s_panelRTs.TryGetValue(psId, out var oldRt) && oldRt != null)
                    {
                        panelSettings.targetTexture = null;
                        string oldPath = AssetDatabase.GetAssetPath(oldRt);
                        oldRt.Release();
                        if (!string.IsNullOrEmpty(oldPath))
                            AssetDatabase.DeleteAsset(oldPath);
                        else
                            UnityEngine.Object.DestroyImmediate(oldRt);
                        s_panelRTs.Remove(psId);
                    }

                    // Create RT as an asset so PanelSettings can serialize the reference properly
                    rt = new RenderTexture(width, height, 32, RenderTextureFormat.ARGB32);
                    rt.name = $"MCP_UI_Render_{psId}";
                    rt.Create();

                    string rtFolder = "Assets/UI";
                    if (!AssetDatabase.IsValidFolder(rtFolder))
                        AssetDatabase.CreateFolder("Assets", "UI");
                    string rtAssetPath = $"{rtFolder}/RT_MCP_UI_Render_{psId}.renderTexture";
                    AssetDatabase.CreateAsset(rt, rtAssetPath);
                    AssetDatabase.SaveAssets();

                    panelSettings.targetTexture = rt;
                    s_panelRTs[psId] = rt;
                    rtJustCreated = true;

                    // Mark dirty and force editor repaint so the panel renders into the RT
                    uiDoc.rootVisualElement?.MarkDirtyRepaint();
                    EditorUtility.SetDirty(panelSettings);
                    UnityEditorInternal.InternalEditorUtility.RepaintAllViews();

                    // Force a synchronous layout + repaint pass
                    Canvas.ForceUpdateCanvases();
                }

                // Read pixels from the RT
                RenderTexture prevActive = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
                tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                tex.Apply();
                RenderTexture.active = prevActive;

                // Restore targetTexture to null so the UI renders back to the
                // actual display / camera.  Keep the RT cached in s_panelRTs so
                // it can be reused on the next render_ui call without re-creating.
                if (!rtJustCreated)
                {
                    panelSettings.targetTexture = null;
                    EditorUtility.SetDirty(panelSettings);
                }

                // Check if any content was rendered
                bool hasContent = false;
                var pixels = tex.GetPixels32();
                for (int i = 0; i < pixels.Length; i += Mathf.Max(1, pixels.Length / 100))
                {
                    if (pixels[i].a > 0) { hasContent = true; break; }
                }

                // Save to Screenshots folder
                string resolvedName = string.IsNullOrWhiteSpace(fileName)
                    ? $"ui-render-{DateTime.Now:yyyyMMdd-HHmmss}.png"
                    : fileName.Trim();
                if (!resolvedName.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                    resolvedName += ".png";

                string folder = Path.Combine(Application.dataPath, "Screenshots");
                Directory.CreateDirectory(folder);
                string fullPath = Path.Combine(folder, resolvedName).Replace('\\', '/');
                fullPath = EnsureUniqueFilePath(fullPath);

                byte[] png = tex.EncodeToPNG();
                File.WriteAllBytes(fullPath, png);

                string assetsRelPath = "Assets/Screenshots/" + Path.GetFileName(fullPath);
                AssetDatabase.ImportAsset(assetsRelPath, ImportAssetOptions.ForceSynchronousImport);

                var data = new Dictionary<string, object>
                {
                    { "path", assetsRelPath },
                    { "fullPath", fullPath },
                    { "width", width },
                    { "height", height },
                    { "hasContent", hasContent },
                };

                if (rtJustCreated)
                    data["note"] = "RenderTexture was just assigned to PanelSettings. Call render_ui again to capture the rendered UI.";

                if (!string.IsNullOrEmpty(target))
                    data["gameObject"] = target;
                if (!string.IsNullOrEmpty(uxmlPath))
                    data["sourceAsset"] = uxmlPath;

                if (includeImage)
                {
                    int targetMax = maxResolution > 0 ? maxResolution : 640;
                    Texture2D downscaled = null;
                    try
                    {
                        if (width > targetMax || height > targetMax)
                        {
                            downscaled = ScreenshotUtility.DownscaleTexture(tex, targetMax);
                            data["imageBase64"] = Convert.ToBase64String(downscaled.EncodeToPNG());
                            data["imageWidth"] = downscaled.width;
                            data["imageHeight"] = downscaled.height;
                        }
                        else
                        {
                            data["imageBase64"] = Convert.ToBase64String(png);
                            data["imageWidth"] = width;
                            data["imageHeight"] = height;
                        }
                    }
                    finally
                    {
                        if (downscaled != null) UnityEngine.Object.DestroyImmediate(downscaled);
                    }
                }

                UnityEngine.Object.DestroyImmediate(tex);

                string msg = hasContent
                    ? $"UI rendered to '{assetsRelPath}'."
                    : rtJustCreated
                        ? $"RenderTexture assigned to PanelSettings. Call render_ui again to capture the rendered content."
                        : $"UI render saved to '{assetsRelPath}' (no visible content detected).";

                return new SuccessResponse(msg, data);
            }
            finally
            {
                if (tempGo != null) UnityEngine.Object.DestroyImmediate(tempGo);
                if (tempPs != null)
                {
                    string tempPsPath = AssetDatabase.GetAssetPath(tempPs);
                    if (!string.IsNullOrEmpty(tempPsPath))
                        AssetDatabase.DeleteAsset(tempPsPath);
                    else
                        UnityEngine.Object.DestroyImmediate(tempPs, true);
                }
            }
        }

        // ---- Link Stylesheet ----

        private static object LinkStylesheet(JObject @params)
        {
            var p = new ToolParams(@params);

            string uxmlPathRaw = p.Get("path");
            string uxmlPath = ValidatePath(uxmlPathRaw, out string pathError);
            if (pathError != null) return new ErrorResponse(pathError);

            // Validate the UXML path is actually a .uxml
            if (!uxmlPath.EndsWith(".uxml", StringComparison.OrdinalIgnoreCase))
                return new ErrorResponse("'path' must point to a .uxml file.");

            string stylesheetPath = p.Get("stylesheet");
            if (string.IsNullOrEmpty(stylesheetPath))
                return new ErrorResponse("'stylesheet' parameter is required.");

            stylesheetPath = AssetPathUtility.SanitizeAssetPath(stylesheetPath);
            if (stylesheetPath == null)
                return new ErrorResponse("Invalid stylesheet path: contains traversal sequences.");

            if (!stylesheetPath.EndsWith(".uss", StringComparison.OrdinalIgnoreCase))
                return new ErrorResponse("'stylesheet' must point to a .uss file.");

            // Read the UXML file
            string fullPath = Path.Combine(Application.dataPath,
                uxmlPath.Substring("Assets/".Length)).Replace('/', Path.DirectorySeparatorChar);

            if (!File.Exists(fullPath))
                return new ErrorResponse($"UXML file not found: {uxmlPath}");

            string content = File.ReadAllText(fullPath, Encoding.UTF8);

            // Check if stylesheet is already linked
            if (content.Contains($"src=\"{stylesheetPath}\"") ||
                content.Contains($"src=\"project://database/{stylesheetPath}\""))
            {
                return new SuccessResponse($"Stylesheet already linked in '{uxmlPath}'.",
                    new { path = uxmlPath, stylesheet = stylesheetPath, alreadyLinked = true });
            }

            // Find the insertion point (after the opening <ui:UXML ...> or <UXML ...> tag)
            int insertIdx = FindUxmlBodyStart(content);
            if (insertIdx < 0)
                return new ErrorResponse("Could not find insertion point. Ensure UXML has a root <ui:UXML> or <UXML> element.");

            string styleTag = $"\n    <Style src=\"project://database/{stylesheetPath}\" />";
            content = content.Insert(insertIdx, styleTag);

            File.WriteAllText(fullPath, content, Encoding.UTF8);
            AssetDatabase.ImportAsset(uxmlPath, ImportAssetOptions.ForceUpdate);

            return new SuccessResponse($"Linked stylesheet '{stylesheetPath}' to '{uxmlPath}'.",
                new { path = uxmlPath, stylesheet = stylesheetPath });
        }

        // ---- Delete ----

        private static object DeleteFile(JObject @params)
        {
            var p = new ToolParams(@params);
            string path = ValidatePath(p.Get("path"), out string pathError);
            if (pathError != null) return new ErrorResponse(pathError);

            string fullPath = Path.Combine(Application.dataPath,
                path.Substring("Assets/".Length));
            fullPath = fullPath.Replace('/', Path.DirectorySeparatorChar);

            if (!File.Exists(fullPath))
            {
                return new ErrorResponse($"File not found: {path}");
            }

            try
            {
                bool success = AssetDatabase.DeleteAsset(path);
                if (!success)
                {
                    return new ErrorResponse($"Failed to delete file through AssetDatabase: '{path}'");
                }

                // Fallback: if file still exists after AssetDatabase.DeleteAsset
                if (File.Exists(fullPath))
                {
                    File.Delete(fullPath);
                }

                return new SuccessResponse($"Deleted {Path.GetExtension(path).TrimStart('.')} file at {path}",
                    new { path });
            }
            catch (Exception e)
            {
                return new ErrorResponse($"Failed to delete '{path}': {e.Message}");
            }
        }

        // ---- List UI Assets ----

        private static object ListUIAssets(JObject @params)
        {
            var p = new ToolParams(@params);
            string scope = p.Get("path") ?? "Assets";
            string filterType = p.Get("filter_type") ?? p.Get("filterType");
            int pageSize = p.GetInt("page_size") ?? p.GetInt("pageSize") ?? 50;
            int pageNumber = p.GetInt("page_number") ?? p.GetInt("pageNumber") ?? 1;

            scope = AssetPathUtility.SanitizeAssetPath(scope);
            if (scope == null)
            {
                return new ErrorResponse("Invalid path: contains traversal sequences.");
            }

            string[] folderScope = AssetDatabase.IsValidFolder(scope)
                ? new[] { scope }
                : null;

            // Find UXML and USS assets based on filter
            var allAssets = new List<object>();

            bool includeUxml = string.IsNullOrEmpty(filterType) ||
                               filterType.Equals("uxml", StringComparison.OrdinalIgnoreCase) ||
                               filterType.Equals("VisualTreeAsset", StringComparison.OrdinalIgnoreCase);
            bool includeUss = string.IsNullOrEmpty(filterType) ||
                              filterType.Equals("uss", StringComparison.OrdinalIgnoreCase) ||
                              filterType.Equals("StyleSheet", StringComparison.OrdinalIgnoreCase);
            bool includePanelSettings = string.IsNullOrEmpty(filterType) ||
                                        filterType.Equals("PanelSettings", StringComparison.OrdinalIgnoreCase);

            if (includeUxml)
            {
                string[] guids = AssetDatabase.FindAssets("t:VisualTreeAsset", folderScope);
                foreach (string guid in guids)
                {
                    string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                    if (!string.IsNullOrEmpty(assetPath))
                    {
                        allAssets.Add(new Dictionary<string, object>
                        {
                            ["path"] = assetPath,
                            ["type"] = "uxml",
                            ["name"] = Path.GetFileName(assetPath),
                        });
                    }
                }
            }

            if (includeUss)
            {
                string[] guids = AssetDatabase.FindAssets("t:StyleSheet", folderScope);
                foreach (string guid in guids)
                {
                    string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                    if (!string.IsNullOrEmpty(assetPath))
                    {
                        allAssets.Add(new Dictionary<string, object>
                        {
                            ["path"] = assetPath,
                            ["type"] = "uss",
                            ["name"] = Path.GetFileName(assetPath),
                        });
                    }
                }
            }

            if (includePanelSettings)
            {
                string[] guids = AssetDatabase.FindAssets("t:PanelSettings", folderScope);
                foreach (string guid in guids)
                {
                    string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                    if (!string.IsNullOrEmpty(assetPath))
                    {
                        allAssets.Add(new Dictionary<string, object>
                        {
                            ["path"] = assetPath,
                            ["type"] = "PanelSettings",
                            ["name"] = Path.GetFileName(assetPath),
                        });
                    }
                }
            }

            int total = allAssets.Count;
            int startIndex = (pageNumber - 1) * pageSize;
            var paged = allAssets.Skip(startIndex).Take(pageSize).ToList();

            return new SuccessResponse(
                $"Found {total} UI asset(s). Returning page {pageNumber} ({paged.Count} items).",
                new
                {
                    total,
                    pageSize,
                    pageNumber,
                    assets = paged,
                });
        }

        // ---- Detach UIDocument ----

        private static object DetachUIDocument(JObject @params)
        {
            var p = new ToolParams(@params);

            var targetResult = p.GetRequired("target");
            var targetError = targetResult.GetOrError(out string target);
            if (targetError != null) return targetError;

            var goInstruction = new JObject { ["find"] = target };
            GameObject go = ObjectResolver.Resolve(goInstruction, typeof(GameObject)) as GameObject;
            if (go == null)
            {
                return new ErrorResponse($"Could not find target GameObject: {target}");
            }

            var uiDoc = go.GetComponent<UIDocument>();
            if (uiDoc == null)
            {
                return new ErrorResponse($"GameObject '{go.name}' has no UIDocument component.");
            }

            string sourceAsset = uiDoc.visualTreeAsset != null
                ? AssetDatabase.GetAssetPath(uiDoc.visualTreeAsset)
                : null;

            Undo.DestroyObjectImmediate(uiDoc);
            EditorUtility.SetDirty(go);

            return new SuccessResponse($"Removed UIDocument from {go.name}",
                new
                {
                    gameObject = go.name,
                    removedSourceAsset = sourceAsset,
                });
        }

        // ---- Modify Visual Element ----

        private static object ModifyVisualElement(JObject @params)
        {
            var p = new ToolParams(@params);

            var targetResult = p.GetRequired("target");
            var targetError = targetResult.GetOrError(out string target);
            if (targetError != null) return targetError;

            string elementName = p.Get("element_name") ?? p.Get("elementName");
            if (string.IsNullOrEmpty(elementName))
            {
                return new ErrorResponse("'element_name' parameter is required.");
            }

            var goInstruction = new JObject { ["find"] = target };
            GameObject go = ObjectResolver.Resolve(goInstruction, typeof(GameObject)) as GameObject;
            if (go == null)
            {
                return new ErrorResponse($"Could not find target GameObject: {target}");
            }

            var uiDoc = go.GetComponent<UIDocument>();
            if (uiDoc == null)
            {
                return new ErrorResponse($"GameObject '{go.name}' has no UIDocument component.");
            }

            var root = uiDoc.rootVisualElement;
            if (root == null)
            {
                return new ErrorResponse($"UIDocument on {go.name} has no visual tree (not yet built).");
            }

            // Find the target element by name
            var element = root.Q(elementName);
            if (element == null)
            {
                return new ErrorResponse($"Visual element with name '{elementName}' not found in the visual tree.");
            }

            var modifications = new List<string>();

            // Set text content (Label, Button, etc.)
            string text = p.Get("text");
            if (text != null && element is TextElement textEl)
            {
                textEl.text = text;
                modifications.Add($"text='{text}'");
            }
            else if (text != null)
            {
                return new ErrorResponse($"Element '{elementName}' ({element.GetType().Name}) does not support text content.");
            }

            // Add CSS classes
            JToken addClassesToken = p.GetRaw("add_classes") ?? p.GetRaw("addClasses");
            if (addClassesToken is JArray addArr)
            {
                foreach (var cls in addArr)
                {
                    string className = cls.ToString();
                    if (!element.ClassListContains(className))
                    {
                        element.AddToClassList(className);
                        modifications.Add($"+class '{className}'");
                    }
                }
            }

            // Remove CSS classes
            JToken removeClassesToken = p.GetRaw("remove_classes") ?? p.GetRaw("removeClasses");
            if (removeClassesToken is JArray removeArr)
            {
                foreach (var cls in removeArr)
                {
                    string className = cls.ToString();
                    if (element.ClassListContains(className))
                    {
                        element.RemoveFromClassList(className);
                        modifications.Add($"-class '{className}'");
                    }
                }
            }

            // Toggle CSS classes
            JToken toggleClassesToken = p.GetRaw("toggle_classes") ?? p.GetRaw("toggleClasses");
            if (toggleClassesToken is JArray toggleArr)
            {
                foreach (var cls in toggleArr)
                {
                    string className = cls.ToString();
                    element.ToggleInClassList(className);
                    modifications.Add($"~class '{className}'");
                }
            }

            // Set inline styles
            JToken styleToken = p.GetRaw("style") ?? p.GetRaw("inline_style") ?? p.GetRaw("inlineStyle");
            if (styleToken is JObject styleObj)
            {
                ApplyInlineStyles(element, styleObj, modifications);
            }

            // Set enabled/disabled
            bool? enabled = p.GetNullableBool("enabled");
            if (enabled.HasValue)
            {
                element.SetEnabled(enabled.Value);
                modifications.Add($"enabled={enabled.Value}");
            }

            // Set visibility
            string visibility = p.Get("visible");
            if (visibility != null)
            {
                bool isVisible = visibility.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                                 visibility == "1";
                element.style.display = isVisible ? DisplayStyle.Flex : DisplayStyle.None;
                modifications.Add($"visible={isVisible}");
            }

            // Set tooltip
            string tooltip = p.Get("tooltip");
            if (tooltip != null)
            {
                element.tooltip = tooltip;
                modifications.Add($"tooltip='{tooltip}'");
            }

            if (modifications.Count == 0)
            {
                return new ErrorResponse("No modifications specified. Provide at least one of: text, add_classes, remove_classes, toggle_classes, style, enabled, visible, tooltip.");
            }

            return new SuccessResponse(
                $"Modified element '{elementName}' on {go.name}: {string.Join(", ", modifications)}",
                new
                {
                    gameObject = go.name,
                    elementName,
                    elementType = element.GetType().Name,
                    modifications,
                    currentClasses = new List<string>(element.GetClasses()),
                });
        }

        private static void ApplyInlineStyles(VisualElement element, JObject styleObj, List<string> modifications)
        {
            foreach (var prop in styleObj)
            {
                string key = prop.Key;
                JToken val = prop.Value;

                switch (key.ToLowerInvariant())
                {
                    case "backgroundcolor":
                    case "background-color":
                        if (ColorUtility.TryParseHtmlString(val.ToString(), out Color bgColor))
                        {
                            element.style.backgroundColor = bgColor;
                            modifications.Add($"backgroundColor={val}");
                        }
                        break;

                    case "color":
                        if (ColorUtility.TryParseHtmlString(val.ToString(), out Color fgColor))
                        {
                            element.style.color = fgColor;
                            modifications.Add($"color={val}");
                        }
                        break;

                    case "fontsize":
                    case "font-size":
                        element.style.fontSize = val.ToObject<float>();
                        modifications.Add($"fontSize={val}");
                        break;

                    case "width":
                        element.style.width = val.ToObject<float>();
                        modifications.Add($"width={val}");
                        break;

                    case "height":
                        element.style.height = val.ToObject<float>();
                        modifications.Add($"height={val}");
                        break;

                    case "opacity":
                        element.style.opacity = val.ToObject<float>();
                        modifications.Add($"opacity={val}");
                        break;

                    case "display":
                        if (Enum.TryParse<DisplayStyle>(val.ToString(), true, out var display))
                        {
                            element.style.display = display;
                            modifications.Add($"display={val}");
                        }
                        break;

                    case "visibility":
                        if (Enum.TryParse<Visibility>(val.ToString(), true, out var vis))
                        {
                            element.style.visibility = vis;
                            modifications.Add($"visibility={val}");
                        }
                        break;

                    case "flexgrow":
                    case "flex-grow":
                        element.style.flexGrow = val.ToObject<float>();
                        modifications.Add($"flexGrow={val}");
                        break;

                    case "flexshrink":
                    case "flex-shrink":
                        element.style.flexShrink = val.ToObject<float>();
                        modifications.Add($"flexShrink={val}");
                        break;

                    case "marginleft":
                    case "margin-left":
                        element.style.marginLeft = val.ToObject<float>();
                        modifications.Add($"marginLeft={val}");
                        break;

                    case "marginright":
                    case "margin-right":
                        element.style.marginRight = val.ToObject<float>();
                        modifications.Add($"marginRight={val}");
                        break;

                    case "margintop":
                    case "margin-top":
                        element.style.marginTop = val.ToObject<float>();
                        modifications.Add($"marginTop={val}");
                        break;

                    case "marginbottom":
                    case "margin-bottom":
                        element.style.marginBottom = val.ToObject<float>();
                        modifications.Add($"marginBottom={val}");
                        break;

                    case "paddingleft":
                    case "padding-left":
                        element.style.paddingLeft = val.ToObject<float>();
                        modifications.Add($"paddingLeft={val}");
                        break;

                    case "paddingright":
                    case "padding-right":
                        element.style.paddingRight = val.ToObject<float>();
                        modifications.Add($"paddingRight={val}");
                        break;

                    case "paddingtop":
                    case "padding-top":
                        element.style.paddingTop = val.ToObject<float>();
                        modifications.Add($"paddingTop={val}");
                        break;

                    case "paddingbottom":
                    case "padding-bottom":
                        element.style.paddingBottom = val.ToObject<float>();
                        modifications.Add($"paddingBottom={val}");
                        break;

                    case "borderradius":
                    case "border-radius":
                        float radius = val.ToObject<float>();
                        element.style.borderTopLeftRadius = radius;
                        element.style.borderTopRightRadius = radius;
                        element.style.borderBottomLeftRadius = radius;
                        element.style.borderBottomRightRadius = radius;
                        modifications.Add($"borderRadius={val}");
                        break;

                    default:
                        modifications.Add($"[skipped] {key} (unsupported inline style)");
                        break;
                }
            }
        }

        private static bool? GetNullableBool(this ToolParams p, string key)
        {
            var raw = p.GetRaw(key);
            if (raw == null) return null;
            if (raw.Type == JTokenType.Boolean) return raw.ToObject<bool>();
            string s = raw.ToString();
            if (bool.TryParse(s, out bool result)) return result;
            return null;
        }

        /// <summary>
        /// Finds the index right after the closing '>' of the root UXML element opening tag.
        /// Returns -1 if not found or if the root tag is self-closing.
        /// </summary>
        private static int FindUxmlBodyStart(string content)
        {
            int searchFrom = 0;
            while (true)
            {
                int idx = content.IndexOf("<ui:UXML", searchFrom, StringComparison.OrdinalIgnoreCase);
                if (idx < 0)
                    idx = content.IndexOf("<UXML", searchFrom, StringComparison.OrdinalIgnoreCase);
                if (idx < 0)
                    return -1;

                // Skip matches inside XML comments (<!-- ... -->)
                int commentStart = content.LastIndexOf("<!--", idx, StringComparison.Ordinal);
                if (commentStart >= 0)
                {
                    int commentEnd = content.IndexOf("-->", commentStart + 4, StringComparison.Ordinal);
                    if (commentEnd >= 0 && commentEnd + 3 > idx)
                    {
                        searchFrom = commentEnd + 3;
                        continue;
                    }
                }

                int closeTag = content.IndexOf('>', idx);
                if (closeTag < 0) return -1;
                // Self-closing tag cannot have children
                if (closeTag > 0 && content[closeTag - 1] == '/') return -1;

                return closeTag + 1;
            }
        }

        // ---- Helpers ----

        private static string EnsureUniqueFilePath(string path)
        {
            if (!File.Exists(path)) return path;
            string dir = Path.GetDirectoryName(path) ?? string.Empty;
            string baseName = Path.GetFileNameWithoutExtension(path);
            string ext = Path.GetExtension(path);
            int counter = 1;
            string candidate;
            do
            {
                candidate = Path.Combine(dir, $"{baseName}-{counter}{ext}").Replace('\\', '/');
                counter++;
            } while (File.Exists(candidate));
            return candidate;
        }

        private static string ColorToHex(Color c)
        {
            return $"#{ColorUtility.ToHtmlStringRGBA(c)}";
        }

        private static string GetDecodedContents(ToolParams p)
        {
            bool isEncoded = p.GetBool("contents_encoded") || p.GetBool("contentsEncoded");

            if (isEncoded)
            {
                string encoded = p.Get("encoded_contents") ?? p.Get("encodedContents");
                if (!string.IsNullOrEmpty(encoded))
                {
                    try
                    {
                        return Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
                    }
                    catch (FormatException ex)
                    {
                        throw new ArgumentException(
                            "Parameter 'encodedContents' must be valid base64 when 'contentsEncoded' is true.",
                            ex);
                    }
                }
            }

            return p.Get("contents");
        }
    }
}
