using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using MCPForUnity.Editor.Helpers;
using UnityEditor;
using UnityEngine;

namespace MCPForUnity.Editor.Tools.ProBuilder
{
    /// <summary>
    /// Tool for managing Unity ProBuilder meshes for in-editor 3D modeling.
    /// Requires com.unity.probuilder package to be installed.
    ///
    /// SHAPE CREATION:
    ///   - create_shape: Create ProBuilder primitive (shapeType, size/radius/height, position, rotation, name)
    ///     Shape types: Cube, Cylinder, Sphere, Plane, Cone, Torus, Pipe, Arch, Stair, CurvedStair, Door, Prism
    ///   - create_poly_shape: Create from 2D polygon footprint (points, extrudeHeight, flipNormals)
    ///
    /// MESH EDITING:
    ///   - extrude_faces: Extrude faces (faceIndices, distance, method: FaceNormal/VertexNormal/IndividualFaces)
    ///   - extrude_edges: Extrude edges (edgeIndices, distance, asGroup)
    ///   - bevel_edges: Bevel edges (edgeIndices, amount 0-1)
    ///   - subdivide: Subdivide faces (faceIndices optional)
    ///   - delete_faces: Delete faces (faceIndices)
    ///   - bridge_edges: Bridge two open edges (edgeA, edgeB as {a,b} pairs)
    ///   - connect_elements: Connect edges/faces (edgeIndices or faceIndices)
    ///   - detach_faces: Detach faces to new object (faceIndices, deleteSource)
    ///   - flip_normals: Flip face normals (faceIndices)
    ///   - merge_faces: Merge faces into one (faceIndices)
    ///   - combine_meshes: Combine ProBuilder objects (targets list)
    ///   - merge_objects: Merge objects (auto-converts non-ProBuilder), convenience wrapper (targets, name)
    ///
    /// VERTEX OPERATIONS:
    ///   - merge_vertices: Merge/weld vertices (vertexIndices)
    ///   - split_vertices: Split shared vertices (vertexIndices)
    ///   - move_vertices: Translate vertices (vertexIndices, offset [x,y,z])
    ///
    /// UV &amp; MATERIALS:
    ///   - set_face_material: Assign material to faces (faceIndices, materialPath)
    ///   - set_face_color: Set vertex color (faceIndices, color [r,g,b,a])
    ///   - set_face_uvs: Set UV params (faceIndices, scale, offset, rotation, flipU, flipV)
    ///
    /// QUERY:
    ///   - get_mesh_info: Get mesh details (face count, vertex count, bounds, materials)
    ///   - convert_to_probuilder: Convert standard mesh to ProBuilder
    /// </summary>
    [McpForUnityTool("manage_probuilder", AutoRegister = false, Group = "probuilder")]
    public static class ManageProBuilder
    {
        // ProBuilder types resolved via reflection (optional package)
        internal static Type _proBuilderMeshType;
        private static Type _shapeGeneratorType;
        internal static Type _shapeTypeEnum;
        private static Type _extrudeMethodEnum;
        private static Type _extrudeElementsType;
        private static Type _bevelType;
        private static Type _deleteElementsType;
        private static Type _appendElementsType;
        private static Type _connectElementsType;
        private static Type _mergeElementsType;
        private static Type _combineMeshesType;
        private static Type _surfaceTopologyType;
        internal static Type _faceType;
        internal static Type _edgeType;
        private static Type _editorMeshUtilityType;
        private static Type _meshImporterType;
        internal static Type _smoothingType;
        internal static Type _meshValidationType;
        private static bool _typesResolved;
        private static bool _proBuilderAvailable;

        private static bool EnsureProBuilder()
        {
            if (_typesResolved) return _proBuilderAvailable;
            _typesResolved = true;

            _proBuilderMeshType = Type.GetType("UnityEngine.ProBuilder.ProBuilderMesh, Unity.ProBuilder");
            if (_proBuilderMeshType == null)
            {
                _proBuilderAvailable = false;
                return false;
            }

            _shapeGeneratorType = Type.GetType("UnityEngine.ProBuilder.ShapeGenerator, Unity.ProBuilder");
            _shapeTypeEnum = Type.GetType("UnityEngine.ProBuilder.ShapeType, Unity.ProBuilder");
            _faceType = Type.GetType("UnityEngine.ProBuilder.Face, Unity.ProBuilder");
            _edgeType = Type.GetType("UnityEngine.ProBuilder.Edge, Unity.ProBuilder");

            // MeshOperations
            _extrudeElementsType = Type.GetType("UnityEngine.ProBuilder.MeshOperations.ExtrudeElements, Unity.ProBuilder");
            _extrudeMethodEnum = Type.GetType("UnityEngine.ProBuilder.ExtrudeMethod, Unity.ProBuilder");
            _bevelType = Type.GetType("UnityEngine.ProBuilder.MeshOperations.Bevel, Unity.ProBuilder");
            _deleteElementsType = Type.GetType("UnityEngine.ProBuilder.MeshOperations.DeleteElements, Unity.ProBuilder");
            _appendElementsType = Type.GetType("UnityEngine.ProBuilder.MeshOperations.AppendElements, Unity.ProBuilder");
            _connectElementsType = Type.GetType("UnityEngine.ProBuilder.MeshOperations.ConnectElements, Unity.ProBuilder");
            _mergeElementsType = Type.GetType("UnityEngine.ProBuilder.MeshOperations.MergeElements, Unity.ProBuilder");
            _combineMeshesType = Type.GetType("UnityEngine.ProBuilder.MeshOperations.CombineMeshes, Unity.ProBuilder");
            _surfaceTopologyType = Type.GetType("UnityEngine.ProBuilder.MeshOperations.SurfaceTopology, Unity.ProBuilder");

            // Editor utilities
            _editorMeshUtilityType = Type.GetType("UnityEditor.ProBuilder.EditorMeshUtility, Unity.ProBuilder.Editor");
            _meshImporterType = Type.GetType("UnityEngine.ProBuilder.MeshOperations.MeshImporter, Unity.ProBuilder");
            _smoothingType = Type.GetType("UnityEngine.ProBuilder.Smoothing, Unity.ProBuilder");
            _meshValidationType = Type.GetType("UnityEngine.ProBuilder.MeshOperations.MeshValidation, Unity.ProBuilder");

            _proBuilderAvailable = true;
            return true;
        }

        public static object HandleCommand(JObject @params)
        {
            if (!EnsureProBuilder())
            {
                return new ErrorResponse(
                    "ProBuilder package is not installed. Install com.unity.probuilder via Package Manager."
                );
            }

            var p = new ToolParams(@params);
            string action = p.Get("action");
            if (string.IsNullOrEmpty(action))
                return new ErrorResponse("Action is required");

            try
            {
                switch (action.ToLowerInvariant())
                {
                    case "ping":
                        return new SuccessResponse("ProBuilder tool is available", new { tool = "manage_probuilder" });

                    // Shape creation
                    case "create_shape": return CreateShape(@params);
                    case "create_poly_shape": return CreatePolyShape(@params);

                    // Mesh editing
                    case "extrude_faces": return ExtrudeFaces(@params);
                    case "extrude_edges": return ExtrudeEdges(@params);
                    case "bevel_edges": return BevelEdges(@params);
                    case "subdivide": return Subdivide(@params);
                    case "delete_faces": return DeleteFaces(@params);
                    case "bridge_edges": return BridgeEdges(@params);
                    case "connect_elements": return ConnectElements(@params);
                    case "detach_faces": return DetachFaces(@params);
                    case "flip_normals": return FlipNormals(@params);
                    case "merge_faces": return MergeFaces(@params);
                    case "combine_meshes": return CombineMeshes(@params);
                    case "merge_objects": return MergeObjects(@params);

                    // Vertex operations
                    case "merge_vertices": return MergeVertices(@params);
                    case "split_vertices": return SplitVertices(@params);
                    case "move_vertices": return MoveVertices(@params);

                    // UV & materials
                    case "set_face_material": return SetFaceMaterial(@params);
                    case "set_face_color": return SetFaceColor(@params);
                    case "set_face_uvs": return SetFaceUVs(@params);

                    // Query
                    case "get_mesh_info": return GetMeshInfo(@params);
                    case "convert_to_probuilder": return ConvertToProBuilder(@params);

                    // Smoothing
                    case "set_smoothing": return ProBuilderSmoothing.SetSmoothing(@params);
                    case "auto_smooth": return ProBuilderSmoothing.AutoSmooth(@params);

                    // Mesh utilities
                    case "center_pivot": return ProBuilderMeshUtils.CenterPivot(@params);
                    case "freeze_transform": return ProBuilderMeshUtils.FreezeTransform(@params);
                    case "validate_mesh": return ProBuilderMeshUtils.ValidateMesh(@params);
                    case "repair_mesh": return ProBuilderMeshUtils.RepairMesh(@params);

                    default:
                        return new ErrorResponse($"Unknown action: {action}");
                }
            }
            catch (Exception ex)
            {
                return new ErrorResponse(ex.Message, new { stackTrace = ex.StackTrace });
            }
        }

        // =====================================================================
        // Helpers
        // =====================================================================

        internal static GameObject FindTarget(JObject @params)
        {
            return ObjectResolver.ResolveGameObject(@params["target"], @params["searchMethod"]?.ToString());
        }

        private static Component GetProBuilderMesh(GameObject go)
        {
            return go.GetComponent(_proBuilderMeshType);
        }

        internal static Component RequireProBuilderMesh(JObject @params)
        {
            var go = FindTarget(@params);
            if (go == null)
                throw new Exception("Target GameObject not found.");
            var pbMesh = GetProBuilderMesh(go);
            if (pbMesh == null)
                throw new Exception($"GameObject '{go.name}' does not have a ProBuilderMesh component.");
            return pbMesh;
        }

        internal static void RefreshMesh(Component pbMesh)
        {
            _proBuilderMeshType.GetMethod("ToMesh", Type.EmptyTypes)?.Invoke(pbMesh, null);
            _proBuilderMeshType.GetMethod("Refresh", Type.EmptyTypes)?.Invoke(pbMesh, null);

            if (_editorMeshUtilityType != null)
            {
                var optimizeMethod = _editorMeshUtilityType.GetMethod("Optimize",
                    BindingFlags.Static | BindingFlags.Public,
                    null,
                    new[] { _proBuilderMeshType },
                    null);
                optimizeMethod?.Invoke(null, new object[] { pbMesh });
            }
        }

        internal static object GetFacesArray(Component pbMesh)
        {
            var facesProperty = _proBuilderMeshType.GetProperty("faces");
            return facesProperty?.GetValue(pbMesh);
        }

        internal static Array GetFacesByIndices(Component pbMesh, JToken faceIndicesToken)
        {
            var allFaces = GetFacesArray(pbMesh);
            if (allFaces == null)
                throw new Exception("Could not read faces from ProBuilderMesh.");

            var facesList = (System.Collections.IList)allFaces;

            if (faceIndicesToken == null)
            {
                // Return all faces when no indices specified
                var allResult = Array.CreateInstance(_faceType, facesList.Count);
                for (int i = 0; i < facesList.Count; i++)
                    allResult.SetValue(facesList[i], i);
                return allResult;
            }

            var indices = faceIndicesToken.ToObject<int[]>();
            var result = Array.CreateInstance(_faceType, indices.Length);
            for (int i = 0; i < indices.Length; i++)
            {
                if (indices[i] < 0 || indices[i] >= facesList.Count)
                    throw new Exception($"Face index {indices[i]} out of range (0-{facesList.Count - 1}).");
                result.SetValue(facesList[indices[i]], i);
            }
            return result;
        }

        internal static JObject ExtractProperties(JObject @params)
        {
            var propsToken = @params["properties"];
            if (propsToken is JObject jObj) return jObj;
            if (propsToken is JValue jVal && jVal.Type == JTokenType.String)
            {
                var parsed = JObject.Parse(jVal.ToString());
                if (parsed != null) return parsed;
            }

            // Fallback: properties might be at the top level
            return @params;
        }

        private static Vector3 ParseVector3(JToken token)
        {
            return VectorParsing.ParseVector3OrDefault(token);
        }

        internal static int GetFaceCount(Component pbMesh)
        {
            var faceCount = _proBuilderMeshType.GetProperty("faceCount");
            return faceCount != null ? (int)faceCount.GetValue(pbMesh) : -1;
        }

        internal static int GetVertexCount(Component pbMesh)
        {
            var vertexCount = _proBuilderMeshType.GetProperty("vertexCount");
            return vertexCount != null ? (int)vertexCount.GetValue(pbMesh) : -1;
        }

        // =====================================================================
        // Shape Creation
        // =====================================================================

        private static object CreateShape(JObject @params)
        {
            var props = ExtractProperties(@params);
            string shapeTypeStr = props["shapeType"]?.ToString() ?? props["shape_type"]?.ToString();
            if (string.IsNullOrEmpty(shapeTypeStr))
                return new ErrorResponse("shapeType parameter is required.");

            if (_shapeGeneratorType == null || _shapeTypeEnum == null)
                return new ErrorResponse("ShapeGenerator or ShapeType not found in ProBuilder assembly.");

            // Parse shape type enum
            object shapeTypeValue;
            try
            {
                shapeTypeValue = Enum.Parse(_shapeTypeEnum, shapeTypeStr, true);
            }
            catch
            {
                var validTypes = string.Join(", ", Enum.GetNames(_shapeTypeEnum));
                return new ErrorResponse($"Unknown shape type '{shapeTypeStr}'. Valid types: {validTypes}");
            }

            // Use ShapeGenerator.CreateShape(ShapeType) or CreateShape(ShapeType, PivotLocation)
            var createMethod = _shapeGeneratorType.GetMethod("CreateShape",
                BindingFlags.Static | BindingFlags.Public,
                null,
                new[] { _shapeTypeEnum },
                null);

            // Fallback: look for overload with PivotLocation (ProBuilder 4.x+)
            object[] invokeArgs;
            if (createMethod != null)
            {
                invokeArgs = new[] { shapeTypeValue };
            }
            else
            {
                var pivotLocationType = Type.GetType("UnityEngine.ProBuilder.PivotLocation, Unity.ProBuilder");
                if (pivotLocationType != null)
                {
                    createMethod = _shapeGeneratorType.GetMethod("CreateShape",
                        BindingFlags.Static | BindingFlags.Public,
                        null,
                        new[] { _shapeTypeEnum, pivotLocationType },
                        null);
                    // PivotLocation.Center = 0
                    invokeArgs = new[] { shapeTypeValue, Enum.ToObject(pivotLocationType, 0) };
                }
                else
                {
                    invokeArgs = null;
                }
            }

            if (createMethod == null)
                return new ErrorResponse("ShapeGenerator.CreateShape method not found. Check your ProBuilder version.");

            Undo.IncrementCurrentGroup();
            var pbMesh = createMethod.Invoke(null, invokeArgs) as Component;
            if (pbMesh == null)
                return new ErrorResponse("Failed to create ProBuilder shape.");

            var go = pbMesh.gameObject;
            Undo.RegisterCreatedObjectUndo(go, $"Create ProBuilder {shapeTypeStr}");

            // Apply name
            string name = props["name"]?.ToString();
            if (!string.IsNullOrEmpty(name))
                go.name = name;

            // Apply position
            var posToken = props["position"];
            if (posToken != null)
                go.transform.position = ParseVector3(posToken);

            // Apply rotation
            var rotToken = props["rotation"];
            if (rotToken != null)
                go.transform.eulerAngles = ParseVector3(rotToken);

            // Apply size/dimensions via scale (ShapeGenerator creates shapes with known defaults)
            ApplyShapeDimensions(go, shapeTypeStr, props);

            RefreshMesh(pbMesh);

            return new SuccessResponse($"Created ProBuilder {shapeTypeStr}: {go.name}", new
            {
                gameObjectName = go.name,
                instanceId = go.GetInstanceID(),
                shapeType = shapeTypeStr,
                faceCount = GetFaceCount(pbMesh),
                vertexCount = GetVertexCount(pbMesh),
            });
        }

        private static void ApplyShapeDimensions(GameObject go, string shapeType, JObject props)
        {
            float size = props["size"]?.Value<float>() ?? 0;
            float width = props["width"]?.Value<float>() ?? 0;
            float height = props["height"]?.Value<float>() ?? 0;
            float depth = props["depth"]?.Value<float>() ?? 0;
            float radius = props["radius"]?.Value<float>() ?? 0;

            if (size <= 0 && width <= 0 && height <= 0 && depth <= 0 && radius <= 0)
                return;

            // Each shape type has known default dimensions from ProBuilder's ShapeGenerator.
            // We compute a scale factor relative to those defaults.
            Vector3 scale;
            string shapeUpper = shapeType.ToUpperInvariant();

            switch (shapeUpper)
            {
                case "CUBE":
                    // Default: 1x1x1
                    scale = new Vector3(
                        width > 0 ? width : (size > 0 ? size : 1f),
                        height > 0 ? height : (size > 0 ? size : 1f),
                        depth > 0 ? depth : (size > 0 ? size : 1f));
                    break;

                case "PRISM":
                    // Default: 1x1x1
                    scale = new Vector3(
                        width > 0 ? width : (size > 0 ? size : 1f),
                        height > 0 ? height : (size > 0 ? size : 1f),
                        depth > 0 ? depth : (size > 0 ? size : 1f));
                    break;

                case "CYLINDER":
                    // Default: radius=0.5 (diameter=1), height=2
                    float cylRadius = radius > 0 ? radius : (size > 0 ? size / 2f : 0.5f);
                    float cylHeight = height > 0 ? height : (size > 0 ? size : 2f);
                    scale = new Vector3(cylRadius / 0.5f, cylHeight / 2f, cylRadius / 0.5f);
                    break;

                case "CONE":
                    // Default: 1x1x1 (radius 0.5)
                    float coneRadius = radius > 0 ? radius : (size > 0 ? size / 2f : 0.5f);
                    float coneHeight = height > 0 ? height : (size > 0 ? size : 1f);
                    scale = new Vector3(coneRadius / 0.5f, coneHeight, coneRadius / 0.5f);
                    break;

                case "SPHERE":
                    // Default: radius=0.5 (diameter=1)
                    float sphereRadius = radius > 0 ? radius : (size > 0 ? size / 2f : 0.5f);
                    scale = Vector3.one * (sphereRadius / 0.5f);
                    break;

                case "TORUS":
                    // Default: fits in ~1x1x1
                    float torusScale = radius > 0 ? radius * 2f : (size > 0 ? size : 1f);
                    scale = Vector3.one * torusScale;
                    break;

                case "ARCH":
                    // Default: approximately 4x2x1
                    scale = new Vector3(
                        width > 0 ? width / 4f : (size > 0 ? size / 4f : 1f),
                        height > 0 ? height / 2f : (size > 0 ? size / 2f : 1f),
                        depth > 0 ? depth : (size > 0 ? size : 1f));
                    break;

                case "STAIR":
                    // Default: approximately 2x2.5x4
                    scale = new Vector3(
                        width > 0 ? width / 2f : (size > 0 ? size / 2f : 1f),
                        height > 0 ? height / 2.5f : (size > 0 ? size / 2.5f : 1f),
                        depth > 0 ? depth / 4f : (size > 0 ? size / 4f : 1f));
                    break;

                case "CURVEDSTAIR":
                    // Default: similar to stair
                    scale = new Vector3(
                        width > 0 ? width / 2f : (size > 0 ? size / 2f : 1f),
                        height > 0 ? height / 2.5f : (size > 0 ? size / 2.5f : 1f),
                        depth > 0 ? depth / 2f : (size > 0 ? size / 2f : 1f));
                    break;

                case "PIPE":
                    // Default: radius=1, height=2
                    float pipeRadius = radius > 0 ? radius : (size > 0 ? size / 2f : 1f);
                    float pipeHeight = height > 0 ? height : (size > 0 ? size : 2f);
                    scale = new Vector3(pipeRadius, pipeHeight / 2f, pipeRadius);
                    break;

                case "PLANE":
                    // Default: 1x1
                    float planeSize = size > 0 ? size : 1f;
                    scale = new Vector3(
                        width > 0 ? width : planeSize,
                        1f,
                        depth > 0 ? depth : planeSize);
                    break;

                case "DOOR":
                    // Default: approximately 4x4x1
                    scale = new Vector3(
                        width > 0 ? width / 4f : (size > 0 ? size / 4f : 1f),
                        height > 0 ? height / 4f : (size > 0 ? size / 4f : 1f),
                        depth > 0 ? depth : (size > 0 ? size : 1f));
                    break;

                default:
                    // Generic fallback: uniform scale from size
                    if (size > 0)
                        scale = Vector3.one * size;
                    else
                        return; // No dimensions to apply
                    break;
            }

            go.transform.localScale = scale;
        }

        private static object CreatePolyShape(JObject @params)
        {
            var props = ExtractProperties(@params);
            var pointsToken = props["points"];
            if (pointsToken == null)
                return new ErrorResponse("points parameter is required.");

            var points = new List<Vector3>();
            foreach (var pt in pointsToken)
                points.Add(ParseVector3(pt));

            if (points.Count < 3)
                return new ErrorResponse("At least 3 points are required for a poly shape.");

            float extrudeHeight = props["extrudeHeight"]?.Value<float>() ?? props["extrude_height"]?.Value<float>() ?? 1f;
            bool flipNormals = props["flipNormals"]?.Value<bool>() ?? props["flip_normals"]?.Value<bool>() ?? false;

            // Create a new GameObject with ProBuilderMesh
            var go = new GameObject("PolyShape");
            Undo.RegisterCreatedObjectUndo(go, "Create ProBuilder PolyShape");
            var pbMesh = go.AddComponent(_proBuilderMeshType);

            // Use AppendElements.CreateShapeFromPolygon
            if (_appendElementsType == null)
            {
                UnityEngine.Object.DestroyImmediate(go);
                return new ErrorResponse("AppendElements type not found in ProBuilder assembly.");
            }

            var createFromPolygonMethod = _appendElementsType.GetMethod("CreateShapeFromPolygon",
                BindingFlags.Static | BindingFlags.Public,
                null,
                new[] { _proBuilderMeshType, typeof(IList<Vector3>), typeof(float), typeof(bool) },
                null);

            if (createFromPolygonMethod == null)
            {
                UnityEngine.Object.DestroyImmediate(go);
                return new ErrorResponse("CreateShapeFromPolygon method not found.");
            }

            var actionResult = createFromPolygonMethod.Invoke(null, new object[] { pbMesh, points, extrudeHeight, flipNormals });

            string name = props["name"]?.ToString();
            if (!string.IsNullOrEmpty(name))
                go.name = name;

            RefreshMesh(pbMesh);

            return new SuccessResponse($"Created poly shape: {go.name}", new
            {
                gameObjectName = go.name,
                instanceId = go.GetInstanceID(),
                pointCount = points.Count,
                extrudeHeight,
                faceCount = GetFaceCount(pbMesh),
                vertexCount = GetVertexCount(pbMesh),
            });
        }

        // =====================================================================
        // Mesh Editing
        // =====================================================================

        private static object ExtrudeFaces(JObject @params)
        {
            var pbMesh = RequireProBuilderMesh(@params);
            var props = ExtractProperties(@params);
            var faces = GetFacesByIndices(pbMesh, props["faceIndices"] ?? props["face_indices"]);
            float distance = props["distance"]?.Value<float>() ?? 0.5f;

            string methodStr = props["method"]?.ToString() ?? "FaceNormal";
            object extrudeMethod;
            try
            {
                extrudeMethod = Enum.Parse(_extrudeMethodEnum, methodStr, true);
            }
            catch
            {
                return new ErrorResponse($"Unknown extrude method '{methodStr}'. Valid: FaceNormal, VertexNormal, IndividualFaces");
            }

            Undo.RegisterCompleteObjectUndo(pbMesh, "Extrude Faces");

            var extrudeMethodInfo = _extrudeElementsType?.GetMethod("Extrude",
                BindingFlags.Static | BindingFlags.Public,
                null,
                new[] { _proBuilderMeshType, faces.GetType(), _extrudeMethodEnum, typeof(float) },
                null);

            if (extrudeMethodInfo == null)
                return new ErrorResponse("ExtrudeElements.Extrude method not found.");

            extrudeMethodInfo.Invoke(null, new object[] { pbMesh, faces, extrudeMethod, distance });
            RefreshMesh(pbMesh);

            return new SuccessResponse($"Extruded {faces.Length} face(s) by {distance}", new
            {
                facesExtruded = faces.Length,
                distance,
                method = methodStr,
                faceCount = GetFaceCount(pbMesh),
            });
        }

        private static object ExtrudeEdges(JObject @params)
        {
            var pbMesh = RequireProBuilderMesh(@params);
            var props = ExtractProperties(@params);
            var edgeIndicesToken = props["edgeIndices"] ?? props["edge_indices"];
            if (edgeIndicesToken == null)
                return new ErrorResponse("edgeIndices parameter is required.");

            float distance = props["distance"]?.Value<float>() ?? 0.5f;
            bool asGroup = props["asGroup"]?.Value<bool>() ?? props["as_group"]?.Value<bool>() ?? true;

            var edgeIndices = edgeIndicesToken.ToObject<int[]>();

            // Get edges from the mesh
            var edgesProperty = _proBuilderMeshType.GetProperty("faces");
            var allFaces = (System.Collections.IList)edgesProperty?.GetValue(pbMesh);
            if (allFaces == null)
                return new ErrorResponse("Could not read faces from mesh.");

            // Collect edges from specified indices
            var edgeList = new List<object>();
            var allEdges = new List<object>();

            // Get all edges via face edges
            foreach (var face in allFaces)
            {
                var edgesProp = _faceType.GetProperty("edges");
                if (edgesProp != null)
                {
                    var faceEdges = edgesProp.GetValue(face) as System.Collections.IList;
                    if (faceEdges != null)
                    {
                        foreach (var edge in faceEdges)
                            allEdges.Add(edge);
                    }
                }
            }

            foreach (int idx in edgeIndices)
            {
                if (idx < 0 || idx >= allEdges.Count)
                    return new ErrorResponse($"Edge index {idx} out of range (0-{allEdges.Count - 1}).");
                edgeList.Add(allEdges[idx]);
            }

            Undo.RegisterCompleteObjectUndo(pbMesh, "Extrude Edges");

            var edgeArray = Array.CreateInstance(_edgeType, edgeList.Count);
            for (int i = 0; i < edgeList.Count; i++)
                edgeArray.SetValue(edgeList[i], i);

            var extrudeMethod = _extrudeElementsType?.GetMethod("Extrude",
                BindingFlags.Static | BindingFlags.Public,
                null,
                new[] { _proBuilderMeshType, edgeArray.GetType(), typeof(float), typeof(bool), typeof(bool) },
                null);

            if (extrudeMethod == null)
                return new ErrorResponse("ExtrudeElements.Extrude (edges) method not found.");

            extrudeMethod.Invoke(null, new object[] { pbMesh, edgeArray, distance, asGroup, true });
            RefreshMesh(pbMesh);

            return new SuccessResponse($"Extruded {edgeList.Count} edge(s) by {distance}", new
            {
                edgesExtruded = edgeList.Count,
                distance,
                faceCount = GetFaceCount(pbMesh),
            });
        }

        private static object BevelEdges(JObject @params)
        {
            var pbMesh = RequireProBuilderMesh(@params);
            var props = ExtractProperties(@params);
            var edgeIndicesToken = props["edgeIndices"] ?? props["edge_indices"];
            if (edgeIndicesToken == null)
                return new ErrorResponse("edgeIndices parameter is required.");

            float amount = props["amount"]?.Value<float>() ?? 0.1f;

            if (_bevelType == null)
                return new ErrorResponse("Bevel type not found in ProBuilder assembly.");

            // Collect edges
            var allEdges = CollectAllEdges(pbMesh);
            var edgeIndices = edgeIndicesToken.ToObject<int[]>();
            var selectedEdges = new List<object>();
            foreach (int idx in edgeIndices)
            {
                if (idx < 0 || idx >= allEdges.Count)
                    return new ErrorResponse($"Edge index {idx} out of range (0-{allEdges.Count - 1}).");
                selectedEdges.Add(allEdges[idx]);
            }

            Undo.RegisterCompleteObjectUndo(pbMesh, "Bevel Edges");

            // BevelEdges expects IList<Edge>
            var edgeListType = typeof(List<>).MakeGenericType(_edgeType);
            var typedList = Activator.CreateInstance(edgeListType) as System.Collections.IList;
            foreach (var e in selectedEdges)
                typedList.Add(e);

            var bevelMethod = _bevelType.GetMethod("BevelEdges",
                BindingFlags.Static | BindingFlags.Public);

            if (bevelMethod == null)
                return new ErrorResponse("Bevel.BevelEdges method not found.");

            bevelMethod.Invoke(null, new object[] { pbMesh, typedList, amount });
            RefreshMesh(pbMesh);

            return new SuccessResponse($"Beveled {selectedEdges.Count} edge(s) with amount {amount}", new
            {
                edgesBeveled = selectedEdges.Count,
                amount,
                faceCount = GetFaceCount(pbMesh),
            });
        }

        private static object Subdivide(JObject @params)
        {
            var pbMesh = RequireProBuilderMesh(@params);
            var props = ExtractProperties(@params);

            if (_surfaceTopologyType == null)
                return new ErrorResponse("SurfaceTopology type not found.");

            Undo.RegisterCompleteObjectUndo(pbMesh, "Subdivide");

            // Find Subdivide method - try by parameter count first to avoid fragile generic type matching
            var subdivideMethod = _surfaceTopologyType.GetMethods(BindingFlags.Static | BindingFlags.Public)
                .FirstOrDefault(m => m.Name == "Subdivide" && m.GetParameters().Length == 2);

            if (subdivideMethod == null)
            {
                subdivideMethod = _surfaceTopologyType.GetMethods(BindingFlags.Static | BindingFlags.Public)
                    .FirstOrDefault(m => m.Name == "Subdivide");
            }

            if (subdivideMethod == null)
                return new ErrorResponse("SurfaceTopology.Subdivide method not found.");

            var faceIndicesToken = props["faceIndices"] ?? props["face_indices"];
            if (faceIndicesToken != null)
            {
                var faces = GetFacesByIndices(pbMesh, faceIndicesToken);
                var faceListType = typeof(List<>).MakeGenericType(_faceType);
                var faceList = Activator.CreateInstance(faceListType) as System.Collections.IList;
                foreach (var f in faces)
                    faceList.Add(f);
                subdivideMethod.Invoke(null, new object[] { pbMesh, faceList });
            }
            else
            {
                // Subdivide all - pass null or all faces
                subdivideMethod.Invoke(null, new object[] { pbMesh, null });
            }

            RefreshMesh(pbMesh);

            return new SuccessResponse("Subdivided mesh", new
            {
                faceCount = GetFaceCount(pbMesh),
                vertexCount = GetVertexCount(pbMesh),
            });
        }

        private static object DeleteFaces(JObject @params)
        {
            var pbMesh = RequireProBuilderMesh(@params);
            var props = ExtractProperties(@params);
            var faceIndicesToken = props["faceIndices"] ?? props["face_indices"];
            if (faceIndicesToken == null)
                return new ErrorResponse("faceIndices parameter is required.");

            if (_deleteElementsType == null)
                return new ErrorResponse("DeleteElements type not found.");

            var faceIndices = faceIndicesToken.ToObject<int[]>();

            Undo.RegisterCompleteObjectUndo(pbMesh, "Delete Faces");

            // DeleteElements.DeleteFaces(ProBuilderMesh, int[])
            var deleteMethod = _deleteElementsType.GetMethod("DeleteFaces",
                BindingFlags.Static | BindingFlags.Public,
                null,
                new[] { _proBuilderMeshType, typeof(int[]) },
                null);

            if (deleteMethod == null)
            {
                // Try with IEnumerable<Face>
                var faces = GetFacesByIndices(pbMesh, faceIndicesToken);
                deleteMethod = _deleteElementsType.GetMethod("DeleteFaces",
                    BindingFlags.Static | BindingFlags.Public,
                    null,
                    new[] { _proBuilderMeshType, faces.GetType() },
                    null);

                if (deleteMethod == null)
                    return new ErrorResponse("DeleteElements.DeleteFaces method not found.");

                deleteMethod.Invoke(null, new object[] { pbMesh, faces });
            }
            else
            {
                deleteMethod.Invoke(null, new object[] { pbMesh, faceIndices });
            }

            RefreshMesh(pbMesh);

            return new SuccessResponse($"Deleted {faceIndices.Length} face(s)", new
            {
                facesDeleted = faceIndices.Length,
                faceCount = GetFaceCount(pbMesh),
            });
        }

        private static object BridgeEdges(JObject @params)
        {
            var pbMesh = RequireProBuilderMesh(@params);
            var props = ExtractProperties(@params);

            if (_appendElementsType == null)
                return new ErrorResponse("AppendElements type not found.");

            var edgeAToken = props["edgeA"] ?? props["edge_a"];
            var edgeBToken = props["edgeB"] ?? props["edge_b"];
            if (edgeAToken == null || edgeBToken == null)
                return new ErrorResponse("edgeA and edgeB parameters are required (as {a, b} vertex index pairs).");

            // Create Edge instances from vertex index pairs
            var edgeACtor = _edgeType.GetConstructor(new[] { typeof(int), typeof(int) });
            var edgeBCtor = _edgeType.GetConstructor(new[] { typeof(int), typeof(int) });
            if (edgeACtor == null)
                return new ErrorResponse("Edge constructor not found.");

            int aA = edgeAToken["a"]?.Value<int>() ?? 0;
            int aB = edgeAToken["b"]?.Value<int>() ?? 0;
            int bA = edgeBToken["a"]?.Value<int>() ?? 0;
            int bB = edgeBToken["b"]?.Value<int>() ?? 0;

            var edgeA = edgeACtor.Invoke(new object[] { aA, aB });
            var edgeB = edgeBCtor.Invoke(new object[] { bA, bB });

            Undo.RegisterCompleteObjectUndo(pbMesh, "Bridge Edges");

            var bridgeMethod = _appendElementsType.GetMethod("Bridge",
                BindingFlags.Static | BindingFlags.Public,
                null,
                new[] { _proBuilderMeshType, _edgeType, _edgeType },
                null);

            if (bridgeMethod == null)
                return new ErrorResponse("AppendElements.Bridge method not found.");

            var result = bridgeMethod.Invoke(null, new object[] { pbMesh, edgeA, edgeB });
            RefreshMesh(pbMesh);

            return new SuccessResponse("Bridged edges", new
            {
                bridgeCreated = result != null,
                faceCount = GetFaceCount(pbMesh),
            });
        }

        private static object ConnectElements(JObject @params)
        {
            var pbMesh = RequireProBuilderMesh(@params);
            var props = ExtractProperties(@params);

            if (_connectElementsType == null)
                return new ErrorResponse("ConnectElements type not found.");

            Undo.RegisterCompleteObjectUndo(pbMesh, "Connect Elements");

            var faceIndicesToken = props["faceIndices"] ?? props["face_indices"];
            var edgeIndicesToken = props["edgeIndices"] ?? props["edge_indices"];

            if (faceIndicesToken != null)
            {
                var faces = GetFacesByIndices(pbMesh, faceIndicesToken);
                var connectMethod = _connectElementsType.GetMethod("Connect",
                    BindingFlags.Static | BindingFlags.Public,
                    null,
                    new[] { _proBuilderMeshType, faces.GetType() },
                    null);

                if (connectMethod == null)
                    return new ErrorResponse("ConnectElements.Connect (faces) method not found.");

                connectMethod.Invoke(null, new object[] { pbMesh, faces });
            }
            else if (edgeIndicesToken != null)
            {
                var allEdges = CollectAllEdges(pbMesh);
                var edgeIndices = edgeIndicesToken.ToObject<int[]>();
                var edgeListType = typeof(List<>).MakeGenericType(_edgeType);
                var typedList = Activator.CreateInstance(edgeListType) as System.Collections.IList;
                foreach (int idx in edgeIndices)
                {
                    if (idx < 0 || idx >= allEdges.Count)
                        return new ErrorResponse($"Edge index {idx} out of range.");
                    typedList.Add(allEdges[idx]);
                }

                var connectMethod = _connectElementsType.GetMethod("Connect",
                    BindingFlags.Static | BindingFlags.Public,
                    null,
                    new[] { _proBuilderMeshType, edgeListType },
                    null);

                if (connectMethod == null)
                    return new ErrorResponse("ConnectElements.Connect (edges) method not found.");

                connectMethod.Invoke(null, new object[] { pbMesh, typedList });
            }
            else
            {
                return new ErrorResponse("Either faceIndices or edgeIndices parameter is required.");
            }

            RefreshMesh(pbMesh);

            return new SuccessResponse("Connected elements", new
            {
                faceCount = GetFaceCount(pbMesh),
            });
        }

        private static object DetachFaces(JObject @params)
        {
            var pbMesh = RequireProBuilderMesh(@params);
            var props = ExtractProperties(@params);
            var faces = GetFacesByIndices(pbMesh, props["faceIndices"] ?? props["face_indices"]);

            if (_extrudeElementsType == null)
                return new ErrorResponse("ExtrudeElements type not found.");

            Undo.RegisterCompleteObjectUndo(pbMesh, "Detach Faces");

            var detachMethod = _extrudeElementsType.GetMethod("DetachFaces",
                BindingFlags.Static | BindingFlags.Public,
                null,
                new[] { _proBuilderMeshType, faces.GetType() },
                null);

            if (detachMethod == null)
                return new ErrorResponse("ExtrudeElements.DetachFaces method not found.");

            var detachedFaces = detachMethod.Invoke(null, new object[] { pbMesh, faces });
            RefreshMesh(pbMesh);

            return new SuccessResponse($"Detached {faces.Length} face(s)", new
            {
                facesDetached = faces.Length,
                faceCount = GetFaceCount(pbMesh),
            });
        }

        private static object FlipNormals(JObject @params)
        {
            var pbMesh = RequireProBuilderMesh(@params);
            var props = ExtractProperties(@params);
            var faces = GetFacesByIndices(pbMesh, props["faceIndices"] ?? props["face_indices"]);

            Undo.RegisterCompleteObjectUndo(pbMesh, "Flip Normals");

            // Face.Reverse() flips the normal of each face
            var reverseMethod = _faceType.GetMethod("Reverse");
            if (reverseMethod == null)
                return new ErrorResponse("Face.Reverse method not found.");

            foreach (var face in faces)
                reverseMethod.Invoke(face, null);

            RefreshMesh(pbMesh);

            return new SuccessResponse($"Flipped normals on {faces.Length} face(s)", new
            {
                facesFlipped = faces.Length,
            });
        }

        private static object MergeFaces(JObject @params)
        {
            var pbMesh = RequireProBuilderMesh(@params);
            var props = ExtractProperties(@params);
            var faces = GetFacesByIndices(pbMesh, props["faceIndices"] ?? props["face_indices"]);

            if (_mergeElementsType == null)
                return new ErrorResponse("MergeElements type not found.");

            Undo.RegisterCompleteObjectUndo(pbMesh, "Merge Faces");

            var mergeMethod = _mergeElementsType.GetMethod("Merge",
                BindingFlags.Static | BindingFlags.Public,
                null,
                new[] { _proBuilderMeshType, faces.GetType() },
                null);

            if (mergeMethod == null)
                return new ErrorResponse("MergeElements.Merge method not found.");

            mergeMethod.Invoke(null, new object[] { pbMesh, faces });
            RefreshMesh(pbMesh);

            return new SuccessResponse($"Merged {faces.Length} face(s)", new
            {
                facesMerged = faces.Length,
                faceCount = GetFaceCount(pbMesh),
            });
        }

        private static object CombineMeshes(JObject @params)
        {
            var props = ExtractProperties(@params);
            var targetsToken = props["targets"];
            if (targetsToken == null)
                return new ErrorResponse("targets parameter is required (list of GameObject names/paths/ids).");

            if (_combineMeshesType == null)
                return new ErrorResponse("CombineMeshes type not found.");

            var targets = targetsToken.ToObject<string[]>();
            var pbMeshes = new List<Component>();

            foreach (var targetStr in targets)
            {
                var go = ObjectResolver.ResolveGameObject(targetStr, null);
                if (go == null)
                    return new ErrorResponse($"GameObject not found: {targetStr}");
                var pbMesh = GetProBuilderMesh(go);
                if (pbMesh == null)
                    return new ErrorResponse($"GameObject '{go.name}' does not have a ProBuilderMesh component.");
                pbMeshes.Add(pbMesh);
            }

            if (pbMeshes.Count < 2)
                return new ErrorResponse("At least 2 ProBuilder meshes are required for combining.");

            Undo.RegisterCompleteObjectUndo(pbMeshes[0], "Combine Meshes");

            // Create typed list
            var listType = typeof(List<>).MakeGenericType(_proBuilderMeshType);
            var typedList = Activator.CreateInstance(listType) as System.Collections.IList;
            foreach (var m in pbMeshes)
                typedList.Add(m);

            var combineMethod = _combineMeshesType.GetMethod("Combine",
                BindingFlags.Static | BindingFlags.Public);

            if (combineMethod == null)
                return new ErrorResponse("CombineMeshes.Combine method not found.");

            combineMethod.Invoke(null, new object[] { typedList, pbMeshes[0] });
            RefreshMesh(pbMeshes[0]);

            return new SuccessResponse($"Combined {pbMeshes.Count} meshes", new
            {
                meshesCombined = pbMeshes.Count,
                targetName = pbMeshes[0].gameObject.name,
                faceCount = GetFaceCount(pbMeshes[0]),
            });
        }

        private static Component ConvertToProBuilderInternal(GameObject go)
        {
            var existingPB = GetProBuilderMesh(go);
            if (existingPB != null)
                return existingPB;

            var meshFilter = go.GetComponent<MeshFilter>();
            if (meshFilter == null || meshFilter.sharedMesh == null)
                return null;

            if (_meshImporterType == null)
                return null;

            var pbMesh = go.AddComponent(_proBuilderMeshType);

            var importerCtor = _meshImporterType.GetConstructor(new[] { _proBuilderMeshType });
            if (importerCtor == null)
                return null;

            var importer = importerCtor.Invoke(new object[] { pbMesh });
            var importM = _meshImporterType.GetMethod("Import",
                BindingFlags.Instance | BindingFlags.Public,
                null,
                new[] { typeof(Mesh) },
                null);

            if (importM == null)
                importM = _meshImporterType.GetMethod("Import",
                    BindingFlags.Instance | BindingFlags.Public);

            if (importM != null)
                importM.Invoke(importer, new object[] { meshFilter.sharedMesh });

            RefreshMesh(pbMesh);
            return pbMesh;
        }

        private static object MergeObjects(JObject @params)
        {
            var props = ExtractProperties(@params);
            var targetsToken = props["targets"];
            if (targetsToken == null)
                return new ErrorResponse("targets parameter is required (list of GameObject names/paths/ids).");

            if (_combineMeshesType == null)
                return new ErrorResponse("CombineMeshes type not found. Ensure ProBuilder is installed.");

            var targets = targetsToken.ToObject<string[]>();
            if (targets.Length < 2)
                return new ErrorResponse("At least 2 targets are required for merging.");

            var pbMeshes = new List<Component>();
            var nonPbObjects = new List<GameObject>();

            foreach (var targetStr in targets)
            {
                var go = ObjectResolver.ResolveGameObject(targetStr, null);
                if (go == null)
                    return new ErrorResponse($"GameObject not found: {targetStr}");
                var pbMesh = GetProBuilderMesh(go);
                if (pbMesh != null)
                    pbMeshes.Add(pbMesh);
                else
                    nonPbObjects.Add(go);
            }

            // Convert non-ProBuilder objects first
            foreach (var go in nonPbObjects)
            {
                var converted = ConvertToProBuilderInternal(go);
                if (converted == null)
                    return new ErrorResponse($"Failed to convert '{go.name}' to ProBuilder mesh.");
                pbMeshes.Add(converted);
            }

            if (pbMeshes.Count < 2)
                return new ErrorResponse("Need at least 2 meshes after conversion.");

            Undo.RegisterCompleteObjectUndo(pbMeshes[0], "Merge Objects");

            var listType = typeof(List<>).MakeGenericType(_proBuilderMeshType);
            var typedList = Activator.CreateInstance(listType) as System.Collections.IList;
            foreach (var m in pbMeshes)
                typedList.Add(m);

            var combineMethod = _combineMeshesType.GetMethod("Combine",
                BindingFlags.Static | BindingFlags.Public);

            if (combineMethod == null)
                return new ErrorResponse("CombineMeshes.Combine method not found.");

            combineMethod.Invoke(null, new object[] { typedList, pbMeshes[0] });
            RefreshMesh(pbMeshes[0]);

            string resultName = props["name"]?.ToString();
            if (!string.IsNullOrEmpty(resultName))
                pbMeshes[0].gameObject.name = resultName;

            return new SuccessResponse($"Merged {targets.Length} objects into '{pbMeshes[0].gameObject.name}'", new
            {
                mergedCount = targets.Length,
                convertedCount = nonPbObjects.Count,
                targetName = pbMeshes[0].gameObject.name,
                faceCount = GetFaceCount(pbMeshes[0]),
                vertexCount = GetVertexCount(pbMeshes[0]),
            });
        }

        // =====================================================================
        // Vertex Operations
        // =====================================================================

        private static object MergeVertices(JObject @params)
        {
            var pbMesh = RequireProBuilderMesh(@params);
            var props = ExtractProperties(@params);
            var vertexIndicesToken = props["vertexIndices"] ?? props["vertex_indices"];
            if (vertexIndicesToken == null)
                return new ErrorResponse("vertexIndices parameter is required.");

            var vertexIndices = vertexIndicesToken.ToObject<int[]>();

            Undo.RegisterCompleteObjectUndo(pbMesh, "Merge Vertices");

            // Use reflection to find the WeldVertices or MergeVertices method
            var vertexEditingType = Type.GetType("UnityEngine.ProBuilder.MeshOperations.VertexEditing, Unity.ProBuilder");
            if (vertexEditingType == null)
                return new ErrorResponse("VertexEditing type not found.");

            var mergeMethod = vertexEditingType.GetMethod("MergeVertices",
                BindingFlags.Static | BindingFlags.Public);

            if (mergeMethod == null)
            {
                // Try WeldVertices
                mergeMethod = vertexEditingType.GetMethod("WeldVertices",
                    BindingFlags.Static | BindingFlags.Public);
            }

            if (mergeMethod == null)
                return new ErrorResponse("MergeVertices/WeldVertices method not found.");

            mergeMethod.Invoke(null, new object[] { pbMesh, vertexIndices, true });
            RefreshMesh(pbMesh);

            return new SuccessResponse($"Merged {vertexIndices.Length} vertices", new
            {
                verticesMerged = vertexIndices.Length,
                vertexCount = GetVertexCount(pbMesh),
            });
        }

        private static object SplitVertices(JObject @params)
        {
            var pbMesh = RequireProBuilderMesh(@params);
            var props = ExtractProperties(@params);
            var vertexIndicesToken = props["vertexIndices"] ?? props["vertex_indices"];
            if (vertexIndicesToken == null)
                return new ErrorResponse("vertexIndices parameter is required.");

            var vertexIndices = vertexIndicesToken.ToObject<int[]>();

            Undo.RegisterCompleteObjectUndo(pbMesh, "Split Vertices");

            var vertexEditingType = Type.GetType("UnityEngine.ProBuilder.MeshOperations.VertexEditing, Unity.ProBuilder");
            if (vertexEditingType == null)
                return new ErrorResponse("VertexEditing type not found.");

            var splitMethod = vertexEditingType.GetMethod("SplitVertices",
                BindingFlags.Static | BindingFlags.Public);

            if (splitMethod == null)
                return new ErrorResponse("SplitVertices method not found.");

            splitMethod.Invoke(null, new object[] { pbMesh, vertexIndices });
            RefreshMesh(pbMesh);

            return new SuccessResponse($"Split {vertexIndices.Length} vertices", new
            {
                verticesSplit = vertexIndices.Length,
                vertexCount = GetVertexCount(pbMesh),
            });
        }

        private static object MoveVertices(JObject @params)
        {
            var pbMesh = RequireProBuilderMesh(@params);
            var props = ExtractProperties(@params);
            var vertexIndicesToken = props["vertexIndices"] ?? props["vertex_indices"];
            if (vertexIndicesToken == null)
                return new ErrorResponse("vertexIndices parameter is required.");

            var offsetToken = props["offset"];
            if (offsetToken == null)
                return new ErrorResponse("offset parameter is required ([x,y,z]).");

            var vertexIndices = vertexIndicesToken.ToObject<int[]>();
            var offset = ParseVector3(offsetToken);

            Undo.RegisterCompleteObjectUndo(pbMesh, "Move Vertices");

            // Get positions array and modify
            var positionsProperty = _proBuilderMeshType.GetProperty("positions");
            if (positionsProperty == null)
                return new ErrorResponse("Could not access positions property.");

            var positions = positionsProperty.GetValue(pbMesh) as IList<Vector3>;
            if (positions == null)
                return new ErrorResponse("Could not read positions.");

            var posList = new List<Vector3>(positions);
            foreach (int idx in vertexIndices)
            {
                if (idx < 0 || idx >= posList.Count)
                    return new ErrorResponse($"Vertex index {idx} out of range (0-{posList.Count - 1}).");
                posList[idx] += offset;
            }

            // Set positions back
            var setPositionsMethod = _proBuilderMeshType.GetMethod("SetVertices",
                BindingFlags.Instance | BindingFlags.Public,
                null,
                new[] { typeof(IList<Vector3>) },
                null);

            if (setPositionsMethod == null)
            {
                // Try alternative: RebuildWithPositionsAndFaces or direct positions
                var posField = _proBuilderMeshType.GetProperty("positions");
                return new ErrorResponse("SetVertices method not found. Use vertex editing tools instead.");
            }

            setPositionsMethod.Invoke(pbMesh, new object[] { posList });
            RefreshMesh(pbMesh);

            return new SuccessResponse($"Moved {vertexIndices.Length} vertices by ({offset.x}, {offset.y}, {offset.z})", new
            {
                verticesMoved = vertexIndices.Length,
                offset = new[] { offset.x, offset.y, offset.z },
            });
        }

        // =====================================================================
        // UV & Materials
        // =====================================================================

        private static object SetFaceMaterial(JObject @params)
        {
            var pbMesh = RequireProBuilderMesh(@params);
            var props = ExtractProperties(@params);
            var faces = GetFacesByIndices(pbMesh, props["faceIndices"] ?? props["face_indices"]);

            string materialPath = props["materialPath"]?.ToString() ?? props["material_path"]?.ToString();
            if (string.IsNullOrEmpty(materialPath))
                return new ErrorResponse("materialPath parameter is required.");

            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
                return new ErrorResponse($"Material not found at path: {materialPath}");

            Undo.RegisterCompleteObjectUndo(pbMesh, "Set Face Material");

            // ProBuilderMesh.SetMaterial(IEnumerable<Face>, Material)
            var setMaterialMethod = _proBuilderMeshType.GetMethod("SetMaterial",
                BindingFlags.Instance | BindingFlags.Public);

            if (setMaterialMethod == null)
                return new ErrorResponse("SetMaterial method not found on ProBuilderMesh.");

            setMaterialMethod.Invoke(pbMesh, new object[] { faces, material });

            // Before RefreshMesh, compact renderer materials to only those referenced by faces.
            // ProBuilder's SetMaterial adds new materials to the renderer array but doesn't
            // remove unused ones, causing "more materials than submeshes" warnings.
            var meshRenderer = pbMesh.gameObject.GetComponent<MeshRenderer>();
            if (meshRenderer != null)
            {
                var allFacesList = (System.Collections.IList)GetFacesArray(pbMesh);
                var submeshIndexProp = _faceType.GetProperty("submeshIndex");
                var currentMats = meshRenderer.sharedMaterials;

                // Collect unique submesh indices actually used by faces
                var usedIndices = new SortedSet<int>();
                foreach (var f in allFacesList)
                    usedIndices.Add((int)submeshIndexProp.GetValue(f));

                // Only compact if there are unused material slots
                if (usedIndices.Count < currentMats.Length)
                {
                    // Build compacted materials array and remap face indices
                    var remap = new Dictionary<int, int>();
                    var newMats = new Material[usedIndices.Count];
                    int newIdx = 0;
                    foreach (int oldIdx in usedIndices)
                    {
                        newMats[newIdx] = oldIdx < currentMats.Length ? currentMats[oldIdx] : material;
                        remap[oldIdx] = newIdx;
                        newIdx++;
                    }

                    foreach (var f in allFacesList)
                    {
                        int si = (int)submeshIndexProp.GetValue(f);
                        if (remap.TryGetValue(si, out int mapped) && mapped != si)
                            submeshIndexProp.SetValue(f, mapped);
                    }

                    meshRenderer.sharedMaterials = newMats;
                }
            }

            RefreshMesh(pbMesh);

            return new SuccessResponse($"Set material on {faces.Length} face(s)", new
            {
                facesModified = faces.Length,
                materialPath,
            });
        }

        private static object SetFaceColor(JObject @params)
        {
            var pbMesh = RequireProBuilderMesh(@params);
            var props = ExtractProperties(@params);
            var faces = GetFacesByIndices(pbMesh, props["faceIndices"] ?? props["face_indices"]);

            var colorToken = props["color"];
            if (colorToken == null)
                return new ErrorResponse("color parameter is required ([r,g,b,a]).");

            var color = VectorParsing.ParseColorOrDefault(colorToken);

            Undo.RegisterCompleteObjectUndo(pbMesh, "Set Face Color");

            // ProBuilderMesh.SetFaceColor(Face, Color)
            var setColorMethod = _proBuilderMeshType.GetMethod("SetFaceColor",
                BindingFlags.Instance | BindingFlags.Public);

            if (setColorMethod == null)
                return new ErrorResponse("SetFaceColor method not found.");

            foreach (var face in faces)
                setColorMethod.Invoke(pbMesh, new object[] { face, color });

            RefreshMesh(pbMesh);

            // Auto-swap to vertex-color shader if current material is Standard
            bool skipSwap = props["skipMaterialSwap"]?.Value<bool>() ?? props["skip_material_swap"]?.Value<bool>() ?? false;
            if (!skipSwap)
            {
                var go = pbMesh.gameObject;
                var renderer = go.GetComponent<Renderer>();
                if (renderer != null && renderer.sharedMaterial != null &&
                    renderer.sharedMaterial.shader.name.Contains("Standard"))
                {
                    var vcShader = Shader.Find("ProBuilder/Standard Vertex Color")
                                ?? Shader.Find("ProBuilder/Diffuse Vertex Color")
                                ?? Shader.Find("Sprites/Default");
                    if (vcShader != null)
                    {
                        var vcMat = new Material(vcShader);
                        renderer.sharedMaterial = vcMat;
                    }
                }
            }

            return new SuccessResponse($"Set color on {faces.Length} face(s)", new
            {
                facesModified = faces.Length,
                color = new[] { color.r, color.g, color.b, color.a },
            });
        }

        private static object SetFaceUVs(JObject @params)
        {
            var pbMesh = RequireProBuilderMesh(@params);
            var props = ExtractProperties(@params);
            var faces = GetFacesByIndices(pbMesh, props["faceIndices"] ?? props["face_indices"]);

            Undo.RegisterCompleteObjectUndo(pbMesh, "Set Face UVs");

            // AutoUnwrapSettings is a struct on each Face
            var uvProperty = _faceType.GetProperty("uv");
            if (uvProperty == null)
                return new ErrorResponse("Face.uv property not found.");

            var autoUnwrapType = uvProperty.PropertyType;

            foreach (var face in faces)
            {
                var uvSettings = uvProperty.GetValue(face);

                // Apply scale
                var scaleToken = props["scale"];
                if (scaleToken != null)
                {
                    var scaleProp = autoUnwrapType.GetField("scale") ?? (MemberInfo)autoUnwrapType.GetProperty("scale");
                    if (scaleProp is FieldInfo fi)
                    {
                        var scaleArr = scaleToken.ToObject<float[]>();
                        fi.SetValue(uvSettings, new Vector2(scaleArr[0], scaleArr.Length > 1 ? scaleArr[1] : scaleArr[0]));
                    }
                }

                // Apply offset
                var offsetToken = props["offset"];
                if (offsetToken != null)
                {
                    var offsetField = autoUnwrapType.GetField("offset");
                    if (offsetField != null)
                    {
                        var offsetArr = offsetToken.ToObject<float[]>();
                        offsetField.SetValue(uvSettings, new Vector2(offsetArr[0], offsetArr.Length > 1 ? offsetArr[1] : 0f));
                    }
                }

                // Apply rotation
                var rotationToken = props["rotation"];
                if (rotationToken != null)
                {
                    var rotField = autoUnwrapType.GetField("rotation");
                    if (rotField != null)
                        rotField.SetValue(uvSettings, rotationToken.Value<float>());
                }

                // Apply flipU
                var flipUToken = props["flipU"] ?? props["flip_u"];
                if (flipUToken != null)
                {
                    var flipUField = autoUnwrapType.GetField("flipU");
                    if (flipUField != null)
                        flipUField.SetValue(uvSettings, flipUToken.Value<bool>());
                }

                // Apply flipV
                var flipVToken = props["flipV"] ?? props["flip_v"];
                if (flipVToken != null)
                {
                    var flipVField = autoUnwrapType.GetField("flipV");
                    if (flipVField != null)
                        flipVField.SetValue(uvSettings, flipVToken.Value<bool>());
                }

                uvProperty.SetValue(face, uvSettings);
            }

            // RefreshUV
            var refreshUVMethod = _proBuilderMeshType.GetMethod("RefreshUV",
                BindingFlags.Instance | BindingFlags.Public);
            if (refreshUVMethod != null)
            {
                var allFaces = GetFacesArray(pbMesh);
                refreshUVMethod.Invoke(pbMesh, new[] { allFaces });
            }

            RefreshMesh(pbMesh);

            return new SuccessResponse($"Set UV parameters on {faces.Length} face(s)", new
            {
                facesModified = faces.Length,
            });
        }

        // =====================================================================
        // Query
        // =====================================================================

        private static object GetMeshInfo(JObject @params)
        {
            var pbMesh = RequireProBuilderMesh(@params);
            var props = ExtractProperties(@params);
            var include = (props["include"]?.ToString() ?? "summary").ToLowerInvariant();

            var allFaces = GetFacesArray(pbMesh);
            var facesList = (System.Collections.IList)allFaces;

            // Get bounds
            var renderer = pbMesh.gameObject.GetComponent<MeshRenderer>();
            Bounds bounds = renderer != null ? renderer.bounds : new Bounds();

            // Get materials
            var materials = new List<string>();
            if (renderer != null)
            {
                foreach (var mat in renderer.sharedMaterials)
                    materials.Add(mat != null ? mat.name : "(none)");
            }

            // Always include summary data
            var data = new Dictionary<string, object>
            {
                ["gameObjectName"] = pbMesh.gameObject.name,
                ["instanceId"] = pbMesh.gameObject.GetInstanceID(),
                ["faceCount"] = GetFaceCount(pbMesh),
                ["vertexCount"] = GetVertexCount(pbMesh),
                ["bounds"] = new
                {
                    center = new[] { bounds.center.x, bounds.center.y, bounds.center.z },
                    size = new[] { bounds.size.x, bounds.size.y, bounds.size.z },
                },
                ["materials"] = materials,
            };

            // Include face details when requested
            if (include == "faces" || include == "all")
            {
                var faceDetails = new List<object>();
                for (int i = 0; i < facesList.Count && i < 100; i++)
                {
                    var face = facesList[i];
                    var smGroup = _faceType.GetProperty("smoothingGroup")?.GetValue(face);
                    var manualUV = _faceType.GetProperty("manualUV")?.GetValue(face);
                    var normal = ComputeFaceNormal(pbMesh, face);
                    var center = ComputeFaceCenter(pbMesh, face);
                    var direction = ClassifyDirection(normal);

                    faceDetails.Add(new
                    {
                        index = i,
                        smoothingGroup = smGroup,
                        manualUV = manualUV,
                        normal = new[] { Round(normal.x), Round(normal.y), Round(normal.z) },
                        center = new[] { Round(center.x), Round(center.y), Round(center.z) },
                        direction,
                    });
                }
                data["faces"] = faceDetails;
                data["truncated"] = facesList.Count > 100;
            }

            // Include edge data when requested
            if (include == "edges" || include == "all")
            {
                var allEdges = CollectAllEdges(pbMesh);
                var edgeDetails = new List<object>();
                var aField = _edgeType.GetField("a");
                var bField = _edgeType.GetField("b");
                var aProp = aField == null ? _edgeType.GetProperty("a") : null;
                var bProp = bField == null ? _edgeType.GetProperty("b") : null;

                for (int i = 0; i < allEdges.Count && i < 200; i++)
                {
                    var edge = allEdges[i];
                    int vertA = aField != null ? (int)aField.GetValue(edge) :
                                aProp != null ? (int)aProp.GetValue(edge) : -1;
                    int vertB = bField != null ? (int)bField.GetValue(edge) :
                                bProp != null ? (int)bProp.GetValue(edge) : -1;
                    edgeDetails.Add(new { index = i, vertexA = vertA, vertexB = vertB });
                }
                data["edges"] = edgeDetails;
                data["edgesTruncated"] = allEdges.Count > 200;
            }

            return new SuccessResponse("ProBuilder mesh info", data);
        }

        private static Vector3 ComputeFaceNormal(Component pbMesh, object face)
        {
            var positionsProp = _proBuilderMeshType.GetProperty("positions");
            var positions = positionsProp?.GetValue(pbMesh) as System.Collections.IList;
            var indexesProp = _faceType.GetProperty("indexes");
            var indexes = indexesProp?.GetValue(face) as System.Collections.IList;

            if (positions == null || indexes == null || indexes.Count < 3)
                return Vector3.up;

            var p0 = (Vector3)positions[(int)indexes[0]];
            var p1 = (Vector3)positions[(int)indexes[1]];
            var p2 = (Vector3)positions[(int)indexes[2]];

            var localNormal = Vector3.Cross(p1 - p0, p2 - p0).normalized;
            return pbMesh.transform.rotation * localNormal;
        }

        private static Vector3 ComputeFaceCenter(Component pbMesh, object face)
        {
            var positionsProp = _proBuilderMeshType.GetProperty("positions");
            var positions = positionsProp?.GetValue(pbMesh) as System.Collections.IList;
            var indexesProp = _faceType.GetProperty("indexes");
            var indexes = indexesProp?.GetValue(face) as System.Collections.IList;

            if (positions == null || indexes == null || indexes.Count == 0)
                return pbMesh.transform.position;

            var sum = Vector3.zero;
            foreach (int idx in indexes)
                sum += (Vector3)positions[idx];

            var localCenter = sum / indexes.Count;
            return pbMesh.transform.TransformPoint(localCenter);
        }

        private static string ClassifyDirection(Vector3 normal)
        {
            var dirs = new (Vector3 dir, string label)[]
            {
                (Vector3.up, "top"),
                (Vector3.down, "bottom"),
                (Vector3.forward, "front"),
                (Vector3.back, "back"),
                (Vector3.left, "left"),
                (Vector3.right, "right"),
            };

            foreach (var (dir, label) in dirs)
            {
                if (Vector3.Dot(normal, dir) > 0.7f)
                    return label;
            }
            return null;
        }

        private static float Round(float v) => (float)Math.Round(v, 4);

        private static object ConvertToProBuilder(JObject @params)
        {
            var go = FindTarget(@params);
            if (go == null)
                return new ErrorResponse("Target GameObject not found.");

            var existingPB = GetProBuilderMesh(go);
            if (existingPB != null)
                return new ErrorResponse($"GameObject '{go.name}' already has a ProBuilderMesh component.");

            var meshFilter = go.GetComponent<MeshFilter>();
            if (meshFilter == null || meshFilter.sharedMesh == null)
                return new ErrorResponse($"GameObject '{go.name}' does not have a MeshFilter with a valid mesh.");

            if (_meshImporterType == null)
                return new ErrorResponse("MeshImporter type not found.");

            Undo.RegisterCompleteObjectUndo(go, "Convert to ProBuilder");

            // Add ProBuilderMesh component
            var pbMesh = go.AddComponent(_proBuilderMeshType);

            // Create MeshImporter and import
            var importerCtor = _meshImporterType.GetConstructor(new[] { _proBuilderMeshType });
            if (importerCtor == null)
            {
                // Try alternative constructor
                var importMethod = _meshImporterType.GetMethod("Import",
                    BindingFlags.Instance | BindingFlags.Public);

                if (importMethod == null)
                    return new ErrorResponse("MeshImporter could not be initialized.");
            }

            var importer = importerCtor.Invoke(new object[] { pbMesh });
            var importM = _meshImporterType.GetMethod("Import",
                BindingFlags.Instance | BindingFlags.Public,
                null,
                new[] { typeof(Mesh) },
                null);

            if (importM == null)
            {
                // Try with MeshImportSettings
                importM = _meshImporterType.GetMethod("Import",
                    BindingFlags.Instance | BindingFlags.Public);
            }

            if (importM != null)
            {
                importM.Invoke(importer, new object[] { meshFilter.sharedMesh });
            }

            RefreshMesh(pbMesh);

            return new SuccessResponse($"Converted '{go.name}' to ProBuilder", new
            {
                gameObjectName = go.name,
                faceCount = GetFaceCount(pbMesh),
                vertexCount = GetVertexCount(pbMesh),
            });
        }

        // =====================================================================
        // Edge Collection Helper
        // =====================================================================

        internal static List<object> CollectAllEdges(Component pbMesh)
        {
            var allFaces = (System.Collections.IList)GetFacesArray(pbMesh);
            var allEdges = new List<object>();
            var edgesProp = _faceType.GetProperty("edges");

            if (allFaces != null && edgesProp != null)
            {
                foreach (var face in allFaces)
                {
                    var faceEdges = edgesProp.GetValue(face) as System.Collections.IList;
                    if (faceEdges != null)
                    {
                        foreach (var edge in faceEdges)
                            allEdges.Add(edge);
                    }
                }
            }
            return allEdges;
        }
    }
}
