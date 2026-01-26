using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEditor;

namespace MCPForUnity.Editor.Helpers
{
    /// <summary>
    /// Utility class for common Renderer property operations.
    /// Used by ManageVFX for ParticleSystem, LineRenderer, and TrailRenderer components.
    /// </summary>
    public static class RendererHelpers
    {
        /// <summary>
        /// Ensures a renderer has a material assigned. If not, auto-assigns a default material
        /// based on the render pipeline and component type.
        /// </summary>
        /// <param name="renderer">The renderer to check</param>
        public static void EnsureMaterial(Renderer renderer)
        {
            if (renderer == null || renderer.sharedMaterial != null)
            {
                return;
            }

            RenderPipelineUtility.VFXComponentType? componentType = null;
            if (renderer is ParticleSystemRenderer)
            {
                componentType = RenderPipelineUtility.VFXComponentType.ParticleSystem;
            }
            else if (renderer is LineRenderer)
            {
                componentType = RenderPipelineUtility.VFXComponentType.LineRenderer;
            }
            else if (renderer is TrailRenderer)
            {
                componentType = RenderPipelineUtility.VFXComponentType.TrailRenderer;
            }

            if (componentType.HasValue)
            {
                Material defaultMat = RenderPipelineUtility.GetOrCreateDefaultVFXMaterial(componentType.Value);
                if (defaultMat != null)
                {
                    Undo.RecordObject(renderer, "Assign default VFX material");
                    EditorUtility.SetDirty(renderer);
                    renderer.sharedMaterial = defaultMat;
                }
            }
        }

        /// <summary>
        /// Applies common Renderer properties (shadows, lighting, probes, sorting, rendering layer).
        /// Used by ParticleSetRenderer, LineSetProperties, TrailSetProperties.
        /// </summary>
        public static void ApplyCommonRendererProperties(Renderer renderer, JObject @params, List<string> changes)
        {
            // Shadows
            if (@params["shadowCastingMode"] != null && Enum.TryParse<UnityEngine.Rendering.ShadowCastingMode>(@params["shadowCastingMode"].ToString(), true, out var shadowMode)) 
            { renderer.shadowCastingMode = shadowMode; changes.Add("shadowCastingMode"); }
            if (@params["receiveShadows"] != null) { renderer.receiveShadows = @params["receiveShadows"].ToObject<bool>(); changes.Add("receiveShadows"); }
            // Note: shadowBias is only available on specific renderer types (e.g., ParticleSystemRenderer), not base Renderer
            
            // Lighting and probes
            if (@params["lightProbeUsage"] != null && Enum.TryParse<UnityEngine.Rendering.LightProbeUsage>(@params["lightProbeUsage"].ToString(), true, out var probeUsage)) 
            { renderer.lightProbeUsage = probeUsage; changes.Add("lightProbeUsage"); }
            if (@params["reflectionProbeUsage"] != null && Enum.TryParse<UnityEngine.Rendering.ReflectionProbeUsage>(@params["reflectionProbeUsage"].ToString(), true, out var reflectionUsage)) 
            { renderer.reflectionProbeUsage = reflectionUsage; changes.Add("reflectionProbeUsage"); }
            
            // Motion vectors
            if (@params["motionVectorGenerationMode"] != null && Enum.TryParse<MotionVectorGenerationMode>(@params["motionVectorGenerationMode"].ToString(), true, out var motionMode)) 
            { renderer.motionVectorGenerationMode = motionMode; changes.Add("motionVectorGenerationMode"); }
            
            // Sorting
            if (@params["sortingOrder"] != null) { renderer.sortingOrder = @params["sortingOrder"].ToObject<int>(); changes.Add("sortingOrder"); }
            if (@params["sortingLayerName"] != null) { renderer.sortingLayerName = @params["sortingLayerName"].ToString(); changes.Add("sortingLayerName"); }
            if (@params["sortingLayerID"] != null) { renderer.sortingLayerID = @params["sortingLayerID"].ToObject<int>(); changes.Add("sortingLayerID"); }
            
            // Rendering layer mask (for SRP)
            if (@params["renderingLayerMask"] != null) { renderer.renderingLayerMask = @params["renderingLayerMask"].ToObject<uint>(); changes.Add("renderingLayerMask"); }
        }

        /// <summary>
        /// Gets common Renderer properties for GetInfo methods.
        /// </summary>
        public static object GetCommonRendererInfo(Renderer renderer)
        {
            return new
            {
                shadowCastingMode = renderer.shadowCastingMode.ToString(),
                receiveShadows = renderer.receiveShadows,
                lightProbeUsage = renderer.lightProbeUsage.ToString(),
                reflectionProbeUsage = renderer.reflectionProbeUsage.ToString(),
                sortingOrder = renderer.sortingOrder,
                sortingLayerName = renderer.sortingLayerName,
                renderingLayerMask = renderer.renderingLayerMask
            };
        }


        /// <summary>
        /// Sets width properties for LineRenderer or TrailRenderer.
        /// </summary>
        /// <param name="params">JSON parameters containing width, startWidth, endWidth, widthCurve, widthMultiplier</param>
        /// <param name="changes">List to track changed properties</param>
        /// <param name="setStartWidth">Action to set start width</param>
        /// <param name="setEndWidth">Action to set end width</param>
        /// <param name="setWidthCurve">Action to set width curve</param>
        /// <param name="setWidthMultiplier">Action to set width multiplier</param>
        /// <param name="parseAnimationCurve">Function to parse animation curve from JToken</param>
        public static void ApplyWidthProperties(JObject @params, List<string> changes,
            Action<float> setStartWidth, Action<float> setEndWidth,
            Action<AnimationCurve> setWidthCurve, Action<float> setWidthMultiplier,
            Func<JToken, float, AnimationCurve> parseAnimationCurve)
        {
            if (@params["width"] != null) 
            { 
                float w = @params["width"].ToObject<float>(); 
                setStartWidth(w); 
                setEndWidth(w); 
                changes.Add("width"); 
            }
            if (@params["startWidth"] != null) { setStartWidth(@params["startWidth"].ToObject<float>()); changes.Add("startWidth"); }
            if (@params["endWidth"] != null) { setEndWidth(@params["endWidth"].ToObject<float>()); changes.Add("endWidth"); }
            if (@params["widthCurve"] != null) { setWidthCurve(parseAnimationCurve(@params["widthCurve"], 1f)); changes.Add("widthCurve"); }
            if (@params["widthMultiplier"] != null) { setWidthMultiplier(@params["widthMultiplier"].ToObject<float>()); changes.Add("widthMultiplier"); }
        }

        /// <summary>
        /// Sets color properties for LineRenderer or TrailRenderer.
        /// </summary>
        /// <param name="params">JSON parameters containing color, startColor, endColor, gradient</param>
        /// <param name="changes">List to track changed properties</param>
        /// <param name="setStartColor">Action to set start color</param>
        /// <param name="setEndColor">Action to set end color</param>
        /// <param name="setGradient">Action to set gradient</param>
        /// <param name="parseColor">Function to parse color from JToken</param>
        /// <param name="parseGradient">Function to parse gradient from JToken</param>
        /// <param name="fadeEndAlpha">If true, sets end color alpha to 0 when using single color</param>
        public static void ApplyColorProperties(JObject @params, List<string> changes,
            Action<Color> setStartColor, Action<Color> setEndColor,
            Action<Gradient> setGradient,
            Func<JToken, Color> parseColor, Func<JToken, Gradient> parseGradient,
            bool fadeEndAlpha = false)
        {
            if (@params["color"] != null) 
            { 
                Color c = parseColor(@params["color"]); 
                setStartColor(c); 
                setEndColor(fadeEndAlpha ? new Color(c.r, c.g, c.b, 0f) : c); 
                changes.Add("color"); 
            }
            if (@params["startColor"] != null) { setStartColor(parseColor(@params["startColor"])); changes.Add("startColor"); }
            if (@params["endColor"] != null) { setEndColor(parseColor(@params["endColor"])); changes.Add("endColor"); }
            if (@params["gradient"] != null) { setGradient(parseGradient(@params["gradient"])); changes.Add("gradient"); }
        }


        /// <summary>
        /// Sets material for a Renderer.
        /// </summary>
        /// <param name="renderer">The renderer to set material on</param>
        /// <param name="params">JSON parameters containing materialPath</param>
        /// <param name="undoName">Name for the undo operation</param>
        /// <param name="findMaterial">Function to find material by path</param>
        /// <param name="autoAssignDefault">If true, auto-assigns default material when materialPath is not provided</param>
        public static object SetRendererMaterial(Renderer renderer, JObject @params, string undoName, Func<string, Material> findMaterial, bool autoAssignDefault = true)
        {
            if (renderer == null) return new { success = false, message = "Renderer not found" };

            string path = @params["materialPath"]?.ToString();

            if (string.IsNullOrEmpty(path))
            {
                if (!autoAssignDefault)
                {
                    return new { success = false, message = "materialPath required" };
                }

                RenderPipelineUtility.VFXComponentType? componentType = null;
                if (renderer is ParticleSystemRenderer)
                {
                    componentType = RenderPipelineUtility.VFXComponentType.ParticleSystem;
                }
                else if (renderer is LineRenderer)
                {
                    componentType = RenderPipelineUtility.VFXComponentType.LineRenderer;
                }
                else if (renderer is TrailRenderer)
                {
                    componentType = RenderPipelineUtility.VFXComponentType.TrailRenderer;
                }

                if (componentType.HasValue)
                {
                    Material defaultMat = RenderPipelineUtility.GetOrCreateDefaultVFXMaterial(componentType.Value);
                    if (defaultMat != null)
                    {
                        Undo.RecordObject(renderer, undoName);
                        renderer.sharedMaterial = defaultMat;
                        EditorUtility.SetDirty(renderer);
                        return new { success = true, message = $"Auto-assigned default material: {defaultMat.name}" };
                    }
                }

                return new { success = false, message = "materialPath required" };
            }

            Material mat = findMaterial(path);
            if (mat == null) return new { success = false, message = $"Material not found: {path}" };

            Undo.RecordObject(renderer, undoName);
            renderer.sharedMaterial = mat;
            EditorUtility.SetDirty(renderer);

            return new { success = true, message = $"Set material to {mat.name}" };
        }


        /// <summary>
        /// Applies Line/Trail specific properties (loop, alignment, textureMode, etc.).
        /// </summary>
        public static void ApplyLineTrailProperties(JObject @params, List<string> changes,
            Action<bool> setLoop, Action<bool> setUseWorldSpace,
            Action<int> setNumCornerVertices, Action<int> setNumCapVertices,
            Action<LineAlignment> setAlignment, Action<LineTextureMode> setTextureMode,
            Action<bool> setGenerateLightingData)
        {
            if (@params["loop"] != null && setLoop != null) { setLoop(@params["loop"].ToObject<bool>()); changes.Add("loop"); }
            if (@params["useWorldSpace"] != null && setUseWorldSpace != null) { setUseWorldSpace(@params["useWorldSpace"].ToObject<bool>()); changes.Add("useWorldSpace"); }
            if (@params["numCornerVertices"] != null && setNumCornerVertices != null) { setNumCornerVertices(@params["numCornerVertices"].ToObject<int>()); changes.Add("numCornerVertices"); }
            if (@params["numCapVertices"] != null && setNumCapVertices != null) { setNumCapVertices(@params["numCapVertices"].ToObject<int>()); changes.Add("numCapVertices"); }
            if (@params["alignment"] != null && setAlignment != null && Enum.TryParse<LineAlignment>(@params["alignment"].ToString(), true, out var align)) { setAlignment(align); changes.Add("alignment"); }
            if (@params["textureMode"] != null && setTextureMode != null && Enum.TryParse<LineTextureMode>(@params["textureMode"].ToString(), true, out var texMode)) { setTextureMode(texMode); changes.Add("textureMode"); }
            if (@params["generateLightingData"] != null && setGenerateLightingData != null) { setGenerateLightingData(@params["generateLightingData"].ToObject<bool>()); changes.Add("generateLightingData"); }
        }

    }
}

