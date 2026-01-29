using NUnit.Framework;
using Newtonsoft.Json.Linq;
using MCPForUnity.Editor.Helpers;

namespace MCPForUnityTests.Editor.Helpers
{
    /// <summary>
    /// Tests for the ToolParams parameter validation wrapper.
    /// </summary>
    public class ToolParamsTests
    {
        #region Constructor Tests

        [Test]
        public void ToolParams_Constructor_ThrowsOnNullParams()
        {
            Assert.Throws<System.ArgumentNullException>(() => new ToolParams(null));
        }

        [Test]
        public void ToolParams_Constructor_AcceptsEmptyJObject()
        {
            Assert.DoesNotThrow(() => new ToolParams(new JObject()));
        }

        #endregion

        #region GetRequired Tests

        [Test]
        public void GetRequired_ExistingParameter_ReturnsSuccess()
        {
            var json = new JObject { ["action"] = "create" };
            var p = new ToolParams(json);

            var result = p.GetRequired("action");

            Assert.IsTrue(result.IsSuccess);
            Assert.AreEqual("create", result.Value);
            Assert.IsNull(result.ErrorMessage);
        }

        [Test]
        public void GetRequired_MissingParameter_ReturnsError()
        {
            var json = new JObject();
            var p = new ToolParams(json);

            var result = p.GetRequired("action");

            Assert.IsFalse(result.IsSuccess);
            Assert.IsNull(result.Value);
            Assert.That(result.ErrorMessage, Does.Contain("'action' parameter is required"));
        }

        [Test]
        public void GetRequired_EmptyStringParameter_ReturnsError()
        {
            var json = new JObject { ["action"] = "" };
            var p = new ToolParams(json);

            var result = p.GetRequired("action");

            Assert.IsFalse(result.IsSuccess);
            Assert.That(result.ErrorMessage, Does.Contain("'action' parameter is required"));
        }

        [Test]
        public void GetRequired_CustomErrorMessage_ReturnsCustomError()
        {
            var json = new JObject();
            var p = new ToolParams(json);

            var result = p.GetRequired("action", "Custom error message");

            Assert.IsFalse(result.IsSuccess);
            Assert.AreEqual("Custom error message", result.ErrorMessage);
        }

        #endregion

        #region Get Tests

        [Test]
        public void Get_ExistingParameter_ReturnsValue()
        {
            var json = new JObject { ["name"] = "TestObject" };
            var p = new ToolParams(json);

            var value = p.Get("name");

            Assert.AreEqual("TestObject", value);
        }

        [Test]
        public void Get_MissingParameter_ReturnsNull()
        {
            var json = new JObject();
            var p = new ToolParams(json);

            var value = p.Get("name");

            Assert.IsNull(value);
        }

        [Test]
        public void Get_MissingParameterWithDefault_ReturnsDefault()
        {
            var json = new JObject();
            var p = new ToolParams(json);

            var value = p.Get("name", "DefaultName");

            Assert.AreEqual("DefaultName", value);
        }

        #endregion

        #region Snake/Camel Case Fallback Tests

        [Test]
        public void Get_SnakeCaseParameter_FindsWithCamelCaseKey()
        {
            var json = new JObject { ["search_method"] = "by_name" };
            var p = new ToolParams(json);

            // Asking for camelCase should find snake_case
            var value = p.Get("searchMethod");

            Assert.AreEqual("by_name", value);
        }

        [Test]
        public void Get_CamelCaseParameter_FindsWithSnakeCaseKey()
        {
            var json = new JObject { ["searchMethod"] = "by_name" };
            var p = new ToolParams(json);

            // Asking for snake_case should find camelCase
            var value = p.Get("search_method");

            Assert.AreEqual("by_name", value);
        }

        [Test]
        public void Get_ExactMatchTakesPrecedence()
        {
            // If both snake_case and camelCase exist, exact match wins
            var json = new JObject
            {
                ["search_method"] = "snake",
                ["searchMethod"] = "camel"
            };
            var p = new ToolParams(json);

            Assert.AreEqual("snake", p.Get("search_method"));
            Assert.AreEqual("camel", p.Get("searchMethod"));
        }

        #endregion

        #region GetInt Tests

        [Test]
        public void GetInt_ValidInteger_ReturnsValue()
        {
            var json = new JObject { ["count"] = "10" };
            var p = new ToolParams(json);

            var value = p.GetInt("count");

            Assert.AreEqual(10, value);
        }

        [Test]
        public void GetInt_MissingParameter_ReturnsNull()
        {
            var json = new JObject();
            var p = new ToolParams(json);

            var value = p.GetInt("count");

            Assert.IsNull(value);
        }

        [Test]
        public void GetInt_MissingParameterWithDefault_ReturnsDefault()
        {
            var json = new JObject();
            var p = new ToolParams(json);

            var value = p.GetInt("count", 5);

            Assert.AreEqual(5, value);
        }

        [Test]
        public void GetInt_InvalidInteger_ReturnsDefault()
        {
            var json = new JObject { ["count"] = "not_a_number" };
            var p = new ToolParams(json);

            var value = p.GetInt("count", 5);

            Assert.AreEqual(5, value);
        }

        #endregion

        #region GetFloat Tests

        [Test]
        public void GetFloat_ValidFloat_ReturnsValue()
        {
            var json = new JObject { ["scale"] = "2.5" };
            var p = new ToolParams(json);

            var value = p.GetFloat("scale");

            Assert.AreEqual(2.5f, value);
        }

        [Test]
        public void GetFloat_MissingParameter_ReturnsNull()
        {
            var json = new JObject();
            var p = new ToolParams(json);

            var value = p.GetFloat("scale");

            Assert.IsNull(value);
        }

        [Test]
        public void GetFloat_MissingParameterWithDefault_ReturnsDefault()
        {
            var json = new JObject();
            var p = new ToolParams(json);

            var value = p.GetFloat("scale", 1.0f);

            Assert.AreEqual(1.0f, value);
        }

        #endregion

        #region GetBool Tests

        [Test]
        public void GetBool_TrueBoolean_ReturnsTrue()
        {
            var json = new JObject { ["enabled"] = true };
            var p = new ToolParams(json);

            var value = p.GetBool("enabled");

            Assert.IsTrue(value);
        }

        [Test]
        public void GetBool_FalseBoolean_ReturnsFalse()
        {
            var json = new JObject { ["enabled"] = false };
            var p = new ToolParams(json);

            var value = p.GetBool("enabled");

            Assert.IsFalse(value);
        }

        [Test]
        public void GetBool_MissingParameter_ReturnsDefault()
        {
            var json = new JObject();
            var p = new ToolParams(json);

            var value = p.GetBool("enabled", true);

            Assert.IsTrue(value);
        }

        [Test]
        public void GetBool_StringTrue_ReturnsTrue()
        {
            var json = new JObject { ["enabled"] = "true" };
            var p = new ToolParams(json);

            var value = p.GetBool("enabled");

            Assert.IsTrue(value);
        }

        [Test]
        public void GetBool_SnakeCaseParameter_FindsWithCamelCaseKey()
        {
            var json = new JObject { ["include_inactive"] = true };
            var p = new ToolParams(json);

            // Asking for camelCase should find snake_case
            var value = p.GetBool("includeInactive");

            Assert.IsTrue(value);
        }

        [Test]
        public void GetBool_CamelCaseParameter_FindsWithSnakeCaseKey()
        {
            var json = new JObject { ["includeInactive"] = true };
            var p = new ToolParams(json);

            // Asking for snake_case should find camelCase
            var value = p.GetBool("include_inactive");

            Assert.IsTrue(value);
        }

        #endregion

        #region Has Tests

        [Test]
        public void Has_ExistingParameter_ReturnsTrue()
        {
            var json = new JObject { ["key"] = "value" };
            var p = new ToolParams(json);

            Assert.IsTrue(p.Has("key"));
        }

        [Test]
        public void Has_MissingParameter_ReturnsFalse()
        {
            var json = new JObject();
            var p = new ToolParams(json);

            Assert.IsFalse(p.Has("key"));
        }

        [Test]
        public void Has_SnakeCaseParameter_FindsWithCamelCaseKey()
        {
            var json = new JObject { ["search_term"] = "Player" };
            var p = new ToolParams(json);

            // Asking for camelCase should find snake_case
            Assert.IsTrue(p.Has("searchTerm"));
        }

        [Test]
        public void Has_CamelCaseParameter_FindsWithSnakeCaseKey()
        {
            var json = new JObject { ["searchTerm"] = "Player" };
            var p = new ToolParams(json);

            // Asking for snake_case should find camelCase
            Assert.IsTrue(p.Has("search_term"));
        }

        #endregion

        #region GetRaw Tests

        [Test]
        public void GetRaw_ComplexObject_ReturnsJToken()
        {
            var json = new JObject { ["data"] = new JObject { ["nested"] = "value" } };
            var p = new ToolParams(json);

            var raw = p.GetRaw("data");

            Assert.IsNotNull(raw);
            Assert.IsInstanceOf<JObject>(raw);
            Assert.AreEqual("value", raw["nested"]?.ToString());
        }

        [Test]
        public void GetRaw_Array_ReturnsJToken()
        {
            var json = new JObject { ["items"] = new JArray { "a", "b", "c" } };
            var p = new ToolParams(json);

            var raw = p.GetRaw("items");

            Assert.IsNotNull(raw);
            Assert.IsInstanceOf<JArray>(raw);
            Assert.AreEqual(3, ((JArray)raw).Count);
        }

        [Test]
        public void GetRaw_SnakeCaseParameter_FindsWithCamelCaseKey()
        {
            var json = new JObject { ["component_properties"] = new JObject { ["mass"] = 1.5 } };
            var p = new ToolParams(json);

            // Asking for camelCase should find snake_case
            var raw = p.GetRaw("componentProperties");

            Assert.IsNotNull(raw);
            Assert.IsInstanceOf<JObject>(raw);
            Assert.AreEqual(1.5, raw["mass"]?.Value<double>());
        }

        [Test]
        public void GetRaw_CamelCaseParameter_FindsWithSnakeCaseKey()
        {
            var json = new JObject { ["componentProperties"] = new JObject { ["mass"] = 1.5 } };
            var p = new ToolParams(json);

            // Asking for snake_case should find camelCase
            var raw = p.GetRaw("component_properties");

            Assert.IsNotNull(raw);
            Assert.IsInstanceOf<JObject>(raw);
            Assert.AreEqual(1.5, raw["mass"]?.Value<double>());
        }

        #endregion

        #region Result<T> Tests

        [Test]
        public void Result_Success_IsSuccessTrue()
        {
            var result = Result<string>.Success("value");

            Assert.IsTrue(result.IsSuccess);
            Assert.AreEqual("value", result.Value);
            Assert.IsNull(result.ErrorMessage);
        }

        [Test]
        public void Result_Error_IsSuccessFalse()
        {
            var result = Result<string>.Error("error message");

            Assert.IsFalse(result.IsSuccess);
            Assert.IsNull(result.Value);
            Assert.AreEqual("error message", result.ErrorMessage);
        }

        [Test]
        public void Result_GetOrError_Success_ReturnsNull()
        {
            var result = Result<string>.Success("value");

            var error = result.GetOrError(out var value);

            Assert.IsNull(error);
            Assert.AreEqual("value", value);
        }

        [Test]
        public void Result_GetOrError_Error_ReturnsErrorResponse()
        {
            var result = Result<string>.Error("error message");

            var error = result.GetOrError(out var value);

            Assert.IsNotNull(error);
            Assert.IsInstanceOf<ErrorResponse>(error);
            Assert.IsNull(value);

            var errorResponse = error as ErrorResponse;
            Assert.AreEqual("error message", errorResponse.error);
        }

        #endregion
    }
}
