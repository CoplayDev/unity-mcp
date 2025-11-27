using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Models;
using MCPForUnity.Editor.Constants;

namespace MCPForUnityTests.Editor.Helpers
{
    public class WriteToConfigTests
    {
        private const string UseHttpTransportPrefKey = EditorPrefKeys.UseHttpTransport;
        private const string HttpUrlPrefKey = EditorPrefKeys.HttpBaseUrl;

        private string _tempRoot;
        private string _fakeUvPath;
        private string _serverSrcDir;

        [SetUp]
        public void SetUp()
        {
            // Tests are designed for Linux/macOS runners. Skip on Windows due to ProcessStartInfo
            // restrictions when UseShellExecute=false for .cmd/.bat scripts.
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Assert.Ignore("WriteToConfig tests are skipped on Windows (CI runs linux).\n" +
                              "ValidateUvBinarySafe requires launching an actual exe on Windows.");
            }
            _tempRoot = Path.Combine(Path.GetTempPath(), "UnityMCPTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempRoot);

            // Create a fake uv executable that prints a valid version string
            _fakeUvPath = Path.Combine(_tempRoot, RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "uv.cmd" : "uv");
            File.WriteAllText(_fakeUvPath, "#!/bin/sh\n\necho 'uv 9.9.9'\n");
            TryChmodX(_fakeUvPath);

            // Create a fake server directory with server.py
            _serverSrcDir = Path.Combine(_tempRoot, "server-src");
            Directory.CreateDirectory(_serverSrcDir);
            File.WriteAllText(Path.Combine(_serverSrcDir, "server.py"), "# dummy server\n");

            // Point the editor to our server dir (so ResolveServerSrc() uses this)
            EditorPrefs.SetString(EditorPrefKeys.ServerSrc, _serverSrcDir);
            // Ensure no lock is enabled
            EditorPrefs.SetBool(EditorPrefKeys.LockCursorConfig, false);
            // Disable auto-registration to avoid hitting user configs during tests
            EditorPrefs.SetBool(EditorPrefKeys.AutoRegisterEnabled, false);
            // Force HTTP transport defaults so expectations match current behavior
            EditorPrefs.SetBool(UseHttpTransportPrefKey, true);
            EditorPrefs.SetString(HttpUrlPrefKey, "http://localhost:8080");
        }

        [TearDown]
        public void TearDown()
        {
            // Clean up editor preferences set during SetUp
            EditorPrefs.DeleteKey(EditorPrefKeys.ServerSrc);
            EditorPrefs.DeleteKey(EditorPrefKeys.LockCursorConfig);
            EditorPrefs.DeleteKey(EditorPrefKeys.AutoRegisterEnabled);
            EditorPrefs.DeleteKey(UseHttpTransportPrefKey);
            EditorPrefs.DeleteKey(HttpUrlPrefKey);

            // Remove temp files
            try { if (Directory.Exists(_tempRoot)) Directory.Delete(_tempRoot, true); } catch { }
        }

        // --- Tests ---

        [Test]
        public void AddsDisabledFalseAndServerUrl_ForWindsurf()
        {
            var configPath = Path.Combine(_tempRoot, "windsurf.json");
            WriteInitialConfig(configPath, isVSCode: false, command: _fakeUvPath, directory: "/old/path");

            var client = new McpClient
            {
                name = "Windsurf",
                HttpUrlProperty = "serverUrl",
                DefaultUnityFields = { { "disabled", false } },
                StripEnvWhenNotRequired = true
            };
            InvokeWriteToConfig(configPath, client);

            var root = JObject.Parse(File.ReadAllText(configPath));
            var unity = (JObject)root.SelectToken("mcpServers.unityMCP");
            Assert.NotNull(unity, "Expected mcpServers.unityMCP node");
            Assert.IsNull(unity["env"], "Windsurf configs should not include an env block");
            Assert.AreEqual(false, (bool)unity["disabled"], "disabled:false should be set for Windsurf when missing");
            AssertTransportConfiguration(unity, client);
        }

        [Test]
        public void AddsEnvAndDisabledFalse_ForKiro()
        {
            var configPath = Path.Combine(_tempRoot, "kiro.json");
            WriteInitialConfig(configPath, isVSCode: false, command: _fakeUvPath, directory: "/old/path");

            var client = new McpClient
            {
                name = "Kiro",
                EnsureEnvObject = true,
                DefaultUnityFields = { { "disabled", false } }
            };
            InvokeWriteToConfig(configPath, client);

            var root = JObject.Parse(File.ReadAllText(configPath));
            var unity = (JObject)root.SelectToken("mcpServers.unityMCP");
            Assert.NotNull(unity, "Expected mcpServers.unityMCP node");
            Assert.NotNull(unity["env"], "env should be present for all clients");
            Assert.IsTrue(unity["env"]!.Type == JTokenType.Object, "env should be an object");
            Assert.AreEqual(false, (bool)unity["disabled"], "disabled:false should be set for Kiro when missing");
            AssertTransportConfiguration(unity, client);
        }

        [Test]
        public void DoesNotAddEnvOrDisabled_ForCursor()
        {
            var configPath = Path.Combine(_tempRoot, "cursor.json");
            WriteInitialConfig(configPath, isVSCode: false, command: _fakeUvPath, directory: "/old/path");

            var client = new McpClient { name = "Cursor" };
            InvokeWriteToConfig(configPath, client);

            var root = JObject.Parse(File.ReadAllText(configPath));
            var unity = (JObject)root.SelectToken("mcpServers.unityMCP");
            Assert.NotNull(unity, "Expected mcpServers.unityMCP node");
            Assert.IsNull(unity["env"], "env should not be added for non-Windsurf/Kiro clients");
            Assert.IsNull(unity["disabled"], "disabled should not be added for non-Windsurf/Kiro clients");
            AssertTransportConfiguration(unity, client);
        }

        [Test]
        public void DoesNotAddEnvOrDisabled_ForVSCode()
        {
            var configPath = Path.Combine(_tempRoot, "vscode.json");
            WriteInitialConfig(configPath, isVSCode: true, command: _fakeUvPath, directory: "/old/path");

            var client = new McpClient { name = "VSCode", IsVsCodeLayout = true };
            InvokeWriteToConfig(configPath, client);

            var root = JObject.Parse(File.ReadAllText(configPath));
            var unity = (JObject)root.SelectToken("servers.unityMCP");
            Assert.NotNull(unity, "Expected servers.unityMCP node");
            Assert.IsNull(unity["env"], "env should not be added for VSCode client");
            Assert.IsNull(unity["disabled"], "disabled should not be added for VSCode client");
            AssertTransportConfiguration(unity, client);
        }

        [Test]
        public void DoesNotAddEnvOrDisabled_ForTrae()
        {
            var configPath = Path.Combine(_tempRoot, "trae.json");
            WriteInitialConfig(configPath, isVSCode: false, command: _fakeUvPath, directory: "/old/path");

            var client = new McpClient { name = "Trae" };
            InvokeWriteToConfig(configPath, client);

            var root = JObject.Parse(File.ReadAllText(configPath));
            var unity = (JObject)root.SelectToken("mcpServers.unityMCP");
            Assert.NotNull(unity, "Expected mcpServers.unityMCP node");
            Assert.IsNull(unity["env"], "env should not be added for Trae client");
            Assert.IsNull(unity["disabled"], "disabled should not be added for Trae client");
            AssertTransportConfiguration(unity, client);
        }

        [Test]
        public void PreservesExistingEnvAndDisabled_ForKiro()
        {
            var configPath = Path.Combine(_tempRoot, "preserve.json");

            // Existing config with env and disabled=true should be preserved
            var json = new JObject
            {
                ["mcpServers"] = new JObject
                {
                    ["unityMCP"] = new JObject
                    {
                        ["command"] = _fakeUvPath,
                        ["args"] = new JArray("run", "--directory", "/old/path", "server.py"),
                        ["env"] = new JObject { ["FOO"] = "bar" },
                        ["disabled"] = true
                    }
                }
            };
            File.WriteAllText(configPath, json.ToString());

            var client = new McpClient
            {
                name = "Kiro",
                EnsureEnvObject = true,
                DefaultUnityFields = { { "disabled", false } }
            };
            InvokeWriteToConfig(configPath, client);

            var root = JObject.Parse(File.ReadAllText(configPath));
            var unity = (JObject)root.SelectToken("mcpServers.unityMCP");
            Assert.NotNull(unity, "Expected mcpServers.unityMCP node");
            Assert.AreEqual("bar", (string)unity["env"]!["FOO"], "Existing env should be preserved");
            Assert.AreEqual(true, (bool)unity["disabled"], "Existing disabled value should be preserved");
            AssertTransportConfiguration(unity, client);
        }

        [Test]
        public void RemovesEnvBlock_ForWindsurf()
        {
            var configPath = Path.Combine(_tempRoot, "windsurf-env.json");

            var json = new JObject
            {
                ["mcpServers"] = new JObject
                {
                    ["unityMCP"] = new JObject
                    {
                        ["command"] = _fakeUvPath,
                        ["args"] = new JArray("run", "--directory", "/old/path", "server.py"),
                        ["env"] = new JObject { ["SHOULD"] = "be removed" },
                        ["disabled"] = true
                    }
                }
            };
            File.WriteAllText(configPath, json.ToString());

            var client = new McpClient
            {
                name = "Windsurf",
                HttpUrlProperty = "serverUrl",
                DefaultUnityFields = { { "disabled", false } },
                StripEnvWhenNotRequired = true
            };
            InvokeWriteToConfig(configPath, client);

            var root = JObject.Parse(File.ReadAllText(configPath));
            var unity = (JObject)root.SelectToken("mcpServers.unityMCP");
            Assert.NotNull(unity, "Expected mcpServers.unityMCP node");
            Assert.IsNull(unity["env"], "Windsurf config should strip any existing env block");
            Assert.AreEqual(true, (bool)unity["disabled"], "Existing disabled value should be preserved");
            AssertTransportConfiguration(unity, client);
        }

        [Test]
        public void UsesStdioTransport_ForNonVSCodeClients_WhenPreferenceDisabled()
        {
            var configPath = Path.Combine(_tempRoot, "stdio-non-vscode.json");
            WriteInitialConfig(configPath, isVSCode: false, command: _fakeUvPath, directory: "/old/path");

            WithTransportPreference(false, () =>
            {
                var client = new McpClient
                {
                    name = "Windsurf",
                    HttpUrlProperty = "serverUrl",
                    DefaultUnityFields = { { "disabled", false } },
                    StripEnvWhenNotRequired = true
                };
                InvokeWriteToConfig(configPath, client);

                var root = JObject.Parse(File.ReadAllText(configPath));
                var unity = (JObject)root.SelectToken("mcpServers.unityMCP");
                Assert.NotNull(unity, "Expected mcpServers.unityMCP node");
                AssertTransportConfiguration(unity, client);
            });
        }

        [Test]
        public void UsesStdioTransport_ForVSCode_WhenPreferenceDisabled()
        {
            var configPath = Path.Combine(_tempRoot, "stdio-vscode.json");
            WriteInitialConfig(configPath, isVSCode: true, command: _fakeUvPath, directory: "/old/path");

            WithTransportPreference(false, () =>
            {
                var client = new McpClient { name = "VSCode", IsVsCodeLayout = true };
                InvokeWriteToConfig(configPath, client);

                var root = JObject.Parse(File.ReadAllText(configPath));
                var unity = (JObject)root.SelectToken("servers.unityMCP");
                Assert.NotNull(unity, "Expected servers.unityMCP node");
                AssertTransportConfiguration(unity, client);
            });
        }

        // --- Helpers ---

        private static void TryChmodX(string path)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "/bin/chmod",
                    Arguments = "+x \"" + path + "\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                using var p = Process.Start(psi);
                p?.WaitForExit(2000);
            }
            catch { /* best-effort on non-Unix */ }
        }

        private static void WriteInitialConfig(string configPath, bool isVSCode, string command, string directory)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
            JObject root;
            if (isVSCode)
            {
                root = new JObject
                {
                    ["servers"] = new JObject
                    {
                        ["unityMCP"] = new JObject
                        {
                            ["command"] = command,
                            ["args"] = new JArray("run", "--directory", directory, "server.py"),
                            ["type"] = "stdio"
                        }
                    }
                };
            }
            else
            {
                root = new JObject
                {
                    ["mcpServers"] = new JObject
                    {
                        ["unityMCP"] = new JObject
                        {
                            ["command"] = command,
                            ["args"] = new JArray("run", "--directory", directory, "server.py")
                        }
                    }
                };
            }
            File.WriteAllText(configPath, root.ToString());
        }

        private static void InvokeWriteToConfig(string configPath, McpClient client)
        {
            var result = McpConfigurationHelper.WriteMcpConfiguration(configPath, client);

            Assert.AreEqual("Configured successfully", result, "WriteMcpConfiguration should return success");
        }

        private static void AssertTransportConfiguration(JObject unity, McpClient client)
        {
            bool useHttp = EditorPrefs.GetBool(UseHttpTransportPrefKey, true);
            bool isVSCode = client.IsVsCodeLayout;
            bool isWindsurf = string.Equals(client.HttpUrlProperty, "serverUrl", StringComparison.OrdinalIgnoreCase);

            if (useHttp)
            {
                string expectedUrl = HttpEndpointUtility.GetMcpRpcUrl();
                if (isWindsurf)
                {
                    Assert.AreEqual(expectedUrl, (string)unity["serverUrl"],
                        "Windsurf should advertise HTTP using serverUrl");
                    Assert.IsNull(unity["url"], "Windsurf configs should not use the url property");
                }
                else
                {
                    Assert.AreEqual(expectedUrl, (string)unity["url"],
                        "HTTP transport should set url to the MCP endpoint");
                    Assert.IsNull(unity["serverUrl"], "serverUrl should be reserved for Windsurf");
                }
                Assert.IsNull(unity["command"], "HTTP transport should remove command");
                Assert.IsNull(unity["args"], "HTTP transport should remove args");

                if (isVSCode)
                {
                    Assert.AreEqual("http", (string)unity["type"],
                        "VSCode entries should advertise HTTP transport");
                }
                else
                {
                    Assert.IsNull(unity["type"],
                        "Non-VSCode entries should not include type metadata in HTTP mode");
                }
            }
            else
            {
                Assert.IsNull(unity["url"], "stdio transport should not include a url");
                Assert.IsNull(unity["serverUrl"], "stdio transport should not include a serverUrl");

                string command = (string)unity["command"];
                Assert.False(string.IsNullOrEmpty(command), "stdio transport should include a command");

                var args = (unity["args"] as JArray)?.ToObject<string[]>();
                Assert.NotNull(args, "stdio transport should include args array");

                int transportIndex = Array.IndexOf(args, "--transport");
                Assert.GreaterOrEqual(transportIndex, 0, "args should include --transport flag");
                Assert.Less(transportIndex + 1, args.Length,
                    "--transport flag should be followed by a mode value");
                Assert.AreEqual("stdio", args[transportIndex + 1],
                    "--transport should be followed by stdio mode");

                if (isVSCode)
                {
                    Assert.AreEqual("stdio", (string)unity["type"],
                        "VSCode entries should advertise stdio transport");
                }
                else
                {
                    Assert.IsNull(unity["type"],
                        "Non-VSCode entries should not include type metadata in stdio mode");
                }
            }
        }

        private static void WithTransportPreference(bool useHttp, Action action)
        {
            bool original = EditorPrefs.GetBool(UseHttpTransportPrefKey, true);
            EditorPrefs.SetBool(UseHttpTransportPrefKey, useHttp);
            try
            {
                action();
            }
            finally
            {
                EditorPrefs.SetBool(UseHttpTransportPrefKey, original);
            }
        }
    }
}
