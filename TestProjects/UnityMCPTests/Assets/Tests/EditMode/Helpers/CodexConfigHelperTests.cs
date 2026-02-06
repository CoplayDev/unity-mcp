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
        private bool _hadHttpTransport;
        private bool _originalHttpTransport;
        private bool _hadDevForceRefresh;
        private bool _originalDevForceRefresh;
        private bool _hadUseBetaServer;
        private bool _originalUseBetaServer;
        private IPlatformService _originalPlatformService;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            _hadGitOverride = EditorPrefs.HasKey(EditorPrefKeys.GitUrlOverride);
            _originalGitOverride = EditorPrefs.GetString(EditorPrefKeys.GitUrlOverride, string.Empty);
            _hadHttpTransport = EditorPrefs.HasKey(EditorPrefKeys.UseHttpTransport);
            _originalHttpTransport = EditorPrefs.GetBool(EditorPrefKeys.UseHttpTransport, true);
            _hadDevForceRefresh = EditorPrefs.HasKey(EditorPrefKeys.DevModeForceServerRefresh);
            _originalDevForceRefresh = EditorPrefs.GetBool(EditorPrefKeys.DevModeForceServerRefresh, false);
            _hadUseBetaServer = EditorPrefs.HasKey(EditorPrefKeys.UseBetaServer);
            _originalUseBetaServer = EditorPrefs.GetBool(EditorPrefKeys.UseBetaServer, true);
            _originalPlatformService = MCPServiceLocator.Platform;
        }

        [SetUp]
        public void SetUp()
        {
            // Ensure per-test deterministic Git URL (ignore developer overrides)
            EditorPrefs.DeleteKey(EditorPrefKeys.GitUrlOverride);
            // Default to stdio mode for existing tests unless specified otherwise
            EditorPrefs.SetBool(EditorPrefKeys.UseHttpTransport, false);
            // Ensure deterministic uvx args ordering for these tests regardless of editor settings
            // (dev-mode inserts --no-cache/--refresh, which changes the first args).
            EditorPrefs.SetBool(EditorPrefKeys.DevModeForceServerRefresh, false);
            // Tests expect beta server mode (--prerelease explicit --from mcpforunityserver>=0.0.0a0)
            EditorPrefs.SetBool(EditorPrefKeys.UseBetaServer, true);
            // Refresh the cache so it picks up the test's pref values
            EditorConfigurationCache.Instance.Refresh();
        }

        [TearDown]
        public void TearDown()
        {
            // IMPORTANT:
            // These tests can be executed while an MCP session is active (e.g., when running tests via MCP).
            // MCPServiceLocator.Reset() disposes the bridge + transport manager, which can kill the MCP connection
            // mid-run. Instead, restore only what this fixture mutates.
            // To avoid leaking global state to other tests/fixtures, restore the original platform service
            // instance captured before this fixture started running.
            if (_originalPlatformService != null)
            {
                MCPServiceLocator.Register<IPlatformService>(_originalPlatformService);
            }
            else
            {
                MCPServiceLocator.Register<IPlatformService>(new PlatformService());
            }
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

            if (_hadHttpTransport)
            {
                EditorPrefs.SetBool(EditorPrefKeys.UseHttpTransport, _originalHttpTransport);
            }
            else
            {
                EditorPrefs.DeleteKey(EditorPrefKeys.UseHttpTransport);
            }

            if (_hadDevForceRefresh)
            {
                EditorPrefs.SetBool(EditorPrefKeys.DevModeForceServerRefresh, _originalDevForceRefresh);
            }
            else
            {
                EditorPrefs.DeleteKey(EditorPrefKeys.DevModeForceServerRefresh);
            }

            if (_hadUseBetaServer)
            {
                EditorPrefs.SetBool(EditorPrefKeys.UseBetaServer, _originalUseBetaServer);
            }
            else
            {
                EditorPrefs.DeleteKey(EditorPrefKeys.UseBetaServer);
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
            // This test verifies that Windows-specific environment configuration is included in stdio mode

            // Force stdio mode
            EditorPrefs.SetBool(EditorPrefKeys.UseHttpTransport, false);

            // Mock Windows platform
            MCPServiceLocator.Register<IPlatformService>(new MockPlatformService(isWindows: true));

            string uvPath = "C:\\Program Files\\uv\\uv.exe";

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
            Assert.IsTrue(args.ChildrenCount >= 5, "Args should contain --prerelease, explicit, --from, PyPI package reference, and package name");

            var firstArg = (args[0] as TomlString).Value;
            var secondArg = (args[1] as TomlString).Value;
            var thirdArg = (args[2] as TomlString).Value;
            var fourthArg = (args[3] as TomlString).Value;
            var fifthArg = (args[4] as TomlString).Value;

            Assert.AreEqual("--prerelease", firstArg, "First arg should be --prerelease");
            Assert.AreEqual("explicit", secondArg, "Second arg should be explicit");
            Assert.AreEqual("--from", thirdArg, "Third arg should be --from");
            Assert.IsTrue(fourthArg.Contains("mcpforunityserver"), "Fourth arg should be PyPI package reference");
            Assert.AreEqual("mcp-for-unity", fifthArg, "Fifth arg should be mcp-for-unity");

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
            // This test verifies that non-Windows platforms don't include env configuration in stdio mode

            // Force stdio mode
            EditorPrefs.SetBool(EditorPrefKeys.UseHttpTransport, false);

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
            Assert.IsTrue(args.ChildrenCount >= 5, "Args should contain --prerelease, explicit, --from, PyPI package reference, and package name");

            var firstArg = (args[0] as TomlString).Value;
            var secondArg = (args[1] as TomlString).Value;
            var thirdArg = (args[2] as TomlString).Value;
            var fourthArg = (args[3] as TomlString).Value;
            var fifthArg = (args[4] as TomlString).Value;

            Assert.AreEqual("--prerelease", firstArg, "First arg should be --prerelease");
            Assert.AreEqual("explicit", secondArg, "Second arg should be explicit");
            Assert.AreEqual("--from", thirdArg, "Third arg should be --from");
            Assert.IsTrue(fourthArg.Contains("mcpforunityserver"), "Fourth arg should be PyPI package reference");
            Assert.AreEqual("mcp-for-unity", fifthArg, "Fifth arg should be mcp-for-unity");

            // Verify env is NOT present on non-Windows platforms
            bool hasEnv = unityMcp.TryGetNode("env", out _);
            Assert.IsFalse(hasEnv, "Non-Windows config should not contain env table");
        }

        [Test]
        public void UpsertCodexServerBlock_OnWindows_IncludesSystemRootEnv()
        {
            // This test verifies the fix for https://github.com/CoplayDev/unity-mcp/issues/315
            // Ensures that upsert operations also include Windows-specific env configuration in stdio mode

            // Force stdio mode
            EditorPrefs.SetBool(EditorPrefKeys.UseHttpTransport, false);

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
            Assert.IsTrue(args.ChildrenCount >= 5, "Args should contain --prerelease, explicit, --from, PyPI package reference, and package name");

            var firstArg = (args[0] as TomlString).Value;
            var secondArg = (args[1] as TomlString).Value;
            var thirdArg = (args[2] as TomlString).Value;
            var fourthArg = (args[3] as TomlString).Value;
            var fifthArg = (args[4] as TomlString).Value;

            Assert.AreEqual("--prerelease", firstArg, "First arg should be --prerelease");
            Assert.AreEqual("explicit", secondArg, "Second arg should be explicit");
            Assert.AreEqual("--from", thirdArg, "Third arg should be --from");
            Assert.IsTrue(fourthArg.Contains("mcpforunityserver"), "Fourth arg should be PyPI package reference");
            Assert.AreEqual("mcp-for-unity", fifthArg, "Fifth arg should be mcp-for-unity");

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
            // This test verifies that upsert operations on non-Windows platforms don't include env configuration in stdio mode

            // Force stdio mode
            EditorPrefs.SetBool(EditorPrefKeys.UseHttpTransport, false);

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
            Assert.IsTrue(args.ChildrenCount >= 5, "Args should contain --prerelease, explicit, --from, PyPI package reference, and package name");

            var firstArg = (args[0] as TomlString).Value;
            var secondArg = (args[1] as TomlString).Value;
            var thirdArg = (args[2] as TomlString).Value;
            var fourthArg = (args[3] as TomlString).Value;
            var fifthArg = (args[4] as TomlString).Value;

            Assert.AreEqual("--prerelease", firstArg, "First arg should be --prerelease");
            Assert.AreEqual("explicit", secondArg, "Second arg should be explicit");
            Assert.AreEqual("--from", thirdArg, "Third arg should be --from");
            Assert.IsTrue(fourthArg.Contains("mcpforunityserver"), "Fourth arg should be PyPI package reference");
            Assert.AreEqual("mcp-for-unity", fifthArg, "Fifth arg should be mcp-for-unity");

            // Verify env is NOT present on non-Windows platforms
            bool hasEnv = unityMcp.TryGetNode("env", out _);
            Assert.IsFalse(hasEnv, "Non-Windows config should not contain env table");
        }

        [Test]
        public void BuildCodexServerBlock_HttpMode_GeneratesUrlField()
        {
            // This test verifies HTTP transport mode generates url field instead of command/args

            // Force HTTP mode
            EditorPrefs.SetBool(EditorPrefKeys.UseHttpTransport, true);

            string uvPath = "C:\\Program Files\\uv\\uv.exe";

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

            // Verify features.rmcp_client is enabled for HTTP transport
            Assert.IsTrue(parsed.TryGetNode("features", out var featuresNode), "HTTP mode should include features table");
            Assert.IsInstanceOf<TomlTable>(featuresNode, "features should be a table");
            var features = featuresNode as TomlTable;
            Assert.IsTrue(features.TryGetNode("rmcp_client", out var rmcpNode), "features should include rmcp_client flag");
            Assert.IsInstanceOf<TomlBoolean>(rmcpNode, "rmcp_client should be a boolean");
            Assert.IsTrue((rmcpNode as TomlBoolean).Value, "rmcp_client should be true");
            
            // Verify url field is present
            Assert.IsTrue(unityMcp.TryGetNode("url", out var urlNode), "unityMCP should contain url in HTTP mode");
            Assert.IsInstanceOf<TomlString>(urlNode, "url should be a string");

            var url = (urlNode as TomlString).Value;
            Assert.IsTrue(url.Contains("http"), "URL should be an HTTP endpoint");
            Assert.IsTrue(url.Contains("/mcp"), "URL should contain /mcp path");

            // Verify command and args are NOT present in HTTP mode
            Assert.IsFalse(unityMcp.TryGetNode("command", out _), "HTTP mode should not contain command field");
            Assert.IsFalse(unityMcp.TryGetNode("args", out _), "HTTP mode should not contain args field");
            Assert.IsFalse(unityMcp.TryGetNode("env", out _), "HTTP mode should not contain env field");
        }

        [Test]
        public void TryParseCodexServer_HttpMode_ParsesUrlSuccessfully()
        {
            // This test verifies HTTP mode parsing with url field

            string toml = string.Join("\n", new[]
            {
                "[mcp_servers.unityMCP]",
                "url = \"http://localhost:8080/mcp/v1/rpc\""
            });

            bool result = CodexConfigHelper.TryParseCodexServer(toml, out string command, out string[] args, out string url);

            Assert.IsTrue(result, "Parser should accept HTTP mode with url field");
            Assert.IsNull(command, "Command should be null in HTTP mode");
            Assert.IsNull(args, "Args should be null in HTTP mode");
            Assert.AreEqual("http://localhost:8080/mcp/v1/rpc", url, "URL should be parsed correctly");
        }

        [Test]
        public void UpsertCodexServerBlock_HttpMode_GeneratesUrlField()
        {
            // This test verifies HTTP mode upsert generates url field

            // Force HTTP mode
            EditorPrefs.SetBool(EditorPrefKeys.UseHttpTransport, true);

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

            // Verify features.rmcp_client is enabled for HTTP transport
            Assert.IsTrue(parsed.TryGetNode("features", out var featuresNode), "HTTP mode should include features table");
            Assert.IsInstanceOf<TomlTable>(featuresNode, "features should be a table");
            var features = featuresNode as TomlTable;
            Assert.IsTrue(features.TryGetNode("rmcp_client", out var rmcpNode), "features should include rmcp_client flag");
            Assert.IsInstanceOf<TomlBoolean>(rmcpNode, "rmcp_client should be a boolean");
            Assert.IsTrue((rmcpNode as TomlBoolean).Value, "rmcp_client should be true");

            // Verify url field is present
            Assert.IsTrue(unityMcp.TryGetNode("url", out var urlNode), "unityMCP should contain url in HTTP mode");
            Assert.IsInstanceOf<TomlString>(urlNode, "url should be a string");

            var url = (urlNode as TomlString).Value;
            Assert.IsTrue(url.Contains("http"), "URL should be an HTTP endpoint");

            // Verify command and args are NOT present in HTTP mode
            Assert.IsFalse(unityMcp.TryGetNode("command", out _), "HTTP mode should not contain command field");
            Assert.IsFalse(unityMcp.TryGetNode("args", out _), "HTTP mode should not contain args field");
        }
    }
}
