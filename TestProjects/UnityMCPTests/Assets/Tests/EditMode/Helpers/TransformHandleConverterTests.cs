using System;
using System.Linq;
using System.Reflection;
using MCPForUnity.Runtime.Serialization;
using Newtonsoft.Json;
using NUnit.Framework;
using UnityEngine;

namespace MCPForUnityTests.Editor.Helpers
{
    /// <summary>
    /// Tests for TransformHandleConverter, which writes UnityEngine.TransformHandle
    /// (Unity 6+) as null instead of letting reflection-based serialization enumerate
    /// the handle's children - that enumeration throws NullReferenceException once the
    /// underlying object is destroyed (a live race when component data is read during
    /// play mode while scripts Destroy() objects).
    /// The type is resolved by reflection so this file compiles on every Unity version
    /// the package supports; handle-specific tests self-ignore where the type is absent.
    /// </summary>
    public class TransformHandleConverterTests
    {
        private JsonSerializerSettings _settings;

        [SetUp]
        public void SetUp()
        {
            _settings = new JsonSerializerSettings
            {
                Converters = { new TransformHandleConverter() }
            };
        }

        private static Type TransformHandleType =>
            typeof(Transform).Assembly.GetType("UnityEngine.TransformHandle");

        // Finds a public instance property on Transform that returns a TransformHandle,
        // which is how the GameObjectSerializer's reflection walk encounters one.
        private static object GetHandleFor(Transform transform)
        {
            Type handleType = TransformHandleType;
            if (handleType == null) return null;

            PropertyInfo handleProperty = typeof(Transform)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(p => p.PropertyType == handleType);
            return handleProperty?.GetValue(transform);
        }

        [Test]
        public void CanConvert_RejectsUnrelatedTypes()
        {
            var converter = new TransformHandleConverter();

            Assert.That(converter.CanConvert(typeof(Vector3)), Is.False);
            Assert.That(converter.CanConvert(typeof(Transform)), Is.False);
            Assert.That(converter.CanConvert(typeof(Matrix4x4)), Is.False);
        }

        [Test]
        public void CanConvert_MatchesTransformHandleByName()
        {
            Type handleType = TransformHandleType;
            if (handleType == null)
                Assert.Ignore("UnityEngine.TransformHandle does not exist on this Unity version.");

            Assert.That(new TransformHandleConverter().CanConvert(handleType), Is.True);
        }

        [Test]
        public void Serialize_LiveHandle_WritesNull()
        {
            var go = new GameObject("TransformHandleConverterTests_Live");
            try
            {
                object handle = GetHandleFor(go.transform);
                if (handle == null)
                    Assert.Ignore("No TransformHandle-returning property on this Unity version.");

                string json = JsonConvert.SerializeObject(handle, _settings);
                Assert.That(json, Is.EqualTo("null"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void Serialize_HandleOfDestroyedObject_DoesNotThrow()
        {
            // The regression: a handle obtained before its object is destroyed. Without
            // the converter, serializing it enumerates its children and throws
            // NullReferenceException from TransformHandle.AssertHandleIsValid.
            var go = new GameObject("TransformHandleConverterTests_Destroyed");
            object handle = GetHandleFor(go.transform);
            UnityEngine.Object.DestroyImmediate(go);

            if (handle == null)
                Assert.Ignore("No TransformHandle-returning property on this Unity version.");

            string json = null;
            Assert.DoesNotThrow(() => json = JsonConvert.SerializeObject(handle, _settings));
            Assert.That(json, Is.EqualTo("null"));
        }
    }
}
