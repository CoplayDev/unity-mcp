using NUnit.Framework;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.External.Tommy;
using MCPForUnity.Editor.Services;
using System.IO;
using System.Linq;

namespace MCPForUnityTests.Editor.Helpers
{
    public class CodexConfigHelperTests
    {
        /// <summary>
        /// Mock platform service for testing
        /// </summary>
        private class MockPlatformService : IPlatformService
        {
            private readonly bool _isWindows;
            private readonly string _systemRoot;

            public MockPlatformService(bool isWindows, string systemRoot = "C:\\Windows")
            {
                _isWindows = isWindows;
                _systemRoot = systemRoot;
            }

            public bool IsWindows() => _isWindows;
            public string GetSystemRoot() => _isWindows ? _systemRoot : null;
        }

        [TearDown]
        public void TearDown()
        {
            // Reset service locator after each test
            MCPServiceLocator.Reset();
        }

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

        [Test]
        public void BuildCodexServerBlock_OnWindows_IncludesSystemRootEnv()
        {
            // This test verifies the fix for https://github.com/CoplayDev/unity-mcp/issues/315
            // On Windows, Codex requires SystemRoot environment variable to be set

            // Mock Windows platform
            MCPServiceLocator.Register<IPlatformService>(new MockPlatformService(isWindows: true, systemRoot: "C:\\Windows"));

            string uvPath = "C:\\path\\to\\uv.exe";
            string serverSrc = "C:\\path\\to\\server";

            string result = CodexConfigHelper.BuildCodexServerBlock(uvPath, serverSrc);

            Assert.IsNotNull(result, "BuildCodexServerBlock should return a valid TOML string");

            // Parse the generated TOML to validate structure
            TomlTable parsed;
            using (var reader = new StringReader(result))
            {
                parsed = TOML.Parse(reader);
            }

            // Verify basic structure
            Assert.IsTrue(parsed.TryGetNode("mcp_servers", out var mcpServersNode), "TOML should contain mcp_servers");
            Assert.IsInstanceOf<TomlTable>(mcpServersNode, "mcp_servers should be a table");

            var mcpServers = mcpServersNode as TomlTable;
            Assert.IsTrue(mcpServers.TryGetNode("unityMCP", out var unityMcpNode), "mcp_servers should contain unityMCP");
            Assert.IsInstanceOf<TomlTable>(unityMcpNode, "unityMCP should be a table");

            var unityMcp = unityMcpNode as TomlTable;
            Assert.IsTrue(unityMcp.TryGetNode("command", out _), "unityMCP should contain command");
            Assert.IsTrue(unityMcp.TryGetNode("args", out _), "unityMCP should contain args");

            // Verify env.SystemRoot is present on Windows
            bool hasEnv = unityMcp.TryGetNode("env", out var envNode);
            Assert.IsTrue(hasEnv, "Windows config should contain env table");
            Assert.IsInstanceOf<TomlTable>(envNode, "env should be a table");

            var env = envNode as TomlTable;
            Assert.IsTrue(env.TryGetNode("SystemRoot", out var systemRootNode), "env should contain SystemRoot");
            Assert.IsInstanceOf<TomlString>(systemRootNode, "SystemRoot should be a string");

            var systemRoot = (systemRootNode as TomlString).Value;
            Assert.AreEqual("C:\\Windows", systemRoot, "SystemRoot should be C:\\Windows");
        }

        [Test]
        public void BuildCodexServerBlock_OnNonWindows_ExcludesEnv()
        {
            // This test verifies that non-Windows platforms don't include env configuration

            // Mock non-Windows platform (e.g., macOS/Linux)
            MCPServiceLocator.Register<IPlatformService>(new MockPlatformService(isWindows: false));

            string uvPath = "/usr/local/bin/uv";
            string serverSrc = "/path/to/server";

            string result = CodexConfigHelper.BuildCodexServerBlock(uvPath, serverSrc);

            Assert.IsNotNull(result, "BuildCodexServerBlock should return a valid TOML string");

            // Parse the generated TOML to validate structure
            TomlTable parsed;
            using (var reader = new StringReader(result))
            {
                parsed = TOML.Parse(reader);
            }

            // Verify basic structure
            Assert.IsTrue(parsed.TryGetNode("mcp_servers", out var mcpServersNode), "TOML should contain mcp_servers");
            Assert.IsInstanceOf<TomlTable>(mcpServersNode, "mcp_servers should be a table");

            var mcpServers = mcpServersNode as TomlTable;
            Assert.IsTrue(mcpServers.TryGetNode("unityMCP", out var unityMcpNode), "mcp_servers should contain unityMCP");
            Assert.IsInstanceOf<TomlTable>(unityMcpNode, "unityMCP should be a table");

            var unityMcp = unityMcpNode as TomlTable;
            Assert.IsTrue(unityMcp.TryGetNode("command", out _), "unityMCP should contain command");
            Assert.IsTrue(unityMcp.TryGetNode("args", out _), "unityMCP should contain args");

            // Verify env is NOT present on non-Windows platforms
            bool hasEnv = unityMcp.TryGetNode("env", out _);
            Assert.IsFalse(hasEnv, "Non-Windows config should not contain env table");
        }

        [Test]
        public void UpsertCodexServerBlock_OnWindows_IncludesSystemRootEnv()
        {
            // This test verifies the fix for https://github.com/CoplayDev/unity-mcp/issues/315
            // Ensures that upsert operations also include Windows-specific env configuration

            // Mock Windows platform
            MCPServiceLocator.Register<IPlatformService>(new MockPlatformService(isWindows: true, systemRoot: "C:\\Windows"));

            string existingToml = string.Join("\n", new[]
            {
                "[other_section]",
                "key = \"value\""
            });

            string uvPath = "C:\\path\\to\\uv.exe";
            string serverSrc = "C:\\path\\to\\server";

            string result = CodexConfigHelper.UpsertCodexServerBlock(existingToml, uvPath, serverSrc);

            Assert.IsNotNull(result, "UpsertCodexServerBlock should return a valid TOML string");

            // Parse the generated TOML to validate structure
            TomlTable parsed;
            using (var reader = new StringReader(result))
            {
                parsed = TOML.Parse(reader);
            }

            // Verify existing sections are preserved
            Assert.IsTrue(parsed.TryGetNode("other_section", out _), "TOML should preserve existing sections");

            // Verify mcp_servers structure
            Assert.IsTrue(parsed.TryGetNode("mcp_servers", out var mcpServersNode), "TOML should contain mcp_servers");
            Assert.IsInstanceOf<TomlTable>(mcpServersNode, "mcp_servers should be a table");

            var mcpServers = mcpServersNode as TomlTable;
            Assert.IsTrue(mcpServers.TryGetNode("unityMCP", out var unityMcpNode), "mcp_servers should contain unityMCP");
            Assert.IsInstanceOf<TomlTable>(unityMcpNode, "unityMCP should be a table");

            var unityMcp = unityMcpNode as TomlTable;
            Assert.IsTrue(unityMcp.TryGetNode("command", out _), "unityMCP should contain command");
            Assert.IsTrue(unityMcp.TryGetNode("args", out _), "unityMCP should contain args");

            // Verify env.SystemRoot is present on Windows
            bool hasEnv = unityMcp.TryGetNode("env", out var envNode);
            Assert.IsTrue(hasEnv, "Windows config should contain env table");
            Assert.IsInstanceOf<TomlTable>(envNode, "env should be a table");

            var env = envNode as TomlTable;
            Assert.IsTrue(env.TryGetNode("SystemRoot", out var systemRootNode), "env should contain SystemRoot");
            Assert.IsInstanceOf<TomlString>(systemRootNode, "SystemRoot should be a string");

            var systemRoot = (systemRootNode as TomlString).Value;
            Assert.AreEqual("C:\\Windows", systemRoot, "SystemRoot should be C:\\Windows");
        }

        [Test]
        public void UpsertCodexServerBlock_OnNonWindows_ExcludesEnv()
        {
            // This test verifies that upsert operations on non-Windows platforms don't include env configuration

            // Mock non-Windows platform (e.g., macOS/Linux)
            MCPServiceLocator.Register<IPlatformService>(new MockPlatformService(isWindows: false));

            string existingToml = string.Join("\n", new[]
            {
                "[other_section]",
                "key = \"value\""
            });

            string uvPath = "/usr/local/bin/uv";
            string serverSrc = "/path/to/server";

            string result = CodexConfigHelper.UpsertCodexServerBlock(existingToml, uvPath, serverSrc);

            Assert.IsNotNull(result, "UpsertCodexServerBlock should return a valid TOML string");

            // Parse the generated TOML to validate structure
            TomlTable parsed;
            using (var reader = new StringReader(result))
            {
                parsed = TOML.Parse(reader);
            }

            // Verify existing sections are preserved
            Assert.IsTrue(parsed.TryGetNode("other_section", out _), "TOML should preserve existing sections");

            // Verify mcp_servers structure
            Assert.IsTrue(parsed.TryGetNode("mcp_servers", out var mcpServersNode), "TOML should contain mcp_servers");
            Assert.IsInstanceOf<TomlTable>(mcpServersNode, "mcp_servers should be a table");

            var mcpServers = mcpServersNode as TomlTable;
            Assert.IsTrue(mcpServers.TryGetNode("unityMCP", out var unityMcpNode), "mcp_servers should contain unityMCP");
            Assert.IsInstanceOf<TomlTable>(unityMcpNode, "unityMCP should be a table");

            var unityMcp = unityMcpNode as TomlTable;
            Assert.IsTrue(unityMcp.TryGetNode("command", out _), "unityMCP should contain command");
            Assert.IsTrue(unityMcp.TryGetNode("args", out _), "unityMCP should contain args");

            // Verify env is NOT present on non-Windows platforms
            bool hasEnv = unityMcp.TryGetNode("env", out _);
            Assert.IsFalse(hasEnv, "Non-Windows config should not contain env table");
        }

        [Test]
        public void BuildCodexServerBlock_WithNullServerSrc_GeneratesRemoteUvxCommand()
        {
            // This test verifies that Asset Store installs (no embedded server) use remote uvx

            // Mock Windows platform
            MCPServiceLocator.Register<IPlatformService>(new MockPlatformService(isWindows: true, systemRoot: "C:\\Windows"));

            string result = CodexConfigHelper.BuildCodexServerBlock(null, null);

            Assert.IsNotNull(result, "BuildCodexServerBlock should return a valid TOML string for remote uvx");

            // Parse the generated TOML to validate structure
            TomlTable parsed;
            using (var reader = new StringReader(result))
            {
                parsed = TOML.Parse(reader);
            }

            // Verify basic structure
            Assert.IsTrue(parsed.TryGetNode("mcp_servers", out var mcpServersNode), "TOML should contain mcp_servers");
            var mcpServers = mcpServersNode as TomlTable;
            Assert.IsTrue(mcpServers.TryGetNode("unityMCP", out var unityMcpNode), "mcp_servers should contain unityMCP");
            var unityMcp = unityMcpNode as TomlTable;

            // Verify remote uvx command
            Assert.IsTrue(unityMcp.TryGetNode("command", out var commandNode), "unityMCP should contain command");
            var command = (commandNode as TomlString).Value;
            Assert.AreEqual("uvx", command, "Command should be uvx for remote execution");

            // Verify remote uvx args
            Assert.IsTrue(unityMcp.TryGetNode("args", out var argsNode), "unityMCP should contain args");
            var argsArray = argsNode as TomlArray;
            Assert.IsNotNull(argsArray, "args should be an array");
            Assert.AreEqual(3, argsArray.ChildrenCount, "Remote uvx should have 3 args: --from, url, package");

            var args = argsArray.Children.OfType<TomlString>().Select(s => s.Value).ToArray();
            Assert.AreEqual("--from", args[0], "First arg should be --from");
            Assert.IsTrue(args[1].StartsWith("git+https://github.com/CoplayDev/unity-mcp@v"), "Second arg should be git URL");
            Assert.IsTrue(args[1].Contains("#subdirectory=MCPForUnity/UnityMcpServer~/src"), "URL should include subdirectory");
            Assert.AreEqual("mcp-for-unity", args[2], "Third arg should be package name");

            // Verify env.SystemRoot is still present on Windows (even with remote uvx)
            bool hasEnv = unityMcp.TryGetNode("env", out var envNode);
            Assert.IsTrue(hasEnv, "Windows config should contain env table even with remote uvx");
            var env = envNode as TomlTable;
            Assert.IsTrue(env.TryGetNode("SystemRoot", out _), "env should contain SystemRoot");
        }

        [Test]
        public void UpsertCodexServerBlock_WithNullServerSrc_GeneratesRemoteUvxCommand()
        {
            // This test verifies that upsert operations for Asset Store installs use remote uvx

            // Mock non-Windows platform
            MCPServiceLocator.Register<IPlatformService>(new MockPlatformService(isWindows: false));

            string existingToml = string.Join("\n", new[]
            {
                "[other_section]",
                "key = \"value\""
            });

            string result = CodexConfigHelper.UpsertCodexServerBlock(existingToml, null, null);

            Assert.IsNotNull(result, "UpsertCodexServerBlock should return a valid TOML string for remote uvx");

            // Parse the generated TOML to validate structure
            TomlTable parsed;
            using (var reader = new StringReader(result))
            {
                parsed = TOML.Parse(reader);
            }

            // Verify existing sections are preserved
            Assert.IsTrue(parsed.TryGetNode("other_section", out _), "TOML should preserve existing sections");

            // Verify mcp_servers structure
            Assert.IsTrue(parsed.TryGetNode("mcp_servers", out var mcpServersNode), "TOML should contain mcp_servers");
            var mcpServers = mcpServersNode as TomlTable;
            Assert.IsTrue(mcpServers.TryGetNode("unityMCP", out var unityMcpNode), "mcp_servers should contain unityMCP");
            var unityMcp = unityMcpNode as TomlTable;

            // Verify remote uvx command
            Assert.IsTrue(unityMcp.TryGetNode("command", out var commandNode), "unityMCP should contain command");
            var command = (commandNode as TomlString).Value;
            Assert.AreEqual("uvx", command, "Command should be uvx for remote execution");

            // Verify remote uvx args
            Assert.IsTrue(unityMcp.TryGetNode("args", out var argsNode), "unityMCP should contain args");
            var argsArray = argsNode as TomlArray;
            Assert.IsNotNull(argsArray, "args should be an array");
            Assert.AreEqual(3, argsArray.ChildrenCount, "Remote uvx should have 3 args");

            var args = argsArray.Children.OfType<TomlString>().Select(s => s.Value).ToArray();
            Assert.AreEqual("--from", args[0], "First arg should be --from");
            Assert.IsTrue(args[1].StartsWith("git+https://github.com/CoplayDev/unity-mcp@v"), "Second arg should be git URL");
            Assert.AreEqual("mcp-for-unity", args[2], "Third arg should be package name");

            // Verify env is NOT present on non-Windows platforms
            bool hasEnv = unityMcp.TryGetNode("env", out _);
            Assert.IsFalse(hasEnv, "Non-Windows config should not contain env table");
        }
    }
}
