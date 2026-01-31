using System;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Newtonsoft.Json.Linq;
using MCPForUnity.Editor.Tools;
using MCPForUnity.Editor.Helpers;

namespace MCPForUnityTests.Editor.Tools
{
    /// <summary>
    /// ISOLATED TEST: Array format [0, 0] for float property
    /// Issue #654 - Test 2 of 8
    /// This is the test that triggers "Error converting token to System.Single"
    /// </summary>
    public class PropertyConversion_ArrayForFloat_Test
    {
        private GameObject testGameObject;

        [SetUp]
        public void SetUp()
        {
            testGameObject = new GameObject("PropertyConversion_ArrayForFloat_Test");
        }

        [TearDown]
        public void TearDown()
        {
            if (testGameObject != null)
            {
                UnityEngine.Object.DestroyImmediate(testGameObject);
            }
        }

        [Test]
        public void SetProperty_ArrayForFloat_ReturnsError()
        {
            // Expect the error log that will be generated
            LogAssert.Expect(LogType.Error, new Regex("Error converting token to System.Single"));

            var audioSource = testGameObject.AddComponent<AudioSource>();

            // This is the exact error from issue #654
            var setPropertyParams = new JObject
            {
                ["action"] = "set_property",
                ["target"] = testGameObject.name,
                ["componentType"] = "AudioSource",
                ["property"] = "spatialBlend",
                ["value"] = JArray.Parse("[0, 0]")  // Array for float = error
            };

            var result = ManageComponents.HandleCommand(setPropertyParams);
            Assert.IsNotNull(result, "Should return a result");
        }
    }
}
