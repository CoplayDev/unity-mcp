using System;
using System.Collections.Generic;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace MCPForUnity.Editor.Tools.Vfx
{
    internal static class TrailWrite
    {
        public static object SetTime(JObject @params)
        {
            TrailRenderer tr = TrailRead.FindTrailRenderer(@params);
            if (tr == null) return new { success = false, message = "TrailRenderer not found" };

            RendererHelpers.EnsureMaterial(tr);

            float time = @params["time"]?.ToObject<float>() ?? 5f;

            Undo.RecordObject(tr, "Set Trail Time");
            tr.time = time;
            EditorUtility.SetDirty(tr);

            return new { success = true, message = $"Set trail time to {time}s" };
        }

        public static object SetWidth(JObject @params)
        {
            TrailRenderer tr = TrailRead.FindTrailRenderer(@params);
            if (tr == null) return new { success = false, message = "TrailRenderer not found" };

            RendererHelpers.EnsureMaterial(tr);

            Undo.RecordObject(tr, "Set Trail Width");
            var changes = new List<string>();

            RendererHelpers.ApplyWidthProperties(@params, changes,
                v => tr.startWidth = v, v => tr.endWidth = v,
                v => tr.widthCurve = v, v => tr.widthMultiplier = v,
                ManageVfxCommon.ParseAnimationCurve);

            EditorUtility.SetDirty(tr);
            return new { success = true, message = $"Updated: {string.Join(", ", changes)}" };
        }

        public static object SetColor(JObject @params)
        {
            TrailRenderer tr = TrailRead.FindTrailRenderer(@params);
            if (tr == null) return new { success = false, message = "TrailRenderer not found" };

            RendererHelpers.EnsureMaterial(tr);

            Undo.RecordObject(tr, "Set Trail Color");
            var changes = new List<string>();

            RendererHelpers.ApplyColorProperties(@params, changes,
                v => tr.startColor = v, v => tr.endColor = v,
                v => tr.colorGradient = v,
                ManageVfxCommon.ParseColor, ManageVfxCommon.ParseGradient, fadeEndAlpha: true);

            EditorUtility.SetDirty(tr);
            return new { success = true, message = $"Updated: {string.Join(", ", changes)}" };
        }

        public static object SetMaterial(JObject @params)
        {
            TrailRenderer tr = TrailRead.FindTrailRenderer(@params);
            return RendererHelpers.SetRendererMaterial(tr, @params, "Set Trail Material", ManageVfxCommon.FindMaterialByPath);
        }

        public static object SetProperties(JObject @params)
        {
            TrailRenderer tr = TrailRead.FindTrailRenderer(@params);
            if (tr == null) return new { success = false, message = "TrailRenderer not found" };

            RendererHelpers.EnsureMaterial(tr);

            Undo.RecordObject(tr, "Set Trail Properties");
            var changes = new List<string>();

            // Handle material if provided
            if (@params["materialPath"] != null)
            {
                Material mat = ManageVfxCommon.FindMaterialByPath(@params["materialPath"].ToString());
                if (mat != null)
                {
                    tr.sharedMaterial = mat;
                    changes.Add($"material={mat.name}");
                }
                else
                {
                    McpLog.Warn($"Material not found: {@params["materialPath"]}");
                }
            }

            // Handle time if provided
            if (@params["time"] != null) { tr.time = @params["time"].ToObject<float>(); changes.Add("time"); }

            // Handle width properties if provided
            if (@params["width"] != null || @params["startWidth"] != null || @params["endWidth"] != null)
            {
                if (@params["width"] != null)
                {
                    float w = @params["width"].ToObject<float>();
                    tr.startWidth = w;
                    tr.endWidth = w;
                    changes.Add("width");
                }
                if (@params["startWidth"] != null) { tr.startWidth = @params["startWidth"].ToObject<float>(); changes.Add("startWidth"); }
                if (@params["endWidth"] != null) { tr.endWidth = @params["endWidth"].ToObject<float>(); changes.Add("endWidth"); }
            }

            if (@params["minVertexDistance"] != null) { tr.minVertexDistance = @params["minVertexDistance"].ToObject<float>(); changes.Add("minVertexDistance"); }
            if (@params["autodestruct"] != null) { tr.autodestruct = @params["autodestruct"].ToObject<bool>(); changes.Add("autodestruct"); }
            if (@params["emitting"] != null) { tr.emitting = @params["emitting"].ToObject<bool>(); changes.Add("emitting"); }

            RendererHelpers.ApplyLineTrailProperties(@params, changes,
                null, null,
                v => tr.numCornerVertices = v, v => tr.numCapVertices = v,
                v => tr.alignment = v, v => tr.textureMode = v,
                v => tr.generateLightingData = v);

            RendererHelpers.ApplyCommonRendererProperties(tr, @params, changes);

            EditorUtility.SetDirty(tr);
            return new { success = true, message = $"Updated: {string.Join(", ", changes)}" };
        }
    }
}
