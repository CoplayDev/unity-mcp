using System;
using Newtonsoft.Json.Linq;
using MCPForUnity.Editor.Helpers;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace MCPForUnity.Editor.Tools.Animation
{
    internal static class AnimatorControl
    {
        public static object Play(JObject @params)
        {
            var go = ObjectResolver.ResolveGameObject(@params["target"], @params["searchMethod"]?.ToString());
            if (go == null)
                return new { success = false, message = "Target GameObject not found" };

            var animator = go.GetComponent<Animator>();
            if (animator == null)
                return new { success = false, message = $"No Animator component on '{go.name}'" };

            string stateName = @params["stateName"]?.ToString();
            if (string.IsNullOrEmpty(stateName))
                return new { success = false, message = "'stateName' is required" };

            int layer = @params["layer"]?.ToObject<int>() ?? -1;

            Undo.RecordObject(animator, "Play Animation State");
            animator.Play(stateName, layer);

            return new { success = true, message = $"Playing state '{stateName}' on '{go.name}'" };
        }

        public static object Crossfade(JObject @params)
        {
            var go = ObjectResolver.ResolveGameObject(@params["target"], @params["searchMethod"]?.ToString());
            if (go == null)
                return new { success = false, message = "Target GameObject not found" };

            var animator = go.GetComponent<Animator>();
            if (animator == null)
                return new { success = false, message = $"No Animator component on '{go.name}'" };

            string stateName = @params["stateName"]?.ToString();
            if (string.IsNullOrEmpty(stateName))
                return new { success = false, message = "'stateName' is required" };

            float duration = @params["duration"]?.ToObject<float>() ?? 0.25f;
            int layer = @params["layer"]?.ToObject<int>() ?? -1;

            Undo.RecordObject(animator, "Crossfade Animation State");
            animator.CrossFade(stateName, duration, layer);

            return new { success = true, message = $"Crossfading to '{stateName}' over {duration}s on '{go.name}'" };
        }

        public static object SetParameter(JObject @params)
        {
            var go = ObjectResolver.ResolveGameObject(@params["target"], @params["searchMethod"]?.ToString());
            if (go == null)
                return new { success = false, message = "Target GameObject not found" };

            var animator = go.GetComponent<Animator>();
            if (animator == null)
                return new { success = false, message = $"No Animator component on '{go.name}'" };

            string paramName = @params["parameterName"]?.ToString();
            if (string.IsNullOrEmpty(paramName))
                return new { success = false, message = "'parameterName' is required" };

            string paramType = @params["parameterType"]?.ToString()?.ToLowerInvariant();

            // Auto-detect type if not specified
            if (string.IsNullOrEmpty(paramType))
            {
                for (int i = 0; i < animator.parameterCount; i++)
                {
                    var p = animator.GetParameter(i);
                    if (p.name == paramName)
                    {
                        paramType = p.type.ToString().ToLowerInvariant();
                        break;
                    }
                }

                if (string.IsNullOrEmpty(paramType))
                    return new { success = false, message = $"Parameter '{paramName}' not found. Specify 'parameterType' explicitly or check the parameter name." };
            }

            JToken valueToken = @params["value"];

            // In Edit mode, runtime Animator.SetFloat/SetInteger/SetBool are no-ops because
            // the Animator graph isn't active. Instead, modify the controller asset's default
            // parameter values so changes actually persist.
            bool isPlaying = Application.isPlaying;

            if (isPlaying)
            {
                Undo.RecordObject(animator, $"Set Animator Parameter {paramName}");

                switch (paramType)
                {
                    case "float":
                        float fVal = valueToken?.ToObject<float>() ?? 0f;
                        animator.SetFloat(paramName, fVal);
                        return new { success = true, message = $"Set float '{paramName}' = {fVal}" };

                    case "int":
                    case "integer":
                        int iVal = valueToken?.ToObject<int>() ?? 0;
                        animator.SetInteger(paramName, iVal);
                        return new { success = true, message = $"Set int '{paramName}' = {iVal}" };

                    case "bool":
                    case "boolean":
                        bool bVal = valueToken?.ToObject<bool>() ?? false;
                        animator.SetBool(paramName, bVal);
                        return new { success = true, message = $"Set bool '{paramName}' = {bVal}" };

                    case "trigger":
                        animator.SetTrigger(paramName);
                        return new { success = true, message = $"Set trigger '{paramName}'" };

                    default:
                        return new { success = false, message = $"Unknown parameter type: {paramType}. Valid: float, int, bool, trigger" };
                }
            }
            else
            {
                // Edit mode: modify the AnimatorController asset's default parameter values
                var controller = animator.runtimeAnimatorController as AnimatorController;
                if (controller == null)
                    return new { success = false, message = $"No AnimatorController assigned to Animator on '{go.name}'. Cannot set parameter defaults in Edit mode." };

                var allParams = controller.parameters;
                int paramIndex = -1;
                for (int i = 0; i < allParams.Length; i++)
                {
                    if (allParams[i].name == paramName)
                    {
                        paramIndex = i;
                        break;
                    }
                }

                if (paramIndex < 0)
                    return new { success = false, message = $"Parameter '{paramName}' not found on controller '{controller.name}'." };

                Undo.RecordObject(controller, $"Set Parameter Default {paramName}");

                switch (paramType)
                {
                    case "float":
                        float fVal = valueToken?.ToObject<float>() ?? 0f;
                        allParams[paramIndex].defaultFloat = fVal;
                        controller.parameters = allParams;
                        EditorUtility.SetDirty(controller);
                        AssetDatabase.SaveAssets();
                        return new { success = true, message = $"Set float '{paramName}' = {fVal} (default value, Edit mode)" };

                    case "int":
                    case "integer":
                        int iVal = valueToken?.ToObject<int>() ?? 0;
                        allParams[paramIndex].defaultInt = iVal;
                        controller.parameters = allParams;
                        EditorUtility.SetDirty(controller);
                        AssetDatabase.SaveAssets();
                        return new { success = true, message = $"Set int '{paramName}' = {iVal} (default value, Edit mode)" };

                    case "bool":
                    case "boolean":
                        bool bVal = valueToken?.ToObject<bool>() ?? false;
                        allParams[paramIndex].defaultBool = bVal;
                        controller.parameters = allParams;
                        EditorUtility.SetDirty(controller);
                        AssetDatabase.SaveAssets();
                        return new { success = true, message = $"Set bool '{paramName}' = {bVal} (default value, Edit mode)" };

                    case "trigger":
                        return new { success = true, message = $"Trigger '{paramName}' noted (triggers are runtime-only, no default to set)" };

                    default:
                        return new { success = false, message = $"Unknown parameter type: {paramType}. Valid: float, int, bool, trigger" };
                }
            }
        }

        public static object SetSpeed(JObject @params)
        {
            var go = ObjectResolver.ResolveGameObject(@params["target"], @params["searchMethod"]?.ToString());
            if (go == null)
                return new { success = false, message = "Target GameObject not found" };

            var animator = go.GetComponent<Animator>();
            if (animator == null)
                return new { success = false, message = $"No Animator component on '{go.name}'" };

            float speed = @params["speed"]?.ToObject<float>() ?? 1f;

            Undo.RecordObject(animator, "Set Animator Speed");
            animator.speed = speed;

            return new { success = true, message = $"Set animator speed to {speed} on '{go.name}'" };
        }

        public static object SetEnabled(JObject @params)
        {
            var go = ObjectResolver.ResolveGameObject(@params["target"], @params["searchMethod"]?.ToString());
            if (go == null)
                return new { success = false, message = "Target GameObject not found" };

            var animator = go.GetComponent<Animator>();
            if (animator == null)
                return new { success = false, message = $"No Animator component on '{go.name}'" };

            bool enabled = @params["enabled"]?.ToObject<bool>() ?? true;

            Undo.RecordObject(animator, "Set Animator Enabled");
            animator.enabled = enabled;

            return new { success = true, message = $"Animator {(enabled ? "enabled" : "disabled")} on '{go.name}'" };
        }
    }
}
