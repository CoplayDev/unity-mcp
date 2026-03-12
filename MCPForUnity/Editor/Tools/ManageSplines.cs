#if UNITY_SPLINES
using System;
using System.Collections.Generic;
using System.Globalization;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Splines;
using Unity.Mathematics;

namespace MCPForUnity.Editor.Tools
{
    [McpForUnityTool("manage_splines", AutoRegister = true)]
    public static class ManageSplines
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
                    "list_splines" => ListSplines(p),
                    "get_spline"   => GetSpline(p),
                    "get_knot"     => GetKnot(p),
                    "add_knot"     => AddKnot(p),
                    "remove_knot"  => RemoveKnot(p),
                    "set_knot"     => SetKnot(p),
                    "evaluate"     => Evaluate(p),
                    _ => new ErrorResponse($"Unknown action: '{action}'.")
                };
            }
            catch (Exception e)
            {
                McpLog.Error($"[ManageSplines] Action '{action}' failed: {e}");
                return new ErrorResponse($"Internal error: {e.Message}");
            }
        }

        private static object ListSplines(ToolParams p)
        {
            var containers = UnityEngine.Object.FindObjectsByType<SplineContainer>(FindObjectsSortMode.InstanceID);
            var pagination = PaginationRequest.FromParams(
                new JObject { ["page_size"] = p.GetInt("page_size"), ["cursor"] = p.GetInt("cursor") });

            var allItems = new List<Dictionary<string, object>>();
            foreach (var sc in containers)
            {
                allItems.Add(new Dictionary<string, object>
                {
                    ["name"] = sc.gameObject.name,
                    ["instance_id"] = sc.gameObject.GetInstanceID(),
                    ["spline_count"] = sc.Splines.Count,
                });
            }

            var page = PaginationResponse<Dictionary<string, object>>.Create(allItems, pagination);
            return new SuccessResponse($"Found {containers.Length} SplineContainer(s).", page);
        }

        private static object GetSpline(ToolParams p)
        {
            var sc = ResolveSplineContainer(p);
            if (sc == null) return new ErrorResponse("SplineContainer not found.");

            int index = p.GetInt("spline_index") ?? 0;
            if (index < 0 || index >= sc.Splines.Count)
                return new ErrorResponse($"Spline index {index} out of range (0-{sc.Splines.Count - 1}).");

            var spline = sc.Splines[index];
            float length = spline.GetLength();

            var knots = new List<Dictionary<string, object>>();
            for (int i = 0; i < spline.Count; i++)
            {
                var knot = spline[i];
                knots.Add(new Dictionary<string, object>
                {
                    ["index"] = i,
                    ["position"] = FormatFloat3(knot.Position),
                    ["rotation"] = FormatQuaternion(knot.Rotation),
                });
            }

            return new SuccessResponse($"Spline {index} on '{sc.gameObject.name}'.", new Dictionary<string, object>
            {
                ["spline_index"] = index,
                ["knot_count"] = spline.Count,
                ["length"] = Math.Round(length, 4),
                ["closed"] = spline.Closed,
                ["knots"] = knots,
            });
        }

        private static object GetKnot(ToolParams p)
        {
            var sc = ResolveSplineContainer(p);
            if (sc == null) return new ErrorResponse("SplineContainer not found.");

            int splineIndex = p.GetInt("spline_index") ?? 0;
            if (splineIndex < 0 || splineIndex >= sc.Splines.Count)
                return new ErrorResponse($"Spline index {splineIndex} out of range.");

            var knotIndexResult = p.GetInt("knot_index");
            if (knotIndexResult == null) return new ErrorResponse("'knot_index' parameter is required.");
            int knotIndex = knotIndexResult.Value;

            var spline = sc.Splines[splineIndex];
            if (knotIndex < 0 || knotIndex >= spline.Count)
                return new ErrorResponse($"Knot index {knotIndex} out of range (0-{spline.Count - 1}).");

            var knot = spline[knotIndex];
            return new SuccessResponse($"Knot {knotIndex} on spline {splineIndex}.", new Dictionary<string, object>
            {
                ["knot_index"] = knotIndex,
                ["position"] = FormatFloat3(knot.Position),
                ["rotation"] = FormatQuaternion(knot.Rotation),
                ["tangent_in"] = FormatFloat3(knot.TangentIn),
                ["tangent_out"] = FormatFloat3(knot.TangentOut),
            });
        }

        private static object AddKnot(ToolParams p)
        {
            var sc = ResolveSplineContainer(p);
            if (sc == null) return new ErrorResponse("SplineContainer not found.");

            int splineIndex = p.GetInt("spline_index") ?? 0;
            if (splineIndex < 0 || splineIndex >= sc.Splines.Count)
                return new ErrorResponse($"Spline index {splineIndex} out of range.");

            if (!TryParseFloat3(p.Get("position"), out var pos))
                return new ErrorResponse("'position' parameter is required (e.g. '0,1,0').");

            var spline = sc.Splines[splineIndex];
            var knot = new BezierKnot(pos);

            if (TryParseFloat4(p.Get("rotation"), out var rot))
                knot.Rotation = rot;

            Undo.RecordObject(sc, "MCP AddKnot");
            spline.Add(knot);
            EditorUtility.SetDirty(sc);

            return new SuccessResponse($"Knot added at index {spline.Count - 1}.");
        }

        private static object RemoveKnot(ToolParams p)
        {
            var sc = ResolveSplineContainer(p);
            if (sc == null) return new ErrorResponse("SplineContainer not found.");

            int splineIndex = p.GetInt("spline_index") ?? 0;
            if (splineIndex < 0 || splineIndex >= sc.Splines.Count)
                return new ErrorResponse($"Spline index {splineIndex} out of range.");

            var knotIndexResult = p.GetInt("knot_index");
            if (knotIndexResult == null) return new ErrorResponse("'knot_index' parameter is required.");
            int knotIndex = knotIndexResult.Value;

            var spline = sc.Splines[splineIndex];
            if (knotIndex < 0 || knotIndex >= spline.Count)
                return new ErrorResponse($"Knot index {knotIndex} out of range.");

            Undo.RecordObject(sc, "MCP RemoveKnot");
            spline.RemoveAt(knotIndex);
            EditorUtility.SetDirty(sc);

            return new SuccessResponse($"Knot {knotIndex} removed.");
        }

        private static object SetKnot(ToolParams p)
        {
            var sc = ResolveSplineContainer(p);
            if (sc == null) return new ErrorResponse("SplineContainer not found.");

            int splineIndex = p.GetInt("spline_index") ?? 0;
            if (splineIndex < 0 || splineIndex >= sc.Splines.Count)
                return new ErrorResponse($"Spline index {splineIndex} out of range.");

            var knotIndexResult = p.GetInt("knot_index");
            if (knotIndexResult == null) return new ErrorResponse("'knot_index' parameter is required.");
            int knotIndex = knotIndexResult.Value;

            var spline = sc.Splines[splineIndex];
            if (knotIndex < 0 || knotIndex >= spline.Count)
                return new ErrorResponse($"Knot index {knotIndex} out of range.");

            var knot = spline[knotIndex];

            if (TryParseFloat3(p.Get("position"), out var pos))
                knot.Position = pos;
            if (TryParseFloat4(p.Get("rotation"), out var rot))
                knot.Rotation = rot;

            Undo.RecordObject(sc, "MCP SetKnot");
            spline[knotIndex] = knot;
            EditorUtility.SetDirty(sc);

            return new SuccessResponse($"Knot {knotIndex} updated.");
        }

        private static object Evaluate(ToolParams p)
        {
            var sc = ResolveSplineContainer(p);
            if (sc == null) return new ErrorResponse("SplineContainer not found.");

            int splineIndex = p.GetInt("spline_index") ?? 0;
            if (splineIndex < 0 || splineIndex >= sc.Splines.Count)
                return new ErrorResponse($"Spline index {splineIndex} out of range.");

            float? tParam = p.GetFloat("t");
            if (tParam == null) return new ErrorResponse("'t' parameter is required (0-1).");
            float t = Mathf.Clamp01(tParam.Value);

            var spline = sc.Splines[splineIndex];
            SplineUtility.Evaluate(spline, t, out float3 position, out float3 tangent, out float3 up);

            return new SuccessResponse($"Evaluated at t={t:F4}.", new Dictionary<string, object>
            {
                ["t"] = Math.Round(t, 4),
                ["position"] = FormatFloat3(position),
                ["tangent"] = FormatFloat3(tangent),
                ["up"] = FormatFloat3(up),
            });
        }

        #region Helpers

        private static SplineContainer ResolveSplineContainer(ToolParams p)
        {
            string target = p.Get("target");
            if (string.IsNullOrEmpty(target)) return null;
            var go = ObjectResolver.ResolveGameObject(new JValue(target));
            return go != null ? go.GetComponent<SplineContainer>() : null;
        }

        private static bool TryParseFloat3(string str, out float3 result)
        {
            result = float3.zero;
            if (string.IsNullOrEmpty(str)) return false;
            var parts = str.Split(',');
            if (parts.Length < 3) return false;
            if (float.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float x) &&
                float.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float y) &&
                float.TryParse(parts[2].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float z))
            {
                result = new float3(x, y, z);
                return true;
            }
            return false;
        }

        private static bool TryParseFloat4(string str, out quaternion result)
        {
            result = quaternion.identity;
            if (string.IsNullOrEmpty(str)) return false;
            var parts = str.Split(',');
            if (parts.Length < 4) return false;
            if (float.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float x) &&
                float.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float y) &&
                float.TryParse(parts[2].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float z) &&
                float.TryParse(parts[3].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float w))
            {
                result = new quaternion(x, y, z, w);
                return true;
            }
            return false;
        }

        private static string FormatFloat3(float3 v) => $"{v.x:F3},{v.y:F3},{v.z:F3}";
        private static string FormatQuaternion(quaternion q) => $"{q.value.x:F3},{q.value.y:F3},{q.value.z:F3},{q.value.w:F3}";

        #endregion
    }
}
#endif
