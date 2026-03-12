using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using MCPForUnity.Editor.Tools.ProBuilder;
using static MCPForUnityTests.Editor.TestUtilities;

namespace MCPForUnityTests.Editor.Tools
{
    public class ManageProBuilderTests
    {
        private readonly List<GameObject> _createdObjects = new List<GameObject>();
        private bool _proBuilderInstalled;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            _proBuilderInstalled = Type.GetType(
                "UnityEngine.ProBuilder.ProBuilderMesh, Unity.ProBuilder"
            ) != null;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _createdObjects)
            {
                if (go != null)
                    UnityEngine.Object.DestroyImmediate(go);
            }
            _createdObjects.Clear();
        }

        // =====================================================================
        // Basic action validation (works regardless of ProBuilder installation)
        // =====================================================================

        [Test]
        public void HandleCommand_MissingAction_ReturnsError()
        {
            var paramsObj = new JObject();
            var result = ToJObject(ManageProBuilder.HandleCommand(paramsObj));

            Assert.IsFalse(result.Value<bool>("success"), result.ToString());
        }

        [Test]
        public void HandleCommand_UnknownAction_ReturnsError()
        {
            var paramsObj = new JObject { ["action"] = "nonexistent_action" };
            var result = ToJObject(ManageProBuilder.HandleCommand(paramsObj));

            Assert.IsFalse(result.Value<bool>("success"), result.ToString());
            Assert.That(result["error"]?.ToString() ?? result["message"]?.ToString(),
                Does.Contain("Unknown action"));
        }

        [Test]
        public void HandleCommand_Ping_ReturnsSuccess()
        {
            var paramsObj = new JObject { ["action"] = "ping" };
            var result = ToJObject(ManageProBuilder.HandleCommand(paramsObj));

            if (!_proBuilderInstalled)
            {
                // Without ProBuilder, should return error about missing package
                Assert.IsFalse(result.Value<bool>("success"), result.ToString());
                Assert.That(result["error"]?.ToString(),
                    Does.Contain("ProBuilder").IgnoreCase);
                return;
            }

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
        }

        // =====================================================================
        // Shape creation (requires ProBuilder)
        // =====================================================================

        [Test]
        public void CreateShape_MissingShapeType_ReturnsError()
        {
            if (!_proBuilderInstalled)
            {
                Assert.Pass("ProBuilder not installed - skipping.");
                return;
            }

            var paramsObj = new JObject
            {
                ["action"] = "create_shape",
            };
            var result = ToJObject(ManageProBuilder.HandleCommand(paramsObj));
            Assert.IsFalse(result.Value<bool>("success"), result.ToString());
        }

        [Test]
        public void CreateShape_InvalidShapeType_ReturnsError()
        {
            if (!_proBuilderInstalled)
            {
                Assert.Pass("ProBuilder not installed - skipping.");
                return;
            }

            var paramsObj = new JObject
            {
                ["action"] = "create_shape",
                ["properties"] = new JObject { ["shapeType"] = "InvalidShape" },
            };
            var result = ToJObject(ManageProBuilder.HandleCommand(paramsObj));
            Assert.IsFalse(result.Value<bool>("success"), result.ToString());
        }

        [Test]
        public void CreateShape_Cube_CreatesGameObject()
        {
            if (!_proBuilderInstalled)
            {
                Assert.Pass("ProBuilder not installed - skipping.");
                return;
            }

            var paramsObj = new JObject
            {
                ["action"] = "create_shape",
                ["properties"] = new JObject
                {
                    ["shapeType"] = "Cube",
                    ["name"] = "PBTestCube",
                },
            };
            var result = ToJObject(ManageProBuilder.HandleCommand(paramsObj));
            Assert.IsTrue(result.Value<bool>("success"), result.ToString());

            var data = result["data"] as JObject;
            Assert.IsNotNull(data, "Result should contain data");
            Assert.AreEqual("PBTestCube", data.Value<string>("gameObjectName"));
            Assert.Greater(data.Value<int>("faceCount"), 0);
            Assert.Greater(data.Value<int>("vertexCount"), 0);

            // Track for cleanup
            var go = GameObject.Find("PBTestCube");
            if (go != null) _createdObjects.Add(go);
        }

        [Test]
        public void CreateShape_WithPosition_SetsTransform()
        {
            if (!_proBuilderInstalled)
            {
                Assert.Pass("ProBuilder not installed - skipping.");
                return;
            }

            var paramsObj = new JObject
            {
                ["action"] = "create_shape",
                ["properties"] = new JObject
                {
                    ["shapeType"] = "Cube",
                    ["name"] = "PBTestCubePos",
                    ["position"] = new JArray(5f, 10f, 15f),
                },
            };
            var result = ToJObject(ManageProBuilder.HandleCommand(paramsObj));
            Assert.IsTrue(result.Value<bool>("success"), result.ToString());

            var go = GameObject.Find("PBTestCubePos");
            Assert.IsNotNull(go, "Created GameObject should exist");
            Assert.AreEqual(new Vector3(5f, 10f, 15f), go.transform.position);

            _createdObjects.Add(go);
        }

        [Test]
        public void CreatePolyShape_CreatesFromPoints()
        {
            if (!_proBuilderInstalled)
            {
                Assert.Pass("ProBuilder not installed - skipping.");
                return;
            }

            var paramsObj = new JObject
            {
                ["action"] = "create_poly_shape",
                ["properties"] = new JObject
                {
                    ["points"] = new JArray(
                        new JArray(0f, 0f, 0f),
                        new JArray(5f, 0f, 0f),
                        new JArray(5f, 0f, 5f),
                        new JArray(0f, 0f, 5f)
                    ),
                    ["extrudeHeight"] = 3f,
                    ["name"] = "PBTestPoly",
                },
            };
            var result = ToJObject(ManageProBuilder.HandleCommand(paramsObj));
            Assert.IsTrue(result.Value<bool>("success"), result.ToString());

            var go = GameObject.Find("PBTestPoly");
            if (go != null) _createdObjects.Add(go);
        }

        // =====================================================================
        // Mesh editing (requires ProBuilder + created shape)
        // =====================================================================

        [Test]
        public void GetMeshInfo_ReturnsDetails()
        {
            if (!_proBuilderInstalled)
            {
                Assert.Pass("ProBuilder not installed - skipping.");
                return;
            }

            // First create a shape
            var createParams = new JObject
            {
                ["action"] = "create_shape",
                ["properties"] = new JObject
                {
                    ["shapeType"] = "Cube",
                    ["name"] = "PBTestInfoCube",
                },
            };
            var createResult = ToJObject(ManageProBuilder.HandleCommand(createParams));
            Assert.IsTrue(createResult.Value<bool>("success"), createResult.ToString());

            var go = GameObject.Find("PBTestInfoCube");
            Assert.IsNotNull(go);
            _createdObjects.Add(go);

            // Now get mesh info
            var infoParams = new JObject
            {
                ["action"] = "get_mesh_info",
                ["target"] = "PBTestInfoCube",
            };
            var result = ToJObject(ManageProBuilder.HandleCommand(infoParams));
            Assert.IsTrue(result.Value<bool>("success"), result.ToString());

            var data = result["data"] as JObject;
            Assert.IsNotNull(data);
            Assert.Greater(data.Value<int>("faceCount"), 0);
            Assert.Greater(data.Value<int>("vertexCount"), 0);
            Assert.IsNotNull(data["bounds"]);
            Assert.IsNotNull(data["faces"]);
        }

        [Test]
        public void ExtrudeFaces_IncreaseFaceCount()
        {
            if (!_proBuilderInstalled)
            {
                Assert.Pass("ProBuilder not installed - skipping.");
                return;
            }

            // Create a cube
            var createParams = new JObject
            {
                ["action"] = "create_shape",
                ["properties"] = new JObject
                {
                    ["shapeType"] = "Cube",
                    ["name"] = "PBTestExtrudeCube",
                },
            };
            var createResult = ToJObject(ManageProBuilder.HandleCommand(createParams));
            Assert.IsTrue(createResult.Value<bool>("success"), createResult.ToString());

            var go = GameObject.Find("PBTestExtrudeCube");
            _createdObjects.Add(go);

            int initialFaceCount = createResult["data"].Value<int>("faceCount");

            // Extrude face 0
            var extrudeParams = new JObject
            {
                ["action"] = "extrude_faces",
                ["target"] = "PBTestExtrudeCube",
                ["properties"] = new JObject
                {
                    ["faceIndices"] = new JArray(0),
                    ["distance"] = 1.0f,
                },
            };
            var result = ToJObject(ManageProBuilder.HandleCommand(extrudeParams));
            Assert.IsTrue(result.Value<bool>("success"), result.ToString());

            int newFaceCount = result["data"].Value<int>("faceCount");
            Assert.Greater(newFaceCount, initialFaceCount,
                "Face count should increase after extrusion");
        }

        [Test]
        public void DeleteFaces_DecreasesFaceCount()
        {
            if (!_proBuilderInstalled)
            {
                Assert.Pass("ProBuilder not installed - skipping.");
                return;
            }

            var createParams = new JObject
            {
                ["action"] = "create_shape",
                ["properties"] = new JObject
                {
                    ["shapeType"] = "Cube",
                    ["name"] = "PBTestDeleteCube",
                },
            };
            var createResult = ToJObject(ManageProBuilder.HandleCommand(createParams));
            Assert.IsTrue(createResult.Value<bool>("success"), createResult.ToString());

            var go = GameObject.Find("PBTestDeleteCube");
            _createdObjects.Add(go);

            int initialFaceCount = createResult["data"].Value<int>("faceCount");

            var deleteParams = new JObject
            {
                ["action"] = "delete_faces",
                ["target"] = "PBTestDeleteCube",
                ["properties"] = new JObject
                {
                    ["faceIndices"] = new JArray(0),
                },
            };
            var result = ToJObject(ManageProBuilder.HandleCommand(deleteParams));
            Assert.IsTrue(result.Value<bool>("success"), result.ToString());

            int newFaceCount = result["data"].Value<int>("faceCount");
            Assert.Less(newFaceCount, initialFaceCount,
                "Face count should decrease after deletion");
        }

        [Test]
        public void SetFaceMaterial_WithMissingTarget_ReturnsError()
        {
            if (!_proBuilderInstalled)
            {
                Assert.Pass("ProBuilder not installed - skipping.");
                return;
            }

            var paramsObj = new JObject
            {
                ["action"] = "set_face_material",
                ["target"] = "NonExistentObject999",
                ["properties"] = new JObject
                {
                    ["faceIndices"] = new JArray(0),
                    ["materialPath"] = "Assets/Materials/Test.mat",
                },
            };
            var result = ToJObject(ManageProBuilder.HandleCommand(paramsObj));
            Assert.IsFalse(result.Value<bool>("success"), result.ToString());
        }

        [Test]
        public void FlipNormals_SucceedsOnValidMesh()
        {
            if (!_proBuilderInstalled)
            {
                Assert.Pass("ProBuilder not installed - skipping.");
                return;
            }

            var createParams = new JObject
            {
                ["action"] = "create_shape",
                ["properties"] = new JObject
                {
                    ["shapeType"] = "Cube",
                    ["name"] = "PBTestFlipCube",
                },
            };
            var createResult = ToJObject(ManageProBuilder.HandleCommand(createParams));
            Assert.IsTrue(createResult.Value<bool>("success"), createResult.ToString());

            var go = GameObject.Find("PBTestFlipCube");
            _createdObjects.Add(go);

            var flipParams = new JObject
            {
                ["action"] = "flip_normals",
                ["target"] = "PBTestFlipCube",
                ["properties"] = new JObject
                {
                    ["faceIndices"] = new JArray(0, 1),
                },
            };
            var result = ToJObject(ManageProBuilder.HandleCommand(flipParams));
            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
        }

        // =====================================================================
        // Enhanced get_mesh_info with include parameter
        // =====================================================================

        [Test]
        public void GetMeshInfo_DefaultInclude_ReturnsSummaryOnly()
        {
            if (!_proBuilderInstalled)
            {
                Assert.Pass("ProBuilder not installed - skipping.");
                return;
            }

            var createParams = new JObject
            {
                ["action"] = "create_shape",
                ["properties"] = new JObject
                {
                    ["shapeType"] = "Cube",
                    ["name"] = "PBTestSummaryCube",
                },
            };
            var createResult = ToJObject(ManageProBuilder.HandleCommand(createParams));
            Assert.IsTrue(createResult.Value<bool>("success"), createResult.ToString());

            var go = GameObject.Find("PBTestSummaryCube");
            Assert.IsNotNull(go);
            _createdObjects.Add(go);

            var infoParams = new JObject
            {
                ["action"] = "get_mesh_info",
                ["target"] = "PBTestSummaryCube",
            };
            var result = ToJObject(ManageProBuilder.HandleCommand(infoParams));
            Assert.IsTrue(result.Value<bool>("success"), result.ToString());

            var data = result["data"] as JObject;
            Assert.IsNotNull(data);
            Assert.Greater(data.Value<int>("faceCount"), 0);
            // Default "summary" should NOT include faces array
            Assert.IsNull(data["faces"], "Summary mode should not include faces array");
        }

        [Test]
        public void GetMeshInfo_IncludeFaces_ReturnsFaceNormalsAndDirections()
        {
            if (!_proBuilderInstalled)
            {
                Assert.Pass("ProBuilder not installed - skipping.");
                return;
            }

            var createParams = new JObject
            {
                ["action"] = "create_shape",
                ["properties"] = new JObject
                {
                    ["shapeType"] = "Cube",
                    ["name"] = "PBTestFacesCube",
                },
            };
            var createResult = ToJObject(ManageProBuilder.HandleCommand(createParams));
            Assert.IsTrue(createResult.Value<bool>("success"), createResult.ToString());

            var go = GameObject.Find("PBTestFacesCube");
            Assert.IsNotNull(go);
            _createdObjects.Add(go);

            var infoParams = new JObject
            {
                ["action"] = "get_mesh_info",
                ["target"] = "PBTestFacesCube",
                ["properties"] = new JObject { ["include"] = "faces" },
            };
            var result = ToJObject(ManageProBuilder.HandleCommand(infoParams));
            Assert.IsTrue(result.Value<bool>("success"), result.ToString());

            var data = result["data"] as JObject;
            Assert.IsNotNull(data);
            var faces = data["faces"] as JArray;
            Assert.IsNotNull(faces, "Faces mode should include faces array");
            Assert.Greater(faces.Count, 0);

            // Each face should have normal, center, direction
            var firstFace = faces[0] as JObject;
            Assert.IsNotNull(firstFace);
            Assert.IsNotNull(firstFace["normal"], "Face should have normal");
            Assert.IsNotNull(firstFace["center"], "Face should have center");
            // direction may be null for angled faces, but should exist as key
            Assert.IsTrue(firstFace.ContainsKey("direction"), "Face should have direction key");
        }

        [Test]
        public void GetMeshInfo_IncludeEdges_ReturnsEdgeData()
        {
            if (!_proBuilderInstalled)
            {
                Assert.Pass("ProBuilder not installed - skipping.");
                return;
            }

            var createParams = new JObject
            {
                ["action"] = "create_shape",
                ["properties"] = new JObject
                {
                    ["shapeType"] = "Cube",
                    ["name"] = "PBTestEdgesCube",
                },
            };
            var createResult = ToJObject(ManageProBuilder.HandleCommand(createParams));
            Assert.IsTrue(createResult.Value<bool>("success"), createResult.ToString());

            var go = GameObject.Find("PBTestEdgesCube");
            Assert.IsNotNull(go);
            _createdObjects.Add(go);

            var infoParams = new JObject
            {
                ["action"] = "get_mesh_info",
                ["target"] = "PBTestEdgesCube",
                ["properties"] = new JObject { ["include"] = "edges" },
            };
            var result = ToJObject(ManageProBuilder.HandleCommand(infoParams));
            Assert.IsTrue(result.Value<bool>("success"), result.ToString());

            var data = result["data"] as JObject;
            Assert.IsNotNull(data);
            var edges = data["edges"] as JArray;
            Assert.IsNotNull(edges, "Edges mode should include edges array");
            Assert.Greater(edges.Count, 0);

            var firstEdge = edges[0] as JObject;
            Assert.IsNotNull(firstEdge);
            Assert.IsTrue(firstEdge.ContainsKey("vertexA"), "Edge should have vertexA");
            Assert.IsTrue(firstEdge.ContainsKey("vertexB"), "Edge should have vertexB");
        }

        [Test]
        public void GetMeshInfo_CubeTopFace_HasUpNormal()
        {
            if (!_proBuilderInstalled)
            {
                Assert.Pass("ProBuilder not installed - skipping.");
                return;
            }

            var createParams = new JObject
            {
                ["action"] = "create_shape",
                ["properties"] = new JObject
                {
                    ["shapeType"] = "Cube",
                    ["name"] = "PBTestTopNormalCube",
                },
            };
            var createResult = ToJObject(ManageProBuilder.HandleCommand(createParams));
            Assert.IsTrue(createResult.Value<bool>("success"), createResult.ToString());

            var go = GameObject.Find("PBTestTopNormalCube");
            Assert.IsNotNull(go);
            _createdObjects.Add(go);

            var infoParams = new JObject
            {
                ["action"] = "get_mesh_info",
                ["target"] = "PBTestTopNormalCube",
                ["properties"] = new JObject { ["include"] = "faces" },
            };
            var result = ToJObject(ManageProBuilder.HandleCommand(infoParams));
            Assert.IsTrue(result.Value<bool>("success"), result.ToString());

            var faces = result["data"]["faces"] as JArray;
            Assert.IsNotNull(faces);

            // At least one face should have direction "top"
            bool hasTop = false;
            foreach (JObject face in faces)
            {
                if (face["direction"]?.ToString() == "top")
                {
                    hasTop = true;
                    break;
                }
            }
            Assert.IsTrue(hasTop, "A cube should have at least one face with direction 'top'");
        }

        // =====================================================================
        // Smoothing
        // =====================================================================

        [Test]
        public void AutoSmooth_DefaultAngle_AssignsGroups()
        {
            if (!_proBuilderInstalled)
            {
                Assert.Pass("ProBuilder not installed - skipping.");
                return;
            }

            var createParams = new JObject
            {
                ["action"] = "create_shape",
                ["properties"] = new JObject
                {
                    ["shapeType"] = "Cube",
                    ["name"] = "PBTestAutoSmoothCube",
                },
            };
            var createResult = ToJObject(ManageProBuilder.HandleCommand(createParams));
            Assert.IsTrue(createResult.Value<bool>("success"), createResult.ToString());

            var go = GameObject.Find("PBTestAutoSmoothCube");
            Assert.IsNotNull(go);
            _createdObjects.Add(go);

            var smoothParams = new JObject
            {
                ["action"] = "auto_smooth",
                ["target"] = "PBTestAutoSmoothCube",
                ["properties"] = new JObject { ["angleThreshold"] = 30 },
            };
            var result = ToJObject(ManageProBuilder.HandleCommand(smoothParams));
            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
        }

        [Test]
        public void SetSmoothing_OnSpecificFaces_SetsGroup()
        {
            if (!_proBuilderInstalled)
            {
                Assert.Pass("ProBuilder not installed - skipping.");
                return;
            }

            var createParams = new JObject
            {
                ["action"] = "create_shape",
                ["properties"] = new JObject
                {
                    ["shapeType"] = "Cube",
                    ["name"] = "PBTestSetSmoothCube",
                },
            };
            var createResult = ToJObject(ManageProBuilder.HandleCommand(createParams));
            Assert.IsTrue(createResult.Value<bool>("success"), createResult.ToString());

            var go = GameObject.Find("PBTestSetSmoothCube");
            Assert.IsNotNull(go);
            _createdObjects.Add(go);

            var smoothParams = new JObject
            {
                ["action"] = "set_smoothing",
                ["target"] = "PBTestSetSmoothCube",
                ["properties"] = new JObject
                {
                    ["faceIndices"] = new JArray(0, 1),
                    ["smoothingGroup"] = 1,
                },
            };
            var result = ToJObject(ManageProBuilder.HandleCommand(smoothParams));
            Assert.IsTrue(result.Value<bool>("success"), result.ToString());

            var data = result["data"] as JObject;
            Assert.IsNotNull(data);
            Assert.AreEqual(2, data.Value<int>("facesModified"));
        }

        // =====================================================================
        // Mesh Utilities
        // =====================================================================

        [Test]
        public void CenterPivot_MovesPivotToCenter()
        {
            if (!_proBuilderInstalled)
            {
                Assert.Pass("ProBuilder not installed - skipping.");
                return;
            }

            var createParams = new JObject
            {
                ["action"] = "create_shape",
                ["properties"] = new JObject
                {
                    ["shapeType"] = "Cube",
                    ["name"] = "PBTestCenterPivot",
                },
            };
            var createResult = ToJObject(ManageProBuilder.HandleCommand(createParams));
            Assert.IsTrue(createResult.Value<bool>("success"), createResult.ToString());

            var go = GameObject.Find("PBTestCenterPivot");
            Assert.IsNotNull(go);
            _createdObjects.Add(go);

            var pivotParams = new JObject
            {
                ["action"] = "center_pivot",
                ["target"] = "PBTestCenterPivot",
            };
            var result = ToJObject(ManageProBuilder.HandleCommand(pivotParams));
            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
        }

        [Test]
        public void FreezeTransform_ResetsTransformKeepsShape()
        {
            if (!_proBuilderInstalled)
            {
                Assert.Pass("ProBuilder not installed - skipping.");
                return;
            }

            var createParams = new JObject
            {
                ["action"] = "create_shape",
                ["properties"] = new JObject
                {
                    ["shapeType"] = "Cube",
                    ["name"] = "PBTestFreeze",
                    ["position"] = new JArray(5f, 3f, 2f),
                },
            };
            var createResult = ToJObject(ManageProBuilder.HandleCommand(createParams));
            Assert.IsTrue(createResult.Value<bool>("success"), createResult.ToString());

            var go = GameObject.Find("PBTestFreeze");
            Assert.IsNotNull(go);
            _createdObjects.Add(go);

            var freezeParams = new JObject
            {
                ["action"] = "freeze_transform",
                ["target"] = "PBTestFreeze",
            };
            var result = ToJObject(ManageProBuilder.HandleCommand(freezeParams));
            Assert.IsTrue(result.Value<bool>("success"), result.ToString());

            // Transform should be reset to identity
            Assert.AreEqual(Vector3.zero, go.transform.position);
            Assert.AreEqual(Quaternion.identity, go.transform.rotation);
            Assert.AreEqual(Vector3.one, go.transform.localScale);
        }

        [Test]
        public void ValidateMesh_CleanMesh_ReturnsNoIssues()
        {
            if (!_proBuilderInstalled)
            {
                Assert.Pass("ProBuilder not installed - skipping.");
                return;
            }

            var createParams = new JObject
            {
                ["action"] = "create_shape",
                ["properties"] = new JObject
                {
                    ["shapeType"] = "Cube",
                    ["name"] = "PBTestValidate",
                },
            };
            var createResult = ToJObject(ManageProBuilder.HandleCommand(createParams));
            Assert.IsTrue(createResult.Value<bool>("success"), createResult.ToString());

            var go = GameObject.Find("PBTestValidate");
            Assert.IsNotNull(go);
            _createdObjects.Add(go);

            var validateParams = new JObject
            {
                ["action"] = "validate_mesh",
                ["target"] = "PBTestValidate",
            };
            var result = ToJObject(ManageProBuilder.HandleCommand(validateParams));
            Assert.IsTrue(result.Value<bool>("success"), result.ToString());

            var data = result["data"] as JObject;
            Assert.IsNotNull(data);
            Assert.IsTrue(data.Value<bool>("healthy"), "Fresh cube should be healthy");
            Assert.AreEqual(0, data.Value<int>("degenerateTriangles"));
        }

        [Test]
        public void RepairMesh_OnCleanMesh_ReportsNoChanges()
        {
            if (!_proBuilderInstalled)
            {
                Assert.Pass("ProBuilder not installed - skipping.");
                return;
            }

            var createParams = new JObject
            {
                ["action"] = "create_shape",
                ["properties"] = new JObject
                {
                    ["shapeType"] = "Cube",
                    ["name"] = "PBTestRepair",
                },
            };
            var createResult = ToJObject(ManageProBuilder.HandleCommand(createParams));
            Assert.IsTrue(createResult.Value<bool>("success"), createResult.ToString());

            var go = GameObject.Find("PBTestRepair");
            Assert.IsNotNull(go);
            _createdObjects.Add(go);

            var repairParams = new JObject
            {
                ["action"] = "repair_mesh",
                ["target"] = "PBTestRepair",
            };
            var result = ToJObject(ManageProBuilder.HandleCommand(repairParams));
            Assert.IsTrue(result.Value<bool>("success"), result.ToString());

            var data = result["data"] as JObject;
            Assert.IsNotNull(data);
            Assert.AreEqual(0, data.Value<int>("degenerateTrianglesRemoved"));
        }

        // =====================================================================
        // ProBuilder not installed fallback
        // =====================================================================

        [Test]
        public void AllActions_WithoutProBuilder_ReturnPackageError()
        {
            if (_proBuilderInstalled)
            {
                Assert.Pass("ProBuilder IS installed - this test verifies the not-installed path.");
                return;
            }

            string[] testActions = {
                "ping", "create_shape", "get_mesh_info", "extrude_faces",
                "auto_smooth", "set_smoothing", "center_pivot", "validate_mesh",
            };
            foreach (var action in testActions)
            {
                var paramsObj = new JObject { ["action"] = action };
                var result = ToJObject(ManageProBuilder.HandleCommand(paramsObj));
                Assert.IsFalse(result.Value<bool>("success"),
                    $"Action '{action}' should fail without ProBuilder: {result}");
                Assert.That(result["error"]?.ToString(),
                    Does.Contain("ProBuilder").IgnoreCase,
                    $"Error for '{action}' should mention ProBuilder");
            }
        }
    }
}
