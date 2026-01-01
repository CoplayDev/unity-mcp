using System.Linq;
using MCPForUnity.Runtime.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace MCPForUnityTests.Editor.Helpers
{
    /// <summary>
    /// Tests for Matrix4x4Converter to ensure it safely serializes matrices
    /// without accessing dangerous computed properties (lossyScale, rotation).
    /// Regression test for https://github.com/CoplayDev/unity-mcp/issues/478
    /// </summary>
    public class Matrix4x4ConverterTests
    {
        private JsonSerializerSettings _settings;

        [SetUp]
        public void SetUp()
        {
            _settings = new JsonSerializerSettings
            {
                Converters = { new Matrix4x4Converter() }
            };
        }

        [Test]
        public void Serialize_IdentityMatrix_ReturnsCorrectJson()
        {
            var matrix = Matrix4x4.identity;
            var json = JsonConvert.SerializeObject(matrix, _settings);

            Assert.That(json, Does.Contain("\"m00\":1"));
            Assert.That(json, Does.Contain("\"m11\":1"));
            Assert.That(json, Does.Contain("\"m22\":1"));
            Assert.That(json, Does.Contain("\"m33\":1"));
            Assert.That(json, Does.Contain("\"m01\":0"));
        }

        [Test]
        public void Deserialize_IdentityMatrix_ReturnsIdentity()
        {
            var original = Matrix4x4.identity;
            var json = JsonConvert.SerializeObject(original, _settings);
            var result = JsonConvert.DeserializeObject<Matrix4x4>(json, _settings);

            Assert.That(result, Is.EqualTo(original));
        }

        [Test]
        public void Serialize_TranslationMatrix_PreservesValues()
        {
            var matrix = Matrix4x4.Translate(new Vector3(10, 20, 30));
            var json = JsonConvert.SerializeObject(matrix, _settings);
            var result = JsonConvert.DeserializeObject<Matrix4x4>(json, _settings);

            Assert.That(result.m03, Is.EqualTo(10f));
            Assert.That(result.m13, Is.EqualTo(20f));
            Assert.That(result.m23, Is.EqualTo(30f));
        }

        [Test]
        public void Serialize_DegenerateMatrix_DoesNotCrashAndRoundtrips()
        {
            // This is the key test - a degenerate matrix that would crash
            // if we accessed lossyScale or rotation properties
            var matrix = new Matrix4x4();
            matrix.m00 = 0; matrix.m11 = 0; matrix.m22 = 0; // Degenerate - determinant = 0

            // This should NOT throw or crash - the old code would fail here
            var json = JsonConvert.SerializeObject(matrix, _settings);
            var result = JsonConvert.DeserializeObject<Matrix4x4>(json, _settings);

            // Verify JSON only contains raw mXY properties
            var jo = JObject.Parse(json);
            var expectedProps = new[]
            {
                "m00", "m01", "m02", "m03",
                "m10", "m11", "m12", "m13",
                "m20", "m21", "m22", "m23",
                "m30", "m31", "m32", "m33"
            };
            CollectionAssert.AreEquivalent(expectedProps, jo.Properties().Select(p => p.Name).ToArray());

            // Verify values roundtrip correctly (all zeros for degenerate matrix)
            Assert.That(result.m00, Is.EqualTo(0f));
            Assert.That(result.m11, Is.EqualTo(0f));
            Assert.That(result.m22, Is.EqualTo(0f));
            Assert.That(result, Is.EqualTo(matrix));
        }

        [Test]
        public void Serialize_NonTRSMatrix_DoesNotCrash()
        {
            // Projection matrices are NOT valid TRS matrices
            // Accessing lossyScale/rotation on them causes ValidTRS() assertion
            var matrix = Matrix4x4.Perspective(60f, 1.77f, 0.1f, 1000f);

            // Verify it's not a valid TRS matrix
            Assert.That(matrix.ValidTRS(), Is.False, "Test requires non-TRS matrix");

            // This should NOT throw - the fix ensures we never access computed properties
            Assert.DoesNotThrow(() =>
            {
                var json = JsonConvert.SerializeObject(matrix, _settings);
                var result = JsonConvert.DeserializeObject<Matrix4x4>(json, _settings);
            });
        }

        [Test]
        public void Deserialize_NullToken_ReturnsZeroMatrix()
        {
            var json = "null";
            var result = JsonConvert.DeserializeObject<Matrix4x4>(json, _settings);

            // Returns zero matrix (consistent with missing field defaults of 0f)
            Assert.That(result, Is.EqualTo(new Matrix4x4()));
        }

        [Test]
        public void Serialize_DoesNotContainDangerousProperties()
        {
            var matrix = Matrix4x4.TRS(Vector3.one, Quaternion.identity, Vector3.one);
            var json = JsonConvert.SerializeObject(matrix, _settings);

            // Ensure we're not serializing the dangerous computed properties
            Assert.That(json, Does.Not.Contain("lossyScale"));
            Assert.That(json, Does.Not.Contain("rotation"));
            Assert.That(json, Does.Not.Contain("inverse"));
            Assert.That(json, Does.Not.Contain("transpose"));
        }
    }
}
