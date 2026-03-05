using System;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json.Linq;
using MCPForUnity.Editor.Helpers;
using UnityEditor;
using UnityEngine;

namespace MCPForUnity.Editor.Tools.ProBuilder
{
    internal static class ProBuilderMeshUtils
    {
        internal static object CenterPivot(JObject @params)
        {
            var pbMesh = ManageProBuilder.RequireProBuilderMesh(@params);

            var positionsProp = ManageProBuilder._proBuilderMeshType.GetProperty("positions");
            var positions = positionsProp?.GetValue(pbMesh) as System.Collections.IList;
            if (positions == null || positions.Count == 0)
                return new ErrorResponse("Could not read vertex positions.");

            // Compute local-space bounds center
            var min = (Vector3)positions[0];
            var max = min;
            foreach (Vector3 pos in positions)
            {
                min = Vector3.Min(min, pos);
                max = Vector3.Max(max, pos);
            }
            var localCenter = (min + max) * 0.5f;

            if (localCenter.sqrMagnitude < 0.0001f)
                return new SuccessResponse("Pivot is already centered", new { offset = new[] { 0f, 0f, 0f } });

            Undo.RecordObject(pbMesh, "Center Pivot");
            Undo.RecordObject(pbMesh.transform, "Center Pivot");

            // Offset all vertices by -localCenter
            var newPositions = new Vector3[positions.Count];
            for (int i = 0; i < positions.Count; i++)
                newPositions[i] = (Vector3)positions[i] - localCenter;

            // Set positions via property setter
            SetVertexPositions(pbMesh, newPositions);


            // Move transform to compensate
            var worldOffset = pbMesh.transform.TransformVector(localCenter);
            pbMesh.transform.position += worldOffset;

            ManageProBuilder.RefreshMesh(pbMesh);

            return new SuccessResponse("Pivot centered to mesh bounds center", new
            {
                offset = new[] { Round(localCenter.x), Round(localCenter.y), Round(localCenter.z) },
                newPosition = new[]
                {
                    Round(pbMesh.transform.position.x),
                    Round(pbMesh.transform.position.y),
                    Round(pbMesh.transform.position.z),
                },
            });
        }

        internal static object FreezeTransform(JObject @params)
        {
            var pbMesh = ManageProBuilder.RequireProBuilderMesh(@params);

            var positionsProp = ManageProBuilder._proBuilderMeshType.GetProperty("positions");
            var positions = positionsProp?.GetValue(pbMesh) as System.Collections.IList;
            if (positions == null || positions.Count == 0)
                return new ErrorResponse("Could not read vertex positions.");

            Undo.RecordObject(pbMesh, "Freeze Transform");
            Undo.RecordObject(pbMesh.transform, "Freeze Transform");

            // Transform each vertex to world space, then back to identity local space
            var worldPositions = new Vector3[positions.Count];
            for (int i = 0; i < positions.Count; i++)
                worldPositions[i] = pbMesh.transform.TransformPoint((Vector3)positions[i]);

            // Reset transform
            pbMesh.transform.position = Vector3.zero;
            pbMesh.transform.rotation = Quaternion.identity;
            pbMesh.transform.localScale = Vector3.one;

            // Set new positions (now in world space = new local space since identity)
            SetVertexPositions(pbMesh, worldPositions);

            ManageProBuilder.RefreshMesh(pbMesh);

            return new SuccessResponse("Transform frozen into vertex data", new
            {
                vertexCount = worldPositions.Length,
            });
        }

        internal static object ValidateMesh(JObject @params)
        {
            var pbMesh = ManageProBuilder.RequireProBuilderMesh(@params);

            var positionsProp = ManageProBuilder._proBuilderMeshType.GetProperty("positions");
            var positions = positionsProp?.GetValue(pbMesh) as System.Collections.IList;
            var allFaces = ManageProBuilder.GetFacesArray(pbMesh);
            var facesList = (System.Collections.IList)allFaces;

            int degenerateCount = 0;
            var indexesProp = ManageProBuilder._faceType.GetProperty("indexes");

            if (indexesProp != null && positions != null)
            {
                foreach (var face in facesList)
                {
                    var indexes = indexesProp.GetValue(face) as System.Collections.IList;
                    if (indexes == null) continue;

                    // Check triangles in groups of 3
                    for (int i = 0; i + 2 < indexes.Count; i += 3)
                    {
                        var p0 = (Vector3)positions[(int)indexes[i]];
                        var p1 = (Vector3)positions[(int)indexes[i + 1]];
                        var p2 = (Vector3)positions[(int)indexes[i + 2]];

                        var area = Vector3.Cross(p1 - p0, p2 - p0).magnitude * 0.5f;
                        if (area < 1e-6f)
                            degenerateCount++;
                    }
                }
            }

            // Check for unused vertices
            var usedVertices = new HashSet<int>();
            if (indexesProp != null)
            {
                foreach (var face in facesList)
                {
                    var indexes = indexesProp.GetValue(face) as System.Collections.IList;
                    if (indexes == null) continue;
                    foreach (int idx in indexes)
                        usedVertices.Add(idx);
                }
            }

            int totalVertices = positions?.Count ?? 0;
            int unusedVertices = totalVertices - usedVertices.Count;

            var issues = new List<string>();
            if (degenerateCount > 0)
                issues.Add($"{degenerateCount} degenerate triangle(s)");
            if (unusedVertices > 0)
                issues.Add($"{unusedVertices} unused vertex/vertices");

            return new SuccessResponse(
                issues.Count == 0 ? "Mesh is clean" : $"Found {issues.Count} issue type(s)",
                new
                {
                    healthy = issues.Count == 0,
                    faceCount = facesList.Count,
                    vertexCount = totalVertices,
                    degenerateTriangles = degenerateCount,
                    unusedVertices,
                    issues,
                });
        }

        internal static object SetPivot(JObject @params)
        {
            var pbMesh = ManageProBuilder.RequireProBuilderMesh(@params);
            var props = ManageProBuilder.ExtractProperties(@params);

            var posToken = props["position"] ?? props["worldPosition"] ?? props["world_position"];
            if (posToken == null)
                return new ErrorResponse("position parameter is required ([x,y,z] in world space).");

            var worldPosition = VectorParsing.ParseVector3OrDefault(posToken);

            Undo.RecordObject(pbMesh, "Set Pivot");
            Undo.RecordObject(pbMesh.transform, "Set Pivot");

            // SetPivot moves the transform without moving the geometry visually.
            // We need to offset vertex positions by the inverse of the transform change.
            var positionsProp = ManageProBuilder._proBuilderMeshType.GetProperty("positions");
            var positions = positionsProp?.GetValue(pbMesh) as System.Collections.IList;
            if (positions == null || positions.Count == 0)
                return new ErrorResponse("Could not read vertex positions.");

            // Calculate delta in local space
            var worldDelta = worldPosition - pbMesh.transform.position;
            var localDelta = pbMesh.transform.InverseTransformVector(worldDelta);

            // Offset all vertices by -localDelta to keep them in place visually
            var newPositions = new Vector3[positions.Count];
            for (int i = 0; i < positions.Count; i++)
                newPositions[i] = (Vector3)positions[i] - localDelta;

            SetVertexPositions(pbMesh, newPositions);

            // Move transform to new pivot position
            pbMesh.transform.position = worldPosition;

            ManageProBuilder.RefreshMesh(pbMesh);

            return new SuccessResponse("Pivot set to world position", new
            {
                position = new[] { Round(worldPosition.x), Round(worldPosition.y), Round(worldPosition.z) },
            });
        }

        private static void SetVertexPositions(Component pbMesh, Vector3[] positions)
        {
            var positionsProp = ManageProBuilder._proBuilderMeshType.GetProperty("positions");
            if (positionsProp != null && positionsProp.CanWrite)
                positionsProp.SetValue(pbMesh, new List<Vector3>(positions));
        }

        internal static object RepairMesh(JObject @params)
        {
            var pbMesh = ManageProBuilder.RequireProBuilderMesh(@params);

            Undo.RecordObject(pbMesh, "Repair Mesh");

            int repaired = 0;

            // Try MeshValidation.RemoveDegenerateTriangles
            if (ManageProBuilder._meshValidationType != null)
            {
                var removeMethod = ManageProBuilder._meshValidationType.GetMethod("RemoveDegenerateTriangles",
                    BindingFlags.Static | BindingFlags.Public);

                if (removeMethod != null)
                {
                    var allFaces = ManageProBuilder.GetFacesArray(pbMesh);
                    try
                    {
                        var result = removeMethod.Invoke(null, new object[] { pbMesh, allFaces });
                        if (result is int count)
                            repaired = count;
                    }
                    catch
                    {
                        // Some overloads differ; try without faces param
                        try
                        {
                            var altMethod = ManageProBuilder._meshValidationType.GetMethod("RemoveDegenerateTriangles",
                                BindingFlags.Static | BindingFlags.Public,
                                null,
                                new[] { ManageProBuilder._proBuilderMeshType },
                                null);
                            if (altMethod != null)
                            {
                                var result = altMethod.Invoke(null, new object[] { pbMesh });
                                if (result is int count)
                                    repaired = count;
                            }
                        }
                        catch
                        {
                            // Ignore fallback failure
                        }
                    }
                }
            }

            ManageProBuilder.RefreshMesh(pbMesh);

            return new SuccessResponse(
                repaired > 0 ? $"Repaired {repaired} degenerate triangle(s)" : "No repairs needed",
                new
                {
                    degenerateTrianglesRemoved = repaired,
                    faceCount = ManageProBuilder.GetFaceCount(pbMesh),
                    vertexCount = ManageProBuilder.GetVertexCount(pbMesh),
                });
        }

        private static float Round(float v) => (float)Math.Round(v, 4);
    }
}
