using System;
using Newtonsoft.Json.Linq;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Runtime.Helpers;
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

            // Resolve the clip either by asset path OR by instanceId string handle. The
            // instanceId path is required for FBX sub-asset clips, which LoadAssetAtPath
            // (main-asset only) cannot reach — same motion-by-reference trick as SetStateProperties.
            string clipPath = @params["clipPath"]?.ToString();
            string clipInstanceId = @params["clipInstanceId"]?.ToString();
            AnimationClip clip = null;
            if (!string.IsNullOrEmpty(clipInstanceId))
            {
                clip = UnityObjectIdCompat.InstanceIDFromString(clipInstanceId) as AnimationClip;
                if (clip == null)
                    return new { success = false, message = $"AnimationClip not resolved from clipInstanceId '{clipInstanceId}'" };
            }
            else if (!string.IsNullOrEmpty(clipPath))
            {
                clipPath = AssetPathUtility.SanitizeAssetPath(clipPath);
                clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
                if (clip == null)
                    return new { success = false, message = $"AnimationClip not found at '{clipPath}'" };
            }
            else
            {
                return new { success = false, message = "'clipInstanceId' or 'clipPath' is required" };
            }

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

        // Resolves a state's BlendTree motion within a controller layer.
        private static BlendTree ResolveBlendTree(JObject @params, out object error)
        {
            error = null;
            string controllerPath = @params["controllerPath"]?.ToString();
            if (string.IsNullOrEmpty(controllerPath))
            { error = new { success = false, message = "'controllerPath' is required" }; return null; }

            controllerPath = AssetPathUtility.SanitizeAssetPath(controllerPath);
            if (controllerPath == null)
            { error = new { success = false, message = "Invalid asset path" }; return null; }

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (controller == null)
            { error = new { success = false, message = $"AnimatorController not found at '{controllerPath}'" }; return null; }

            string stateName = @params["stateName"]?.ToString();
            if (string.IsNullOrEmpty(stateName))
            { error = new { success = false, message = "'stateName' is required" }; return null; }

            int layerIndex = @params["layerIndex"]?.ToObject<int>() ?? 0;
            var layers = controller.layers;
            if (layerIndex < 0 || layerIndex >= layers.Length)
            { error = new { success = false, message = $"Layer index {layerIndex} out of range (0-{layers.Length - 1})" }; return null; }

            AnimatorState state = null;
            foreach (var s in layers[layerIndex].stateMachine.states)
                if (s.state.name == stateName) { state = s.state; break; }

            if (state == null)
            { error = new { success = false, message = $"State '{stateName}' not found in layer {layerIndex}" }; return null; }

            if (!(state.motion is BlendTree blendTree))
            { error = new { success = false, message = $"State '{stateName}' does not have a BlendTree motion" }; return null; }

            return blendTree;
        }

        public static object GetBlendTree(JObject @params)
        {
            var blendTree = ResolveBlendTree(@params, out var error);
            if (blendTree == null) return error;

            var children = blendTree.children;
            var childData = new object[children.Length];
            for (int i = 0; i < children.Length; i++)
            {
                var c = children[i];
                childData[i] = new
                {
                    index = i,
                    motionName = c.motion != null ? c.motion.name : null,
                    motionInstanceId = c.motion != null ? c.motion.GetInstanceIDString() : null,
                    threshold = c.threshold,
                    position = new { x = c.position.x, y = c.position.y },
                    timeScale = c.timeScale,
                    cycleOffset = c.cycleOffset,
                    mirror = c.mirror
                };
            }

            return new
            {
                success = true,
                message = $"Read {children.Length} blend tree child(ren) for '{blendTree.name}'",
                data = new
                {
                    name = blendTree.name,
                    blendType = blendTree.blendType.ToString(),
                    blendParameter = blendTree.blendParameter,
                    blendParameterY = blendTree.blendParameterY,
                    useAutomaticThresholds = blendTree.useAutomaticThresholds,
                    childCount = children.Length,
                    children = childData
                }
            };
        }

        public static object EditBlendTree(JObject @params)
        {
            var blendTree = ResolveBlendTree(@params, out var error);
            if (blendTree == null) return error;

            var edits = @params["children"] as JArray;
            if (edits == null || edits.Count == 0)
                return new { success = false, message = "'children' array is required: [{index, position:[x,y], threshold?, timeScale?, cycleOffset?, mirror?}]" };

            // ChildMotion is a struct; must reassign the whole array.
            var children = blendTree.children;
            Undo.RecordObject(blendTree, "Edit Blend Tree");

            int applied = 0;
            foreach (var token in edits)
            {
                if (!(token is JObject edit)) continue;
                int? idx = edit["index"]?.ToObject<int?>();
                if (!idx.HasValue || idx.Value < 0 || idx.Value >= children.Length)
                    return new { success = false, message = $"Child index {(idx.HasValue ? idx.Value.ToString() : "null")} out of range (0-{children.Length - 1})" };

                var c = children[idx.Value];

                if (edit["position"] is JArray pos && pos.Count >= 2)
                    c.position = new Vector2(pos[0].ToObject<float>(), pos[1].ToObject<float>());
                if (edit["threshold"] != null)
                    c.threshold = edit["threshold"].ToObject<float>();
                if (edit["timeScale"] != null)
                    c.timeScale = edit["timeScale"].ToObject<float>();
                if (edit["cycleOffset"] != null)
                    c.cycleOffset = edit["cycleOffset"].ToObject<float>();
                if (edit["mirror"] != null)
                    c.mirror = edit["mirror"].ToObject<bool>();

                children[idx.Value] = c;
                applied++;
            }

            blendTree.children = children;
            EditorUtility.SetDirty(blendTree);
            AssetDatabase.SaveAssets();

            return new
            {
                success = true,
                message = $"Applied {applied} edit(s) to blend tree '{blendTree.name}'",
                data = new { name = blendTree.name, edited = applied, childCount = children.Length }
            };
        }
    }
}
