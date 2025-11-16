using NUnit.Framework;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.External.Tommy;
using MCPForUnity.Editor.Services;
using System.IO;
using MCPForUnity.Editor.Constants;
using UnityEditor;

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

        private bool _hadGitOverride;
        private string _originalGitOverride;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            _hadGitOverride = EditorPrefs.HasKey(EditorPrefKeys.GitUrlOverride);
            _originalGitOverride = EditorPrefs.GetString(EditorPrefKeys.GitUrlOverride, string.Empty);
        }

        [SetUp]
        public void SetUp()
        {
            // Ensure per-test deterministic Git URL (ignore developer overrides)
            EditorPrefs.DeleteKey(EditorPrefKeys.GitUrlOverride);
        }

        [TearDown]
        public void TearDown()
        {
            // Reset service locator after each test
            MCPServiceLocator.Reset();
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            if (_hadGitOverride)
            {
                EditorPrefs.SetString(EditorPrefKeys.GitUrlOverride, _originalGitOverride);
            }
            else
            {
                EditorPrefs.DeleteKey(EditorPrefKeys.GitUrlOverride);
            }
        }

        [Test]
        public void TryParseCodexServer_SingleLineArgs_ParsesSuccessfully()
        {
            string toml = string.Join("\n", new[]
            {
                "[mcp_servers.unityMCP]",
                "command = \"uvx --from git+https://github.com/CoplayDev/unity-mcp@v6.3.0#subdirectory=Server\"",
                "args = [\"mcp-for-unity\"]"
            });

            bool result = CodexConfigHelper.TryParseCodexServer(toml, out string command, out string[] args);

            Assert.IsTrue(result, "Parser should detect server definition");
            Assert.AreEqual("uvx --from git+https://github.com/CoplayDev/unity-mcp@v6.3.0#subdirectory=Server", command);
            CollectionAssert.AreEqual(new[] { "mcp-for-unity" }, args);
        }

        [Test]
        public void TryParseCodexServer_MultiLineArgsWithTrailingComma_ParsesSuccessfully()
        {
            string toml = string.Join("\n", new[]
            {
                "[mcp_servers.unityMCP]",
                "command = \"uvx\"",
                "args = [",
                "  \"mcp-for-unity\",",
                "]"
            });

            bool result = CodexConfigHelper.TryParseCodexServer(toml, out string command, out string[] args);

            Assert.IsTrue(result, "Parser should handle multi-line arrays with trailing comma");
            Assert.AreEqual("uvx", command);
            CollectionAssert.AreEqual(new[] { "mcp-for-unity" }, args);
        }

        [Test]
        public void TryParseCodexServer_MultiLineArgsWithComments_IgnoresComments()
        {
            string toml = string.Join("\n", new[]
            {
                "[mcp_servers.unityMCP]",
                "command = \"uvx\"",
                "args = [",
                "  \"mcp-for-unity\", # package name",
                "]"
            });

            bool result = CodexConfigHelper.TryParseCodexServer(toml, out string command, out string[] args);

            Assert.IsTrue(result, "Parser should tolerate comments within the array block");
            Assert.AreEqual("uvx", command);
            CollectionAssert.AreEqual(new[] { "mcp-for-unity" }, args);
        }

        [Test]
        public void TryParseCodexServer_HeaderWithComment_StillDetected()
        {
            string toml = string.Join("\n", new[]
            {
                "[mcp_servers.unityMCP] # annotated header",
                "command = \"uvx\"",
                "args = [\"mcp-for-unity\"]"
            });

            bool result = CodexConfigHelper.TryParseCodexServer(toml, out string command, out string[] args);

            Assert.IsTrue(result, "Parser should recognize section headers even with inline comments");
            Assert.AreEqual("uvx", command);
            CollectionAssert.AreEqual(new[] { "mcp-for-unity" }, args);
        }

        [Test]
        public void TryParseCodexServer_SingleQuotedArgsWithApostrophes_ParsesSuccessfully()
        {
            string toml = string.Join("\n", new[]
            {
                "[mcp_servers.unityMCP]",
                "command = 'uvx'",
                "args = ['mcp-for-unity']"
            });

            bool result = CodexConfigHelper.TryParseCodexServer(toml, out string command, out string[] args);

            Assert.IsTrue(result, "Parser should accept single-quoted arrays with escaped apostrophes");
            Assert.AreEqual("uvx", command);
            CollectionAssert.AreEqual(new[] { "mcp-for-unity" }, args);
        }

        [Test]
        public void BuildCodexServerBlock_OnWindows_IncludesSystemRootEnv()
        {
            // This test verifies the fix for https://github.com/CoplayDev/unity-mcp/issues/315
            // Ensures Windows-specific env configuration is included

            // Mock Windows platform
            MCPServiceLocator.Register<IPlatformService>(new MockPlatformService(isWindows: true, systemRoot: "C:\\Windows"));

            string uvPath = "C:\\path\\to\\uv.exe";

            string result = CodexConfigHelper.BuildCodexServerBlock(uvPath);

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
            Assert.IsTrue(unityMcp.TryGetNode("command", out var commandNode), "unityMCP should contain command");
            Assert.IsTrue(unityMcp.TryGetNode("args", out var argsNode), "unityMCP should contain args");

            // Verify command contains uvx
            var command = (commandNode as TomlString).Value;
            Assert.IsTrue(command.Contains("uvx"), "Command should contain uvx");

            // Verify args contains the proper uvx command structure
            var args = argsNode as TomlArray;
            Assert.IsTrue(args.ChildrenCount >= 3, "Args should contain --from, git URL, and package name");
            
            var firstArg = (args[0] as TomlString).Value;
            var secondArg = (args[1] as TomlString).Value;
            var thirdArg = (args[2] as TomlString).Value;
            
            Assert.AreEqual("--from", firstArg, "First arg should be --from");
            Assert.IsTrue(secondArg.Contains("git+https://github.com/CoplayDev/unity-mcp"), "Second arg should be git URL");
            Assert.AreEqual("mcp-for-unity", thirdArg, "Third arg should be mcp-for-unity");

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

            string result = CodexConfigHelper.BuildCodexServerBlock(uvPath);

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
            Assert.IsTrue(unityMcp.TryGetNode("command", out var commandNode), "unityMCP should contain command");
            Assert.IsTrue(unityMcp.TryGetNode("args", out var argsNode), "unityMCP should contain args");

            // Verify command contains uvx
            var command = (commandNode as TomlString).Value;
            Assert.IsTrue(command.Contains("uvx"), "Command should contain uvx");

            // Verify args contains the proper uvx command structure
            var args = argsNode as TomlArray;
            Assert.IsTrue(args.ChildrenCount >= 3, "Args should contain --from, git URL, and package name");
            
            var firstArg = (args[0] as TomlString).Value;
            var secondArg = (args[1] as TomlString).Value;
            var thirdArg = (args[2] as TomlString).Value;
            
            Assert.AreEqual("--from", firstArg, "First arg should be --from");
            Assert.IsTrue(secondArg.Contains("git+https://github.com/CoplayDev/unity-mcp"), "Second arg should be git URL");
            Assert.AreEqual("mcp-for-unity", thirdArg, "Third arg should be mcp-for-unity");

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

            string result = CodexConfigHelper.UpsertCodexServerBlock(existingToml, uvPath);

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
            Assert.IsTrue(unityMcp.TryGetNode("command", out var commandNode), "unityMCP should contain command");
            Assert.IsTrue(unityMcp.TryGetNode("args", out var argsNode), "unityMCP should contain args");

            // Verify command contains uvx
            var command = (commandNode as TomlString).Value;
            Assert.IsTrue(command.Contains("uvx"), "Command should contain uvx");

            // Verify args contains the proper uvx command structure
            var args = argsNode as TomlArray;
            Assert.IsTrue(args.ChildrenCount >= 3, "Args should contain --from, git URL, and package name");
            
            var firstArg = (args[0] as TomlString).Value;
            var secondArg = (args[1] as TomlString).Value;
            var thirdArg = (args[2] as TomlString).Value;
            
            Assert.AreEqual("--from", firstArg, "First arg should be --from");
            Assert.IsTrue(secondArg.Contains("git+https://github.com/CoplayDev/unity-mcp"), "Second arg should be git URL");
            Assert.AreEqual("mcp-for-unity", thirdArg, "Third arg should be mcp-for-unity");

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

            string result = CodexConfigHelper.UpsertCodexServerBlock(existingToml, uvPath);

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
            Assert.IsTrue(unityMcp.TryGetNode("command", out var commandNode), "unityMCP should contain command");
            Assert.IsTrue(unityMcp.TryGetNode("args", out var argsNode), "unityMCP should contain args");

            // Verify command contains uvx
            var command = (commandNode as TomlString).Value;
            Assert.IsTrue(command.Contains("uvx"), "Command should contain uvx");

            // Verify args contains the proper uvx command structure
            var args = argsNode as TomlArray;
            Assert.IsTrue(args.ChildrenCount >= 3, "Args should contain --from, git URL, and package name");
            
            var firstArg = (args[0] as TomlString).Value;
            var secondArg = (args[1] as TomlString).Value;
            var thirdArg = (args[2] as TomlString).Value;
            
            Assert.AreEqual("--from", firstArg, "First arg should be --from");
            Assert.IsTrue(secondArg.Contains("git+https://github.com/CoplayDev/unity-mcp"), "Second arg should be git URL");
            Assert.AreEqual("mcp-for-unity", thirdArg, "Third arg should be mcp-for-unity");

            // Verify env is NOT present on non-Windows platforms
            bool hasEnv = unityMcp.TryGetNode("env", out _);
            Assert.IsFalse(hasEnv, "Non-Windows config should not contain env table");
        }
    }
}
