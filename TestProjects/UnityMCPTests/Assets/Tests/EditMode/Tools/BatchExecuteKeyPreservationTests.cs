using NUnit.Framework;
using UnityEngine;
using UnityEngine.Events;
using UnityEditor;
using Newtonsoft.Json.Linq;
using MCPForUnity.Editor.Tools;
using TestNamespace;

namespace MCPForUnityTests.Editor.Tools
{
    /// <summary>
    /// Verifies that batch_execute only normalizes top-level parameter keys (snake_case → camelCase)
    /// and preserves nested value keys (e.g. Unity serialized property paths like m_PersistentCalls).
    /// </summary>
    public class BatchExecuteKeyPreservationTests
    {
        private GameObject testGo;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            CommandRegistry.Initialize();
        }

        [SetUp]
        public void SetUp()
        {
            testGo = new GameObject("BatchKeyTestGO");
        }

        [TearDown]
        public void TearDown()
        {
            if (testGo != null)
                Object.DestroyImmediate(testGo);
        }

        [Test]
        public void NestedValueKeys_WithUnderscores_ArePreservedThroughBatch()
        {
            testGo.AddComponent<UnityEventTestComponent>();
            int targetId = testGo.GetInstanceID();

            var batchParams = new JObject
            {
                ["commands"] = new JArray
                {
                    new JObject
                    {
                        ["tool"] = "manage_components",
                        ["params"] = new JObject
                        {
                            ["action"] = "set_property",
                            ["target"] = testGo.name,
                            ["search_method"] = "by_name",
                            ["component_type"] = "UnityEventTestComponent",
                            ["property"] = "onSimpleEvent",
                            ["value"] = JObject.Parse(@"{
                                ""m_PersistentCalls"": {
                                    ""m_Calls"": [
                                        {
                                            ""m_Target"": { ""instanceID"": " + targetId + @" },
                                            ""m_TargetAssemblyTypeName"": ""UnityEngine.GameObject, UnityEngine"",
                                            ""m_MethodName"": ""SetActive"",
                                            ""m_Mode"": 6,
                                            ""m_Arguments"": { ""m_BoolArgument"": true },
                                            ""m_CallState"": 2
                                        }
                                    ]
                                }
                            }")
                        }
                    }
                }
            };

            var result = BatchExecute.HandleCommand(batchParams).GetAwaiter().GetResult();
            var resultObj = JObject.FromObject(result);

            Assert.IsTrue(resultObj.Value<bool>("success"), $"Batch should succeed: {resultObj}");

            // Verify the nested m_PersistentCalls keys were preserved (not mangled to mPersistentCalls)
            var comp = testGo.GetComponent<UnityEventTestComponent>();
            var so = new SerializedObject(comp);
            var callsProp = so.FindProperty("onSimpleEvent.m_PersistentCalls.m_Calls");
            Assert.IsNotNull(callsProp, "m_Calls property should exist");
            Assert.AreEqual(1, callsProp.arraySize, "Should have 1 persistent call");
            Assert.AreEqual("SetActive",
                callsProp.GetArrayElementAtIndex(0).FindPropertyRelative("m_MethodName").stringValue);
        }

        [Test]
        public void TopLevelParameterKeys_AreStillNormalized()
        {
            testGo.AddComponent<AudioSource>();

            // Use snake_case top-level keys: search_method, component_type
            var batchParams = new JObject
            {
                ["commands"] = new JArray
                {
                    new JObject
                    {
                        ["tool"] = "manage_components",
                        ["params"] = new JObject
                        {
                            ["action"] = "set_property",
                            ["target"] = testGo.name,
                            ["search_method"] = "by_name",
                            ["component_type"] = "AudioSource",
                            ["property"] = "volume",
                            ["value"] = 0.42f
                        }
                    }
                }
            };

            var result = BatchExecute.HandleCommand(batchParams).GetAwaiter().GetResult();
            var resultObj = JObject.FromObject(result);

            Assert.IsTrue(resultObj.Value<bool>("success"),
                $"Batch with snake_case top-level keys should succeed: {resultObj}");
            Assert.AreEqual(0.42f, testGo.GetComponent<AudioSource>().volume, 0.001f);
        }

        [Test]
        public void Regression_CreateGameObject_StillWorksViaBatch()
        {
            string goName = "BatchCreatedGO_" + System.Guid.NewGuid().ToString("N").Substring(0, 8);
            GameObject created = null;

            try
            {
                var batchParams = new JObject
                {
                    ["commands"] = new JArray
                    {
                        new JObject
                        {
                            ["tool"] = "manage_gameobject",
                            ["params"] = new JObject
                            {
                                ["action"] = "create",
                                ["name"] = goName,
                                ["primitive_type"] = "Cube"
                            }
                        }
                    }
                };

                var result = BatchExecute.HandleCommand(batchParams).GetAwaiter().GetResult();
                var resultObj = JObject.FromObject(result);

                Assert.IsTrue(resultObj.Value<bool>("success"), $"Batch create GO should succeed: {resultObj}");

                created = GameObject.Find(goName);
                Assert.IsNotNull(created, $"GameObject '{goName}' should exist in scene");
            }
            finally
            {
                if (created != null)
                    Object.DestroyImmediate(created);
            }
        }
    }
}
