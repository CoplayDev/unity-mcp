using NUnit.Framework;
using MCPForUnity.Editor.Tools;

namespace MCPForUnityTests.Editor.Tools
{
    public class BatchExecuteStripPrefixTests
    {
        [Test]
        public void StripMcpPrefix_PlainName_ReturnsUnchanged()
        {
            Assert.AreEqual("manage_gameobject", BatchExecute.StripMcpPrefix("manage_gameobject"));
        }

        [Test]
        public void StripMcpPrefix_ColonFormat_StripsPrefix()
        {
            Assert.AreEqual("manage_gameobject", BatchExecute.StripMcpPrefix("UnityMCP:manage_gameobject"));
        }

        [Test]
        public void StripMcpPrefix_DoubleUnderscoreFormat_StripsPrefix()
        {
            Assert.AreEqual("manage_gameobject", BatchExecute.StripMcpPrefix("mcp__UnityMCP__manage_gameobject"));
        }

        [Test]
        public void StripMcpPrefix_DoubleUnderscore_ToolWithUnderscores_PreservesToolName()
        {
            Assert.AreEqual("batch_execute", BatchExecute.StripMcpPrefix("mcp__UnityMCP__batch_execute"));
        }

        [Test]
        public void StripMcpPrefix_NullOrEmpty_ReturnsSame()
        {
            Assert.IsNull(BatchExecute.StripMcpPrefix(null));
            Assert.AreEqual("", BatchExecute.StripMcpPrefix(""));
        }

        [Test]
        public void StripMcpPrefix_ColonOnly_PassesThrough()
        {
            // Edge case: ":" alone — no valid suffix, passes through (will fail at CommandRegistry)
            Assert.AreEqual(":", BatchExecute.StripMcpPrefix(":"));
        }

        [Test]
        public void StripMcpPrefix_MultipleColons_UsesLast()
        {
            Assert.AreEqual("manage_gameobject", BatchExecute.StripMcpPrefix("a:b:manage_gameobject"));
        }
    }
}
