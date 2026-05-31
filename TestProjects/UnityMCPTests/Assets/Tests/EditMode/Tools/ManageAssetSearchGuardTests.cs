using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Tools;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace MCPForUnityTests.Editor.Tools
{
    public class ManageAssetSearchGuardTests
    {
        [Test]
        public void Search_WithoutScopeOrFilter_ReturnsError()
        {
            var result = ManageAsset.HandleCommand(new JObject
            {
                ["action"] = "search",
                ["path"] = ""
            });

            var error = result as ErrorResponse;
            Assert.IsNotNull(error, "Unfiltered project-wide search should be rejected.");
            Assert.That(error.Error, Does.Contain("searchPattern"));
        }

        [Test]
        public void Search_WithInvalidFolderScope_ReturnsError()
        {
            var result = ManageAsset.HandleCommand(new JObject
            {
                ["action"] = "search",
                ["path"] = "DefinitelyNotAUnityAssetFolder",
                ["filterType"] = "Prefab"
            });

            var error = result as ErrorResponse;
            Assert.IsNotNull(error, "Invalid search folder must not fall back to a full-project search.");
            Assert.That(error.Error, Does.Contain("valid folder"));
        }
    }
}
