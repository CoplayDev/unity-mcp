using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Newtonsoft.Json.Linq;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Tools;
using MCPForUnity.Editor.Tools.GameObjects;

namespace MCPForUnityTests.Editor.Tools
{
    /// <summary>
    /// Tests for GameObjectComponentHelpers.SetComponentPropertiesInternal error reporting.
    /// Reproduces issue #765: conversion failures incorrectly reported as "Property not found".
    /// </summary>
    public class GameObjectComponentHelpersErrorTests
    {
        private GameObject testGo;

        [SetUp]
        public void SetUp()
        {
            testGo = new GameObject("ErrorTestGO");
            CommandRegistry.Initialize();
        }

        [TearDown]
        public void TearDown()
        {
            if (testGo != null)
                Object.DestroyImmediate(testGo);
        }

        /// <summary>
        /// When a property exists but conversion fails, the error should say
        /// "Failed to convert" rather than "Property not found. Did you mean: X?"
        /// </summary>
        [Test]
        public void SetComponentProperties_ConversionFailure_ReportsConversionError_NotPropertyNotFound()
        {
            // Expect conversion error log from PropertyConversion (ComponentOps reflection attempt)
            LogAssert.Expect(LogType.Error, new Regex("Error converting token"));
            // Expect the warning log from SetComponentPropertiesInternal
            LogAssert.Expect(LogType.Warning, new Regex("Failed to set"));

            var audioSource = testGo.AddComponent<AudioSource>();

            // spatialBlend is a float property — passing an array triggers conversion failure
            var props = new JObject { ["spatialBlend"] = JArray.Parse("[1, 2, 3]") };

            var result = GameObjectComponentHelpers.SetComponentPropertiesInternal(
                testGo, "AudioSource", props, audioSource);

            Assert.IsNotNull(result, "Should return an error response");
            Assert.IsInstanceOf<ErrorResponse>(result);

            var errorResponse = (ErrorResponse)result;

            // The error message must NOT say "not found" for a property that exists
            Assert.IsFalse(
                errorResponse.Error.Contains("not found"),
                $"Error should report conversion failure, not 'not found'. Got: {errorResponse.Error}");
        }

        /// <summary>
        /// When a property genuinely doesn't exist, the error should still say "not found" with suggestions.
        /// </summary>
        [Test]
        public void SetComponentProperties_NonexistentProperty_ReportsNotFound()
        {
            // Expect the "not found" warning
            LogAssert.Expect(LogType.Warning, new Regex("not found"));

            var audioSource = testGo.AddComponent<AudioSource>();

            var props = new JObject { ["totallyFakeProperty"] = 42 };

            var result = GameObjectComponentHelpers.SetComponentPropertiesInternal(
                testGo, "AudioSource", props, audioSource);

            Assert.IsNotNull(result);
            Assert.IsInstanceOf<ErrorResponse>(result);

            var errorResponse = (ErrorResponse)result;

            Assert.IsTrue(
                errorResponse.Error.Contains("not found") || errorResponse.Error.Contains("failed"),
                $"Error for nonexistent property should say 'not found'. Got: {errorResponse.Error}");
        }

        /// <summary>
        /// Valid property setting should still succeed.
        /// </summary>
        [Test]
        public void SetComponentProperties_ValidProperty_Succeeds()
        {
            var audioSource = testGo.AddComponent<AudioSource>();

            var props = new JObject { ["volume"] = 0.42f };

            var result = GameObjectComponentHelpers.SetComponentPropertiesInternal(
                testGo, "AudioSource", props, audioSource);

            Assert.IsNull(result, "Should return null on success (no errors)");
            Assert.AreEqual(0.42f, audioSource.volume, 0.001f);
        }
    }
}
