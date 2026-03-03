#if PROBUILDER
using System;
using System.Collections.Generic;
using System.Linq;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.ProBuilder;
using UnityEngine;
using UnityEngine.ProBuilder;
using UnityEngine.ProBuilder.MeshOperations;

namespace MCPForUnity.Editor.Tools
{
    /// <summary>
    /// MCP tool for ProBuilder mesh creation and editing.
    /// Actions: create_shape, get_info, extrude, set_vertex_colors,
    ///          subdivide, merge, to_mesh
    /// Requires com.unity.probuilder package (defines PROBUILDER scripting symbol).
    /// </summary>
    [McpForUnityTool("manage_probuilder", AutoRegister = true)]
    public static class ManageProBuilder
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
                    "create_shape"       => CreateShape(p),
                    "get_info"           => GetInfo(p),
                    "extrude"            => Extrude(p),
                    "set_vertex_colors"  => SetVertexColors(p),
                    "subdivide"          => Subdivide(p),
                    "merge"              => Merge(p),
                    "to_mesh"            => ToMesh(p),
                    _ => new ErrorResponse(
                        $"Unknown action: '{action}'. Supported: create_shape, get_info, extrude, " +
                        "set_vertex_colors, subdivide, merge, to_mesh")
                };
            }
            catch (Exception e)
            {
                McpLog.Error($"[ManageProBuilder] Action '{action}' failed: {e}");
                return new ErrorResponse($"Internal error processing action '{action}': {e.Message}");
            }
        }

        #region Shape Creation

        private static object CreateShape(ToolParams p)
        {
            string shapeStr = p.Get("shape") ?? "cube";
            string sizeStr  = p.Get("size") ?? "1,1,1";
            string posStr   = p.Get("position") ?? "0,0,0";

            if (!TryParseVector3(sizeStr, out Vector3 size))
                return new ErrorResponse($"Invalid 'size' format '{sizeStr}'. Expected 'x,y,z'.");
            if (!TryParseVector3(posStr, out Vector3 position))
                return new ErrorResponse($"Invalid 'position' format '{posStr}'. Expected 'x,y,z'.");

            if (!TryResolveShapeType(shapeStr, out ShapeType shapeType))
                return new ErrorResponse(
                    $"Unknown shape '{shapeStr}'. Supported: cube, cylinder, sphere, plane, stair, arch, prism, torus");

            ProBuilderMesh pb = ShapeGenerator.CreateShape(shapeType, PivotLocation.Center);
            pb.transform.localScale = size;
            pb.transform.position   = position;

            pb.ToMesh();
            pb.Refresh();

            Undo.RegisterCreatedObjectUndo(pb.gameObject, $"Create ProBuilder {shapeStr}");

            return new SuccessResponse(
                $"Created ProBuilder {shapeStr} at ({position.x}, {position.y}, {position.z}).",
                new Dictionary<string, object>
                {
                    ["instanceID"]   = pb.gameObject.GetInstanceID(),
                    ["name"]         = pb.gameObject.name,
                    ["faceCount"]    = pb.faceCount,
                    ["vertexCount"]  = pb.vertexCount
                });
        }

        private static bool TryResolveShapeType(string shape, out ShapeType shapeType)
        {
            shapeType = ShapeType.Cube;
            return shape.ToLowerInvariant() switch
            {
                "cube"     => (shapeType = ShapeType.Cube)     == ShapeType.Cube,
                "cylinder" => (shapeType = ShapeType.Cylinder) == ShapeType.Cylinder,
                "sphere"   => (shapeType = ShapeType.Sphere)   == ShapeType.Sphere,
                "plane"    => (shapeType = ShapeType.Plane)    == ShapeType.Plane,
                "stair"    => (shapeType = ShapeType.Stair)    == ShapeType.Stair,
                "arch"     => (shapeType = ShapeType.Arch)     == ShapeType.Arch,
                "prism"    => (shapeType = ShapeType.Prism)    == ShapeType.Prism,
                "torus"    => (shapeType = ShapeType.Torus)    == ShapeType.Torus,
                _          => false
            };
        }

        #endregion

        #region Mesh Info

        private static object GetInfo(ToolParams p)
        {
            var pb = ResolveProBuilderMesh(p);
            if (pb == null)
                return new ErrorResponse("ProBuilder mesh not found. Check 'target' parameter (name or instanceID).");

            return new SuccessResponse(
                $"ProBuilder info for '{pb.gameObject.name}'.",
                new Dictionary<string, object>
                {
                    ["name"]          = pb.gameObject.name,
                    ["instanceID"]    = pb.gameObject.GetInstanceID(),
                    ["faceCount"]     = pb.faceCount,
                    ["vertexCount"]   = pb.vertexCount,
                    ["edgeCount"]     = pb.edgeCount,
                    ["positionCount"] = pb.positions?.Count ?? 0,
                    ["hasColors"]     = pb.colors != null && pb.colors.Count > 0
                });
        }

        #endregion

        #region Extrude

        private static object Extrude(ToolParams p)
        {
            var pb = ResolveProBuilderMesh(p);
            if (pb == null)
                return new ErrorResponse("ProBuilder mesh not found. Check 'target' parameter.");

            string faceIndicesStr = p.Get("face_indices");
            if (string.IsNullOrEmpty(faceIndicesStr))
                return new ErrorResponse("'face_indices' parameter is required (comma-separated face indices).");

            float distance = p.GetFloat("distance") ?? 0.5f;

            if (!TryParseFaces(pb, faceIndicesStr, out Face[] faces, out string parseError))
                return new ErrorResponse(parseError);

            Undo.RecordObject(pb, "ProBuilder Extrude");
            pb.Extrude(faces, ExtrudeMethod.FaceNormal, distance);
            pb.ToMesh();
            pb.Refresh();

            return new SuccessResponse(
                $"Extruded {faces.Length} face(s) on '{pb.gameObject.name}' by {distance}.",
                new Dictionary<string, object>
                {
                    ["faceCount"]   = pb.faceCount,
                    ["vertexCount"] = pb.vertexCount
                });
        }

        #endregion

        #region Vertex Colors

        private static object SetVertexColors(ToolParams p)
        {
            var pb = ResolveProBuilderMesh(p);
            if (pb == null)
                return new ErrorResponse("ProBuilder mesh not found. Check 'target' parameter.");

            string colorStr = p.Get("color");
            if (string.IsNullOrEmpty(colorStr))
                return new ErrorResponse("'color' parameter is required (format: 'r,g,b' or 'r,g,b,a', values 0-1).");

            if (!TryParseColor(colorStr, out Color color))
                return new ErrorResponse($"Invalid 'color' format '{colorStr}'. Expected 'r,g,b' or 'r,g,b,a'.");

            string faceIndicesStr = p.Get("face_indices");

            Undo.RecordObject(pb, "ProBuilder Set Vertex Colors");

            IEnumerable<Face> targetFaces = string.IsNullOrEmpty(faceIndicesStr)
                ? pb.faces
                : (IEnumerable<Face>)(TryParseFaces(pb, faceIndicesStr, out Face[] faces, out _) ? faces : pb.faces);

            // Ensure colors array is initialized
            var existingColors = pb.colors;
            Color[] colors = new Color[pb.vertexCount];
            if (existingColors != null && existingColors.Count == pb.vertexCount)
                for (int i = 0; i < pb.vertexCount; i++)
                    colors[i] = existingColors[i];

            foreach (Face face in targetFaces)
            {
                foreach (int idx in face.distinctIndexes)
                {
                    if (idx >= 0 && idx < colors.Length)
                        colors[idx] = color;
                }
            }

            pb.colors = colors;
            pb.ToMesh();
            pb.Refresh();

            return new SuccessResponse(
                $"Set vertex colors on '{pb.gameObject.name}'.",
                new Dictionary<string, object>
                {
                    ["color"] = $"({color.r:F2}, {color.g:F2}, {color.b:F2}, {color.a:F2})"
                });
        }

        #endregion

        #region Subdivide

        private static object Subdivide(ToolParams p)
        {
            var pb = ResolveProBuilderMesh(p);
            if (pb == null)
                return new ErrorResponse("ProBuilder mesh not found. Check 'target' parameter.");

            string faceIndicesStr = p.Get("face_indices");

            Undo.RecordObject(pb, "ProBuilder Subdivide");

            if (string.IsNullOrEmpty(faceIndicesStr))
            {
                // Subdivide all faces via ConnectElements
                pb.Connect(pb.faces);
            }
            else
            {
                if (!TryParseFaces(pb, faceIndicesStr, out Face[] faces, out string parseError))
                    return new ErrorResponse(parseError);
                pb.Connect(faces);
            }

            pb.ToMesh();
            pb.Refresh();

            return new SuccessResponse(
                $"Subdivided faces on '{pb.gameObject.name}'.",
                new Dictionary<string, object>
                {
                    ["faceCount"]   = pb.faceCount,
                    ["vertexCount"] = pb.vertexCount
                });
        }

        #endregion

        #region Merge

        private static object Merge(ToolParams p)
        {
            string targetsStr = p.Get("targets");
            if (string.IsNullOrEmpty(targetsStr))
                return new ErrorResponse("'targets' parameter is required (comma-separated GO names or instanceIDs).");

            string[] targetNames = targetsStr.Split(',')
                .Select(t => t.Trim())
                .Where(t => !string.IsNullOrEmpty(t))
                .ToArray();

            if (targetNames.Length < 2)
                return new ErrorResponse("At least 2 targets are required for merge.");

            var meshes = new List<ProBuilderMesh>();
            foreach (string target in targetNames)
            {
                GameObject go = null;
                if (int.TryParse(target, out int id))
                    go = UnityEditor.EditorUtility.InstanceIDToObject(id) as GameObject;
                if (go == null)
                    go = GameObject.Find(target);

                if (go == null)
                    return new ErrorResponse($"GameObject '{target}' not found.");

                var pb = go.GetComponent<ProBuilderMesh>();
                if (pb == null)
                    return new ErrorResponse($"GameObject '{target}' has no ProBuilderMesh component.");

                meshes.Add(pb);
            }

            var merged = CombineMeshes.Combine(meshes);
            if (merged == null || merged.Count == 0)
                return new ErrorResponse("Merge failed — CombineMeshes returned no result.");

            var result = merged[0];
            result.ToMesh();
            result.Refresh();

            Undo.RegisterCreatedObjectUndo(result.gameObject, "ProBuilder Merge");

            return new SuccessResponse(
                $"Merged {meshes.Count} meshes into '{result.gameObject.name}'.",
                new Dictionary<string, object>
                {
                    ["instanceID"]  = result.gameObject.GetInstanceID(),
                    ["name"]        = result.gameObject.name,
                    ["faceCount"]   = result.faceCount,
                    ["vertexCount"] = result.vertexCount
                });
        }

        #endregion

        #region Finalize

        private static object ToMesh(ToolParams p)
        {
            var pb = ResolveProBuilderMesh(p);
            if (pb == null)
                return new ErrorResponse("ProBuilder mesh not found. Check 'target' parameter.");

            Undo.RecordObject(pb, "ProBuilder ToMesh");
            pb.ToMesh();
            pb.Refresh();
            pb.Optimize();

            return new SuccessResponse(
                $"Finalized mesh on '{pb.gameObject.name}'.",
                new Dictionary<string, object>
                {
                    ["faceCount"]   = pb.faceCount,
                    ["vertexCount"] = pb.vertexCount
                });
        }

        #endregion

        #region Helpers

        private static ProBuilderMesh ResolveProBuilderMesh(ToolParams p)
        {
            var targetResult = p.GetRequired("target");
            if (!targetResult.IsSuccess) return null;
            string target = targetResult.Value;

            GameObject go = null;
            if (int.TryParse(target, out int id))
                go = UnityEditor.EditorUtility.InstanceIDToObject(id) as GameObject;
            if (go == null)
                go = GameObject.Find(target);
            return go?.GetComponent<ProBuilderMesh>();
        }

        private static bool TryParseVector3(string xyz, out Vector3 result)
        {
            result = Vector3.zero;
            var parts = xyz.Split(',');
            if (parts.Length < 3) return false;
            if (!float.TryParse(parts[0].Trim(), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float x)) return false;
            if (!float.TryParse(parts[1].Trim(), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float y)) return false;
            if (!float.TryParse(parts[2].Trim(), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float z)) return false;
            result = new Vector3(x, y, z);
            return true;
        }

        private static bool TryParseColor(string rgba, out Color color)
        {
            color = Color.white;
            var parts = rgba.Split(',');
            if (parts.Length < 3) return false;
            if (!float.TryParse(parts[0].Trim(), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float r)) return false;
            if (!float.TryParse(parts[1].Trim(), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float g)) return false;
            if (!float.TryParse(parts[2].Trim(), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float b)) return false;
            float a = 1f;
            if (parts.Length > 3)
                float.TryParse(parts[3].Trim(), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out a);
            color = new Color(r, g, b, a);
            return true;
        }

        private static bool TryParseFaces(ProBuilderMesh pb, string faceIndicesStr, out Face[] faces, out string error)
        {
            faces = null;
            error = null;
            var allFaces = pb.faces.ToArray();
            var result   = new List<Face>();

            foreach (string part in faceIndicesStr.Split(','))
            {
                string trimmed = part.Trim();
                if (!int.TryParse(trimmed, out int idx))
                {
                    error = $"Invalid face index '{trimmed}'. Must be an integer.";
                    return false;
                }
                if (idx < 0 || idx >= allFaces.Length)
                {
                    error = $"Face index {idx} out of range. Mesh has {allFaces.Length} face(s).";
                    return false;
                }
                result.Add(allFaces[idx]);
            }

            faces = result.ToArray();
            return true;
        }

        #endregion
    }
}
#endif
