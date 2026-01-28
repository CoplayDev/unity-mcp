using System.Collections.Generic;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace MCPForUnity.Editor.Tools.Vfx
{
    internal static class LineWrite
    {
        public static object SetPositions(JObject @params)
        {
            LineRenderer lr = LineRead.FindLineRenderer(@params);
            if (lr == null) return new { success = false, message = "LineRenderer not found" };

            RendererHelpers.EnsureMaterial(lr);

            JArray posArr = @params["positions"] as JArray;
            if (posArr == null) return new { success = false, message = "Positions array required" };

            var positions = new Vector3[posArr.Count];
            for (int i = 0; i < posArr.Count; i++)
            {
                positions[i] = ManageVfxCommon.ParseVector3(posArr[i]);
            }

            Undo.RecordObject(lr, "Set Line Positions");
            lr.positionCount = positions.Length;
            lr.SetPositions(positions);
            EditorUtility.SetDirty(lr);

            return new { success = true, message = $"Set {positions.Length} positions" };
        }

        public static object AddPosition(JObject @params)
        {
            LineRenderer lr = LineRead.FindLineRenderer(@params);
            if (lr == null) return new { success = false, message = "LineRenderer not found" };

            RendererHelpers.EnsureMaterial(lr);

            Vector3 pos = ManageVfxCommon.ParseVector3(@params["position"]);

            Undo.RecordObject(lr, "Add Line Position");
            int idx = lr.positionCount;
            lr.positionCount = idx + 1;
            lr.SetPosition(idx, pos);
            EditorUtility.SetDirty(lr);

            return new { success = true, message = $"Added position at index {idx}", index = idx };
        }

        public static object SetPosition(JObject @params)
        {
            LineRenderer lr = LineRead.FindLineRenderer(@params);
            if (lr == null) return new { success = false, message = "LineRenderer not found" };

            RendererHelpers.EnsureMaterial(lr);

            int index = @params["index"]?.ToObject<int>() ?? -1;
            if (index < 0 || index >= lr.positionCount) return new { success = false, message = $"Invalid index {index}" };

            Vector3 pos = ManageVfxCommon.ParseVector3(@params["position"]);

            Undo.RecordObject(lr, "Set Line Position");
            lr.SetPosition(index, pos);
            EditorUtility.SetDirty(lr);

            return new { success = true, message = $"Set position at index {index}" };
        }

        public static object SetWidth(JObject @params)
        {
            LineRenderer lr = LineRead.FindLineRenderer(@params);
            if (lr == null) return new { success = false, message = "LineRenderer not found" };

            RendererHelpers.EnsureMaterial(lr);

            Undo.RecordObject(lr, "Set Line Width");
            var changes = new List<string>();

            RendererHelpers.ApplyWidthProperties(@params, changes,
                v => lr.startWidth = v, v => lr.endWidth = v,
                v => lr.widthCurve = v, v => lr.widthMultiplier = v,
                ManageVfxCommon.ParseAnimationCurve);

            EditorUtility.SetDirty(lr);
            return new { success = true, message = $"Updated: {string.Join(", ", changes)}" };
        }

        public static object SetColor(JObject @params)
        {
            LineRenderer lr = LineRead.FindLineRenderer(@params);
            if (lr == null) return new { success = false, message = "LineRenderer not found" };

            RendererHelpers.EnsureMaterial(lr);

            Undo.RecordObject(lr, "Set Line Color");
            var changes = new List<string>();

            RendererHelpers.ApplyColorProperties(@params, changes,
                v => lr.startColor = v, v => lr.endColor = v,
                v => lr.colorGradient = v,
                ManageVfxCommon.ParseColor, ManageVfxCommon.ParseGradient, fadeEndAlpha: false);

            EditorUtility.SetDirty(lr);
            return new { success = true, message = $"Updated: {string.Join(", ", changes)}" };
        }

        public static object SetMaterial(JObject @params)
        {
            LineRenderer lr = LineRead.FindLineRenderer(@params);
            return RendererHelpers.SetRendererMaterial(lr, @params, "Set Line Material", ManageVfxCommon.FindMaterialByPath);
        }

        public static object SetProperties(JObject @params)
        {
            LineRenderer lr = LineRead.FindLineRenderer(@params);
            if (lr == null) return new { success = false, message = "LineRenderer not found" };

            RendererHelpers.EnsureMaterial(lr);

            Undo.RecordObject(lr, "Set Line Properties");
            var changes = new List<string>();

            // Handle material if provided
            if (@params["materialPath"] != null)
            {
                Material mat = ManageVfxCommon.FindMaterialByPath(@params["materialPath"].ToString());
                if (mat != null)
                {
                    lr.sharedMaterial = mat;
                    changes.Add($"material={mat.name}");
                }
                else
                {
                    McpLog.Warn($"Material not found: {@params["materialPath"]}");
                }
            }

            // Handle positions if provided
            if (@params["positions"] != null)
            {
                JArray posArr = @params["positions"] as JArray;
                if (posArr != null && posArr.Count > 0)
                {
                    var positions = new Vector3[posArr.Count];
                    for (int i = 0; i < posArr.Count; i++)
                    {
                        positions[i] = ManageVfxCommon.ParseVector3(posArr[i]);
                    }
                    lr.positionCount = positions.Length;
                    lr.SetPositions(positions);
                    changes.Add($"positions({positions.Length})");
                }
            }
            else if (@params["positionCount"] != null)
            {
                int count = @params["positionCount"].ToObject<int>();
                lr.positionCount = count;
                changes.Add("positionCount");
            }

            RendererHelpers.ApplyLineTrailProperties(@params, changes,
                v => lr.loop = v, v => lr.useWorldSpace = v,
                v => lr.numCornerVertices = v, v => lr.numCapVertices = v,
                v => lr.alignment = v, v => lr.textureMode = v,
                v => lr.generateLightingData = v);

            RendererHelpers.ApplyCommonRendererProperties(lr, @params, changes);

            EditorUtility.SetDirty(lr);
            return new { success = true, message = $"Updated: {string.Join(", ", changes)}" };
        }

        public static object Clear(JObject @params)
        {
            LineRenderer lr = LineRead.FindLineRenderer(@params);
            if (lr == null) return new { success = false, message = "LineRenderer not found" };

            int count = lr.positionCount;
            Undo.RecordObject(lr, "Clear Line");
            lr.positionCount = 0;
            EditorUtility.SetDirty(lr);

            return new { success = true, message = $"Cleared {count} positions" };
        }
    }
}
