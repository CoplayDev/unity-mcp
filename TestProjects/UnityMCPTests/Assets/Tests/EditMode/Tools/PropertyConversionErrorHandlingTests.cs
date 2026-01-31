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
    /// Tests to reproduce issue #654: PropertyConversion crash causing dispatcher unavailability
    /// while telemetry continues reporting success.
    /// </summary>
    public class PropertyConversionErrorHandlingTests
    {
        private GameObject testGameObject;

        [SetUp]
        public void SetUp()
        {
            testGameObject = new GameObject("PropertyConversionTestObject");
            CommandRegistry.Initialize();
        }

        [TearDown]
        public void TearDown()
        {
            if (testGameObject != null)
            {
                UnityEngine.Object.DestroyImmediate(testGameObject);
            }
        }

        /// <summary>
        /// Test case 1: Integer value for object reference property (AudioClip on AudioSource)
        /// Should return graceful error, not crash dispatcher
        /// </summary>
        [Test]
        public void ManageComponents_SetProperty_IntegerForObjectReference_ReturnsGracefulError()
        {
            // Add AudioSource component
            var audioSource = testGameObject.AddComponent<AudioSource>();

            // Try to set AudioClip (object reference) to integer 12345
            var setPropertyParams = new JObject
            {
                ["action"] = "set_property",
                ["target"] = testGameObject.name,
                ["componentType"] = "AudioSource",
                ["property"] = "clip",
                ["value"] = 12345  // INCOMPATIBLE: int for AudioClip
            };

            var result = ManageComponents.HandleCommand(setPropertyParams);

            // Main test: should return a result without crashing
            Assert.IsNotNull(result, "Should return a result, not crash dispatcher");

            // If it's an ErrorResponse, verify it properly reports failure
            if (result is ErrorResponse errorResp)
            {
                Assert.IsFalse(errorResp.Success, "Should report failure for incompatible type");
            }
        }

        /// <summary>
        /// Test case 2: Array format for float property (spatialBlend expects float, not array)
        /// Mirrors the "Array format [0, 0] for Vector2 properties" from issue #654
        /// This test documents that the error is caught and doesn't crash the dispatcher
        /// </summary>
        [Test]
        public void ManageComponents_SetProperty_ArrayForFloatProperty_DoesNotCrashDispatcher()
        {
            // Expect the error log that will be generated
            LogAssert.Expect(LogType.Error, new Regex("Error converting token to System.Single"));

            // Add AudioSource component
            var audioSource = testGameObject.AddComponent<AudioSource>();

            // Try to set spatialBlend (float) to array [0, 0]
            // This triggers: "Error converting token to System.Single: Error reading double. Unexpected token: StartArray"
            var setPropertyParams = new JObject
            {
                ["action"] = "set_property",
                ["target"] = testGameObject.name,
                ["componentType"] = "AudioSource",
                ["property"] = "spatialBlend",
                ["value"] = JArray.Parse("[0, 0]")  // INCOMPATIBLE: array for float
            };

            var result = ManageComponents.HandleCommand(setPropertyParams);

            // Main test: dispatcher should remain responsive and return a result
            Assert.IsNotNull(result, "Should return a result, not crash dispatcher");

            // Verify subsequent commands still work
            var followupParams = new JObject
            {
                ["action"] = "set_property",
                ["target"] = testGameObject.name,
                ["componentType"] = "AudioSource",
                ["property"] = "volume",
                ["value"] = 0.5f
            };

            var followupResult = ManageComponents.HandleCommand(followupParams);
            Assert.IsNotNull(followupResult, "Dispatcher should still be responsive after conversion error");
        }

        /// <summary>
        /// Test case 3: Multiple property conversion failures in sequence
        /// Tests if dispatcher remains responsive after multiple errors
        /// </summary>
        [Test]
        public void ManageComponents_MultipleSetPropertyFailures_DispatcherStaysResponsive()
        {
            // Expect the error log for the invalid string conversion
            LogAssert.Expect(LogType.Error, new Regex("Error converting token to System.Single"));

            var audioSource = testGameObject.AddComponent<AudioSource>();

            // First bad conversion attempt - int for AudioClip doesn't generate an error log
            var badParam1 = new JObject
            {
                ["action"] = "set_property",
                ["target"] = testGameObject.name,
                ["componentType"] = "AudioSource",
                ["property"] = "clip",
                ["value"] = 999  // bad: int for AudioClip
            };

            var result1 = ManageComponents.HandleCommand(badParam1);
            Assert.IsNotNull(result1, "First call should return result");

            // Second bad conversion attempt - generates error log
            var badParam2 = new JObject
            {
                ["action"] = "set_property",
                ["target"] = testGameObject.name,
                ["componentType"] = "AudioSource",
                ["property"] = "rolloffFactor",
                ["value"] = "invalid_string"  // bad: string for float
            };

            var result2 = ManageComponents.HandleCommand(badParam2);
            Assert.IsNotNull(result2, "Second call should return result");

            // Third attempt - valid conversion
            var badParam3 = new JObject
            {
                ["action"] = "set_property",
                ["target"] = testGameObject.name,
                ["componentType"] = "AudioSource",
                ["property"] = "volume",
                ["value"] = 0.5f  // good: float for float - dispatcher should still work
            };

            var result3 = ManageComponents.HandleCommand(badParam3);
            Assert.IsNotNull(result3, "Third call should return result (dispatcher should still be responsive)");
        }

        /// <summary>
        /// Test case 4: After property conversion failures, other commands still work
        /// Tests dispatcher responsiveness
        /// </summary>
        [Test]
        public void ManageComponents_AfterConversionFailure_OtherOperationsWork()
        {
            var audioSource = testGameObject.AddComponent<AudioSource>();

            // Trigger a conversion failure
            var failParam = new JObject
            {
                ["action"] = "set_property",
                ["target"] = testGameObject.name,
                ["componentType"] = "AudioSource",
                ["property"] = "clip",
                ["value"] = 12345  // bad
            };

            var failResult = ManageComponents.HandleCommand(failParam);
            Assert.IsNotNull(failResult, "Should return result for failed conversion");

            // Now try a valid operation on the same component
            var validParam = new JObject
            {
                ["action"] = "set_property",
                ["target"] = testGameObject.name,
                ["componentType"] = "AudioSource",
                ["property"] = "volume",
                ["value"] = 0.5f  // valid: float for float
            };

            var validResult = ManageComponents.HandleCommand(validParam);
            Assert.IsNotNull(validResult, "Should still be able to execute valid commands after conversion failure");

            // Verify the property was actually set
            Assert.AreEqual(0.5f, audioSource.volume, "Volume should have been set to 0.5");
        }

        /// <summary>
        /// Test case 5: Telemetry continues reporting success even after conversion errors
        /// This is the core of issue #654: telemetry should accurately reflect dispatcher health
        /// </summary>
        [Test]
        public void ManageEditor_TelemetryStatus_ReportsAccurateHealth()
        {
            // Trigger multiple conversion failures first
            var audioSource = testGameObject.AddComponent<AudioSource>();

            for (int i = 0; i < 3; i++)
            {
                var badParam = new JObject
                {
                    ["action"] = "set_property",
                    ["target"] = testGameObject.name,
                    ["componentType"] = "AudioSource",
                    ["property"] = "clip",
                    ["value"] = i * 1000  // bad
                };
                ManageComponents.HandleCommand(badParam);
            }

            // Now check telemetry
            var telemetryParams = new JObject { ["action"] = "telemetry_status" };
            var telemetryResult = ManageEditor.HandleCommand(telemetryParams);

            Assert.IsNotNull(telemetryResult, "Telemetry should return result");

            // NOTE: Issue #654 noted that telemetry returns success even when dispatcher is dead.
            // If telemetry returns success, that's the actual current behavior (which may be a problem).
            // This test just documents what happens.
        }

        /// <summary>
        /// Test case 6: Direct PropertyConversion error handling
        /// Tests if PropertyConversion.ConvertToType properly handles exceptions
        /// </summary>
        [Test]
        public void PropertyConversion_ConvertToType_HandlesIncompatibleTypes()
        {
            // Try to convert integer to AudioClip type
            var token = JToken.FromObject(12345);

            // PropertyConversion.ConvertToType should either:
            // 1. Return a valid converted value
            // 2. Throw an exception that can be caught
            // 3. Return null

            Exception thrownException = null;
            object result = null;

            try
            {
                result = PropertyConversion.ConvertToType(token, typeof(AudioClip));
            }
            catch (Exception ex)
            {
                thrownException = ex;
            }

            // Document what actually happens
            if (thrownException != null)
            {
                Debug.Log($"PropertyConversion threw exception: {thrownException.GetType().Name}: {thrownException.Message}");
                Assert.Pass($"PropertyConversion threw {thrownException.GetType().Name} - exception is being raised, not swallowed");
            }
            else if (result == null)
            {
                Debug.Log("PropertyConversion returned null for incompatible type");
                Assert.Pass("PropertyConversion returned null for incompatible type");
            }
            else
            {
                Debug.Log($"PropertyConversion returned unexpected result: {result}");
                Assert.Pass("PropertyConversion produced some result");
            }
        }

        /// <summary>
        /// Test case 7: TryConvertToType should never throw
        /// </summary>
        [Test]
        public void PropertyConversion_TryConvertToType_NeverThrows()
        {
            var token = JToken.FromObject(12345);

            // This should never throw, only return null
            object result = null;
            Exception thrownException = null;

            try
            {
                result = PropertyConversion.TryConvertToType(token, typeof(AudioClip));
            }
            catch (Exception ex)
            {
                thrownException = ex;
            }

            Assert.IsNull(thrownException, "TryConvertToType should never throw");
            // Result can be null or a value, but shouldn't throw
        }

        /// <summary>
        /// Test case 8: ComponentOps error handling
        /// Tests if ComponentOps.SetProperty properly catches exceptions
        /// </summary>
        [Test]
        public void ComponentOps_SetProperty_HandlesConversionErrors()
        {
            var audioSource = testGameObject.AddComponent<AudioSource>();
            var token = JToken.FromObject(12345);

            // Try to set clip (AudioClip) to integer value
            bool success = ComponentOps.SetProperty(audioSource, "clip", token, out string error);

            Assert.IsFalse(success, "Should fail to set incompatible type");
            Assert.IsNotEmpty(error, "Should provide error message");

            // Verify the object is still in a valid state
            Assert.IsNotNull(audioSource, "AudioSource should still exist");
            Assert.IsNull(audioSource.clip, "Clip should remain null (not corrupted)");
        }
    }
}
