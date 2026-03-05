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
    ///   - create_shape: Create ProBuilder primitive with real dimensions via Generate* methods
    ///     Shape types: Cube, Cylinder, Sphere, Plane, Cone, Torus, Pipe, Arch, Stair, CurvedStair, Door, Prism
    ///     Each shape accepts type-specific parameters (radius, height, steps, segments, etc.)
    ///   - create_poly_shape: Create from 2D polygon footprint (points, extrudeHeight, flipNormals)
    ///
    /// MESH EDITING:
    ///   - extrude_faces: Extrude faces (faceIndices, distance, method: FaceNormal/VertexNormal/IndividualFaces)
    ///   - extrude_edges: Extrude edges (edgeIndices or edges [{a,b},...], distance, asGroup)
    ///   - bevel_edges: Bevel edges (edgeIndices or edges [{a,b},...], amount 0-1)
    ///   - subdivide: Subdivide faces (faceIndices optional)
    ///   - delete_faces: Delete faces (faceIndices)
    ///   - bridge_edges: Bridge two open edges (edgeA, edgeB as {a,b} pairs, allowNonManifold)
    ///   - connect_elements: Connect edges/faces (edgeIndices or faceIndices)
    ///   - detach_faces: Detach faces (faceIndices, deleteSourceFaces)
    ///   - flip_normals: Flip face normals (faceIndices)
    ///   - merge_faces: Merge faces into one (faceIndices)
    ///   - combine_meshes: Combine ProBuilder objects (targets list)
    ///   - merge_objects: Merge objects (auto-converts non-ProBuilder), convenience wrapper (targets, name)
    ///   - duplicate_and_flip: Create double-sided geometry (faceIndices)
    ///   - create_polygon: Connect existing vertices into a new face (vertexIndices, unordered)
    ///
    /// VERTEX OPERATIONS:
    ///   - merge_vertices: Collapse vertices to single point (vertexIndices, collapseToFirst)
    ///   - weld_vertices: Weld vertices within proximity radius (vertexIndices, radius)
    ///   - split_vertices: Split shared vertices (vertexIndices)
    ///   - move_vertices: Translate vertices (vertexIndices, offset [x,y,z])
    ///   - insert_vertex: Insert vertex on edge or face (edge {a,b} or faceIndex + point [x,y,z])
    ///   - append_vertices_to_edge: Insert evenly-spaced points on edges (edgeIndices or edges, count)
    ///
    /// SELECTION:
    ///   - select_faces: Select faces by criteria (direction, growAngle, floodAngle, loop, ring)
    ///
    /// UV &amp; MATERIALS:
    ///   - set_face_material: Assign material to faces (faceIndices, materialPath)
    ///   - set_face_color: Set vertex color (faceIndices, color [r,g,b,a])
    ///   - set_face_uvs: Set UV params (faceIndices, scale, offset, rotation, flipU, flipV)
    ///
    /// QUERY:
    ///   - get_mesh_info: Get mesh details (face count, vertex count, bounds, materials, edges with positions)
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
        private static Type _pivotLocationType;
        private static Type _vertexEditingType;
        private static Type _elementSelectionType;
        private static Type _axisEnum;
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
            _vertexEditingType = Type.GetType("UnityEngine.ProBuilder.MeshOperations.VertexEditing, Unity.ProBuilder");
            _elementSelectionType = Type.GetType("UnityEngine.ProBuilder.MeshOperations.ElementSelection, Unity.ProBuilder");

            // Enums & structs
            _pivotLocationType = Type.GetType("UnityEngine.ProBuilder.PivotLocation, Unity.ProBuilder");
            _axisEnum = Type.GetType("UnityEngine.ProBuilder.Axis, Unity.ProBuilder");

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
                    case "duplicate_and_flip": return DuplicateAndFlip(@params);
                    case "create_polygon": return CreatePolygon(@params);

                    // Vertex operations
                    case "merge_vertices": return MergeVertices(@params);
                    case "weld_vertices": return WeldVertices(@params);
                    case "split_vertices": return SplitVertices(@params);
                    case "move_vertices": return MoveVertices(@params);
                    case "insert_vertex": return InsertVertex(@params);
                    case "append_vertices_to_edge": return AppendVerticesToEdge(@params);

                    // Selection
                    case "select_faces": return SelectFaces(@params);

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
                    case "set_pivot": return ProBuilderMeshUtils.SetPivot(@params);
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
            // ToMesh and Refresh have optional parameters (MeshTopology, RefreshMask) —
            // Type.EmptyTypes won't find them. Use name-only lookup with default args.
            var toMeshMethod = _proBuilderMeshType.GetMethod("ToMesh", Type.EmptyTypes)
                ?? _proBuilderMeshType.GetMethod("ToMesh", BindingFlags.Instance | BindingFlags.Public);
            toMeshMethod?.Invoke(pbMesh, toMeshMethod.GetParameters().Length > 0
                ? new object[toMeshMethod.GetParameters().Length]
                : null);

            var refreshMethod = _proBuilderMeshType.GetMethod("Refresh", Type.EmptyTypes)
                ?? _proBuilderMeshType.GetMethod("Refresh", BindingFlags.Instance | BindingFlags.Public);
            refreshMethod?.Invoke(pbMesh, refreshMethod.GetParameters().Length > 0
                ? new object[refreshMethod.GetParameters().Length]
                : null);

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

        private static object GetPivotCenter()
        {
            if (_pivotLocationType == null) return null;
            // PivotLocation.Center = 0
            return Enum.ToObject(_pivotLocationType, 0);
        }

        private static Component InvokeGenerator(string methodName, Type[] paramTypes, object[] args)
        {
            if (_shapeGeneratorType == null) return null;
            var method = _shapeGeneratorType.GetMethod(methodName,
                BindingFlags.Static | BindingFlags.Public,
                null, paramTypes, null);
            return method?.Invoke(null, args) as Component;
        }

        // =====================================================================
        // Edge Helpers
        // =====================================================================

        private static int GetEdgeVertexA(object edge)
        {
            var f = _edgeType.GetField("a");
            if (f != null) return (int)f.GetValue(edge);
            var p = _edgeType.GetProperty("a");
            return p != null ? (int)p.GetValue(edge) : -1;
        }

        private static int GetEdgeVertexB(object edge)
        {
            var f = _edgeType.GetField("b");
            if (f != null) return (int)f.GetValue(edge);
            var p = _edgeType.GetProperty("b");
            return p != null ? (int)p.GetValue(edge) : -1;
        }

        private static object CreateEdge(int a, int b)
        {
            var ctor = _edgeType.GetConstructor(new[] { typeof(int), typeof(int) });
            return ctor?.Invoke(new object[] { a, b });
        }

        /// <summary>
        /// Collect unique (deduplicated) edges from the mesh.
        /// Edges shared between faces appear only once.
        /// </summary>
        internal static List<object> CollectUniqueEdges(Component pbMesh)
        {
            var allFaces = (System.Collections.IList)GetFacesArray(pbMesh);
            var uniqueEdges = new List<object>();
            var edgeSet = new HashSet<(int, int)>();
            var edgesProp = _faceType.GetProperty("edges");

            // Build shared vertex lookup so edges on different faces with different
            // vertex indices but the same spatial position are correctly deduplicated.
            var sharedLookup = BuildSharedVertexLookup(pbMesh);

            if (allFaces != null && edgesProp != null)
            {
                foreach (var face in allFaces)
                {
                    var faceEdges = edgesProp.GetValue(face) as System.Collections.IList;
                    if (faceEdges == null) continue;
                    foreach (var edge in faceEdges)
                    {
                        int a = GetEdgeVertexA(edge);
                        int b = GetEdgeVertexB(edge);
                        int sa = sharedLookup != null && sharedLookup.ContainsKey(a) ? sharedLookup[a] : a;
                        int sb = sharedLookup != null && sharedLookup.ContainsKey(b) ? sharedLookup[b] : b;
                        var key = (Math.Min(sa, sb), Math.Max(sa, sb));
                        if (edgeSet.Add(key))
                            uniqueEdges.Add(edge);
                    }
                }
            }
            return uniqueEdges;
        }

        private static Dictionary<int, int> BuildSharedVertexLookup(Component pbMesh)
        {
            var sharedVerticesProp = _proBuilderMeshType.GetProperty("sharedVertices");
            var sharedVertices = sharedVerticesProp?.GetValue(pbMesh) as System.Collections.IList;
            if (sharedVertices == null) return null;

            var lookup = new Dictionary<int, int>();
            for (int groupIdx = 0; groupIdx < sharedVertices.Count; groupIdx++)
            {
                var group = sharedVertices[groupIdx] as System.Collections.IEnumerable;
                if (group == null) continue;
                foreach (object vertIdx in group)
                    lookup[(int)vertIdx] = groupIdx;
            }
            return lookup;
        }

        /// <summary>
        /// Resolve edges from parameters. Supports:
        /// - "edgeIndices" / "edge_indices": flat array of indices into unique edge list
        /// - "edges": array of {a, b} vertex pair objects
        /// Returns a typed Edge[] array suitable for reflection calls.
        /// </summary>
        private static Array ResolveEdges(Component pbMesh, JObject props, out int count)
        {
            var edgeIndicesToken = props["edgeIndices"] ?? props["edge_indices"];
            var edgePairsToken = props["edges"];

            var edgeList = new List<object>();

            if (edgePairsToken != null && edgePairsToken.Type == JTokenType.Array)
            {
                // Edge specification by vertex pairs: [{a: 0, b: 1}, ...]
                foreach (var pair in edgePairsToken)
                {
                    int a = pair["a"]?.Value<int>() ?? 0;
                    int b = pair["b"]?.Value<int>() ?? 0;
                    edgeList.Add(CreateEdge(a, b));
                }
            }
            else if (edgeIndicesToken != null)
            {
                // Edge specification by index into unique edges
                var allEdges = CollectUniqueEdges(pbMesh);
                var edgeIndices = edgeIndicesToken.ToObject<int[]>();
                foreach (int idx in edgeIndices)
                {
                    if (idx < 0 || idx >= allEdges.Count)
                        throw new Exception($"Edge index {idx} out of range (0-{allEdges.Count - 1}).");
                    edgeList.Add(allEdges[idx]);
                }
            }
            else
            {
                throw new Exception("edgeIndices or edges parameter is required.");
            }

            count = edgeList.Count;
            var edgeArray = Array.CreateInstance(_edgeType, edgeList.Count);
            for (int i = 0; i < edgeList.Count; i++)
                edgeArray.SetValue(edgeList[i], i);
            return edgeArray;
        }

        /// <summary>
        /// Create a typed List&lt;Edge&gt; from an Edge[] array for APIs that require IList&lt;Edge&gt;.
        /// </summary>
        private static System.Collections.IList ToTypedEdgeList(Array edgeArray)
        {
            var edgeListType = typeof(List<>).MakeGenericType(_edgeType);
            var typedList = Activator.CreateInstance(edgeListType) as System.Collections.IList;
            foreach (var e in edgeArray)
                typedList.Add(e);
            return typedList;
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

            Undo.IncrementCurrentGroup();

            Component pbMesh = null;
            var pivot = GetPivotCenter();

            // Try shape-specific generators with real dimension parameters
            if (pivot != null)
                pbMesh = CreateShapeViaGenerator(shapeTypeStr, props, pivot);

            // Fallback: generic CreateShape(ShapeType) for unknown shapes or if generator failed
            if (pbMesh == null)
                pbMesh = CreateShapeGeneric(shapeTypeStr);

            if (pbMesh == null)
                return new ErrorResponse($"Failed to create ProBuilder shape '{shapeTypeStr}'.");

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

        private static Component CreateShapeViaGenerator(string shapeType, JObject props, object pivot)
        {
            float size = props["size"]?.Value<float>() ?? 0;
            float width = props["width"]?.Value<float>() ?? 0;
            float height = props["height"]?.Value<float>() ?? 0;
            float depth = props["depth"]?.Value<float>() ?? 0;
            float radius = props["radius"]?.Value<float>() ?? 0;

            switch (shapeType.ToUpperInvariant())
            {
                case "CUBE":
                {
                    float w = width > 0 ? width : (size > 0 ? size : 1f);
                    float h = height > 0 ? height : (size > 0 ? size : 1f);
                    float d = depth > 0 ? depth : (size > 0 ? size : 1f);
                    return InvokeGenerator("GenerateCube",
                        new[] { _pivotLocationType, typeof(Vector3) },
                        new object[] { pivot, new Vector3(w, h, d) });
                }

                case "PRISM":
                {
                    float w = width > 0 ? width : (size > 0 ? size : 1f);
                    float h = height > 0 ? height : (size > 0 ? size : 1f);
                    float d = depth > 0 ? depth : (size > 0 ? size : 1f);
                    return InvokeGenerator("GeneratePrism",
                        new[] { _pivotLocationType, typeof(Vector3) },
                        new object[] { pivot, new Vector3(w, h, d) });
                }

                case "CYLINDER":
                {
                    float r = radius > 0 ? radius : (size > 0 ? size / 2f : 0.5f);
                    float h = height > 0 ? height : (size > 0 ? size : 2f);
                    int axisDivisions = props["axisDivisions"]?.Value<int>()
                        ?? props["axis_divisions"]?.Value<int>()
                        ?? props["segments"]?.Value<int>() ?? 24;
                    int heightCuts = props["heightCuts"]?.Value<int>()
                        ?? props["height_cuts"]?.Value<int>() ?? 0;
                    int smoothing = props["smoothing"]?.Value<int>() ?? -1;
                    return InvokeGenerator("GenerateCylinder",
                        new[] { _pivotLocationType, typeof(int), typeof(float), typeof(float), typeof(int), typeof(int) },
                        new object[] { pivot, axisDivisions, r, h, heightCuts, smoothing });
                }

                case "CONE":
                {
                    float r = radius > 0 ? radius : (size > 0 ? size / 2f : 0.5f);
                    float h = height > 0 ? height : (size > 0 ? size : 1f);
                    int subdivAxis = props["subdivAxis"]?.Value<int>()
                        ?? props["subdiv_axis"]?.Value<int>()
                        ?? props["segments"]?.Value<int>() ?? 6;
                    return InvokeGenerator("GenerateCone",
                        new[] { _pivotLocationType, typeof(float), typeof(float), typeof(int) },
                        new object[] { pivot, r, h, subdivAxis });
                }

                case "SPHERE":
                {
                    float r = radius > 0 ? radius : (size > 0 ? size / 2f : 0.5f);
                    int subdivisions = props["subdivisions"]?.Value<int>() ?? 2;
                    return InvokeGenerator("GenerateIcosahedron",
                        new[] { _pivotLocationType, typeof(float), typeof(int), typeof(bool), typeof(bool) },
                        new object[] { pivot, r, subdivisions, true, false });
                }

                case "TORUS":
                {
                    int rows = props["rows"]?.Value<int>() ?? 8;
                    int columns = props["columns"]?.Value<int>() ?? 16;
                    // ProBuilder convention: innerRadius = ring radius (major), outerRadius = tube radius (minor).
                    // Our API uses the intuitive naming: outerRadius = ring, innerRadius = tube.
                    // So we swap when passing to ProBuilder's GenerateTorus.
                    float tubeRadius = props["innerRadius"]?.Value<float>()
                        ?? props["inner_radius"]?.Value<float>()
                        ?? props["tubeRadius"]?.Value<float>()
                        ?? props["tube_radius"]?.Value<float>()
                        ?? (radius > 0 ? radius * 0.1f : 0.1f);
                    float ringRadius = props["outerRadius"]?.Value<float>()
                        ?? props["outer_radius"]?.Value<float>()
                        ?? props["ringRadius"]?.Value<float>()
                        ?? props["ring_radius"]?.Value<float>()
                        ?? (radius > 0 ? radius : (size > 0 ? size / 2f : 0.5f));
                    bool smooth = props["smooth"]?.Value<bool>() ?? true;
                    float hCirc = props["horizontalCircumference"]?.Value<float>()
                        ?? props["horizontal_circumference"]?.Value<float>() ?? 360f;
                    float vCirc = props["verticalCircumference"]?.Value<float>()
                        ?? props["vertical_circumference"]?.Value<float>() ?? 360f;
                    return InvokeGenerator("GenerateTorus",
                        new[] { _pivotLocationType, typeof(int), typeof(int), typeof(float), typeof(float),
                                typeof(bool), typeof(float), typeof(float), typeof(bool) },
                        new object[] { pivot, rows, columns, ringRadius, tubeRadius, smooth, hCirc, vCirc, false });
                }

                case "PIPE":
                {
                    float r = radius > 0 ? radius : (size > 0 ? size / 2f : 1f);
                    float h = height > 0 ? height : (size > 0 ? size : 2f);
                    float thickness = props["thickness"]?.Value<float>() ?? 0.2f;
                    int subdivAxis = props["subdivAxis"]?.Value<int>()
                        ?? props["subdiv_axis"]?.Value<int>()
                        ?? props["segments"]?.Value<int>() ?? 6;
                    int subdivHeight = props["subdivHeight"]?.Value<int>()
                        ?? props["subdiv_height"]?.Value<int>() ?? 1;
                    return InvokeGenerator("GeneratePipe",
                        new[] { _pivotLocationType, typeof(float), typeof(float), typeof(float), typeof(int), typeof(int) },
                        new object[] { pivot, r, h, thickness, subdivAxis, subdivHeight });
                }

                case "PLANE":
                {
                    float w = width > 0 ? width : (size > 0 ? size : 1f);
                    float h = height > 0 ? height : (depth > 0 ? depth : (size > 0 ? size : 1f));
                    int widthCuts = props["widthCuts"]?.Value<int>()
                        ?? props["width_cuts"]?.Value<int>() ?? 0;
                    int heightCuts = props["heightCuts"]?.Value<int>()
                        ?? props["height_cuts"]?.Value<int>() ?? 0;
                    // Axis enum: default Y-up (2)
                    if (_axisEnum != null)
                    {
                        int axisVal = props["axis"]?.Value<int>() ?? 2;
                        var axisObj = Enum.ToObject(_axisEnum, axisVal);
                        return InvokeGenerator("GeneratePlane",
                            new[] { _pivotLocationType, typeof(float), typeof(float), typeof(int), typeof(int), _axisEnum },
                            new object[] { pivot, w, h, widthCuts, heightCuts, axisObj });
                    }
                    return InvokeGenerator("GeneratePlane",
                        new[] { _pivotLocationType, typeof(float), typeof(float), typeof(int), typeof(int) },
                        new object[] { pivot, w, h, widthCuts, heightCuts });
                }

                case "STAIR":
                {
                    float w = width > 0 ? width : (size > 0 ? size : 2f);
                    float h = height > 0 ? height : (size > 0 ? size : 2.5f);
                    float d = depth > 0 ? depth : (size > 0 ? size : 4f);
                    int steps = props["steps"]?.Value<int>() ?? 10;
                    bool buildSides = props["buildSides"]?.Value<bool>()
                        ?? props["build_sides"]?.Value<bool>() ?? true;
                    return InvokeGenerator("GenerateStair",
                        new[] { _pivotLocationType, typeof(Vector3), typeof(int), typeof(bool) },
                        new object[] { pivot, new Vector3(w, h, d), steps, buildSides });
                }

                case "CURVEDSTAIR":
                {
                    float stairWidth = width > 0 ? width : (size > 0 ? size : 2f);
                    float h = height > 0 ? height : (size > 0 ? size : 2.5f);
                    float innerR = props["innerRadius"]?.Value<float>()
                        ?? props["inner_radius"]?.Value<float>()
                        ?? (radius > 0 ? radius : 2f);
                    float circumference = props["circumference"]?.Value<float>() ?? 90f;
                    int steps = props["steps"]?.Value<int>() ?? 10;
                    bool buildSides = props["buildSides"]?.Value<bool>()
                        ?? props["build_sides"]?.Value<bool>() ?? true;
                    return InvokeGenerator("GenerateCurvedStair",
                        new[] { _pivotLocationType, typeof(float), typeof(float), typeof(float), typeof(float), typeof(int), typeof(bool) },
                        new object[] { pivot, stairWidth, h, innerR, circumference, steps, buildSides });
                }

                case "ARCH":
                {
                    float angle = props["angle"]?.Value<float>() ?? 180f;
                    float r = radius > 0 ? radius : (size > 0 ? size / 2f : 2f);
                    float w = width > 0 ? width : 0.5f;
                    float d = depth > 0 ? depth : 0.5f;
                    int radialCuts = props["radialCuts"]?.Value<int>()
                        ?? props["radial_cuts"]?.Value<int>() ?? 6;
                    bool insideFaces = props["insideFaces"]?.Value<bool>()
                        ?? props["inside_faces"]?.Value<bool>() ?? true;
                    bool outsideFaces = props["outsideFaces"]?.Value<bool>()
                        ?? props["outside_faces"]?.Value<bool>() ?? true;
                    bool frontFaces = props["frontFaces"]?.Value<bool>()
                        ?? props["front_faces"]?.Value<bool>() ?? true;
                    bool backFaces = props["backFaces"]?.Value<bool>()
                        ?? props["back_faces"]?.Value<bool>() ?? true;
                    bool endCaps = props["endCaps"]?.Value<bool>()
                        ?? props["end_caps"]?.Value<bool>() ?? true;
                    return InvokeGenerator("GenerateArch",
                        new[] { _pivotLocationType, typeof(float), typeof(float), typeof(float), typeof(float),
                                typeof(int), typeof(bool), typeof(bool), typeof(bool), typeof(bool), typeof(bool) },
                        new object[] { pivot, angle, r, w, d, radialCuts,
                                      insideFaces, outsideFaces, frontFaces, backFaces, endCaps });
                }

                case "DOOR":
                {
                    float totalWidth = width > 0 ? width : (size > 0 ? size : 4f);
                    float totalHeight = height > 0 ? height : (size > 0 ? size : 4f);
                    float ledgeHeight = props["ledgeHeight"]?.Value<float>()
                        ?? props["ledge_height"]?.Value<float>() ?? 0.1f;
                    float legWidth = props["legWidth"]?.Value<float>()
                        ?? props["leg_width"]?.Value<float>() ?? 1f;
                    float d = depth > 0 ? depth : (size > 0 ? size : 0.5f);
                    return InvokeGenerator("GenerateDoor",
                        new[] { _pivotLocationType, typeof(float), typeof(float), typeof(float), typeof(float), typeof(float) },
                        new object[] { pivot, totalWidth, totalHeight, ledgeHeight, legWidth, d });
                }

                default:
                    return null;
            }
        }

        private static Component CreateShapeGeneric(string shapeTypeStr)
        {
            object shapeTypeValue;
            try
            {
                shapeTypeValue = Enum.Parse(_shapeTypeEnum, shapeTypeStr, true);
            }
            catch
            {
                var validTypes = string.Join(", ", Enum.GetNames(_shapeTypeEnum));
                throw new Exception($"Unknown shape type '{shapeTypeStr}'. Valid types: {validTypes}");
            }

            // Try CreateShape(ShapeType) first
            var createMethod = _shapeGeneratorType.GetMethod("CreateShape",
                BindingFlags.Static | BindingFlags.Public,
                null,
                new[] { _shapeTypeEnum },
                null);

            object[] invokeArgs;
            if (createMethod != null)
            {
                invokeArgs = new[] { shapeTypeValue };
            }
            else if (_pivotLocationType != null)
            {
                createMethod = _shapeGeneratorType.GetMethod("CreateShape",
                    BindingFlags.Static | BindingFlags.Public,
                    null,
                    new[] { _shapeTypeEnum, _pivotLocationType },
                    null);
                invokeArgs = new[] { shapeTypeValue, GetPivotCenter() };
            }
            else
            {
                return null;
            }

            return createMethod?.Invoke(null, invokeArgs) as Component;
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

            createFromPolygonMethod.Invoke(null, new object[] { pbMesh, points, extrudeHeight, flipNormals });

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

            int edgeCount;
            Array edgeArray;
            try
            {
                edgeArray = ResolveEdges(pbMesh, props, out edgeCount);
            }
            catch (Exception ex)
            {
                return new ErrorResponse(ex.Message);
            }

            float distance = props["distance"]?.Value<float>() ?? 0.5f;
            bool asGroup = props["asGroup"]?.Value<bool>() ?? props["as_group"]?.Value<bool>() ?? true;

            Undo.RegisterCompleteObjectUndo(pbMesh, "Extrude Edges");

            var extrudeMethod = _extrudeElementsType?.GetMethod("Extrude",
                BindingFlags.Static | BindingFlags.Public,
                null,
                new[] { _proBuilderMeshType, edgeArray.GetType(), typeof(float), typeof(bool), typeof(bool) },
                null);

            if (extrudeMethod == null)
                return new ErrorResponse("ExtrudeElements.Extrude (edges) method not found.");

            extrudeMethod.Invoke(null, new object[] { pbMesh, edgeArray, distance, asGroup, true });
            RefreshMesh(pbMesh);

            return new SuccessResponse($"Extruded {edgeCount} edge(s) by {distance}", new
            {
                edgesExtruded = edgeCount,
                distance,
                faceCount = GetFaceCount(pbMesh),
            });
        }

        private static object BevelEdges(JObject @params)
        {
            var pbMesh = RequireProBuilderMesh(@params);
            var props = ExtractProperties(@params);

            int edgeCount;
            Array edgeArray;
            try
            {
                edgeArray = ResolveEdges(pbMesh, props, out edgeCount);
            }
            catch (Exception ex)
            {
                return new ErrorResponse(ex.Message);
            }

            float amount = props["amount"]?.Value<float>() ?? 0.1f;

            if (_bevelType == null)
                return new ErrorResponse("Bevel type not found in ProBuilder assembly.");

            Undo.RegisterCompleteObjectUndo(pbMesh, "Bevel Edges");

            var typedList = ToTypedEdgeList(edgeArray);

            var bevelMethod = _bevelType.GetMethod("BevelEdges",
                BindingFlags.Static | BindingFlags.Public);

            if (bevelMethod == null)
                return new ErrorResponse("Bevel.BevelEdges method not found.");

            bevelMethod.Invoke(null, new object[] { pbMesh, typedList, amount });
            RefreshMesh(pbMesh);

            return new SuccessResponse($"Beveled {edgeCount} edge(s) with amount {amount}", new
            {
                edgesBeveled = edgeCount,
                amount,
                faceCount = GetFaceCount(pbMesh),
            });
        }

        private static object Subdivide(JObject @params)
        {
            var pbMesh = RequireProBuilderMesh(@params);
            var props = ExtractProperties(@params);

            if (_connectElementsType == null)
                return new ErrorResponse("ConnectElements type not found.");

            Undo.RegisterCompleteObjectUndo(pbMesh, "Subdivide");

            var faceIndicesToken = props["faceIndices"] ?? props["face_indices"];

            // Get faces to subdivide (all faces if none specified)
            var faces = GetFacesByIndices(pbMesh, faceIndicesToken);
            var faceListType = typeof(List<>).MakeGenericType(_faceType);
            var faceList = Activator.CreateInstance(faceListType) as System.Collections.IList;
            foreach (var f in faces)
                faceList.Add(f);

            // ProBuilder uses ConnectElements.Connect(mesh, faces) for face subdivision
            var connectMethod = _connectElementsType.GetMethods(BindingFlags.Static | BindingFlags.Public)
                .FirstOrDefault(m => m.Name == "Connect" && m.GetParameters().Length == 2
                    && m.GetParameters()[1].ParameterType.IsAssignableFrom(faceListType));

            if (connectMethod == null)
                return new ErrorResponse("ConnectElements.Connect (faces) method not found.");

            connectMethod.Invoke(null, new object[] { pbMesh, faceList });

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

            // Prefer DeleteFaces(ProBuilderMesh, IList<int>) overload
            var deleteMethod = _deleteElementsType.GetMethod("DeleteFaces",
                BindingFlags.Static | BindingFlags.Public,
                null,
                new[] { _proBuilderMeshType, typeof(IList<int>) },
                null);

            if (deleteMethod != null)
            {
                deleteMethod.Invoke(null, new object[] { pbMesh, faceIndices.ToList() });
            }
            else
            {
                // Try int[] overload
                deleteMethod = _deleteElementsType.GetMethod("DeleteFaces",
                    BindingFlags.Static | BindingFlags.Public,
                    null,
                    new[] { _proBuilderMeshType, typeof(int[]) },
                    null);

                if (deleteMethod == null)
                {
                    // Try IEnumerable<Face> overload
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

            int aA = edgeAToken["a"]?.Value<int>() ?? 0;
            int aB = edgeAToken["b"]?.Value<int>() ?? 0;
            int bA = edgeBToken["a"]?.Value<int>() ?? 0;
            int bB = edgeBToken["b"]?.Value<int>() ?? 0;

            var edgeA = CreateEdge(aA, aB);
            var edgeB = CreateEdge(bA, bB);

            bool allowNonManifold = props["allowNonManifold"]?.Value<bool>()
                ?? props["allow_non_manifold"]?.Value<bool>()
                ?? props["allowNonManifoldGeometry"]?.Value<bool>()
                ?? props["allow_non_manifold_geometry"]?.Value<bool>()
                ?? false;

            Undo.RegisterCompleteObjectUndo(pbMesh, "Bridge Edges");

            // Try overload with allowNonManifoldGeometry parameter first
            var bridgeMethod = _appendElementsType.GetMethod("Bridge",
                BindingFlags.Static | BindingFlags.Public,
                null,
                new[] { _proBuilderMeshType, _edgeType, _edgeType, typeof(bool) },
                null);

            object result;
            if (bridgeMethod != null)
            {
                result = bridgeMethod.Invoke(null, new object[] { pbMesh, edgeA, edgeB, allowNonManifold });
            }
            else
            {
                // Fallback without allowNonManifold
                bridgeMethod = _appendElementsType.GetMethod("Bridge",
                    BindingFlags.Static | BindingFlags.Public,
                    null,
                    new[] { _proBuilderMeshType, _edgeType, _edgeType },
                    null);

                if (bridgeMethod == null)
                    return new ErrorResponse("AppendElements.Bridge method not found.");

                result = bridgeMethod.Invoke(null, new object[] { pbMesh, edgeA, edgeB });
            }

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
            var edgePairsToken = props["edges"];

            if (faceIndicesToken != null)
            {
                var faces = GetFacesByIndices(pbMesh, faceIndicesToken);

                // Build IEnumerable<Face> compatible type (List<Face>)
                var faceListType = typeof(List<>).MakeGenericType(_faceType);
                var faceList = Activator.CreateInstance(faceListType) as System.Collections.IList;
                foreach (var f in faces)
                    faceList.Add(f);

                // Try Connect(ProBuilderMesh, IEnumerable<Face>)
                var connectMethod = _connectElementsType.GetMethods(BindingFlags.Static | BindingFlags.Public)
                    .FirstOrDefault(m => m.Name == "Connect" && m.GetParameters().Length == 2
                        && m.GetParameters()[1].ParameterType.IsAssignableFrom(faceListType));

                if (connectMethod == null)
                    return new ErrorResponse("ConnectElements.Connect (faces) method not found.");

                connectMethod.Invoke(null, new object[] { pbMesh, faceList });
            }
            else if (edgeIndicesToken != null || edgePairsToken != null)
            {
                int edgeCount;
                Array edgeArray;
                try
                {
                    edgeArray = ResolveEdges(pbMesh, props, out edgeCount);
                }
                catch (Exception ex)
                {
                    return new ErrorResponse(ex.Message);
                }

                var typedList = ToTypedEdgeList(edgeArray);
                var edgeListType = typedList.GetType();

                var connectMethod = _connectElementsType.GetMethods(BindingFlags.Static | BindingFlags.Public)
                    .FirstOrDefault(m => m.Name == "Connect" && m.GetParameters().Length == 2
                        && m.GetParameters()[1].ParameterType.IsAssignableFrom(edgeListType));

                if (connectMethod == null)
                    return new ErrorResponse("ConnectElements.Connect (edges) method not found.");

                connectMethod.Invoke(null, new object[] { pbMesh, typedList });
            }
            else
            {
                return new ErrorResponse("Either faceIndices or edgeIndices/edges parameter is required.");
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

            bool deleteSource = props["deleteSourceFaces"]?.Value<bool>()
                ?? props["delete_source_faces"]?.Value<bool>()
                ?? props["deleteSource"]?.Value<bool>()
                ?? props["delete_source"]?.Value<bool>()
                ?? false;

            Undo.RegisterCompleteObjectUndo(pbMesh, "Detach Faces");

            // Build IEnumerable<Face> compatible list for reflection matching
            var faceListType = typeof(List<>).MakeGenericType(_faceType);
            var faceList = Activator.CreateInstance(faceListType) as System.Collections.IList;
            foreach (var f in faces)
                faceList.Add(f);

            // Try overload: DetachFaces(ProBuilderMesh, IEnumerable<Face>, bool)
            var detachMethod = _extrudeElementsType.GetMethods(BindingFlags.Static | BindingFlags.Public)
                .FirstOrDefault(m => m.Name == "DetachFaces" && m.GetParameters().Length == 3
                    && m.GetParameters()[1].ParameterType.IsAssignableFrom(faceListType)
                    && m.GetParameters()[2].ParameterType == typeof(bool));

            if (detachMethod != null)
            {
                detachMethod.Invoke(null, new object[] { pbMesh, faceList, deleteSource });
            }
            else
            {
                // Fallback: DetachFaces(ProBuilderMesh, IEnumerable<Face>)
                detachMethod = _extrudeElementsType.GetMethods(BindingFlags.Static | BindingFlags.Public)
                    .FirstOrDefault(m => m.Name == "DetachFaces" && m.GetParameters().Length == 2
                        && m.GetParameters()[1].ParameterType.IsAssignableFrom(faceListType));

                if (detachMethod == null)
                    return new ErrorResponse("ExtrudeElements.DetachFaces method not found.");

                detachMethod.Invoke(null, new object[] { pbMesh, faceList });
            }

            RefreshMesh(pbMesh);

            return new SuccessResponse($"Detached {faces.Length} face(s)", new
            {
                facesDetached = faces.Length,
                deleteSourceFaces = deleteSource,
                faceCount = GetFaceCount(pbMesh),
            });
        }

        private static object FlipNormals(JObject @params)
        {
            var pbMesh = RequireProBuilderMesh(@params);
            var props = ExtractProperties(@params);
            var faces = GetFacesByIndices(pbMesh, props["faceIndices"] ?? props["face_indices"]);

            Undo.RegisterCompleteObjectUndo(pbMesh, "Flip Normals");

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

            // Build IEnumerable<Face> compatible type
            var faceListType = typeof(List<>).MakeGenericType(_faceType);
            var faceList = Activator.CreateInstance(faceListType) as System.Collections.IList;
            foreach (var f in faces)
                faceList.Add(f);

            var mergeMethod = _mergeElementsType.GetMethods(BindingFlags.Static | BindingFlags.Public)
                .FirstOrDefault(m => m.Name == "Merge" && m.GetParameters().Length == 2
                    && m.GetParameters()[1].ParameterType.IsAssignableFrom(faceListType));

            if (mergeMethod == null)
                return new ErrorResponse("MergeElements.Merge method not found.");

            mergeMethod.Invoke(null, new object[] { pbMesh, faceList });
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

        private static object DuplicateAndFlip(JObject @params)
        {
            var pbMesh = RequireProBuilderMesh(@params);
            var props = ExtractProperties(@params);
            var faces = GetFacesByIndices(pbMesh, props["faceIndices"] ?? props["face_indices"]);

            if (_appendElementsType == null)
                return new ErrorResponse("AppendElements type not found.");

            Undo.RegisterCompleteObjectUndo(pbMesh, "Duplicate and Flip");

            // DuplicateAndFlip(ProBuilderMesh, Face[])
            var faceArrayType = Array.CreateInstance(_faceType, 0).GetType();
            var dupMethod = _appendElementsType.GetMethod("DuplicateAndFlip",
                BindingFlags.Static | BindingFlags.Public,
                null,
                new[] { _proBuilderMeshType, faceArrayType },
                null);

            if (dupMethod == null)
                return new ErrorResponse("AppendElements.DuplicateAndFlip method not found.");

            dupMethod.Invoke(null, new object[] { pbMesh, faces });
            RefreshMesh(pbMesh);

            return new SuccessResponse($"Duplicated and flipped {faces.Length} face(s)", new
            {
                facesDuplicated = faces.Length,
                faceCount = GetFaceCount(pbMesh),
            });
        }

        private static object CreatePolygon(JObject @params)
        {
            var pbMesh = RequireProBuilderMesh(@params);
            var props = ExtractProperties(@params);

            var vertexIndicesToken = props["vertexIndices"] ?? props["vertex_indices"];
            if (vertexIndicesToken == null)
                return new ErrorResponse("vertexIndices parameter is required.");

            if (_appendElementsType == null)
                return new ErrorResponse("AppendElements type not found.");

            var vertexIndices = vertexIndicesToken.ToObject<int[]>();
            bool unordered = props["unordered"]?.Value<bool>() ?? true;

            Undo.RegisterCompleteObjectUndo(pbMesh, "Create Polygon");

            // CreatePolygon(ProBuilderMesh, IList<int>, bool)
            var createPolyMethod = _appendElementsType.GetMethod("CreatePolygon",
                BindingFlags.Static | BindingFlags.Public,
                null,
                new[] { _proBuilderMeshType, typeof(IList<int>), typeof(bool) },
                null);

            if (createPolyMethod == null)
                return new ErrorResponse("AppendElements.CreatePolygon method not found.");

            var result = createPolyMethod.Invoke(null, new object[] { pbMesh, vertexIndices.ToList(), unordered });
            RefreshMesh(pbMesh);

            return new SuccessResponse($"Created polygon from {vertexIndices.Length} vertices", new
            {
                vertexCount = vertexIndices.Length,
                unordered,
                faceCreated = result != null,
                faceCount = GetFaceCount(pbMesh),
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
            bool collapseToFirst = props["collapseToFirst"]?.Value<bool>()
                ?? props["collapse_to_first"]?.Value<bool>()
                ?? false;

            if (_vertexEditingType == null)
                return new ErrorResponse("VertexEditing type not found.");

            Undo.RegisterCompleteObjectUndo(pbMesh, "Merge Vertices");

            // MergeVertices(ProBuilderMesh mesh, int[] indexes, bool collapseToFirst = false)
            var mergeMethod = _vertexEditingType.GetMethod("MergeVertices",
                BindingFlags.Static | BindingFlags.Public);

            if (mergeMethod == null)
                return new ErrorResponse("VertexEditing.MergeVertices method not found.");

            var result = mergeMethod.Invoke(null, new object[] { pbMesh, vertexIndices, collapseToFirst });
            RefreshMesh(pbMesh);

            return new SuccessResponse($"Merged {vertexIndices.Length} vertices", new
            {
                verticesMerged = vertexIndices.Length,
                collapseToFirst,
                resultIndex = result is int idx ? idx : -1,
                vertexCount = GetVertexCount(pbMesh),
            });
        }

        private static object WeldVertices(JObject @params)
        {
            var pbMesh = RequireProBuilderMesh(@params);
            var props = ExtractProperties(@params);
            var vertexIndicesToken = props["vertexIndices"] ?? props["vertex_indices"];
            if (vertexIndicesToken == null)
                return new ErrorResponse("vertexIndices parameter is required.");

            var vertexIndices = vertexIndicesToken.ToObject<int[]>();
            float neighborRadius = props["radius"]?.Value<float>()
                ?? props["neighborRadius"]?.Value<float>()
                ?? props["neighbor_radius"]?.Value<float>()
                ?? 0.01f;

            if (_vertexEditingType == null)
                return new ErrorResponse("VertexEditing type not found.");

            Undo.RegisterCompleteObjectUndo(pbMesh, "Weld Vertices");

            // WeldVertices(ProBuilderMesh mesh, IEnumerable<int> indexes, float neighborRadius)
            var weldMethod = _vertexEditingType.GetMethod("WeldVertices",
                BindingFlags.Static | BindingFlags.Public);

            if (weldMethod == null)
                return new ErrorResponse("VertexEditing.WeldVertices method not found.");

            var result = weldMethod.Invoke(null, new object[] { pbMesh, vertexIndices.ToList(), neighborRadius });
            RefreshMesh(pbMesh);

            int[] newIndices = result as int[] ?? Array.Empty<int>();

            return new SuccessResponse($"Welded vertices within radius {neighborRadius}", new
            {
                inputCount = vertexIndices.Length,
                resultCount = newIndices.Length,
                radius = neighborRadius,
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

            if (_vertexEditingType == null)
                return new ErrorResponse("VertexEditing type not found.");

            Undo.RegisterCompleteObjectUndo(pbMesh, "Split Vertices");

            // SplitVertices(ProBuilderMesh mesh, IEnumerable<int> vertices)
            var splitMethod = _vertexEditingType.GetMethod("SplitVertices",
                BindingFlags.Static | BindingFlags.Public,
                null,
                new[] { _proBuilderMeshType, typeof(IEnumerable<int>) },
                null);

            if (splitMethod == null)
            {
                // Fallback: try any 2-param overload
                splitMethod = _vertexEditingType.GetMethods(BindingFlags.Static | BindingFlags.Public)
                    .FirstOrDefault(m => m.Name == "SplitVertices" && m.GetParameters().Length == 2
                        && m.GetParameters()[0].ParameterType == _proBuilderMeshType);
            }

            if (splitMethod == null)
                return new ErrorResponse("VertexEditing.SplitVertices method not found.");

            splitMethod.Invoke(null, new object[] { pbMesh, vertexIndices.ToList() });
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

            // Get positions via property and modify directly
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

            // Set positions back via property setter
            if (positionsProperty.CanWrite)
            {
                positionsProperty.SetValue(pbMesh, posList);
            }
            else
            {
                // Try SetPositions method
                var setPositionsMethod = _proBuilderMeshType.GetMethod("SetPositions",
                    BindingFlags.Instance | BindingFlags.Public);
                if (setPositionsMethod != null)
                {
                    setPositionsMethod.Invoke(pbMesh, new object[] { posList.ToArray() });
                }
                else
                {
                    // Try RebuildWithPositionsAndFaces
                    var rebuildMethod = _proBuilderMeshType.GetMethod("RebuildWithPositionsAndFaces",
                        BindingFlags.Instance | BindingFlags.Public);
                    if (rebuildMethod != null)
                    {
                        var allFaces = GetFacesArray(pbMesh);
                        rebuildMethod.Invoke(pbMesh, new object[] { posList, allFaces });
                    }
                    else
                    {
                        return new ErrorResponse("Cannot set vertex positions on ProBuilderMesh.");
                    }
                }
            }

            RefreshMesh(pbMesh);

            return new SuccessResponse($"Moved {vertexIndices.Length} vertices by ({offset.x}, {offset.y}, {offset.z})", new
            {
                verticesMoved = vertexIndices.Length,
                offset = new[] { offset.x, offset.y, offset.z },
            });
        }

        private static object InsertVertex(JObject @params)
        {
            var pbMesh = RequireProBuilderMesh(@params);
            var props = ExtractProperties(@params);

            if (_appendElementsType == null)
                return new ErrorResponse("AppendElements type not found.");

            var pointToken = props["point"] ?? props["position"];
            if (pointToken == null)
                return new ErrorResponse("point parameter is required ([x,y,z] in local space).");

            var point = ParseVector3(pointToken);

            Undo.RegisterCompleteObjectUndo(pbMesh, "Insert Vertex");

            var edgeToken = props["edge"];
            if (edgeToken != null)
            {
                // InsertVertexOnEdge(ProBuilderMesh mesh, Edge edge, Vector3 point)
                int a = edgeToken["a"]?.Value<int>() ?? 0;
                int b = edgeToken["b"]?.Value<int>() ?? 0;
                var edge = CreateEdge(a, b);

                var insertMethod = _appendElementsType.GetMethod("InsertVertexOnEdge",
                    BindingFlags.Static | BindingFlags.Public,
                    null,
                    new[] { _proBuilderMeshType, _edgeType, typeof(Vector3) },
                    null);

                if (insertMethod == null)
                    return new ErrorResponse("AppendElements.InsertVertexOnEdge method not found.");

                insertMethod.Invoke(null, new object[] { pbMesh, edge, point });
            }
            else
            {
                var faceIndexToken = props["faceIndex"] ?? props["face_index"];
                if (faceIndexToken == null)
                    return new ErrorResponse("Either edge ({a,b}) or faceIndex parameter is required.");

                int faceIndex = faceIndexToken.Value<int>();
                var allFaces = (System.Collections.IList)GetFacesArray(pbMesh);
                if (faceIndex < 0 || faceIndex >= allFaces.Count)
                    return new ErrorResponse($"Face index {faceIndex} out of range (0-{allFaces.Count - 1}).");

                var face = allFaces[faceIndex];

                // InsertVertexInFace(ProBuilderMesh mesh, Face face, Vector3 point)
                var insertMethod = _appendElementsType.GetMethod("InsertVertexInFace",
                    BindingFlags.Static | BindingFlags.Public,
                    null,
                    new[] { _proBuilderMeshType, _faceType, typeof(Vector3) },
                    null);

                if (insertMethod == null)
                    return new ErrorResponse("AppendElements.InsertVertexInFace method not found.");

                insertMethod.Invoke(null, new object[] { pbMesh, face, point });
            }

            RefreshMesh(pbMesh);

            return new SuccessResponse("Inserted vertex", new
            {
                point = new[] { point.x, point.y, point.z },
                vertexCount = GetVertexCount(pbMesh),
                faceCount = GetFaceCount(pbMesh),
            });
        }

        private static object AppendVerticesToEdge(JObject @params)
        {
            var pbMesh = RequireProBuilderMesh(@params);
            var props = ExtractProperties(@params);

            if (_appendElementsType == null)
                return new ErrorResponse("AppendElements type not found.");

            int count = props["count"]?.Value<int>() ?? 1;

            Undo.RegisterCompleteObjectUndo(pbMesh, "Append Vertices to Edge");

            int edgeCount;
            Array edgeArray;
            try
            {
                edgeArray = ResolveEdges(pbMesh, props, out edgeCount);
            }
            catch (Exception ex)
            {
                return new ErrorResponse(ex.Message);
            }

            var typedList = ToTypedEdgeList(edgeArray);
            var edgeListType = typedList.GetType();

            // AppendVerticesToEdge(ProBuilderMesh mesh, IList<Edge> edges, int count)
            var appendMethod = _appendElementsType.GetMethod("AppendVerticesToEdge",
                BindingFlags.Static | BindingFlags.Public,
                null,
                new[] { _proBuilderMeshType, edgeListType, typeof(int) },
                null);

            if (appendMethod == null)
            {
                // Try IList<Edge> interface match
                appendMethod = _appendElementsType.GetMethods(BindingFlags.Static | BindingFlags.Public)
                    .FirstOrDefault(m => m.Name == "AppendVerticesToEdge" && m.GetParameters().Length == 3
                        && m.GetParameters()[2].ParameterType == typeof(int));
            }

            if (appendMethod == null)
                return new ErrorResponse("AppendElements.AppendVerticesToEdge method not found.");

            appendMethod.Invoke(null, new object[] { pbMesh, typedList, count });
            RefreshMesh(pbMesh);

            return new SuccessResponse($"Inserted {count} point(s) on {edgeCount} edge(s)", new
            {
                edgesModified = edgeCount,
                pointsPerEdge = count,
                vertexCount = GetVertexCount(pbMesh),
                faceCount = GetFaceCount(pbMesh),
            });
        }

        // =====================================================================
        // Selection
        // =====================================================================

        private static object SelectFaces(JObject @params)
        {
            var pbMesh = RequireProBuilderMesh(@params);
            var props = ExtractProperties(@params);

            var allFaces = GetFacesArray(pbMesh);
            var facesList = (System.Collections.IList)allFaces;
            var selectedIndices = new List<int>();

            // Selection by direction
            var directionStr = props["direction"]?.ToString();
            if (!string.IsNullOrEmpty(directionStr))
            {
                float tolerance = props["tolerance"]?.Value<float>() ?? 0.7f;
                Vector3 targetDir;
                switch (directionStr.ToLowerInvariant())
                {
                    case "up": case "top": targetDir = Vector3.up; break;
                    case "down": case "bottom": targetDir = Vector3.down; break;
                    case "forward": case "front": targetDir = Vector3.forward; break;
                    case "back": case "backward": targetDir = Vector3.back; break;
                    case "left": targetDir = Vector3.left; break;
                    case "right": targetDir = Vector3.right; break;
                    default:
                        return new ErrorResponse($"Unknown direction '{directionStr}'. Valid: up/down/forward/back/left/right");
                }

                for (int i = 0; i < facesList.Count; i++)
                {
                    var normal = ComputeFaceNormal(pbMesh, facesList[i]);
                    if (Vector3.Dot(normal, targetDir) > tolerance)
                        selectedIndices.Add(i);
                }
            }

            // Grow selection from existing faces
            var growFromToken = props["growFrom"] ?? props["grow_from"];
            var growAngle = props["growAngle"]?.Value<float>() ?? props["grow_angle"]?.Value<float>() ?? -1f;
            if (growFromToken != null && _elementSelectionType != null)
            {
                var seedFaces = GetFacesByIndices(pbMesh, growFromToken);
                var faceListType = typeof(List<>).MakeGenericType(_faceType);
                var seedList = Activator.CreateInstance(faceListType) as System.Collections.IList;
                foreach (var f in seedFaces)
                    seedList.Add(f);

                var growMethod = _elementSelectionType.GetMethods(BindingFlags.Static | BindingFlags.Public)
                    .FirstOrDefault(m => m.Name == "GrowSelection" && m.GetParameters().Length == 3);

                if (growMethod != null)
                {
                    var result = growMethod.Invoke(null, new object[] { pbMesh, seedList, growAngle });
                    if (result is System.Collections.IEnumerable resultFaces)
                    {
                        foreach (var face in resultFaces)
                        {
                            int idx = IndexOfFace(facesList, face);
                            if (idx >= 0 && !selectedIndices.Contains(idx))
                                selectedIndices.Add(idx);
                        }
                    }
                }
            }

            // Flood selection from existing faces
            var floodFromToken = props["floodFrom"] ?? props["flood_from"];
            var floodAngle = props["floodAngle"]?.Value<float>() ?? props["flood_angle"]?.Value<float>() ?? 15f;
            if (floodFromToken != null && _elementSelectionType != null)
            {
                var seedFaces = GetFacesByIndices(pbMesh, floodFromToken);
                var faceListType = typeof(List<>).MakeGenericType(_faceType);
                var seedList = Activator.CreateInstance(faceListType) as System.Collections.IList;
                foreach (var f in seedFaces)
                    seedList.Add(f);

                var floodMethod = _elementSelectionType.GetMethods(BindingFlags.Static | BindingFlags.Public)
                    .FirstOrDefault(m => m.Name == "FloodSelection" && m.GetParameters().Length == 3);

                if (floodMethod != null)
                {
                    var result = floodMethod.Invoke(null, new object[] { pbMesh, seedList, floodAngle });
                    if (result is System.Collections.IEnumerable resultFaces)
                    {
                        foreach (var face in resultFaces)
                        {
                            int idx = IndexOfFace(facesList, face);
                            if (idx >= 0 && !selectedIndices.Contains(idx))
                                selectedIndices.Add(idx);
                        }
                    }
                }
            }

            // Loop/ring selection
            var loopFromToken = props["loopFrom"] ?? props["loop_from"];
            bool ring = props["ring"]?.Value<bool>() ?? false;
            if (loopFromToken != null && _elementSelectionType != null)
            {
                var seedFaces = GetFacesByIndices(pbMesh, loopFromToken);
                var faceArrayType = Array.CreateInstance(_faceType, 0).GetType();

                var loopMethod = _elementSelectionType.GetMethods(BindingFlags.Static | BindingFlags.Public)
                    .FirstOrDefault(m => m.Name == "GetFaceLoop" && m.GetParameters().Length >= 2);

                if (loopMethod != null)
                {
                    object result;
                    if (loopMethod.GetParameters().Length == 3)
                        result = loopMethod.Invoke(null, new object[] { pbMesh, seedFaces, ring });
                    else
                        result = loopMethod.Invoke(null, new object[] { pbMesh, seedFaces });

                    if (result is System.Collections.IEnumerable resultFaces)
                    {
                        foreach (var face in resultFaces)
                        {
                            int idx = IndexOfFace(facesList, face);
                            if (idx >= 0 && !selectedIndices.Contains(idx))
                                selectedIndices.Add(idx);
                        }
                    }
                }
            }

            selectedIndices.Sort();

            return new SuccessResponse($"Selected {selectedIndices.Count} face(s)", new
            {
                faceIndices = selectedIndices,
                count = selectedIndices.Count,
                totalFaces = facesList.Count,
            });
        }

        private static int IndexOfFace(System.Collections.IList facesList, object face)
        {
            for (int i = 0; i < facesList.Count; i++)
            {
                if (ReferenceEquals(facesList[i], face))
                    return i;
            }
            return -1;
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

                var usedIndices = new SortedSet<int>();
                foreach (var f in allFacesList)
                    usedIndices.Add((int)submeshIndexProp.GetValue(f));

                if (usedIndices.Count < currentMats.Length)
                {
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

            var setColorMethod = _proBuilderMeshType.GetMethod("SetFaceColor",
                BindingFlags.Instance | BindingFlags.Public);

            if (setColorMethod == null)
                return new ErrorResponse("SetFaceColor method not found.");

            foreach (var face in faces)
                setColorMethod.Invoke(pbMesh, new object[] { face, color });

            RefreshMesh(pbMesh);

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

            var uvProperty = _faceType.GetProperty("uv");
            if (uvProperty == null)
                return new ErrorResponse("Face.uv property not found.");

            var autoUnwrapType = uvProperty.PropertyType;

            foreach (var face in faces)
            {
                var uvSettings = uvProperty.GetValue(face);

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

                var rotationToken = props["rotation"];
                if (rotationToken != null)
                {
                    var rotField = autoUnwrapType.GetField("rotation");
                    if (rotField != null)
                        rotField.SetValue(uvSettings, rotationToken.Value<float>());
                }

                var flipUToken = props["flipU"] ?? props["flip_u"];
                if (flipUToken != null)
                {
                    var flipUField = autoUnwrapType.GetField("flipU");
                    if (flipUField != null)
                        flipUField.SetValue(uvSettings, flipUToken.Value<bool>());
                }

                var flipVToken = props["flipV"] ?? props["flip_v"];
                if (flipVToken != null)
                {
                    var flipVField = autoUnwrapType.GetField("flipV");
                    if (flipVField != null)
                        flipVField.SetValue(uvSettings, flipVToken.Value<bool>());
                }

                uvProperty.SetValue(face, uvSettings);
            }

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

            var renderer = pbMesh.gameObject.GetComponent<MeshRenderer>();
            Bounds bounds = renderer != null ? renderer.bounds : new Bounds();

            var materials = new List<string>();
            if (renderer != null)
            {
                foreach (var mat in renderer.sharedMaterials)
                    materials.Add(mat != null ? mat.name : "(none)");
            }

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

            if (include == "edges" || include == "all")
            {
                var uniqueEdges = CollectUniqueEdges(pbMesh);

                // Get vertex positions for enriched edge data
                var positionsProp = _proBuilderMeshType.GetProperty("positions");
                var positions = positionsProp?.GetValue(pbMesh) as IList<Vector3>;

                var edgeDetails = new List<object>();
                for (int i = 0; i < uniqueEdges.Count && i < 200; i++)
                {
                    var edge = uniqueEdges[i];
                    int vertA = GetEdgeVertexA(edge);
                    int vertB = GetEdgeVertexB(edge);

                    var edgeInfo = new Dictionary<string, object>
                    {
                        ["index"] = i,
                        ["vertexA"] = vertA,
                        ["vertexB"] = vertB,
                    };

                    // Include world-space positions for each endpoint
                    if (positions != null)
                    {
                        if (vertA >= 0 && vertA < positions.Count)
                        {
                            var posA = pbMesh.transform.TransformPoint(positions[vertA]);
                            edgeInfo["positionA"] = new[] { Round(posA.x), Round(posA.y), Round(posA.z) };
                        }
                        if (vertB >= 0 && vertB < positions.Count)
                        {
                            var posB = pbMesh.transform.TransformPoint(positions[vertB]);
                            edgeInfo["positionB"] = new[] { Round(posB.x), Round(posB.y), Round(posB.z) };
                        }
                    }

                    edgeDetails.Add(edgeInfo);
                }
                data["edges"] = edgeDetails;
                data["edgeCount"] = uniqueEdges.Count;
                data["edgesTruncated"] = uniqueEdges.Count > 200;
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

            var pbMesh = go.AddComponent(_proBuilderMeshType);

            // Use MeshImporter(Mesh, Material[], ProBuilderMesh) constructor
            var renderer = go.GetComponent<MeshRenderer>();
            var materials = renderer != null ? renderer.sharedMaterials : new Material[0];
            var importerCtor = _meshImporterType.GetConstructor(
                new[] { typeof(Mesh), typeof(Material[]), _proBuilderMeshType });

            if (importerCtor == null)
            {
                // Fall back to MeshImporter(ProBuilderMesh)
                importerCtor = _meshImporterType.GetConstructor(new[] { _proBuilderMeshType });
                if (importerCtor == null)
                    return new ErrorResponse("MeshImporter constructor not found.");
            }

            object importer;
            if (importerCtor.GetParameters().Length == 3)
                importer = importerCtor.Invoke(new object[] { meshFilter.sharedMesh, materials, pbMesh });
            else
                importer = importerCtor.Invoke(new object[] { pbMesh });

            // Find Import() overload with fewest parameters (takes optional MeshImportSettings)
            var importM = _meshImporterType.GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .Where(m => m.Name == "Import")
                .OrderBy(m => m.GetParameters().Length)
                .FirstOrDefault();

            if (importM != null)
            {
                var importParams = importM.GetParameters();
                if (importParams.Length == 0)
                    importM.Invoke(importer, null);
                else
                    importM.Invoke(importer, new object[] { null });
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
        // Legacy compatibility: CollectAllEdges (now returns unique edges)
        // =====================================================================

        internal static List<object> CollectAllEdges(Component pbMesh)
        {
            return CollectUniqueEdges(pbMesh);
        }
    }
}
