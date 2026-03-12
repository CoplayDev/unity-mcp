using System;
using System.Collections.Generic;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace MCPForUnity.Editor.Tools
{
    /// <summary>
    /// MCP tool for inspecting and modifying Unity Mesh data at runtime.
    /// Actions: inspect, get_info, get_attributes, has_attribute, sample_colors,
    ///          sample_vertices, set_colors, force_upload
    /// </summary>
    [McpForUnityTool("manage_mesh", AutoRegister = true)]
    public static class ManageMesh
    {
        public static object HandleCommand(JObject @params)
        {
            if (@params == null)
                return new ErrorResponse("Parameters cannot be null.");

            var p = new ToolParams(@params);

            var actionResult = p.GetRequired("action");
            if (!actionResult.IsSuccess)
                return new ErrorResponse(actionResult.ErrorMessage);

            string action = actionResult.Value.ToLowerInvariant();

            try
            {
                return action switch
                {
                    "inspect"         => Inspect(p),
                    "get_info"        => GetInfo(p),
                    "get_attributes"  => GetAttributes(p),
                    "has_attribute"   => HasAttribute(p),
                    "sample_colors"   => SampleColors(p),
                    "sample_vertices" => SampleVertices(p),
                    "set_colors"      => SetColors(p),
                    "force_upload"    => ForceUpload(p),
                    _ => new ErrorResponse(
                        $"Unknown action: '{action}'. Supported: inspect, get_info, get_attributes, " +
                        "has_attribute, sample_colors, sample_vertices, set_colors, force_upload")
                };
            }
            catch (Exception e)
            {
                McpLog.Error($"[ManageMesh] Action '{action}' failed: {e}");
                return new ErrorResponse($"Internal error processing action '{action}': {e.Message}");
            }
        }

        // ─── Actions ──────────────────────────────────────────────────────────

        /// <summary>Combined all-in-one inspection: info + attributes + color samples.</summary>
        private static object Inspect(ToolParams p)
        {
            var (mesh, error) = ResolveMesh(p);
            if (mesh == null) return new ErrorResponse(error);

            int sampleCount = p.GetInt("count") ?? 10;
            int sampleOffset = p.GetInt("offset") ?? 0;

            var attributes = SerializeAttributes(mesh);
            bool hasColors = mesh.HasVertexAttribute(VertexAttribute.Color);
            var colorSamples = hasColors
                ? SampleArray(mesh.colors, sampleCount, sampleOffset, c => ColorDict(c))
                : new List<object>();

            return new SuccessResponse($"Mesh '{mesh.name}' inspection.", new Dictionary<string, object>
            {
                ["name"]          = mesh.name,
                ["vertexCount"]   = mesh.vertexCount,
                ["triangleCount"] = mesh.triangles.Length / 3,
                ["bounds"]        = BoundsDict(mesh.bounds),
                ["indexFormat"]   = mesh.indexFormat.ToString(),
                ["submeshCount"]  = mesh.subMeshCount,
                ["isReadable"]    = mesh.isReadable,
                ["hasColors"]     = hasColors,
                ["colorCount"]    = hasColors ? mesh.colors.Length : 0,
                ["attributes"]    = attributes,
                ["colorSamples"]  = colorSamples
            });
        }

        private static object GetInfo(ToolParams p)
        {
            var (mesh, error) = ResolveMesh(p);
            if (mesh == null) return new ErrorResponse(error);

            return new SuccessResponse($"Mesh '{mesh.name}' info.", new Dictionary<string, object>
            {
                ["name"]          = mesh.name,
                ["vertexCount"]   = mesh.vertexCount,
                ["triangleCount"] = mesh.triangles.Length / 3,
                ["bounds"]        = BoundsDict(mesh.bounds),
                ["indexFormat"]   = mesh.indexFormat.ToString(),
                ["submeshCount"]  = mesh.subMeshCount,
                ["isReadable"]    = mesh.isReadable
            });
        }

        private static object GetAttributes(ToolParams p)
        {
            var (mesh, error) = ResolveMesh(p);
            if (mesh == null) return new ErrorResponse(error);

            return new SuccessResponse($"Mesh '{mesh.name}' vertex attributes.", SerializeAttributes(mesh));
        }

        private static object HasAttribute(ToolParams p)
        {
            var (mesh, error) = ResolveMesh(p);
            if (mesh == null) return new ErrorResponse(error);

            var attrResult = p.GetRequired("attribute");
            if (!attrResult.IsSuccess)
                return new ErrorResponse(attrResult.ErrorMessage);

            if (!Enum.TryParse<VertexAttribute>(attrResult.Value, ignoreCase: true, out var attr))
            {
                return new ErrorResponse(
                    $"Unknown vertex attribute '{attrResult.Value}'. " +
                    "Valid values: Position, Normal, Tangent, Color, TexCoord0-7, BlendWeight, BlendIndices.");
            }

            bool has = mesh.HasVertexAttribute(attr);
            return new SuccessResponse(
                $"Mesh '{mesh.name}' {(has ? "has" : "does not have")} attribute '{attr}'.",
                new Dictionary<string, object>
                {
                    ["attribute"] = attr.ToString(),
                    ["has"]       = has
                });
        }

        private static object SampleColors(ToolParams p)
        {
            var (mesh, error) = ResolveMesh(p);
            if (mesh == null) return new ErrorResponse(error);

            if (!mesh.HasVertexAttribute(VertexAttribute.Color))
                return new ErrorResponse($"Mesh '{mesh.name}' has no vertex color attribute.");

            int count  = p.GetInt("count")  ?? 10;
            int offset = p.GetInt("offset") ?? 0;
            var colors = mesh.colors;
            var samples = SampleArray(colors, count, offset, c => ColorDict(c));

            return new SuccessResponse(
                $"Sampled {samples.Count} color(s) from mesh '{mesh.name}' ({colors.Length} total).",
                new Dictionary<string, object>
                {
                    ["total"]   = colors.Length,
                    ["samples"] = samples
                });
        }

        private static object SampleVertices(ToolParams p)
        {
            var (mesh, error) = ResolveMesh(p);
            if (mesh == null) return new ErrorResponse(error);

            int count  = p.GetInt("count")  ?? 10;
            int offset = p.GetInt("offset") ?? 0;
            var verts  = mesh.vertices;
            var samples = SampleArray(verts, count, offset, v => VectorDict(v));

            return new SuccessResponse(
                $"Sampled {samples.Count} vertex/vertices from mesh '{mesh.name}' ({verts.Length} total).",
                new Dictionary<string, object>
                {
                    ["total"]   = verts.Length,
                    ["samples"] = samples
                });
        }

        private static object SetColors(ToolParams p)
        {
            var (mesh, error) = ResolveMesh(p);
            if (mesh == null) return new ErrorResponse(error);

            var colorStr = p.Get("color");
            if (string.IsNullOrEmpty(colorStr))
                return new ErrorResponse("'color' parameter is required (format: 'r,g,b,a' floats 0-1).");

            if (!TryParseColor(colorStr, out Color c))
                return new ErrorResponse($"Cannot parse color '{colorStr}'. Expected format: 'r,g,b,a' (e.g. '1,0,0,1').");

            int vertexCount = mesh.vertexCount;
            var colors = new Color[vertexCount];
            for (int i = 0; i < vertexCount; i++)
                colors[i] = c;

            Undo.RecordObject(mesh, "MCP Set Vertex Colors");
            mesh.colors = colors;
            mesh.UploadMeshData(false);

            return new SuccessResponse(
                $"Set {vertexCount} vertex colors on mesh '{mesh.name}' to {colorStr}.",
                new Dictionary<string, object>
                {
                    ["mesh"]        = mesh.name,
                    ["vertexCount"] = vertexCount,
                    ["color"]       = ColorDict(c)
                });
        }

        private static object ForceUpload(ToolParams p)
        {
            var (mesh, error) = ResolveMesh(p);
            if (mesh == null) return new ErrorResponse(error);

            mesh.UploadMeshData(false);
            return new SuccessResponse($"Uploaded mesh data for '{mesh.name}'.",
                new Dictionary<string, object> { ["mesh"] = mesh.name });
        }

        // ─── Helpers ──────────────────────────────────────────────────────────

        private static (Mesh mesh, string error) ResolveMesh(ToolParams p)
        {
            var targetResult = p.GetRequired("target");
            if (!targetResult.IsSuccess) return (null, targetResult.ErrorMessage);
            string target = targetResult.Value;

            GameObject go = null;
            if (int.TryParse(target, out int id))
                go = EditorUtility.EntityIdToObject(id) as GameObject;
            if (go == null)
                go = GameObject.Find(target);
            if (go == null) return (null, $"GameObject not found: '{target}'");

            var mf = go.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null)
                return (null, $"No MeshFilter or sharedMesh on GameObject: '{target}'");

            return (mf.sharedMesh, null);
        }

        private static List<object> SerializeAttributes(Mesh mesh)
        {
            var result = new List<object>();
            for (int i = 0; i < mesh.vertexAttributeCount; i++)
            {
                var desc = mesh.GetVertexAttribute(i);
                result.Add(new Dictionary<string, object>
                {
                    ["attribute"] = desc.attribute.ToString(),
                    ["format"]    = desc.format.ToString(),
                    ["dimension"] = desc.dimension,
                    ["stream"]    = desc.stream
                });
            }
            return result;
        }

        /// <summary>
        /// Samples up to <paramref name="count"/> elements evenly spaced from <paramref name="array"/>,
        /// starting at logical index <paramref name="offset"/>.
        /// </summary>
        private static List<object> SampleArray<T>(T[] array, int count, int offset, Func<T, object> serialize)
        {
            var result = new List<object>();
            if (array == null || array.Length == 0) return result;

            int available = Math.Max(0, array.Length - offset);
            if (available == 0) return result;

            count = Math.Clamp(count, 1, 1000);
            int take  = Math.Min(count, available);
            float step = available <= 1 ? 1f : (float)(available - 1) / (take - 1 < 1 ? 1 : take - 1);

            for (int i = 0; i < take; i++)
            {
                int idx = offset + (int)Math.Round(i * step);
                idx = Math.Clamp(idx, 0, array.Length - 1);
                result.Add(serialize(array[idx]));
            }
            return result;
        }

        private static bool TryParseColor(string s, out Color color)
        {
            color = Color.white;
            var parts = s.Split(',');
            if (parts.Length < 3) return false;
            if (!float.TryParse(parts[0].Trim(), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float r)) return false;
            if (!float.TryParse(parts[1].Trim(), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float g)) return false;
            if (!float.TryParse(parts[2].Trim(), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float b)) return false;
            float a = 1f;
            if (parts.Length >= 4)
                float.TryParse(parts[3].Trim(), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out a);
            color = new Color(r, g, b, a);
            return true;
        }

        private static Dictionary<string, object> ColorDict(Color c) =>
            new Dictionary<string, object>
            {
                ["r"] = Math.Round(c.r, 4),
                ["g"] = Math.Round(c.g, 4),
                ["b"] = Math.Round(c.b, 4),
                ["a"] = Math.Round(c.a, 4)
            };

        private static Dictionary<string, object> VectorDict(Vector3 v) =>
            new Dictionary<string, object>
            {
                ["x"] = Math.Round(v.x, 4),
                ["y"] = Math.Round(v.y, 4),
                ["z"] = Math.Round(v.z, 4)
            };

        private static Dictionary<string, object> BoundsDict(Bounds b) =>
            new Dictionary<string, object>
            {
                ["center"] = VectorDict(b.center),
                ["size"]   = VectorDict(b.size),
                ["min"]    = VectorDict(b.min),
                ["max"]    = VectorDict(b.max)
            };
    }
}
