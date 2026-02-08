using System;
using Newtonsoft.Json.Linq;
using MCPForUnity.Editor.Helpers;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace MCPForUnity.Editor.Tools.Animation
{
    internal static class ControllerBlendTrees
    {
        public static object CreateBlendTree1D(JObject @params)
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

            string stateName = @params["stateName"]?.ToString();
            if (string.IsNullOrEmpty(stateName))
                return new { success = false, message = "'stateName' is required" };

            string blendParameter = @params["blendParameter"]?.ToString();
            if (string.IsNullOrEmpty(blendParameter))
                return new { success = false, message = "'blendParameter' is required" };

            int layerIndex = @params["layerIndex"]?.ToObject<int>() ?? 0;

            var layers = controller.layers;
            if (layerIndex < 0 || layerIndex >= layers.Length)
                return new { success = false, message = $"Layer index {layerIndex} out of range (0-{layers.Length - 1})" };

            var stateMachine = layers[layerIndex].stateMachine;

            Undo.RecordObject(controller, "Create Blend Tree 1D");
            var state = stateMachine.AddState(stateName);
            var blendTree = new BlendTree
            {
                name = stateName,
                blendType = BlendTreeType.Simple1D,
                blendParameter = blendParameter,
                hideFlags = HideFlags.HideInHierarchy
            };

            AssetDatabase.AddObjectToAsset(blendTree, controller);
            state.motion = blendTree;

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();

            return new
            {
                success = true,
                message = $"Created 1D blend tree state '{stateName}' in '{controllerPath}'",
                data = new
                {
                    controllerPath,
                    stateName,
                    layerIndex,
                    blendParameter,
                    blendType = "Simple1D"
                }
            };
        }

        public static object CreateBlendTree2D(JObject @params)
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

            string stateName = @params["stateName"]?.ToString();
            if (string.IsNullOrEmpty(stateName))
                return new { success = false, message = "'stateName' is required" };

            string blendParameterX = @params["blendParameterX"]?.ToString();
            string blendParameterY = @params["blendParameterY"]?.ToString();
            if (string.IsNullOrEmpty(blendParameterX) || string.IsNullOrEmpty(blendParameterY))
                return new { success = false, message = "'blendParameterX' and 'blendParameterY' are required" };

            int layerIndex = @params["layerIndex"]?.ToObject<int>() ?? 0;
            string blendTypeStr = @params["blendType"]?.ToString()?.ToLowerInvariant() ?? "simpledirectional2d";

            BlendTreeType blendType = blendTypeStr switch
            {
                "freeformdirectional2d" => BlendTreeType.FreeformDirectional2D,
                "freeformcartesian2d" => BlendTreeType.FreeformCartesian2D,
                _ => BlendTreeType.SimpleDirectional2D
            };

            var layers = controller.layers;
            if (layerIndex < 0 || layerIndex >= layers.Length)
                return new { success = false, message = $"Layer index {layerIndex} out of range (0-{layers.Length - 1})" };

            var stateMachine = layers[layerIndex].stateMachine;

            Undo.RecordObject(controller, "Create Blend Tree 2D");
            var state = stateMachine.AddState(stateName);
            var blendTree = new BlendTree
            {
                name = stateName,
                blendType = blendType,
                blendParameter = blendParameterX,
                blendParameterY = blendParameterY,
                hideFlags = HideFlags.HideInHierarchy
            };

            AssetDatabase.AddObjectToAsset(blendTree, controller);
            state.motion = blendTree;

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();

            return new
            {
                success = true,
                message = $"Created 2D blend tree state '{stateName}' in '{controllerPath}'",
                data = new
                {
                    controllerPath,
                    stateName,
                    layerIndex,
                    blendParameterX,
                    blendParameterY,
                    blendType = blendType.ToString()
                }
            };
        }

        public static object AddBlendTreeChild(JObject @params)
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

            string stateName = @params["stateName"]?.ToString();
            if (string.IsNullOrEmpty(stateName))
                return new { success = false, message = "'stateName' is required" };

            string clipPath = @params["clipPath"]?.ToString();
            if (string.IsNullOrEmpty(clipPath))
                return new { success = false, message = "'clipPath' is required" };

            clipPath = AssetPathUtility.SanitizeAssetPath(clipPath);
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
            if (clip == null)
                return new { success = false, message = $"AnimationClip not found at '{clipPath}'" };

            int layerIndex = @params["layerIndex"]?.ToObject<int>() ?? 0;

            var layers = controller.layers;
            if (layerIndex < 0 || layerIndex >= layers.Length)
                return new { success = false, message = $"Layer index {layerIndex} out of range (0-{layers.Length - 1})" };

            var stateMachine = layers[layerIndex].stateMachine;
            AnimatorState state = null;
            foreach (var s in stateMachine.states)
            {
                if (s.state.name == stateName)
                {
                    state = s.state;
                    break;
                }
            }

            if (state == null)
                return new { success = false, message = $"State '{stateName}' not found in layer {layerIndex}" };

            if (!(state.motion is BlendTree blendTree))
                return new { success = false, message = $"State '{stateName}' does not have a BlendTree motion" };

            Undo.RecordObject(blendTree, "Add Blend Tree Child");

            if (blendTree.blendType == BlendTreeType.Simple1D)
            {
                float? threshold = @params["threshold"]?.ToObject<float?>();
                if (!threshold.HasValue)
                    return new { success = false, message = "'threshold' is required for 1D blend trees" };

                blendTree.AddChild(clip, threshold.Value);

                EditorUtility.SetDirty(blendTree);
                EditorUtility.SetDirty(controller);
                AssetDatabase.SaveAssets();

                return new
                {
                    success = true,
                    message = $"Added clip '{clip.name}' to blend tree '{stateName}' at threshold {threshold.Value}",
                    data = new
                    {
                        controllerPath,
                        stateName,
                        clipPath,
                        threshold = threshold.Value,
                        childCount = blendTree.children.Length
                    }
                };
            }
            else
            {
                JToken positionToken = @params["position"];
                if (positionToken == null || !(positionToken is JArray posArray) || posArray.Count < 2)
                    return new { success = false, message = "'position' is required for 2D blend trees as [x, y]" };

                float posX = posArray[0].ToObject<float>();
                float posY = posArray[1].ToObject<float>();
                Vector2 position = new Vector2(posX, posY);

                blendTree.AddChild(clip, position);

                EditorUtility.SetDirty(blendTree);
                EditorUtility.SetDirty(controller);
                AssetDatabase.SaveAssets();

                return new
                {
                    success = true,
                    message = $"Added clip '{clip.name}' to blend tree '{stateName}' at position ({posX}, {posY})",
                    data = new
                    {
                        controllerPath,
                        stateName,
                        clipPath,
                        position = new { x = posX, y = posY },
                        childCount = blendTree.children.Length
                    }
                };
            }
        }
    }
}
