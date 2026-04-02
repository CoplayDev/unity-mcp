#nullable disable
using System;
using System.Collections.Generic;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace MCPForUnity.Editor.Tools
{
    [McpForUnityTool("manage_ugui")]
    public static class ManageUGUI
    {
        private static DefaultControls.Resources s_StandardResources;

        private static DefaultControls.Resources GetStandardResources()
        {
            if (s_StandardResources.standard == null)
            {
                s_StandardResources = new DefaultControls.Resources();
            }
            return s_StandardResources;
        }

        public static object HandleCommand(JObject @params)
        {
            var p = new ToolParams(@params);
            string action = p.RequireString("action");

            switch (action.ToLowerInvariant())
            {
                case "create_element":
                    return CreateElement(p);
                case "set_layout":
                case "modify_element":
                    return ModifyElement(p);
                case "ensure_canvas":
                    return EnsureCanvas(p);
                default:
                    return new ErrorResponse($"Unknown action '{action}' for 'manage_ugui' tool.");
            }
        }

        private static object CreateElement(ToolParams p)
        {
            string type = p.RequireString("type");
            string name = p.GetString("name");
            JToken parentToken = p.GetToken("parent");

            // 1. Find Parent
            GameObject parentGo = null;
            if (parentToken != null)
            {
                parentGo = MCPForUnity.Editor.Tools.GameObjects.ManageGameObjectCommon.FindObjectInternal(parentToken, "by_id_or_name_or_path");
            }

            // 2. Ensure Canvas if no parent or parent is not UI
            if (parentGo == null || (parentGo.GetComponentInParent<Canvas>() == null && parentGo.GetComponent<Canvas>() == null))
            {
                McpLog.Info("[ManageUGUI] No Canvas found in parent hierarchy. Ensuring Canvas exists.");
                parentGo = EnsureCanvasInternal(parentGo);
            }

            // 3. Create using DefaultControls
            GameObject uiGo = null;
            var resources = GetStandardResources();

            try {
                switch (type.ToLowerInvariant())
                {
                    case "button": uiGo = DefaultControls.CreateButton(resources); break;
                    case "image": uiGo = DefaultControls.CreateImage(resources); break;
                    case "text": uiGo = DefaultControls.CreateText(resources); break;
                    case "scrollview": case "scroll_view": uiGo = DefaultControls.CreateScrollView(resources); break;
                    case "slider": uiGo = DefaultControls.CreateSlider(resources); break;
                    case "toggle": uiGo = DefaultControls.CreateToggle(resources); break;
                    case "panel": uiGo = DefaultControls.CreatePanel(resources); break;
                    case "dropdown": uiGo = DefaultControls.CreateDropdown(resources); break;
                    case "inputfield": case "input_field": uiGo = DefaultControls.CreateInputField(resources); break;
                    case "scrollbar": uiGo = DefaultControls.CreateScrollbar(resources); break;
                    case "rawimage": case "raw_image": uiGo = DefaultControls.CreateRawImage(resources); break;
                    case "empty":
                        uiGo = new GameObject(name ?? "UI Element", typeof(RectTransform));
                        break;
                    default:
                        return new ErrorResponse($"Unsupported UI type '{type}'. Valid types: Button, Image, Text, ScrollView, Slider, Toggle, Panel, Dropdown, InputField, Scrollbar, RawImage, Empty.");
                }
            } catch (Exception e) {
                return new ErrorResponse($"Internal error creating {type}: {e.Message}");
            }

            if (uiGo == null)
            {
                return new ErrorResponse($"Failed to create UI element of type '{type}'.");
            }

            if (!string.IsNullOrEmpty(name))
            {
                uiGo.name = name;
            }

            // 4. Parenting and Reset
            uiGo.transform.SetParent(parentGo.transform, false);
            uiGo.transform.localScale = Vector3.one;
            uiGo.transform.localPosition = Vector3.zero;

            // 5. Apply Anchor Preset if provided or default for Panel
            string preset = p.GetString("anchor_preset", "anchorPreset")?.ToLowerInvariant();
            if (string.IsNullOrEmpty(preset) && type.ToLowerInvariant() == "panel")
            {
                preset = "stretch_stretch";
            }
            
            if (!string.IsNullOrEmpty(preset))
            {
                ApplyAnchorPreset(uiGo.GetComponent<RectTransform>(), preset);
            }

            // 6. Apply Visual Properties if provided
            ApplyVisualProperties(uiGo, p);

            // 7. Special case: Button stretching child text
            if (type.ToLowerInvariant() == "button")
            {
                // Find any text component (legacy or TMPro)
                var rtChildText = uiGo.GetComponentInChildren<Text>()?.rectTransform;
                if (rtChildText == null) {
                    var tmproType = Type.GetType("TMPro.TMP_Text, Unity.TextMeshPro") ?? Type.GetType("TMPro.TextMeshProUGUI, Unity.TextMeshPro");
                    if (tmproType != null) {
                        var tmpro = uiGo.GetComponentInChildren(tmproType);
                        if (tmpro != null) rtChildText = tmpro.GetComponent<RectTransform>();
                    }
                }

                if (rtChildText != null) {
                    ApplyAnchorPreset(rtChildText, "stretch_stretch");
                }
            }

            Undo.RegisterCreatedObjectUndo(uiGo, $"Create UI {type}");
            Selection.activeGameObject = uiGo;

            return new SuccessResponse($"Created UI {type} '{uiGo.name}' successfully.", GameObjectSerializer.GetGameObjectData(uiGo));
        }

        private static object ModifyElement(ToolParams p)
        {
            JToken targetToken = p.GetToken("target");
            GameObject targetGo = MCPForUnity.Editor.Tools.GameObjects.ManageGameObjectCommon.FindObjectInternal(targetToken, "by_id_or_name_or_path");
            
            if (targetGo == null) return new ErrorResponse("Target UI element not found.");
            
            RectTransform rt = targetGo.GetComponent<RectTransform>();
            if (rt == null) return new ErrorResponse("Target element does not have a RectTransform.");

            Undo.RecordObject(targetGo, "Modify UGUI Element");
            Undo.RecordObject(rt, "Modify UI Layout");

            // Apply Layout
            string preset = p.GetString("anchor_preset", "anchorPreset")?.ToLowerInvariant();
            if (!string.IsNullOrEmpty(preset))
            {
                ApplyAnchorPreset(rt, preset);
            }

            Vector2? sizeDelta = VectorParsing.ParseVector2(p.GetToken("size_delta", "sizeDelta"));
            if (sizeDelta.HasValue) rt.sizeDelta = sizeDelta.Value;

            Vector2? pos = VectorParsing.ParseVector2(p.GetToken("anchored_position", "anchoredPosition"));
            if (pos.HasValue) rt.anchoredPosition = pos.Value;

            float? pivotX = p.GetFloat("pivot_x", "pivotX");
            float? pivotY = p.GetFloat("pivot_y", "pivotY");
            if (pivotX.HasValue || pivotY.HasValue)
            {
                rt.pivot = new Vector2(pivotX ?? rt.pivot.x, pivotY ?? rt.pivot.y);
            }

            // Apply Visuals
            ApplyVisualProperties(targetGo, p);

            EditorUtility.SetDirty(targetGo);
            EditorUtility.SetDirty(rt);
            return new SuccessResponse($"Element '{targetGo.name}' updated successfully.", GameObjectSerializer.GetGameObjectData(targetGo));
        }

        private static void ApplyVisualProperties(GameObject go, ToolParams p)
        {
            // Image / RawImage / Panel properties
            Image img = go.GetComponent<Image>();
            RawImage rawImg = go.GetComponent<RawImage>();
            
            if (img != null)
            {
                string spritePath = p.GetString("sprite");
                if (!string.IsNullOrEmpty(spritePath))
                {
                    img.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(AssetPathUtility.SanitizeAssetPath(spritePath));
                }
                
                Color? color = ParseColor(p.GetString("color"));
                if (color.HasValue) img.color = color.Value;
                
                bool? raycast = p.GetBool("raycast_target", "raycastTarget");
                if (raycast.HasValue) img.raycastTarget = raycast.Value;
                
                bool? preserve = p.GetBool("preserve_aspect", "preserveAspect");
                if (preserve.HasValue) img.preserveAspect = preserve.Value;
            }
            else if (rawImg != null)
            {
                string texPath = p.GetString("texture");
                if (!string.IsNullOrEmpty(texPath))
                {
                    rawImg.texture = AssetDatabase.LoadAssetAtPath<Texture>(AssetPathUtility.SanitizeAssetPath(texPath));
                }
                
                Color? color = ParseColor(p.GetString("color"));
                if (color.HasValue) rawImg.color = color.Value;
            }

            // Text properties
            string text = p.GetString("text");
            int? fontSize = p.GetInt("font_size", "fontSize");
            string fontPath = p.GetString("font");
            string align = p.GetString("alignment");
            Color? textColor = ParseColor(p.GetString("color"));

            // Legacy Text
            Text txt = go.GetComponent<Text>();
            if (txt != null)
            {
                if (text != null) txt.text = text;
                if (fontSize.HasValue) txt.fontSize = fontSize.Value;
                if (!string.IsNullOrEmpty(fontPath))
                {
                    txt.font = AssetDatabase.LoadAssetAtPath<Font>(AssetPathUtility.SanitizeAssetPath(fontPath));
                }
                if (!string.IsNullOrEmpty(align))
                {
                    if (Enum.TryParse<TextAnchor>(align, true, out var result))
                        txt.alignment = result;
                }
                if (textColor.HasValue) txt.color = textColor.Value;
            }

            // TextMeshPro support via reflection
            var tmproType = Type.GetType("TMPro.TextMeshProUGUI, Unity.TextMeshPro");
            if (tmproType != null)
            {
                var tmpro = go.GetComponent(tmproType);
                if (tmpro != null)
                {
                    if (text != null) SetPropertyValue(tmpro, "text", text);
                    if (fontSize.HasValue) SetPropertyValue(tmpro, "fontSize", (float)fontSize.Value);
                    if (textColor.HasValue) SetPropertyValue(tmpro, "color", textColor.Value);
                    if (!string.IsNullOrEmpty(align))
                    {
                        // Alignment in TMPro is a different enum (TMP_TextAlignmentOptions)
                        // For simplicity, we'll try to map common ones or just skip for now
                    }
                }
            }
        }

        private static void SetPropertyValue(object obj, string propertyName, object value)
        {
            var prop = obj.GetType().GetProperty(propertyName);
            if (prop != null && prop.CanWrite)
            {
                prop.SetValue(obj, value);
            }
        }

        private static Color? ParseColor(string hex)
        {
            if (string.IsNullOrEmpty(hex)) return null;
            if (ColorUtility.TryParseHtmlString(hex, out Color color)) return color;
            if (ColorUtility.TryParseHtmlString("#" + hex, out color)) return color;
            return null;
        }

        private static object EnsureCanvas(ToolParams p)
        {
            GameObject canvasGo = EnsureCanvasInternal(null);
            return new SuccessResponse("Canvas and EventSystem ensured.", GameObjectSerializer.GetGameObjectData(canvasGo));
        }

        private static GameObject EnsureCanvasInternal(GameObject parent = null)
        {
            Canvas existing = null;
            if (parent != null) existing = parent.GetComponentInParent<Canvas>();
            
            if (existing == null) 
            {
                // Intelligent Search: Prioritize "Main" or active Canvases
                var allCanvases = UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None);
                
                Canvas bestMatch = null;
                int bestScore = -1;

                foreach (var c in allCanvases)
                {
                    if (!c.gameObject.activeInHierarchy) continue;

                    int score = 0;
                    if (c.name.Contains("Main", StringComparison.OrdinalIgnoreCase)) score += 100;
                    if (c.name.Contains("UI", StringComparison.OrdinalIgnoreCase)) score += 50;
                    score += c.transform.childCount;

                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestMatch = c;
                    }
                }
                existing = bestMatch;
            }

            if (existing != null) return existing.gameObject;

            // Create Canvas
            GameObject canvasGo = new GameObject("Canvas");
            canvasGo.layer = LayerMask.NameToLayer("UI");
            Canvas canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            
            CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            canvasGo.AddComponent<GraphicRaycaster>();
            Undo.RegisterCreatedObjectUndo(canvasGo, "Create Canvas");

            // Create EventSystem
            if (UnityEngine.Object.FindAnyObjectByType<EventSystem>() == null)
            {
                GameObject esGo = new GameObject("EventSystem");
                esGo.AddComponent<EventSystem>();
                esGo.AddComponent<StandaloneInputModule>();
                Undo.RegisterCreatedObjectUndo(esGo, "Create EventSystem");
            }

            return canvasGo;
        }

        private static void ApplyAnchorPreset(RectTransform rt, string preset)
        {
            switch (preset)
            {
                case "stretch_stretch": 
                    rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; 
                    rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero; 
                    break;
                case "top_left": rt.anchorMin = new Vector2(0, 1); rt.anchorMax = new Vector2(0, 1); rt.pivot = new Vector2(0, 1); break;
                case "top_center": rt.anchorMin = new Vector2(0.5f, 1); rt.anchorMax = new Vector2(0.5f, 1); rt.pivot = new Vector2(0.5f, 1); break;
                case "top_right": rt.anchorMin = new Vector2(1, 1); rt.anchorMax = new Vector2(1, 1); rt.pivot = new Vector2(1, 1); break;
                case "middle_left": rt.anchorMin = new Vector2(0, 0.5f); rt.anchorMax = new Vector2(0, 0.5f); rt.pivot = new Vector2(0, 0.5f); break;
                case "middle_center": rt.anchorMin = new Vector2(0.5f, 0.5f); rt.anchorMax = new Vector2(0.5f, 0.5f); rt.pivot = new Vector2(0.5f, 0.5f); break;
                case "middle_right": rt.anchorMin = new Vector2(1, 0.5f); rt.anchorMax = new Vector2(1, 0.5f); rt.pivot = new Vector2(1, 0.5f); break;
                case "bottom_left": rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.zero; rt.pivot = Vector2.zero; break;
                case "bottom_center": rt.anchorMin = new Vector2(0.5f, 0); rt.anchorMax = new Vector2(0.5f, 0); rt.pivot = new Vector2(0.5f, 0); break;
                case "bottom_right": rt.anchorMin = new Vector2(1, 0); rt.anchorMax = new Vector2(1, 0); rt.pivot = new Vector2(1, 0); break;
                
                case "horiz_stretch_top": 
                    rt.anchorMin = new Vector2(0, 1); rt.anchorMax = Vector2.one; rt.pivot = new Vector2(0.5f, 1); 
                    rt.offsetMin = new Vector2(0, 0); rt.offsetMax = new Vector2(0, 0); // Logic: Full horiz stretch should have 0 horiz offsets
                    break;
                case "horiz_stretch_middle": 
                    rt.anchorMin = new Vector2(0, 0.5f); rt.anchorMax = new Vector2(1, 0.5f); rt.pivot = new Vector2(0.5f, 0.5f); 
                    rt.offsetMin = new Vector2(0, 0); rt.offsetMax = new Vector2(0, 0);
                    break;
                case "horiz_stretch_bottom": 
                    rt.anchorMin = Vector2.zero; rt.anchorMax = new Vector2(1, 0); rt.pivot = new Vector2(0.5f, 0); 
                    rt.offsetMin = new Vector2(0, 0); rt.offsetMax = new Vector2(0, 0);
                    break;
                case "vert_stretch_left": 
                    rt.anchorMin = Vector2.zero; rt.anchorMax = new Vector2(0, 1); rt.pivot = new Vector2(0, 0.5f); 
                    rt.offsetMin = new Vector2(0, 0); rt.offsetMax = new Vector2(0, 0);
                    break;
                case "vert_stretch_center": 
                    rt.anchorMin = new Vector2(0.5f, 0); rt.anchorMax = new Vector2(0.5f, 1); rt.pivot = new Vector2(0.5f, 0.5f); 
                    rt.offsetMin = new Vector2(0, 0); rt.offsetMax = new Vector2(0, 0);
                    break;
                case "vert_stretch_right": 
                    rt.anchorMin = new Vector2(1, 0); rt.anchorMax = Vector2.one; rt.pivot = new Vector2(1, 0.5f); 
                    rt.offsetMin = new Vector2(0, 0); rt.offsetMax = new Vector2(0, 0);
                    break;
            }
        }
    }
}
