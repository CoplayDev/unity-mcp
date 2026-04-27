using NUnit.Framework;
using Newtonsoft.Json.Linq;
using UnityEngine;
using MCPForUnity.Editor.Helpers;

namespace MCPForUnityTests.Editor.Tools
{
    /// <summary>
    /// Guards array compatibility for Unity structs that commonly flow through
    /// manage_components as JSON-stringified values.
    /// </summary>
    public class PropertyConversion_UnityStructArraySupport_Tests
    {
        [Test]
        public void ConvertToType_ColorArrayWithAlpha_Succeeds()
        {
            var result = (Color)PropertyConversion.ConvertToType(
                JArray.Parse("[1.0, 0.5, 0.25, 0.75]"),
                typeof(Color)
            );

            Assert.AreEqual(new Color(1.0f, 0.5f, 0.25f, 0.75f), result);
        }

        [Test]
        public void ConvertToType_ColorArrayWithoutAlpha_DefaultsToOne()
        {
            var result = (Color)PropertyConversion.ConvertToType(
                JArray.Parse("[1.0, 0.5, 0.25]"),
                typeof(Color)
            );

            Assert.AreEqual(new Color(1.0f, 0.5f, 0.25f, 1.0f), result);
        }

        [Test]
        public void ConvertToType_RectArray_Succeeds()
        {
            var result = (Rect)PropertyConversion.ConvertToType(
                JArray.Parse("[10.0, 20.0, 30.0, 40.0]"),
                typeof(Rect)
            );

            Assert.AreEqual(new Rect(10.0f, 20.0f, 30.0f, 40.0f), result);
        }
    }
}
