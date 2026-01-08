using System;
using System.Collections.Generic;
using System.Diagnostics;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Resources.Scene;
using MCPForUnity.Editor.Tools;
using MCPForUnity.Editor.Tools.GameObjects;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using static MCPForUnityTests.Editor.TestUtilities;
using Debug = UnityEngine.Debug;

namespace MCPForUnityTests.Editor.Tools
{
    /// <summary>
    /// Stress tests for the GameObject API redesign.
    /// Tests volume operations, pagination, and performance with large datasets.
    /// </summary>
    [TestFixture]
    public class GameObjectAPIStressTests
    {
        private List<GameObject> _createdObjects = new List<GameObject>();
        private const int SMALL_BATCH = 10;
        private const int MEDIUM_BATCH = 50;
        private const int LARGE_BATCH = 100;

        [SetUp]
        public void SetUp()
        {
            _createdObjects.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _createdObjects)
            {
                if (go != null)
                {
                    UnityEngine.Object.DestroyImmediate(go);
                }
            }
            _createdObjects.Clear();
        }

        private GameObject CreateTestObject(string name)
        {
            var go = new GameObject(name);
            _createdObjects.Add(go);
            return go;
        }

        #region Bulk GameObject Creation

        [Test]
        public void BulkCreate_SmallBatch_AllSucceed()
        {
            var sw = Stopwatch.StartNew();

            for (int i = 0; i < SMALL_BATCH; i++)
            {
                var result = ToJObject(ManageGameObject.HandleCommand(new JObject
                {
                    ["action"] = "create",
                    ["name"] = $"BulkTest_{i}"
                }));

                Assert.IsTrue(result["success"]?.Value<bool>() ?? false, $"Failed to create object {i}");

                // Track for cleanup
                int instanceId = result["data"]?["instanceID"]?.Value<int>() ?? 0;
                if (instanceId != 0)
                {
                    var go = EditorUtility.InstanceIDToObject(instanceId) as GameObject;
                    if (go != null) _createdObjects.Add(go);
                }
            }

            sw.Stop();
            Debug.Log($"[BulkCreate] Created {SMALL_BATCH} objects in {sw.ElapsedMilliseconds}ms");
            // Use generous threshold for CI variability
            Assert.Less(sw.ElapsedMilliseconds, 10000, "Bulk create took too long (CI threshold)");
        }

        [Test]
        public void BulkCreate_MediumBatch_AllSucceed()
        {
            var sw = Stopwatch.StartNew();

            for (int i = 0; i < MEDIUM_BATCH; i++)
            {
                var result = ToJObject(ManageGameObject.HandleCommand(new JObject
                {
                    ["action"] = "create",
                    ["name"] = $"MediumBulk_{i}"
                }));

                Assert.IsTrue(result["success"]?.Value<bool>() ?? false, $"Failed to create object {i}");

                int instanceId = result["data"]?["instanceID"]?.Value<int>() ?? 0;
                if (instanceId != 0)
                {
                    var go = EditorUtility.InstanceIDToObject(instanceId) as GameObject;
                    if (go != null) _createdObjects.Add(go);
                }
            }

            sw.Stop();
            Debug.Log($"[BulkCreate] Created {MEDIUM_BATCH} objects in {sw.ElapsedMilliseconds}ms");
            Assert.Less(sw.ElapsedMilliseconds, 15000, "Medium batch create took too long");
        }

        #endregion

        #region Find GameObjects Pagination

        [Test]
        public void FindGameObjects_LargeBatch_PaginatesCorrectly()
        {
            // Create many objects with a unique marker component for reliable search
            for (int i = 0; i < LARGE_BATCH; i++)
            {
                var go = CreateTestObject($"Searchable_{i:D3}");
                go.AddComponent<GameObjectAPIStressTestMarker>();
            }

            // Find by searching for a specific object first
            var firstResult = ToJObject(FindGameObjects.HandleCommand(new JObject
            {
                ["searchTerm"] = "Searchable_000",
                ["searchMethod"] = "by_name",
                ["pageSize"] = 10
            }));

            Assert.IsTrue(firstResult["success"]?.Value<bool>() ?? false, "Should find specific named object");
            var firstData = firstResult["data"] as JObject;
            var firstIds = firstData?["instanceIDs"] as JArray;
            Assert.IsNotNull(firstIds);
            Assert.AreEqual(1, firstIds.Count, "Should find exactly one object with exact name match");

            Debug.Log($"[FindGameObjects] Found object by exact name. Testing pagination with a unique marker component.");

            // Now test pagination by searching for only the objects created by this test
            var result = ToJObject(FindGameObjects.HandleCommand(new JObject
            {
                ["searchTerm"] = typeof(GameObjectAPIStressTestMarker).FullName,
                ["searchMethod"] = "by_component",
                ["pageSize"] = 25
            }));

            Assert.IsTrue(result["success"]?.Value<bool>() ?? false);
            var data = result["data"] as JObject;
            Assert.IsNotNull(data);

            var instanceIds = data["instanceIDs"] as JArray;
            Assert.IsNotNull(instanceIds);
            Assert.AreEqual(25, instanceIds.Count, "First page should have 25 items");

            int totalCount = data["totalCount"]?.Value<int>() ?? 0;
            Assert.AreEqual(LARGE_BATCH, totalCount, $"Should find exactly {LARGE_BATCH} objects created by this test");

            bool hasMore = data["hasMore"]?.Value<bool>() ?? false;
            Assert.IsTrue(hasMore, "Should have more pages");

            Debug.Log($"[FindGameObjects] Found {totalCount} objects, first page has {instanceIds.Count}");
        }

        [Test]
        public void FindGameObjects_PaginateThroughAll()
        {
            // Create objects - all will have a unique marker component
            for (int i = 0; i < MEDIUM_BATCH; i++)
            {
                var go = CreateTestObject($"Paginate_{i:D3}");
                go.AddComponent<GameObjectAPIStressTestMarker>();
            }

            // Track IDs we've created for verification
            var createdIds = new HashSet<int>();
            foreach (var go in _createdObjects)
            {
                if (go != null && go.name.StartsWith("Paginate_"))
                {
                    createdIds.Add(go.GetInstanceID());
                }
            }

            int pageSize = 10;
            int cursor = 0;
            int foundFromCreated = 0;
            int pageCount = 0;

            // Search by the unique marker component and check our created objects
            while (true)
            {
                var result = ToJObject(FindGameObjects.HandleCommand(new JObject
                {
                    ["searchTerm"] = typeof(GameObjectAPIStressTestMarker).FullName,
                    ["searchMethod"] = "by_component",
                    ["pageSize"] = pageSize,
                    ["cursor"] = cursor
                }));

                Assert.IsTrue(result["success"]?.Value<bool>() ?? false);
                var data = result["data"] as JObject;
                var instanceIds = data["instanceIDs"] as JArray;

                // Count how many of our created objects are in this page
                foreach (var id in instanceIds)
                {
                    if (createdIds.Contains(id.Value<int>()))
                    {
                        foundFromCreated++;
                    }
                }
                pageCount++;

                bool hasMore = data["hasMore"]?.Value<bool>() ?? false;
                if (!hasMore) break;

                cursor = data["nextCursor"]?.Value<int>() ?? cursor + pageSize;

                // Safety limit
                if (pageCount > 50) break;
            }

            Assert.AreEqual(MEDIUM_BATCH, foundFromCreated, $"Should find all {MEDIUM_BATCH} created objects across pages");
            Debug.Log($"[Pagination] Found {foundFromCreated} created objects across {pageCount} pages");
        }

        #endregion

        #region Component Operations at Scale

        [Test]
        public void AddComponents_MultipleToSingleObject()
        {
            var go = CreateTestObject("ComponentHost");

            string[] componentTypeNames = new[]
            {
                "BoxCollider",
                "Rigidbody",
                "Light",
                "Camera"
            };

            var sw = Stopwatch.StartNew();

            foreach (var compType in componentTypeNames)
            {
                var result = ToJObject(ManageComponents.HandleCommand(new JObject
                {
                    ["action"] = "add",
                    ["target"] = go.GetInstanceID().ToString(),
                    ["searchMethod"] = "by_id",
                    ["componentType"] = compType  // Correct parameter name
                }));

                Assert.IsTrue(result["success"]?.Value<bool>() ?? false, $"Failed to add {compType}: {result["message"]}");
            }

            sw.Stop();
            Debug.Log($"[AddComponents] Added {componentTypeNames.Length} components in {sw.ElapsedMilliseconds}ms");

            // Verify all components present
            Assert.AreEqual(componentTypeNames.Length + 1, go.GetComponents<Component>().Length); // +1 for Transform
        }

        [Test]
        public void GetComponents_ObjectWithManyComponents()
        {
            var go = CreateTestObject("HeavyComponents");

            // Add many components - but skip AudioSource as it triggers deprecated API warnings
            go.AddComponent<BoxCollider>();
            go.AddComponent<SphereCollider>();
            go.AddComponent<CapsuleCollider>();
            go.AddComponent<MeshCollider>();
            go.AddComponent<Rigidbody>();
            go.AddComponent<Light>();
            go.AddComponent<Camera>();
            go.AddComponent<AudioListener>();

            var sw = Stopwatch.StartNew();

            // Use the resource handler for getting components
            var result = ToJObject(GameObjectComponentsResource.HandleCommand(new JObject
            {
                ["instanceID"] = go.GetInstanceID(),
                ["includeProperties"] = true,
                ["pageSize"] = 50
            }));

            sw.Stop();

            Assert.IsTrue(result["success"]?.Value<bool>() ?? false, $"GetComponents failed: {result["message"]}");
            var data = result["data"] as JObject;
            var components = data?["components"] as JArray;

            Assert.IsNotNull(components);
            Assert.AreEqual(9, components.Count); // 8 added + Transform

            Debug.Log($"[GetComponents] Retrieved {components.Count} components with properties in {sw.ElapsedMilliseconds}ms");
        }

        [Test]
        public void SetComponentProperties_ComplexRigidbody()
        {
            var go = CreateTestObject("RigidbodyTest");
            go.AddComponent<Rigidbody>();

            var result = ToJObject(ManageComponents.HandleCommand(new JObject
            {
                ["action"] = "set_property",
                ["target"] = go.GetInstanceID().ToString(),
                ["searchMethod"] = "by_id",
                ["componentType"] = "Rigidbody",  // Correct parameter name
                ["properties"] = new JObject       // Correct parameter name
                {
                    ["mass"] = 10.5f,
                    ["drag"] = 0.5f,
                    ["angularDrag"] = 0.1f,
                    ["useGravity"] = false,
                    ["isKinematic"] = true
                }
            }));

            Assert.IsTrue(result["success"]?.Value<bool>() ?? false, $"Set property failed: {result["message"]}");

            var rb = go.GetComponent<Rigidbody>();
            Assert.AreEqual(10.5f, rb.mass, 0.01f);
            Assert.AreEqual(0.5f, rb.drag, 0.01f);
            Assert.AreEqual(0.1f, rb.angularDrag, 0.01f);
            Assert.IsFalse(rb.useGravity);
            Assert.IsTrue(rb.isKinematic);
        }

        #endregion

        #region Deep Hierarchy Operations

        [Test]
        public void CreateDeepHierarchy_FindByPath()
        {
            // Create a deep hierarchy: Root/Level1/Level2/Level3/Target
            var root = CreateTestObject("DeepRoot");
            var current = root;

            for (int i = 1; i <= 5; i++)
            {
                var child = CreateTestObject($"Level{i}");
                child.transform.SetParent(current.transform);
                current = child;
            }

            var target = CreateTestObject("DeepTarget");
            target.transform.SetParent(current.transform);

            // Find by path
            var result = ToJObject(FindGameObjects.HandleCommand(new JObject
            {
                ["searchTerm"] = "DeepRoot/Level1/Level2/Level3/Level4/Level5/DeepTarget",
                ["searchMethod"] = "by_path"
            }));

            Assert.IsTrue(result["success"]?.Value<bool>() ?? false);
            var data = result["data"] as JObject;
            var ids = data?["instanceIDs"] as JArray;

            Assert.IsNotNull(ids);
            Assert.AreEqual(1, ids.Count);
            Assert.AreEqual(target.GetInstanceID(), ids[0].Value<int>());
        }

        [Test]
        public void GetHierarchy_LargeScene_Paginated()
        {
            // Create flat hierarchy with many objects
            for (int i = 0; i < MEDIUM_BATCH; i++)
            {
                CreateTestObject($"HierarchyItem_{i:D3}");
            }

            var result = ToJObject(ManageScene.HandleCommand(new JObject
            {
                ["action"] = "get_hierarchy",
                ["pageSize"] = 20,
                ["maxNodes"] = 100
            }));

            Assert.IsTrue(result["success"]?.Value<bool>() ?? false);
            var data = result["data"] as JObject;
            var items = data?["items"] as JArray;

            Assert.IsNotNull(items);
            Assert.GreaterOrEqual(items.Count, 1);

            // Verify componentTypes is included
            var firstItem = items[0] as JObject;
            Assert.IsNotNull(firstItem?["componentTypes"], "Should include componentTypes");

            Debug.Log($"[GetHierarchy] Retrieved {items.Count} items from hierarchy");
        }

        #endregion

        #region Resource Read Performance

        [Test]
        public void GameObjectResource_ReadComplexObject()
        {
            var go = CreateTestObject("ComplexObject");
            go.tag = "Player";
            go.layer = 8;
            go.isStatic = true;

            // Add components - AudioSource is OK here since we're only reading component types, not serializing properties
            go.AddComponent<Rigidbody>();
            go.AddComponent<BoxCollider>();
            go.AddComponent<AudioSource>();

            // Add children
            for (int i = 0; i < 5; i++)
            {
                var child = CreateTestObject($"Child_{i}");
                child.transform.SetParent(go.transform);
            }

            var sw = Stopwatch.StartNew();

            // Call the resource directly (no action param needed)
            var result = ToJObject(GameObjectResource.HandleCommand(new JObject
            {
                ["instanceID"] = go.GetInstanceID()
            }));

            sw.Stop();

            Assert.IsTrue(result["success"]?.Value<bool>() ?? false);
            var data = result["data"] as JObject;

            Assert.AreEqual("ComplexObject", data?["name"]?.Value<string>());
            Assert.AreEqual("Player", data?["tag"]?.Value<string>());
            Assert.AreEqual(8, data?["layer"]?.Value<int>());

            var componentTypes = data?["componentTypes"] as JArray;
            Assert.IsNotNull(componentTypes);
            Assert.AreEqual(4, componentTypes.Count); // Transform + 3 added

            var children = data?["children"] as JArray;
            Assert.IsNotNull(children);
            Assert.AreEqual(5, children.Count);

            Debug.Log($"[GameObjectResource] Read complex object in {sw.ElapsedMilliseconds}ms");
        }

        [Test]
        public void ComponentsResource_ReadAllWithFullSerialization()
        {
            var go = CreateTestObject("FullSerialize");

            var rb = go.AddComponent<Rigidbody>();
            rb.mass = 5.5f;
            rb.drag = 1.2f;

            var col = go.AddComponent<BoxCollider>();
            col.size = new Vector3(2, 3, 4);
            col.center = new Vector3(0.5f, 0.5f, 0.5f);

            // Skip AudioSource to avoid deprecated API warnings

            var sw = Stopwatch.StartNew();

            // Use the components resource handler
            var result = ToJObject(GameObjectComponentsResource.HandleCommand(new JObject
            {
                ["instanceID"] = go.GetInstanceID(),
                ["includeProperties"] = true
            }));

            sw.Stop();

            Assert.IsTrue(result["success"]?.Value<bool>() ?? false);
            var data = result["data"] as JObject;
            var components = data?["components"] as JArray;

            Assert.IsNotNull(components);
            Assert.AreEqual(3, components.Count); // Transform + Rigidbody + BoxCollider

            Debug.Log($"[ComponentsResource] Full serialization of {components.Count} components in {sw.ElapsedMilliseconds}ms");

            // Verify serialized data includes properties
            bool foundRigidbody = false;
            foreach (JObject comp in components)
            {
                var typeName = comp["typeName"]?.Value<string>();
                if (typeName != null && typeName.Contains("Rigidbody"))
                {
                    foundRigidbody = true;
                    // GameObjectSerializer puts properties inside a "properties" nested object
                    var props = comp["properties"] as JObject;
                    Assert.IsNotNull(props, $"Rigidbody should have properties. Component data: {comp}");
                    float massValue = props["mass"]?.Value<float>() ?? 0;
                    Assert.AreEqual(5.5f, massValue, 0.01f, $"Mass should be 5.5");
                }
            }
            Assert.IsTrue(foundRigidbody, "Should find Rigidbody with serialized properties");
        }

        #endregion

        #region Concurrent-Like Operations

        [Test]
        public void RapidFireOperations_CreateModifyDelete()
        {
            var sw = Stopwatch.StartNew();

            for (int i = 0; i < SMALL_BATCH; i++)
            {
                // Create
                var createResult = ToJObject(ManageGameObject.HandleCommand(new JObject
                {
                    ["action"] = "create",
                    ["name"] = $"RapidFire_{i}"
                }));
                Assert.IsTrue(createResult["success"]?.Value<bool>() ?? false, $"Create failed: {createResult["message"]}");

                int instanceId = createResult["data"]?["instanceID"]?.Value<int>() ?? 0;
                Assert.AreNotEqual(0, instanceId, "Instance ID should not be 0");

                // Modify - use layer 0 (Default) to avoid layer name issues
                var modifyResult = ToJObject(ManageGameObject.HandleCommand(new JObject
                {
                    ["action"] = "modify",
                    ["target"] = instanceId.ToString(),
                    ["searchMethod"] = "by_id",
                    ["name"] = $"RapidFire_Modified_{i}",  // Use name modification instead
                    ["setActive"] = true
                }));
                Assert.IsTrue(modifyResult["success"]?.Value<bool>() ?? false, $"Modify failed: {modifyResult["message"]}");

                // Delete
                var deleteResult = ToJObject(ManageGameObject.HandleCommand(new JObject
                {
                    ["action"] = "delete",
                    ["target"] = instanceId.ToString(),
                    ["searchMethod"] = "by_id"
                }));
                Assert.IsTrue(deleteResult["success"]?.Value<bool>() ?? false, $"Delete failed: {deleteResult["message"]}");
            }

            sw.Stop();
            Debug.Log($"[RapidFire] {SMALL_BATCH} create-modify-delete cycles in {sw.ElapsedMilliseconds}ms");
            Assert.Less(sw.ElapsedMilliseconds, 10000, "Rapid fire operations took too long");
        }

        #endregion
    }

    /// <summary>
    /// Marker component used for isolating component-based searches to objects created by this test fixture.
    /// </summary>
    public sealed class GameObjectAPIStressTestMarker : MonoBehaviour { }
}

