using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using MCPForUnity.Editor.Helpers;

namespace MCPForUnity.Editor.Tools.Vfx
{
    internal static class LineCreate
    {
        public static object CreateLine(JObject @params)
        {
            LineRenderer lr = LineRead.FindLineRenderer(@params);
            if (lr == null) return new { success = false, message = "LineRenderer not found" };

            Vector3 start = ManageVfxCommon.ParseVector3(@params["start"]);
            Vector3 end = ManageVfxCommon.ParseVector3(@params["end"]);

            Undo.RecordObject(lr, "Create Line");
            lr.positionCount = 2;
            lr.SetPosition(0, start);
            lr.SetPosition(1, end);

            RendererHelpers.EnsureMaterial(lr);

            // Apply optional width
            if (@params["width"] != null)
            {
                float w = @params["width"].ToObject<float>();
                lr.startWidth = w;
                lr.endWidth = w;
            }
            if (@params["startWidth"] != null) lr.startWidth = @params["startWidth"].ToObject<float>();
            if (@params["endWidth"] != null) lr.endWidth = @params["endWidth"].ToObject<float>();

            // Apply optional color
            if (@params["color"] != null)
            {
                Color c = ManageVfxCommon.ParseColor(@params["color"]);
                lr.startColor = c;
                lr.endColor = c;
            }
            if (@params["startColor"] != null) lr.startColor = ManageVfxCommon.ParseColor(@params["startColor"]);
            if (@params["endColor"] != null) lr.endColor = ManageVfxCommon.ParseColor(@params["endColor"]);

            EditorUtility.SetDirty(lr);

            return new { success = true, message = "Created line" };
        }

        public static object CreateCircle(JObject @params)
        {
            LineRenderer lr = LineRead.FindLineRenderer(@params);
            if (lr == null) return new { success = false, message = "LineRenderer not found" };

            Vector3 center = ManageVfxCommon.ParseVector3(@params["center"]);
            float radius = @params["radius"]?.ToObject<float>() ?? 1f;
            int segments = @params["segments"]?.ToObject<int>() ?? 32;
            Vector3 normal = @params["normal"] != null ? ManageVfxCommon.ParseVector3(@params["normal"]).normalized : Vector3.up;

            Vector3 right = Vector3.Cross(normal, Vector3.forward);
            if (right.sqrMagnitude < 0.001f) right = Vector3.Cross(normal, Vector3.up);
            right = right.normalized;
            Vector3 forward = Vector3.Cross(right, normal).normalized;

            Undo.RecordObject(lr, "Create Circle");
            lr.positionCount = segments;
            lr.loop = true;

            for (int i = 0; i < segments; i++)
            {
                float angle = (float)i / segments * Mathf.PI * 2f;
                Vector3 point = center + (right * Mathf.Cos(angle) + forward * Mathf.Sin(angle)) * radius;
                lr.SetPosition(i, point);
            }

            RendererHelpers.EnsureMaterial(lr);

            // Apply optional width
            if (@params["width"] != null)
            {
                float w = @params["width"].ToObject<float>();
                lr.startWidth = w;
                lr.endWidth = w;
            }
            if (@params["startWidth"] != null) lr.startWidth = @params["startWidth"].ToObject<float>();
            if (@params["endWidth"] != null) lr.endWidth = @params["endWidth"].ToObject<float>();

            // Apply optional color
            if (@params["color"] != null)
            {
                Color c = ManageVfxCommon.ParseColor(@params["color"]);
                lr.startColor = c;
                lr.endColor = c;
            }
            if (@params["startColor"] != null) lr.startColor = ManageVfxCommon.ParseColor(@params["startColor"]);
            if (@params["endColor"] != null) lr.endColor = ManageVfxCommon.ParseColor(@params["endColor"]);

            EditorUtility.SetDirty(lr);
            return new { success = true, message = $"Created circle with {segments} segments" };
        }

        public static object CreateArc(JObject @params)
        {
            LineRenderer lr = LineRead.FindLineRenderer(@params);
            if (lr == null) return new { success = false, message = "LineRenderer not found" };

            Vector3 center = ManageVfxCommon.ParseVector3(@params["center"]);
            float radius = @params["radius"]?.ToObject<float>() ?? 1f;
            float startAngle = (@params["startAngle"]?.ToObject<float>() ?? 0f) * Mathf.Deg2Rad;
            float endAngle = (@params["endAngle"]?.ToObject<float>() ?? 180f) * Mathf.Deg2Rad;
            int segments = @params["segments"]?.ToObject<int>() ?? 16;
            Vector3 normal = @params["normal"] != null ? ManageVfxCommon.ParseVector3(@params["normal"]).normalized : Vector3.up;

            Vector3 right = Vector3.Cross(normal, Vector3.forward);
            if (right.sqrMagnitude < 0.001f) right = Vector3.Cross(normal, Vector3.up);
            right = right.normalized;
            Vector3 forward = Vector3.Cross(right, normal).normalized;

            Undo.RecordObject(lr, "Create Arc");
            lr.positionCount = segments + 1;
            lr.loop = false;

            for (int i = 0; i <= segments; i++)
            {
                float t = (float)i / segments;
                float angle = Mathf.Lerp(startAngle, endAngle, t);
                Vector3 point = center + (right * Mathf.Cos(angle) + forward * Mathf.Sin(angle)) * radius;
                lr.SetPosition(i, point);
            }

            RendererHelpers.EnsureMaterial(lr);

            // Apply optional width
            if (@params["width"] != null)
            {
                float w = @params["width"].ToObject<float>();
                lr.startWidth = w;
                lr.endWidth = w;
            }
            if (@params["startWidth"] != null) lr.startWidth = @params["startWidth"].ToObject<float>();
            if (@params["endWidth"] != null) lr.endWidth = @params["endWidth"].ToObject<float>();

            // Apply optional color
            if (@params["color"] != null)
            {
                Color c = ManageVfxCommon.ParseColor(@params["color"]);
                lr.startColor = c;
                lr.endColor = c;
            }
            if (@params["startColor"] != null) lr.startColor = ManageVfxCommon.ParseColor(@params["startColor"]);
            if (@params["endColor"] != null) lr.endColor = ManageVfxCommon.ParseColor(@params["endColor"]);

            EditorUtility.SetDirty(lr);
            return new { success = true, message = $"Created arc with {segments} segments" };
        }

        public static object CreateBezier(JObject @params)
        {
            LineRenderer lr = LineRead.FindLineRenderer(@params);
            if (lr == null) return new { success = false, message = "LineRenderer not found" };

            Vector3 start = ManageVfxCommon.ParseVector3(@params["start"]);
            Vector3 end = ManageVfxCommon.ParseVector3(@params["end"]);
            Vector3 cp1 = ManageVfxCommon.ParseVector3(@params["controlPoint1"] ?? @params["control1"]);
            Vector3 cp2 = @params["controlPoint2"] != null || @params["control2"] != null
                ? ManageVfxCommon.ParseVector3(@params["controlPoint2"] ?? @params["control2"])
                : cp1;
            int segments = @params["segments"]?.ToObject<int>() ?? 32;
            bool isQuadratic = @params["controlPoint2"] == null && @params["control2"] == null;

            Undo.RecordObject(lr, "Create Bezier");
            lr.positionCount = segments + 1;
            lr.loop = false;

            for (int i = 0; i <= segments; i++)
            {
                float t = (float)i / segments;
                Vector3 point;

                if (isQuadratic)
                {
                    float u = 1 - t;
                    point = u * u * start + 2 * u * t * cp1 + t * t * end;
                }
                else
                {
                    float u = 1 - t;
                    point = u * u * u * start + 3 * u * u * t * cp1 + 3 * u * t * t * cp2 + t * t * t * end;
                }

                lr.SetPosition(i, point);
            }

            RendererHelpers.EnsureMaterial(lr);

            // Apply optional width
            if (@params["width"] != null)
            {
                float w = @params["width"].ToObject<float>();
                lr.startWidth = w;
                lr.endWidth = w;
            }
            if (@params["startWidth"] != null) lr.startWidth = @params["startWidth"].ToObject<float>();
            if (@params["endWidth"] != null) lr.endWidth = @params["endWidth"].ToObject<float>();

            // Apply optional color
            if (@params["color"] != null)
            {
                Color c = ManageVfxCommon.ParseColor(@params["color"]);
                lr.startColor = c;
                lr.endColor = c;
            }
            if (@params["startColor"] != null) lr.startColor = ManageVfxCommon.ParseColor(@params["startColor"]);
            if (@params["endColor"] != null) lr.endColor = ManageVfxCommon.ParseColor(@params["endColor"]);

            EditorUtility.SetDirty(lr);
            return new { success = true, message = $"Created {(isQuadratic ? "quadratic" : "cubic")} Bezier" };
        }
    }
}
