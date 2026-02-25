using NUnit.Framework;
using Newtonsoft.Json.Linq;
using MCPForUnity.Editor.Tools;

namespace MCPForUnityTests.Editor.Tools
{
    /// <summary>
    /// EditMode tests for the ReserializeAssets tool.
    /// Tests parameter validation and error handling.
    /// </summary>
    [TestFixture]
    public class ReserializeAssetsTests
    {
        private static JObject ToJO(object o) => JObject.FromObject(o);

        [Test]
        public void HandleCommand_NullParams_ReturnsError()
        {
            var result = ReserializeAssets.HandleCommand(null);
            var jo = ToJO(result);
            Assert.IsFalse((bool)jo["success"]);
            Assert.IsNotNull(jo["error"]);
            Assert.That((string)jo["error"], Does.Contain("cannot be null"));
        }

        [Test]
        public void HandleCommand_NoPathOrPaths_ReturnsError()
        {
            var result = ReserializeAssets.HandleCommand(new JObject());
            var jo = ToJO(result);
            Assert.IsFalse((bool)jo["success"]);
            Assert.That((string)jo["error"], Does.Contain("path"));
        }

        [Test]
        public void HandleCommand_EmptyPathsArray_ReturnsError()
        {
            var result = ReserializeAssets.HandleCommand(new JObject
            {
                ["paths"] = new JArray()
            });
            var jo = ToJO(result);
            Assert.IsFalse((bool)jo["success"]);
        }
    }
}
