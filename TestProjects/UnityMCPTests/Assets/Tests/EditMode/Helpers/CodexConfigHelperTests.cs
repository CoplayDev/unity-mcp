using NUnit.Framework;
using MCPForUnity.Editor.Helpers;

namespace MCPForUnityTests.Editor.Helpers
{
    public class CodexConfigHelperTests
    {
        [Test]
        public void TryParseCodexServer_SingleLineArgs_ParsesSuccessfully()
        {
            string toml = string.Join("\n", new[]
            {
                "[mcp_servers.unityMCP]",
                "command = \"uv\"",
                "args = [\"run\", \"--directory\", \"/abs/path\", \"server.py\"]"
            });

            bool result = CodexConfigHelper.TryParseCodexServer(toml, out string command, out string[] args);

            Assert.IsTrue(result, "Parser should detect server definition");
            Assert.AreEqual("uv", command);
            CollectionAssert.AreEqual(new[] { "run", "--directory", "/abs/path", "server.py" }, args);
        }

        [Test]
        public void TryParseCodexServer_MultiLineArgsWithTrailingComma_ParsesSuccessfully()
        {
            string toml = string.Join("\n", new[]
            {
                "[mcp_servers.unityMCP]",
                "command = \"uv\"",
                "args = [",
                "  \"run\",",
                "  \"--directory\",",
                "  \"/abs/path\",",
                "  \"server.py\",",
                "]"
            });

            bool result = CodexConfigHelper.TryParseCodexServer(toml, out string command, out string[] args);

            Assert.IsTrue(result, "Parser should handle multi-line arrays with trailing comma");
            Assert.AreEqual("uv", command);
            CollectionAssert.AreEqual(new[] { "run", "--directory", "/abs/path", "server.py" }, args);
        }

        [Test]
        public void TryParseCodexServer_MultiLineArgsWithComments_IgnoresComments()
        {
            string toml = string.Join("\n", new[]
            {
                "[mcp_servers.unityMCP]",
                "command = \"uv\"",
                "args = [",
                "  \"run\", # launch command",
                "  \"--directory\",",
                "  \"/abs/path\",",
                "  \"server.py\"",
                "]"
            });

            bool result = CodexConfigHelper.TryParseCodexServer(toml, out string command, out string[] args);

            Assert.IsTrue(result, "Parser should tolerate comments within the array block");
            Assert.AreEqual("uv", command);
            CollectionAssert.AreEqual(new[] { "run", "--directory", "/abs/path", "server.py" }, args);
        }

        [Test]
        public void TryParseCodexServer_HeaderWithComment_StillDetected()
        {
            string toml = string.Join("\n", new[]
            {
                "[mcp_servers.unityMCP] # annotated header",
                "command = \"uv\"",
                "args = [\"run\", \"--directory\", \"/abs/path\", \"server.py\"]"
            });

            bool result = CodexConfigHelper.TryParseCodexServer(toml, out string command, out string[] args);

            Assert.IsTrue(result, "Parser should recognize section headers even with inline comments");
            Assert.AreEqual("uv", command);
            CollectionAssert.AreEqual(new[] { "run", "--directory", "/abs/path", "server.py" }, args);
        }

        [Test]
        public void TryParseCodexServer_SingleQuotedArgsWithApostrophes_ParsesSuccessfully()
        {
            string toml = string.Join("\n", new[]
            {
                "[mcp_servers.unityMCP]",
                "command = 'uv'",
                "args = ['run', '--directory', '/Users/O''Connor/codex', 'server.py']"
            });

            bool result = CodexConfigHelper.TryParseCodexServer(toml, out string command, out string[] args);

            Assert.IsTrue(result, "Parser should accept single-quoted arrays with escaped apostrophes");
            Assert.AreEqual("uv", command);
            CollectionAssert.AreEqual(new[] { "run", "--directory", "/Users/O'Connor/codex", "server.py" }, args);
        }
    }
}
