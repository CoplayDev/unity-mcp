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
                // DefaultControls.Resources are essentially sprites/fonts. 
                // Passing null-initialized struct usually triggers Unity's internal defaults.
            }
            return s_StandardResources;
        }

        public static object HandleCommand(JObject @params)
        {
            string action = @params["action"]?.ToString();
            if (string.IsNullOrEmpty(action))
            {
                return new ErrorResponse("Action parameter is required for 'manage_ugui' tool.");
            }

            switch (action.ToLowerInvariant())
            {
                case "create_element":
                    return CreateElement(@params);
                case "set_layout":
                    return SetLayout(@params);
                case "ensure_canvas":
                    return EnsureCanvas(@params);
                default:
                    return new ErrorResponse($"Unknown action '{action}' for 'manage_ugui' tool.");
            }
        }

        private static object CreateElement(JObject @params)
        {
            string type = @params["type"]?.ToString();
            string name = @params["name"]?.ToString();
            JToken parentToken = @params["parent"];

            if (string.IsNullOrEmpty(type))
            {
                return new ErrorResponse("'type' is required (e.g., Button, Image, ScrollView).");
            }

            // 1. Find Parent
            GameObject parentGo = null;
            if (parentToken != null)
            {
                parentGo = MCPForUnity.Editor.Tools.GameObjects.ManageGameObjectCommon.FindObjectInternal(parentToken, "by_id_or_name_or_path");
            }

            // 2. Ensure Canvas if no parent or parent is not UI
            if (parentGo == null || parentGo.GetComponentInParent<Canvas>() == null)
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
                    default:
                        return new ErrorResponse($"Unsupported UI type '{type}'. Valid types: Button, Image, Text, ScrollView, Slider, Toggle, Panel, Dropdown, InputField, Scrollbar, RawImage.");
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

            Undo.RegisterCreatedObjectUndo(uiGo, $"Create UI {type}");
            Selection.activeGameObject = uiGo;

            return new SuccessResponse($"Created UI {type} '{uiGo.name}' successfully.", GameObjectSerializer.GetGameObjectData(uiGo));
        }

        private static object SetLayout(JObject @params)
        {
            JToken targetToken = @params["target"];
            GameObject targetGo = MCPForUnity.Editor.Tools.GameObjects.ManageGameObjectCommon.FindObjectInternal(targetToken, "by_id_or_name_or_path");
            
            if (targetGo == null) return new ErrorResponse("Target UI element not found.");
            
            RectTransform rt = targetGo.GetComponent<RectTransform>();
            if (rt == null) return new ErrorResponse("Target element does not have a RectTransform.");

            Undo.RecordObject(rt, "Set UI Layout");

            string preset = @params["anchorPreset"]?.ToString()?.ToLowerInvariant();
            if (!string.IsNullOrEmpty(preset))
            {
                ApplyAnchorPreset(rt, preset);
            }

            Vector2? sizeDelta = VectorParsing.ParseVector2(@params["sizeDelta"]);
            if (sizeDelta.HasValue) rt.sizeDelta = sizeDelta.Value;

            Vector2? pos = VectorParsing.ParseVector2(@params["anchoredPosition"]);
            if (pos.HasValue) rt.anchoredPosition = pos.Value;

            float? pivotX = @params["pivotX"]?.ToObject<float>();
            float? pivotY = @params["pivotY"]?.ToObject<float>();
            if (pivotX.HasValue || pivotY.HasValue)
            {
                rt.pivot = new Vector2(pivotX ?? rt.pivot.x, pivotY ?? rt.pivot.y);
            }

            EditorUtility.SetDirty(rt);
            return new SuccessResponse($"Layout updated for '{targetGo.name}'.", GameObjectSerializer.GetGameObjectData(targetGo));
        }

        private static object EnsureCanvas(JObject @params)
        {
            GameObject canvasGo = EnsureCanvasInternal(null);
            return new SuccessResponse("Canvas and EventSystem ensured.", GameObjectSerializer.GetGameObjectData(canvasGo));
        }

        private static GameObject EnsureCanvasInternal(GameObject parent = null)
        {
            Canvas existing = null;
            if (parent != null) existing = parent.GetComponentInParent<Canvas>();
            
            if (existing == null) existing = UnityEngine.Object.FindAnyObjectByType<Canvas>();

            if (existing != null) return existing.gameObject;

            // Create Canvas
            GameObject canvasGo = new GameObject("Canvas");
            canvasGo.layer = LayerMask.NameToLayer("UI");
            Canvas canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasGo.AddComponent<CanvasScaler>();
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
                case "stretch_stretch": rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero; break;
                case "top_left": rt.anchorMin = new Vector2(0, 1); rt.anchorMax = new Vector2(0, 1); rt.pivot = new Vector2(0, 1); break;
                case "top_center": rt.anchorMin = new Vector2(0.5f, 1); rt.anchorMax = new Vector2(0.5f, 1); rt.pivot = new Vector2(0.5f, 1); break;
                case "top_right": rt.anchorMin = new Vector2(1, 1); rt.anchorMax = new Vector2(1, 1); rt.pivot = new Vector2(1, 1); break;
                case "middle_left": rt.anchorMin = new Vector2(0, 0.5f); rt.anchorMax = new Vector2(0, 0.5f); rt.pivot = new Vector2(0, 0.5f); break;
                case "middle_center": rt.anchorMin = new Vector2(0.5f, 0.5f); rt.anchorMax = new Vector2(0.5f, 0.5f); rt.pivot = new Vector2(0.5f, 0.5f); break;
                case "middle_right": rt.anchorMin = new Vector2(1, 0.5f); rt.anchorMax = new Vector2(1, 0.5f); rt.pivot = new Vector2(1, 0.5f); break;
                case "bottom_left": rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.zero; rt.pivot = Vector2.zero; break;
                case "bottom_center": rt.anchorMin = new Vector2(0.5f, 0); rt.anchorMax = new Vector2(0.5f, 0); rt.pivot = new Vector2(0.5f, 0); break;
                case "bottom_right": rt.anchorMin = new Vector2(1, 0); rt.anchorMax = new Vector2(1, 0); rt.pivot = new Vector2(1, 0); break;
                case "horiz_stretch_top": rt.anchorMin = new Vector2(0, 1); rt.anchorMax = Vector2.one; rt.pivot = new Vector2(0.5f, 1); break;
                case "horiz_stretch_middle": rt.anchorMin = new Vector2(0, 0.5f); rt.anchorMax = new Vector2(1, 0.5f); rt.pivot = new Vector2(0.5f, 0.5f); break;
                case "horiz_stretch_bottom": rt.anchorMin = Vector2.zero; rt.anchorMax = new Vector2(1, 0); rt.pivot = new Vector2(0.5f, 0); break;
                case "vert_stretch_left": rt.anchorMin = Vector2.zero; rt.anchorMax = new Vector2(0, 1); rt.pivot = new Vector2(0, 0.5f); break;
                case "vert_stretch_center": rt.anchorMin = new Vector2(0.5f, 0); rt.anchorMax = new Vector2(0.5f, 1); rt.pivot = new Vector2(0.5f, 0.5f); break;
                case "vert_stretch_right": rt.anchorMin = new Vector2(1, 0); rt.anchorMax = Vector2.one; rt.pivot = new Vector2(1, 0.5f); break;
            }
        }
    }
}
