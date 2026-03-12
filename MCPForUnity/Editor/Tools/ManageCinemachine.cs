#if UNITY_CINEMACHINE
using System;
using System.Collections.Generic;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using Unity.Cinemachine;

namespace MCPForUnity.Editor.Tools
{
    [McpForUnityTool("manage_cinemachine", AutoRegister = true)]
    public static class ManageCinemachine
    {
        public static object HandleCommand(JObject @params)
        {
            if (@params == null) return new ErrorResponse("Parameters cannot be null.");
            var p = new ToolParams(@params);
            var actionResult = p.GetRequired("action");
            if (!actionResult.IsSuccess) return new ErrorResponse(actionResult.ErrorMessage);
            string action = actionResult.Value.ToLowerInvariant();

            try
            {
                return action switch
                {
                    "list_vcams"    => ListVCams(p),
                    "get_vcam"      => GetVCam(p),
                    "set_vcam"      => SetVCam(p),
                    "get_brain"     => GetBrain(),
                    "set_priority"  => SetPriority(p),
                    "list_blends"   => ListBlends(),
                    _ => new ErrorResponse($"Unknown action: '{action}'.")
                };
            }
            catch (Exception e)
            {
                McpLog.Error($"[ManageCinemachine] Action '{action}' failed: {e}");
                return new ErrorResponse($"Internal error: {e.Message}");
            }
        }

        private static object ListVCams(ToolParams p)
        {
            var vcams = UnityEngine.Object.FindObjectsByType<CinemachineCamera>(FindObjectsSortMode.InstanceID);
            var pagination = PaginationRequest.FromParams(
                new JObject { ["page_size"] = p.GetInt("page_size"), ["cursor"] = p.GetInt("cursor") });

            var allItems = new List<Dictionary<string, object>>();
            foreach (var vcam in vcams)
            {
                allItems.Add(new Dictionary<string, object>
                {
                    ["name"] = vcam.gameObject.name,
                    ["instance_id"] = vcam.gameObject.GetInstanceID(),
                    ["priority"] = vcam.Priority.Value,
                    ["follow"] = vcam.Follow != null ? vcam.Follow.name : null,
                    ["look_at"] = vcam.LookAt != null ? vcam.LookAt.name : null,
                    ["enabled"] = vcam.enabled,
                });
            }

            var page = PaginationResponse<Dictionary<string, object>>.Create(allItems, pagination);
            return new SuccessResponse($"Found {vcams.Length} CinemachineCamera(s).", page);
        }

        private static object GetVCam(ToolParams p)
        {
            var targetResult = p.GetRequired("target");
            if (!targetResult.IsSuccess) return new ErrorResponse(targetResult.ErrorMessage);

            var go = ObjectResolver.ResolveGameObject(new JValue(targetResult.Value));
            if (go == null) return new ErrorResponse($"GameObject '{targetResult.Value}' not found.");

            var vcam = go.GetComponent<CinemachineCamera>();
            if (vcam == null) return new ErrorResponse($"No CinemachineCamera on '{go.name}'.");

            return new SuccessResponse($"CinemachineCamera '{go.name}'.", new Dictionary<string, object>
            {
                ["name"] = go.name,
                ["priority"] = vcam.Priority.Value,
                ["follow"] = vcam.Follow != null ? vcam.Follow.name : null,
                ["look_at"] = vcam.LookAt != null ? vcam.LookAt.name : null,
                ["lens_fov"] = Math.Round(vcam.Lens.FieldOfView, 4),
                ["lens_near_clip"] = Math.Round(vcam.Lens.NearClipPlane, 4),
                ["lens_far_clip"] = Math.Round(vcam.Lens.FarClipPlane, 4),
                ["blend_hint"] = vcam.BlendHint.ToString(),
                ["enabled"] = vcam.enabled,
            });
        }

        private static object SetVCam(ToolParams p)
        {
            var targetResult = p.GetRequired("target");
            if (!targetResult.IsSuccess) return new ErrorResponse(targetResult.ErrorMessage);

            var go = ObjectResolver.ResolveGameObject(new JValue(targetResult.Value));
            if (go == null) return new ErrorResponse($"GameObject '{targetResult.Value}' not found.");

            var vcam = go.GetComponent<CinemachineCamera>();
            if (vcam == null) return new ErrorResponse($"No CinemachineCamera on '{go.name}'.");

            var propsToken = p.GetRaw("properties");
            if (propsToken == null) return new ErrorResponse("'properties' parameter is required.");

            JObject props;
            if (propsToken.Type == JTokenType.String)
            {
                try { props = JObject.Parse(propsToken.ToString()); }
                catch { return new ErrorResponse("'properties' must be valid JSON."); }
            }
            else
            {
                props = propsToken as JObject;
                if (props == null) return new ErrorResponse("'properties' must be a JSON object.");
            }

            Undo.RecordObject(vcam, "MCP SetVCam");

            if (props["priority"] != null)
                vcam.Priority = new PrioritySettings { Value = props["priority"].ToObject<int>() };
            if (props["enabled"] != null) vcam.enabled = props["enabled"].ToObject<bool>();

            EditorUtility.SetDirty(vcam);
            return new SuccessResponse($"CinemachineCamera '{go.name}' updated.");
        }

        private static object GetBrain()
        {
            var brain = UnityEngine.Object.FindFirstObjectByType<CinemachineBrain>();
            if (brain == null) return new ErrorResponse("No CinemachineBrain found.");

            var activeCam = brain.ActiveVirtualCamera;

            return new SuccessResponse("CinemachineBrain.", new Dictionary<string, object>
            {
                ["gameobject_name"] = brain.gameObject.name,
                ["active_camera"] = activeCam != null ? activeCam.Name : null,
                ["is_blending"] = brain.IsBlending,
                ["default_blend_time"] = Math.Round(brain.DefaultBlend.Time, 4),
                ["default_blend_style"] = brain.DefaultBlend.Style.ToString(),
                ["update_method"] = brain.UpdateMethod.ToString(),
            });
        }

        private static object SetPriority(ToolParams p)
        {
            var targetResult = p.GetRequired("target");
            if (!targetResult.IsSuccess) return new ErrorResponse(targetResult.ErrorMessage);

            int? priority = p.GetInt("priority");
            if (priority == null) return new ErrorResponse("'priority' parameter is required.");

            var go = ObjectResolver.ResolveGameObject(new JValue(targetResult.Value));
            if (go == null) return new ErrorResponse($"GameObject '{targetResult.Value}' not found.");

            var vcam = go.GetComponent<CinemachineCamera>();
            if (vcam == null) return new ErrorResponse($"No CinemachineCamera on '{go.name}'.");

            Undo.RecordObject(vcam, "MCP SetPriority");
            vcam.Priority = new PrioritySettings { Value = priority.Value };
            EditorUtility.SetDirty(vcam);

            return new SuccessResponse($"Priority set to {priority.Value} on '{go.name}'.");
        }

        private static object ListBlends()
        {
            var brain = UnityEngine.Object.FindFirstObjectByType<CinemachineBrain>();
            if (brain == null) return new ErrorResponse("No CinemachineBrain found.");

            var blends = new List<Dictionary<string, object>>();

            if (brain.CustomBlends != null)
            {
                foreach (var blend in brain.CustomBlends.CustomBlends)
                {
                    blends.Add(new Dictionary<string, object>
                    {
                        ["from"] = blend.From,
                        ["to"] = blend.To,
                        ["style"] = blend.Blend.Style.ToString(),
                        ["time"] = Math.Round(blend.Blend.Time, 4),
                    });
                }
            }

            return new SuccessResponse($"Found {blends.Count} custom blend(s).", new Dictionary<string, object>
            {
                ["default_blend_style"] = brain.DefaultBlend.Style.ToString(),
                ["default_blend_time"] = Math.Round(brain.DefaultBlend.Time, 4),
                ["custom_blends"] = blends,
            });
        }
    }
}
#endif
