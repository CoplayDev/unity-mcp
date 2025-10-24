using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Models;

namespace MCPForUnityTests.Editor.Helpers
{
    public class WriteToConfigTests
    {
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
            EditorPrefs.SetString("MCPForUnity.ServerSrc", _serverSrcDir);
            // Ensure no lock is enabled
            EditorPrefs.SetBool("MCPForUnity.LockCursorConfig", false);
            // Disable auto-registration to avoid hitting user configs during tests
            EditorPrefs.SetBool("MCPForUnity.AutoRegisterEnabled", false);
        }

        [TearDown]
        public void TearDown()
        {
            // Clean up editor preferences set during SetUp
            EditorPrefs.DeleteKey("MCPForUnity.ServerSrc");
            EditorPrefs.DeleteKey("MCPForUnity.LockCursorConfig");
            EditorPrefs.DeleteKey("MCPForUnity.AutoRegisterEnabled");

            // Remove temp files
            try { if (Directory.Exists(_tempRoot)) Directory.Delete(_tempRoot, true); } catch { }
        }

        // --- Tests ---

        [Test]
        public void AddsEnvAndDisabledFalse_ForWindsurf()
        {
            var configPath = Path.Combine(_tempRoot, "windsurf.json");
            WriteInitialConfig(configPath, isVSCode: false, command: _fakeUvPath, directory: "/old/path");

            var client = new McpClient { name = "Windsurf", mcpType = McpTypes.Windsurf };
            InvokeWriteToConfig(configPath, client);

            var root = JObject.Parse(File.ReadAllText(configPath));
            var unity = (JObject)root.SelectToken("mcpServers.unityMCP");
            Assert.NotNull(unity, "Expected mcpServers.unityMCP node");
            Assert.NotNull(unity["env"], "env should be present for all clients");
            Assert.IsTrue(unity["env"]!.Type == JTokenType.Object, "env should be an object");
            Assert.AreEqual(false, (bool)unity["disabled"], "disabled:false should be set for Windsurf when missing");
        }

        [Test]
        public void AddsEnvAndDisabledFalse_ForKiro()
        {
            var configPath = Path.Combine(_tempRoot, "kiro.json");
            WriteInitialConfig(configPath, isVSCode: false, command: _fakeUvPath, directory: "/old/path");

            var client = new McpClient { name = "Kiro", mcpType = McpTypes.Kiro };
            InvokeWriteToConfig(configPath, client);

            var root = JObject.Parse(File.ReadAllText(configPath));
            var unity = (JObject)root.SelectToken("mcpServers.unityMCP");
            Assert.NotNull(unity, "Expected mcpServers.unityMCP node");
            Assert.NotNull(unity["env"], "env should be present for all clients");
            Assert.IsTrue(unity["env"]!.Type == JTokenType.Object, "env should be an object");
            Assert.AreEqual(false, (bool)unity["disabled"], "disabled:false should be set for Kiro when missing");
        }

        [Test]
        public void DoesNotAddEnvOrDisabled_ForCursor()
        {
            var configPath = Path.Combine(_tempRoot, "cursor.json");
            WriteInitialConfig(configPath, isVSCode: false, command: _fakeUvPath, directory: "/old/path");

            var client = new McpClient { name = "Cursor", mcpType = McpTypes.Cursor };
            InvokeWriteToConfig(configPath, client);

            var root = JObject.Parse(File.ReadAllText(configPath));
            var unity = (JObject)root.SelectToken("mcpServers.unityMCP");
            Assert.NotNull(unity, "Expected mcpServers.unityMCP node");
            Assert.IsNull(unity["env"], "env should not be added for non-Windsurf/Kiro clients");
            Assert.IsNull(unity["disabled"], "disabled should not be added for non-Windsurf/Kiro clients");
        }

        [Test]
        public void DoesNotAddEnvOrDisabled_ForVSCode()
        {
            var configPath = Path.Combine(_tempRoot, "vscode.json");
            WriteInitialConfig(configPath, isVSCode: true, command: _fakeUvPath, directory: "/old/path");

            var client = new McpClient { name = "VSCode", mcpType = McpTypes.VSCode };
            InvokeWriteToConfig(configPath, client);

            var root = JObject.Parse(File.ReadAllText(configPath));
            var unity = (JObject)root.SelectToken("servers.unityMCP");
            Assert.NotNull(unity, "Expected servers.unityMCP node");
            Assert.IsNull(unity["env"], "env should not be added for VSCode client");
            Assert.IsNull(unity["disabled"], "disabled should not be added for VSCode client");
            Assert.AreEqual("stdio", (string)unity["type"], "VSCode entry should include type=stdio");
        }

        [Test]
        public void DoesNotAddEnvOrDisabled_ForTrae()
        {
            var configPath = Path.Combine(_tempRoot, "trae.json");
            WriteInitialConfig(configPath, isVSCode: false, command: _fakeUvPath, directory: "/old/path");

            if (!Enum.TryParse<McpTypes>("Trae", out var traeValue))
            {
                Assert.Ignore("McpTypes.Trae not available in this package version; skipping test.");
            }

            var client = new McpClient { name = "Trae", mcpType = traeValue };
            InvokeWriteToConfig(configPath, client);

            var root = JObject.Parse(File.ReadAllText(configPath));
            var unity = (JObject)root.SelectToken("mcpServers.unityMCP");
            Assert.NotNull(unity, "Expected mcpServers.unityMCP node");
            Assert.IsNull(unity["env"], "env should not be added for Trae client");
            Assert.IsNull(unity["disabled"], "disabled should not be added for Trae client");
        }

        [Test]
        public void PreservesExistingEnvAndDisabled()
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

            var client = new McpClient { name = "Windsurf", mcpType = McpTypes.Windsurf };
            InvokeWriteToConfig(configPath, client);

            var root = JObject.Parse(File.ReadAllText(configPath));
            var unity = (JObject)root.SelectToken("mcpServers.unityMCP");
            Assert.NotNull(unity, "Expected mcpServers.unityMCP node");
            Assert.AreEqual("bar", (string)unity["env"]!["FOO"], "Existing env should be preserved");
            Assert.AreEqual(true, (bool)unity["disabled"], "Existing disabled value should be preserved");
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
            var result = McpConfigurationHelper.WriteMcpConfiguration(
                pythonDir: string.Empty,
                configPath: configPath,
                mcpClient: client
            );

            Assert.AreEqual("Configured successfully", result, "WriteMcpConfiguration should return success");
        }
    }
}
