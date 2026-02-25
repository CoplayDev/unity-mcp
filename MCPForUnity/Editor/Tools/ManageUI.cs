using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using MCPForUnity.Editor.Helpers;
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

                    case "get_visual_tree":
                        return GetVisualTree(@params);

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

            string contents = GetDecodedContents(p);
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

            string contents = GetDecodedContents(p);
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
            string panelSettingsPath = p.Get("panel_settings");
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

            string scaleMode = p.Get("scale_mode");
            JToken refResToken = p.GetRaw("reference_resolution");

            var ps = CreateDefaultPanelSettings(path);
            if (ps == null)
            {
                return new ErrorResponse("Failed to create PanelSettings asset.");
            }

            if (!string.IsNullOrEmpty(scaleMode))
            {
                if (Enum.TryParse<PanelScaleMode>(scaleMode, true, out var mode))
                {
                    ps.scaleMode = mode;
                }
            }

            if (refResToken is JObject refRes)
            {
                int w = refRes["width"]?.ToObject<int>() ?? 1920;
                int h = refRes["height"]?.ToObject<int>() ?? 1080;
                ps.referenceResolution = new Vector2Int(w, h);
            }

            EditorUtility.SetDirty(ps);
            AssetDatabase.SaveAssets();

            return new SuccessResponse($"Created PanelSettings at {path}",
                new { path });
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
                    catch (FormatException)
                    {
                        return null;
                    }
                }
            }

            return p.Get("contents");
        }
    }
}
