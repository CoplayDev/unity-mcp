using System;
using Newtonsoft.Json.Linq;
using MCPForUnity.Editor.Helpers;
using UnityEditor;
using UnityEditor.Animations;

namespace MCPForUnity.Editor.Tools.Animation
{
    internal static class ControllerLayers
    {
        public static object AddLayer(JObject @params)
        {
            string controllerPath = @params["controllerPath"]?.ToString();
            if (string.IsNullOrEmpty(controllerPath))
                return new { success = false, message = "'controllerPath' is required" };

            controllerPath = AssetPathUtility.SanitizeAssetPath(controllerPath);
            if (controllerPath == null)
                return new { success = false, message = "Invalid asset path" };

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (controller == null)
                return new { success = false, message = $"AnimatorController not found at '{controllerPath}'" };

            string layerName = @params["layerName"]?.ToString();
            if (string.IsNullOrEmpty(layerName))
                return new { success = false, message = "'layerName' is required" };

            float weight = @params["weight"]?.ToObject<float>() ?? 1f;
            string blendingModeStr = @params["blendingMode"]?.ToString()?.ToLowerInvariant() ?? "override";

            AnimatorLayerBlendingMode blendingMode = blendingModeStr == "additive"
                ? AnimatorLayerBlendingMode.Additive
                : AnimatorLayerBlendingMode.Override;

            Undo.RecordObject(controller, "Add Layer");
            controller.AddLayer(layerName);

            var layers = controller.layers;
            var newLayer = layers[layers.Length - 1];
            newLayer.defaultWeight = weight;
            newLayer.blendingMode = blendingMode;
            layers[layers.Length - 1] = newLayer;
            controller.layers = layers;

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();

            return new
            {
                success = true,
                message = $"Added layer '{layerName}' to '{controllerPath}'",
                data = new
                {
                    controllerPath,
                    layerName,
                    layerIndex = layers.Length - 1,
                    weight,
                    blendingMode = blendingMode.ToString()
                }
            };
        }

        public static object RemoveLayer(JObject @params)
        {
            string controllerPath = @params["controllerPath"]?.ToString();
            if (string.IsNullOrEmpty(controllerPath))
                return new { success = false, message = "'controllerPath' is required" };

            controllerPath = AssetPathUtility.SanitizeAssetPath(controllerPath);
            if (controllerPath == null)
                return new { success = false, message = "Invalid asset path" };

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (controller == null)
                return new { success = false, message = $"AnimatorController not found at '{controllerPath}'" };

            int? layerIndex = @params["layerIndex"]?.ToObject<int?>();
            string layerName = @params["layerName"]?.ToString();

            if (!layerIndex.HasValue && string.IsNullOrEmpty(layerName))
                return new { success = false, message = "Either 'layerIndex' or 'layerName' is required" };

            var layers = controller.layers;
            if (layerIndex.HasValue)
            {
                if (layerIndex.Value < 0 || layerIndex.Value >= layers.Length)
                    return new { success = false, message = $"Layer index {layerIndex.Value} out of range (0-{layers.Length - 1})" };

                if (layerIndex.Value == 0)
                    return new { success = false, message = "Cannot remove base layer (index 0)" };

                layerName = layers[layerIndex.Value].name;
            }
            else
            {
                layerIndex = -1;
                for (int i = 0; i < layers.Length; i++)
                {
                    if (layers[i].name == layerName)
                    {
                        layerIndex = i;
                        break;
                    }
                }

                if (layerIndex.Value < 0)
                    return new { success = false, message = $"Layer '{layerName}' not found" };

                if (layerIndex.Value == 0)
                    return new { success = false, message = $"Cannot remove base layer '{layerName}'" };
            }

            Undo.RecordObject(controller, "Remove Layer");
            controller.RemoveLayer(layerIndex.Value);

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();

            return new
            {
                success = true,
                message = $"Removed layer '{layerName}' from '{controllerPath}'",
                data = new
                {
                    controllerPath,
                    layerName,
                    layerIndex = layerIndex.Value
                }
            };
        }

        public static object SetLayerWeight(JObject @params)
        {
            string controllerPath = @params["controllerPath"]?.ToString();
            if (string.IsNullOrEmpty(controllerPath))
                return new { success = false, message = "'controllerPath' is required" };

            controllerPath = AssetPathUtility.SanitizeAssetPath(controllerPath);
            if (controllerPath == null)
                return new { success = false, message = "Invalid asset path" };

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (controller == null)
                return new { success = false, message = $"AnimatorController not found at '{controllerPath}'" };

            int? layerIndex = @params["layerIndex"]?.ToObject<int?>();
            string layerName = @params["layerName"]?.ToString();

            if (!layerIndex.HasValue && string.IsNullOrEmpty(layerName))
                return new { success = false, message = "Either 'layerIndex' or 'layerName' is required" };

            float weight = @params["weight"]?.ToObject<float>() ?? 1f;

            var layers = controller.layers;
            if (layerIndex.HasValue)
            {
                if (layerIndex.Value < 0 || layerIndex.Value >= layers.Length)
                    return new { success = false, message = $"Layer index {layerIndex.Value} out of range (0-{layers.Length - 1})" };

                layerName = layers[layerIndex.Value].name;
            }
            else
            {
                layerIndex = -1;
                for (int i = 0; i < layers.Length; i++)
                {
                    if (layers[i].name == layerName)
                    {
                        layerIndex = i;
                        break;
                    }
                }

                if (layerIndex.Value < 0)
                    return new { success = false, message = $"Layer '{layerName}' not found" };
            }

            Undo.RecordObject(controller, "Set Layer Weight");
            var layer = layers[layerIndex.Value];
            layer.defaultWeight = weight;
            layers[layerIndex.Value] = layer;
            controller.layers = layers;

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();

            return new
            {
                success = true,
                message = $"Set layer '{layerName}' weight to {weight}",
                data = new
                {
                    controllerPath,
                    layerName,
                    layerIndex = layerIndex.Value,
                    weight
                }
            };
        }
    }
}
