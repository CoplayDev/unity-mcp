using System.Collections.Generic;
using MCPForUnity.Editor.Helpers;
using NUnit.Framework;
using UnityEngine;

namespace Fusion
{
    public struct NetworkBehaviourBuffer
    {
        public int Value;
    }

    public struct Changed<T>
    {
        public T Value;
    }
}

namespace MCPForUnityTests.Editor.Tools
{
    public class FusionUnsafeTypeSerializationTests
    {
        [Test]
        public void GetComponentData_SkipsFusionUnsafeTypesInsideContainers()
        {
            var testObject = new GameObject("FusionUnsafeTypeTestObject");

            try
            {
                var component = testObject.AddComponent<FusionUnsafeTypeComponent>();
                component.bufferList.Add(new Fusion.NetworkBehaviourBuffer { Value = 1 });
                component.nestedChangedLookup["changed"] = new List<Fusion.Changed<int>>
                {
                    new Fusion.Changed<int> { Value = 2 }
                };
                component.ChangedListProperty.Add(new Fusion.Changed<int> { Value = 3 });

                var result = GameObjectSerializer.GetComponentData(component) as Dictionary<string, object>;

                Assert.IsNotNull(result, "GetComponentData should return dictionary data.");
                Assert.IsTrue(result.TryGetValue("properties", out object propertiesObject), "Serialized data should contain properties.");

                var properties = propertiesObject as Dictionary<string, object>;
                Assert.IsNotNull(properties, "Serialized properties should be a dictionary.");

                Assert.IsTrue(properties.ContainsKey(nameof(FusionUnsafeTypeComponent.safeValue)), "Safe fields should still serialize.");
                Assert.IsFalse(properties.ContainsKey(nameof(FusionUnsafeTypeComponent.directBuffer)), "Direct Fusion buffer fields should be skipped.");
                Assert.IsFalse(properties.ContainsKey(nameof(FusionUnsafeTypeComponent.bufferList)), "Collections containing Fusion buffer types should be skipped.");
                Assert.IsFalse(properties.ContainsKey(nameof(FusionUnsafeTypeComponent.nestedChangedLookup)), "Nested generic containers containing Fusion Changed<T> should be skipped.");
                Assert.IsFalse(properties.ContainsKey(nameof(FusionUnsafeTypeComponent.ChangedListProperty)), "Properties returning collections of Fusion Changed<T> should be skipped.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(testObject);
            }
        }
    }

    public sealed class FusionUnsafeTypeComponent : MonoBehaviour
    {
        public string safeValue = "kept";
        public Fusion.NetworkBehaviourBuffer directBuffer;
        public List<Fusion.NetworkBehaviourBuffer> bufferList = new List<Fusion.NetworkBehaviourBuffer>();
        public Dictionary<string, List<Fusion.Changed<int>>> nestedChangedLookup = new Dictionary<string, List<Fusion.Changed<int>>>();

        public List<Fusion.Changed<int>> ChangedListProperty { get; } = new List<Fusion.Changed<int>>();
    }
}
