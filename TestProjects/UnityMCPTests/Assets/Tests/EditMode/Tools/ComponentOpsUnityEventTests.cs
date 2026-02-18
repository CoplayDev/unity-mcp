using NUnit.Framework;
using UnityEngine;
using UnityEngine.Events;
using UnityEditor;
using Newtonsoft.Json.Linq;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Tools;
using TestNamespace;

namespace MCPForUnityTests.Editor.Tools
{
    public class ComponentOpsUnityEventTests
    {
        private GameObject testGo;

        [SetUp]
        public void SetUp()
        {
            testGo = new GameObject("UnityEventTestGO");
        }

        [TearDown]
        public void TearDown()
        {
            if (testGo != null)
                Object.DestroyImmediate(testGo);
        }

        [Test]
        public void SetProperty_UnityEvent_SinglePersistentCall_PersistsViaSerialization()
        {
            var comp = testGo.AddComponent<UnityEventTestComponent>();
            int targetId = testGo.GetInstanceID();

            var value = JObject.Parse(@"{
                ""m_PersistentCalls"": {
                    ""m_Calls"": [
                        {
                            ""m_Target"": { ""instanceID"": " + targetId + @" },
                            ""m_TargetAssemblyTypeName"": ""UnityEngine.GameObject, UnityEngine"",
                            ""m_MethodName"": ""SetActive"",
                            ""m_Mode"": 6,
                            ""m_Arguments"": {
                                ""m_BoolArgument"": true
                            },
                            ""m_CallState"": 2
                        }
                    ]
                }
            }");

            bool ok = ComponentOps.SetProperty(comp, "onSimpleEvent", value, out string error);

            Assert.IsTrue(ok, $"SetProperty should succeed, got error: {error}");

            // Verify via SerializedObject readback
            var so = new SerializedObject(comp);
            var callsProp = so.FindProperty("onSimpleEvent.m_PersistentCalls.m_Calls");
            Assert.IsNotNull(callsProp, "m_Calls property should exist");
            Assert.AreEqual(1, callsProp.arraySize, "Should have 1 persistent call");

            var call0 = callsProp.GetArrayElementAtIndex(0);
            Assert.AreEqual("SetActive", call0.FindPropertyRelative("m_MethodName").stringValue);
            Assert.AreEqual(testGo, call0.FindPropertyRelative("m_Target").objectReferenceValue);
            Assert.AreEqual(6, call0.FindPropertyRelative("m_Mode").enumValueIndex);
            Assert.AreEqual(2, call0.FindPropertyRelative("m_CallState").enumValueIndex);
        }

        [Test]
        public void SetProperty_UnityEvent_MultiplePersistentCalls_AllPersist()
        {
            var comp = testGo.AddComponent<UnityEventTestComponent>();
            int targetId = testGo.GetInstanceID();

            var value = JObject.Parse(@"{
                ""m_PersistentCalls"": {
                    ""m_Calls"": [
                        {
                            ""m_Target"": { ""instanceID"": " + targetId + @" },
                            ""m_TargetAssemblyTypeName"": ""UnityEngine.GameObject, UnityEngine"",
                            ""m_MethodName"": ""SetActive"",
                            ""m_Mode"": 6,
                            ""m_Arguments"": { ""m_BoolArgument"": true },
                            ""m_CallState"": 2
                        },
                        {
                            ""m_Target"": { ""instanceID"": " + targetId + @" },
                            ""m_TargetAssemblyTypeName"": ""UnityEngine.GameObject, UnityEngine"",
                            ""m_MethodName"": ""SetActive"",
                            ""m_Mode"": 6,
                            ""m_Arguments"": { ""m_BoolArgument"": false },
                            ""m_CallState"": 2
                        }
                    ]
                }
            }");

            bool ok = ComponentOps.SetProperty(comp, "onSimpleEvent", value, out string error);

            Assert.IsTrue(ok, $"SetProperty should succeed, got error: {error}");

            var so = new SerializedObject(comp);
            var callsProp = so.FindProperty("onSimpleEvent.m_PersistentCalls.m_Calls");
            Assert.AreEqual(2, callsProp.arraySize, "Should have 2 persistent calls");

            Assert.AreEqual("SetActive", callsProp.GetArrayElementAtIndex(0).FindPropertyRelative("m_MethodName").stringValue);
            Assert.AreEqual("SetActive", callsProp.GetArrayElementAtIndex(1).FindPropertyRelative("m_MethodName").stringValue);
        }

        [Test]
        public void SetProperty_UnityEvent_EmptyCalls_ClearsEvent()
        {
            var comp = testGo.AddComponent<UnityEventTestComponent>();

            // First set a call
            int targetId = testGo.GetInstanceID();
            var withCall = JObject.Parse(@"{
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
            }");
            ComponentOps.SetProperty(comp, "onSimpleEvent", withCall, out _);

            // Now clear it
            var empty = JObject.Parse(@"{
                ""m_PersistentCalls"": {
                    ""m_Calls"": []
                }
            }");

            bool ok = ComponentOps.SetProperty(comp, "onSimpleEvent", empty, out string error);

            Assert.IsTrue(ok, $"SetProperty should succeed, got error: {error}");

            var so = new SerializedObject(comp);
            var callsProp = so.FindProperty("onSimpleEvent.m_PersistentCalls.m_Calls");
            Assert.AreEqual(0, callsProp.arraySize, "Should have 0 persistent calls after clearing");
        }

        [Test]
        public void SetProperty_PrivateSerializedUnityEvent_RoutesViaSerialization()
        {
            var comp = testGo.AddComponent<UnityEventTestComponent>();
            int targetId = testGo.GetInstanceID();

            var value = JObject.Parse(@"{
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
            }");

            bool ok = ComponentOps.SetProperty(comp, "_onPrivateEvent", value, out string error);

            Assert.IsTrue(ok, $"SetProperty on private [SerializeField] UnityEvent should succeed, got error: {error}");

            var so = new SerializedObject(comp);
            var callsProp = so.FindProperty("_onPrivateEvent.m_PersistentCalls.m_Calls");
            Assert.IsNotNull(callsProp, "Private event m_Calls should exist");
            Assert.AreEqual(1, callsProp.arraySize, "Should have 1 persistent call");
        }

        [Test]
        public void SetProperty_SimpleFloat_StillWorksViaReflection()
        {
            var audioSource = testGo.AddComponent<AudioSource>();

            bool ok = ComponentOps.SetProperty(audioSource, "volume", new JValue(0.5f), out string error);

            Assert.IsTrue(ok, $"SetProperty for float should succeed, got error: {error}");
            Assert.AreEqual(0.5f, audioSource.volume, 0.001f);
        }

        [Test]
        public void HandleCommand_EndToEnd_UnityEventWiring()
        {
            testGo.AddComponent<UnityEventTestComponent>();
            int targetId = testGo.GetInstanceID();

            var p = new JObject
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
            };

            var result = ManageComponents.HandleCommand(p);
            var resultObj = result as JObject ?? JObject.FromObject(result);

            Assert.IsTrue(resultObj.Value<bool>("success"), $"HandleCommand should succeed: {resultObj}");

            // Verify via SerializedObject
            var comp = testGo.GetComponent<UnityEventTestComponent>();
            var so = new SerializedObject(comp);
            var callsProp = so.FindProperty("onSimpleEvent.m_PersistentCalls.m_Calls");
            Assert.AreEqual(1, callsProp.arraySize, "Should have 1 persistent call after end-to-end");
            Assert.AreEqual("SetActive", callsProp.GetArrayElementAtIndex(0).FindPropertyRelative("m_MethodName").stringValue);
        }
    }
}
