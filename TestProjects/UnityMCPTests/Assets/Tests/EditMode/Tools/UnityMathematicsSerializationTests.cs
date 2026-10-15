using System.Collections.Generic;
using MCPForUnity.Editor.Helpers;
using NUnit.Framework;
using UnityEngine;

// Stand-ins for com.unity.mathematics, which the test project does not reference. The real structs
// have the same shape: public fields plus public swizzle properties that return new structs, which
// is what made the default Newtonsoft walk never finish (issue #1415). The converter matches on the
// namespace, so these behave exactly like the package types.
namespace Unity.Mathematics
{
    public struct float3
    {
        public float x;
        public float y;
        public float z;

        public float3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }

        public float3 xyz => new float3(x, y, z);
        public float3 yzx => new float3(y, z, x);
        public float3 zxy => new float3(z, x, y);
        public float3 xxx => new float3(x, x, x);
        public float2 xy => new float2(x, y);
    }

    public struct float2
    {
        public float x;
        public float y;

        public float2(float x, float y) { this.x = x; this.y = y; }

        public float2 yx => new float2(y, x);
        public float3 xyx => new float3(x, y, x);
    }

    public struct float4
    {
        public float x;
        public float y;
        public float z;
        public float w;

        public float4(float x, float y, float z, float w) { this.x = x; this.y = y; this.z = z; this.w = w; }

        public float4 wzyx => new float4(w, z, y, x);
        public float3 xyz => new float3(x, y, z);
    }

    public struct quaternion
    {
        public float4 value;

        public quaternion(float x, float y, float z, float w) { value = new float4(x, y, z, w); }
    }
}

namespace MCPForUnityTests.Editor.Tools
{
    public class UnityMathematicsSerializationTests
    {
        [Test]
        public void GetComponentData_WritesMathematicsStructsAsTheirFieldsOnly()
        {
            var testObject = new GameObject("UnityMathematicsTestObject");

            try
            {
                var component = testObject.AddComponent<UnityMathematicsComponent>();
                component.position = new Unity.Mathematics.float3(1f, 2f, 3f);
                component.rotation = new Unity.Mathematics.quaternion(0f, 0f, 0f, 1f);
                component.knots.Add(new Unity.Mathematics.float3(4f, 5f, 6f));

                var result = GameObjectSerializer.GetComponentData(component) as Dictionary<string, object>;

                Assert.IsNotNull(result, "GetComponentData should return dictionary data.");
                Assert.IsTrue(result.TryGetValue("properties", out object propertiesObject), "Serialized data should contain properties.");
                var properties = propertiesObject as Dictionary<string, object>;
                Assert.IsNotNull(properties, "Serialized properties should be a dictionary.");

                var position = properties[nameof(UnityMathematicsComponent.position)] as Dictionary<string, object>;
                Assert.IsNotNull(position, "float3 should serialize as an object.");
                CollectionAssert.AreEquivalent(new[] { "x", "y", "z" }, position.Keys, "float3 should expose its fields and none of its swizzle properties.");
                Assert.AreEqual(1.0, position["x"]);
                Assert.AreEqual(2.0, position["y"]);
                Assert.AreEqual(3.0, position["z"]);

                var rotation = properties[nameof(UnityMathematicsComponent.rotation)] as Dictionary<string, object>;
                Assert.IsNotNull(rotation, "quaternion should serialize as an object.");
                CollectionAssert.AreEquivalent(new[] { "value" }, rotation.Keys);
                var rotationValue = rotation["value"] as Dictionary<string, object>;
                Assert.IsNotNull(rotationValue, "quaternion.value (a float4) should go through the same converter.");
                CollectionAssert.AreEquivalent(new[] { "x", "y", "z", "w" }, rotationValue.Keys);
                Assert.AreEqual(1.0, rotationValue["w"]);

                var knots = properties[nameof(UnityMathematicsComponent.knots)] as List<object>;
                Assert.IsNotNull(knots, "Lists of math structs should still serialize as lists.");
                Assert.AreEqual(1, knots.Count);
                var knot = knots[0] as Dictionary<string, object>;
                Assert.IsNotNull(knot);
                CollectionAssert.AreEquivalent(new[] { "x", "y", "z" }, knot.Keys);
                Assert.AreEqual(6.0, knot["z"]);

                Assert.AreEqual("kept", properties[nameof(UnityMathematicsComponent.label)], "Unrelated fields are unaffected.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(testObject);
            }
        }
    }

    public sealed class UnityMathematicsComponent : MonoBehaviour
    {
        public string label = "kept";
        public Unity.Mathematics.float3 position;
        public Unity.Mathematics.quaternion rotation;
        public List<Unity.Mathematics.float3> knots = new List<Unity.Mathematics.float3>();
    }
}
