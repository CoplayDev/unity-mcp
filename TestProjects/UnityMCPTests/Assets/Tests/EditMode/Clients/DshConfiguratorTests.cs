using System;
using System.Collections.Generic;
using System.IO;
using MCPForUnity.Editor.Clients.Configurators;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Models;
using MCPForUnity.Editor.Services;
using NUnit.Framework;

namespace MCPForUnityTests.Editor.Clients
{
    /// <summary>
    /// Covers the DeepSeek Harness integration: the marker-fenced YAML patch block helper
    /// and the DeepSeekHarnessConfigurator that writes a configuration-only DSH bundle
    /// and installs it via the DSH plugin manager (<c>dsh plugin add</c>).
    /// </summary>
    [TestFixture]
    public class DshConfiguratorTests
    {
        private string _tempDir;
        private Func<string[], (int ExitCode, string Output)> _originalRunner;

        [SetUp]
        public void SetUp()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "UnityMCPTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
            _originalRunner = DeepSeekHarnessConfigurator.DshCommandRunner;
        }

        [TearDown]
        public void TearDown()
        {
            DeepSeekHarnessConfigurator.DshCommandRunner = _originalRunner;
            try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); } catch { }
        }

        // ---------------------------------------------------------------- helper: build

        [Test]
        public void BuildBlock_Http_EmitsExpectedRows()
        {
            string block = DshConfigHelper.BuildUnityMcpBlock(
                useHttp: true, httpUrl: "http://127.0.0.1:8090/mcp",
                uvxCommand: null, uvxArgs: null,
                apiKeyHeader: null, apiKey: null, windowsSystemRoot: null);

            StringAssert.Contains(DshConfigHelper.BeginMarker, block);
            StringAssert.Contains(DshConfigHelper.EndMarker, block);
            StringAssert.Contains("- insert:", block);
            StringAssert.Contains($"- id: {DshConfigHelper.ServerId}", block);
            StringAssert.Contains($"name: '{DshConfigHelper.PluginName}'", block);
            StringAssert.Contains($"serverName: {DshConfigHelper.ServerName}", block);
            StringAssert.Contains("transport: streamable-http", block);
            StringAssert.Contains("url: 'http://127.0.0.1:8090/mcp'", block);
            StringAssert.DoesNotContain("command:", block);
        }

        [Test]
        public void BuildBlock_Http_WithApiKey_EmitsHeader()
        {
            string block = DshConfigHelper.BuildUnityMcpBlock(
                useHttp: true, httpUrl: "http://127.0.0.1:8090/mcp",
                uvxCommand: null, uvxArgs: null,
                apiKeyHeader: "X-API-Key", apiKey: "s3cret", windowsSystemRoot: null);

            StringAssert.Contains("headers:", block);
            StringAssert.Contains("X-API-Key: 's3cret'", block);
        }

        [Test]
        public void BuildBlock_Stdio_EmitsCommandArgsAndWindowsEnv()
        {
            string[] args = { "--from", "mcp-for-unity", "mcp-for-unity", "--transport", "stdio" };
            string block = DshConfigHelper.BuildUnityMcpBlock(
                useHttp: false, httpUrl: null,
                uvxCommand: @"C:\tools\uvx.exe", uvxArgs: args,
                apiKeyHeader: null, apiKey: null,
                windowsSystemRoot: @"C:\Windows");

            StringAssert.Contains("transport: stdio", block);
            StringAssert.Contains(@"command: 'C:\tools\uvx.exe'", block);
            StringAssert.Contains("- '--transport'", block);
            StringAssert.Contains("- 'stdio'", block);
            StringAssert.Contains(@"SystemRoot: 'C:\Windows'", block);
            StringAssert.DoesNotContain("url:", block);
        }

        // ---------------------------------------------------------------- helper: parse

        [Test]
        public void TryParse_RoundTripsHttpBlock()
        {
            string block = DshConfigHelper.BuildUnityMcpBlock(
                useHttp: true, httpUrl: "http://127.0.0.1:8090/mcp",
                uvxCommand: null, uvxArgs: null,
                apiKeyHeader: "X-API-Key", apiKey: "it's-a-key", windowsSystemRoot: null);

            bool parsed = DshConfigHelper.TryParseManagedBlock(
                block, out string transport, out string url, out string command, out string[] args,
                out string headerName, out string headerValue);

            Assert.IsTrue(parsed);
            Assert.AreEqual("streamable-http", transport);
            Assert.AreEqual("http://127.0.0.1:8090/mcp", url);
            Assert.IsNull(command);
            Assert.IsTrue(args == null || args.Length == 0, "http block must have no args");
            Assert.AreEqual("X-API-Key", headerName);
            Assert.AreEqual("it's-a-key", headerValue, "doubled single quotes must unescape");
        }

        [Test]
        public void TryParse_RoundTripsStdioBlock_WithWindowsPaths()
        {
            string[] args = { "--from", @"E:\Dev\unity-mcp\Server", "mcp-for-unity", "--transport", "stdio" };
            string block = DshConfigHelper.BuildUnityMcpBlock(
                useHttp: false, httpUrl: null,
                uvxCommand: @"C:\Users\test user\.local\bin\uvx.exe", uvxArgs: args,
                apiKeyHeader: null, apiKey: null,
                windowsSystemRoot: @"C:\Windows");

            bool parsed = DshConfigHelper.TryParseManagedBlock(
                block, out string transport, out string url, out string command, out string[] parsedArgs,
                out string headerName, out string headerValue);

            Assert.IsTrue(parsed);
            Assert.AreEqual("stdio", transport);
            Assert.IsNull(url);
            Assert.AreEqual(@"C:\Users\test user\.local\bin\uvx.exe", command);
            Assert.AreEqual(args, parsedArgs);
            Assert.IsNull(headerName);
            Assert.IsNull(headerValue, "env rows must not be mistaken for headers");
        }

        [Test]
        public void TryParse_RejectsEmptyBlock()
        {
            bool parsed = DshConfigHelper.TryParseManagedBlock(
                "", out _, out _, out _, out _, out _, out _);
            Assert.IsFalse(parsed);
        }

        // ---------------------------------------------------------------- helper: upsert/remove

        [Test]
        public void Upsert_IntoEmptyFile_ProducesValidListDocument()
        {
            string block = BuildHttpBlock("http://127.0.0.1:8090/mcp");
            string result = DshConfigHelper.UpsertManagedBlock(null, block);

            Assert.IsTrue(result.StartsWith(DshConfigHelper.BeginMarker), "file must remain a top-level YAML list");
            StringAssert.EndsWith("\n", result);
        }

        [Test]
        public void Upsert_Twice_IsIdempotent()
        {
            string block = BuildHttpBlock("http://127.0.0.1:8090/mcp");
            string once = DshConfigHelper.UpsertManagedBlock(null, block);
            string twice = DshConfigHelper.UpsertManagedBlock(once, BuildHttpBlock("http://127.0.0.1:9090/mcp"));

            Assert.AreEqual(1, CountOccurrences(twice, DshConfigHelper.BeginMarker),
                "re-configuring must replace the block, not append a second one");
            StringAssert.Contains("9090", twice);
            StringAssert.DoesNotContain("8090", twice);
        }

        [Test]
        public void Upsert_PreservesForeignUserPatches_ByteForByte()
        {
            string userPatches =
                "- insert:\n" +
                "    - id: mcp-memory\n" +
                "      name: '@deepseek-ai/dsh-mcp-client'\n" +
                "      config:\n" +
                "        serverName: memory\n" +
                "        transport: stdio\n" +
                "        command: my-memory-mcp\n" +
                "        env:\n" +
                "          MEMORY_TOKEN: !!js process.env.MEMORY_TOKEN\n" +
                "\n";

            string block = BuildHttpBlock("http://127.0.0.1:8090/mcp");
            string result = DshConfigHelper.UpsertManagedBlock(userPatches, block);

            StringAssert.StartsWith(userPatches, result, "user patch content must be preserved verbatim");
            Assert.AreEqual(1, CountOccurrences(result, DshConfigHelper.BeginMarker));
        }

        [Test]
        public void Upsert_AppendsWithoutTrailingNewlineInput()
        {
            string existing = "- insert:\n    - id: x\n      name: pkg-x";
            string result = DshConfigHelper.UpsertManagedBlock(existing, BuildHttpBlock("http://127.0.0.1:8090/mcp"));

            StringAssert.StartsWith(existing + "\n", result);
        }

        [Test]
        public void Remove_DeletesOnlyManagedBlock()
        {
            string userPatches = "- insert:\n    - id: mcp-memory\n      name: '@deepseek-ai/dsh-mcp-client'\n";
            string file = userPatches + BuildHttpBlock("http://127.0.0.1:8090/mcp");

            string result = DshConfigHelper.RemoveManagedBlock(file);

            Assert.AreEqual(userPatches, result, "only the managed block may be removed");
        }

        [Test]
        public void Remove_WithoutBlock_ReturnsTextUnchanged()
        {
            string text = "- insert: []\n";
            Assert.AreEqual(text, DshConfigHelper.RemoveManagedBlock(text));
        }

        [Test]
        public void Extract_ReturnsNullWithoutMarkers_AndRowsWithMarkers()
        {
            Assert.IsNull(DshConfigHelper.ExtractManagedBlock("- insert: []\n"));

            string block = BuildHttpBlock("http://127.0.0.1:8090/mcp");
            string inner = DshConfigHelper.ExtractManagedBlock(block);
            StringAssert.Contains("transport: streamable-http", inner);
            StringAssert.DoesNotContain(DshConfigHelper.BeginMarker, inner);
        }

        // ---------------------------------------------------------------- configurator wiring

        [Test]
        public void ConfigPath_UsesBundlePatchInsideOverrideHome()
        {
            var configurator = new DeepSeekHarnessConfigurator(_tempDir);
            string expected = Path.Combine(_tempDir, "bundles", "unity-mcp", "cordis.patch.yml");
            Assert.AreEqual(expected, configurator.GetConfigPath());
        }

        [Test]
        public void ResolveDshHome_PrefersOverride_ThenEnv_ThenDotDsh()
        {
            Assert.AreEqual(@"E:\custom\home", DeepSeekHarnessConfigurator.ResolveDshHome(@"E:\custom\home"));

            string previous = Environment.GetEnvironmentVariable("DSH_HOME");
            try
            {
                Environment.SetEnvironmentVariable("DSH_HOME", @"E:\env\home");
                Assert.AreEqual(@"E:\env\home", DeepSeekHarnessConfigurator.ResolveDshHome(null));

                Environment.SetEnvironmentVariable("DSH_HOME", null);
                string expectedHome = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dsh");
                Assert.AreEqual(expectedHome, DeepSeekHarnessConfigurator.ResolveDshHome(null));
            }
            finally
            {
                Environment.SetEnvironmentVariable("DSH_HOME", previous);
            }
        }

        [Test]
        public void ResolveDshProfile_PrefersOverride_ThenEnv_ThenDesktop()
        {
            Assert.AreEqual("my-profile", DeepSeekHarnessConfigurator.ResolveDshProfile("my-profile"));

            string previous = Environment.GetEnvironmentVariable("DSH_PROFILE");
            try
            {
                Environment.SetEnvironmentVariable("DSH_PROFILE", "web");
                Assert.AreEqual("web", DeepSeekHarnessConfigurator.ResolveDshProfile(null));

                Environment.SetEnvironmentVariable("DSH_PROFILE", null);
                Assert.AreEqual("desktop", DeepSeekHarnessConfigurator.ResolveDshProfile(null));
            }
            finally
            {
                Environment.SetEnvironmentVariable("DSH_PROFILE", previous);
            }
        }

        [Test]
        public void IsInstalled_FollowsParentDirectory()
        {
            string missing = Path.Combine(_tempDir, "does-not-exist");
            Assert.IsFalse(new DeepSeekHarnessConfigurator(missing).IsInstalled);
            Assert.IsTrue(new DeepSeekHarnessConfigurator(_tempDir).IsInstalled);
        }

        [Test]
        public void CheckStatus_MissingBundle_NotConfigured_NoAutoWrite()
        {
            bool seamCalled = false;
            DeepSeekHarnessConfigurator.DshCommandRunner = args =>
            {
                seamCalled = true;
                return (0, "");
            };

            var configurator = new DeepSeekHarnessConfigurator(_tempDir);
            var status = configurator.CheckStatus(attemptAutoRewrite: true);

            Assert.AreEqual(McpStatus.NotConfigured, status);
            Assert.IsFalse(seamCalled, "CheckStatus must never invoke the dsh command seam");
            string bundleDir = Path.Combine(_tempDir, "bundles", "unity-mcp");
            Assert.IsFalse(Directory.Exists(bundleDir), "CheckStatus must never write bundle files");
        }

        [Test]
        public void CheckStatus_HttpMatch_Configured()
        {
            WithHttpTransport(true, () =>
            {
                string expectedUrl = HttpEndpointUtility.GetMcpRpcUrl();
                WriteBundleFiles(_tempDir, BuildHttpBlock(expectedUrl));
                WriteProfileBundleEntry(_tempDir, "desktop");

                var configurator = new DeepSeekHarnessConfigurator(_tempDir);
                var status = configurator.CheckStatus(attemptAutoRewrite: false);

                Assert.AreEqual(McpStatus.Configured, status);
                Assert.AreNotEqual(ConfiguredTransport.Unknown, configurator.ConfiguredTransport);
            });
        }

        [Test]
        public void CheckStatus_HttpMismatch_ReturnsIncorrectPath_WithoutRewrite()
        {
            WithHttpTransport(true, () =>
            {
                string bundlePatch = Path.Combine(_tempDir, "bundles", "unity-mcp", "cordis.patch.yml");
                WriteBundleFiles(_tempDir, BuildHttpBlock("http://127.0.0.1:1/mcp"));
                WriteProfileBundleEntry(_tempDir, "desktop");

                var configurator = new DeepSeekHarnessConfigurator(_tempDir);
                var before = configurator.CheckStatus(attemptAutoRewrite: false);
                Assert.AreEqual(McpStatus.IncorrectPath, before);

                var after = configurator.CheckStatus(attemptAutoRewrite: true);
                Assert.AreEqual(McpStatus.IncorrectPath, after, "must not auto-rewrite");

                string updated = File.ReadAllText(bundlePatch);
                StringAssert.Contains("127.0.0.1:1", updated, "bundle must not have been rewritten");
            });
        }

        [Test]
        public void Configure_WritesBundleAndInvokesSeam()
        {
            WithHttpTransport(true, () =>
            {
                string capturedArgs = null;
                DeepSeekHarnessConfigurator.DshCommandRunner = args =>
                {
                    capturedArgs = string.Join(" ", args);
                    return (0, "ok");
                };

                var configurator = new DeepSeekHarnessConfigurator(_tempDir);
                configurator.Configure();

                string bundleDir = Path.Combine(_tempDir, "bundles", "unity-mcp");
                Assert.IsTrue(File.Exists(Path.Combine(bundleDir, "package.json")), "manifest must exist");
                Assert.IsTrue(File.Exists(Path.Combine(bundleDir, "cordis.patch.yml")), "patch must exist");

                StringAssert.Contains("plugin", capturedArgs);
                StringAssert.Contains("--profile", capturedArgs);
                StringAssert.Contains("desktop", capturedArgs);
                StringAssert.Contains("add", capturedArgs);
                StringAssert.Contains(bundleDir, capturedArgs);
                Assert.AreEqual(McpStatus.Configured, configurator.Status);
            });
        }

        [Test]
        public void Configure_MigrationRemovesLegacyRootPatchBlock()
        {
            WithHttpTransport(true, () =>
            {
                string rootPatch = Path.Combine(_tempDir, "cordis.patch.yml");
                string userRow = "- insert:\n    - id: mcp-memory\n      name: '@deepseek-ai/dsh-mcp-client'\n";
                File.WriteAllText(rootPatch, userRow + BuildHttpBlock("http://127.0.0.1:1/mcp"));

                DeepSeekHarnessConfigurator.DshCommandRunner = _ => (0, "ok");

                var configurator = new DeepSeekHarnessConfigurator(_tempDir);
                configurator.Configure();

                string after = File.ReadAllText(rootPatch);
                Assert.AreEqual(userRow, after, "legacy managed block must be removed from root patch");
                Assert.AreEqual(0, CountOccurrences(after, DshConfigHelper.BeginMarker));
            });
        }

        [Test]
        public void Configure_ThenUnregister_LeavesUserPatchesIntact()
        {
            WithHttpTransport(true, () =>
            {
                string rootPatch = Path.Combine(_tempDir, "cordis.patch.yml");
                string userRow = "- insert:\n    - id: mcp-memory\n      name: '@deepseek-ai/dsh-mcp-client'\n";
                File.WriteAllText(rootPatch, userRow);

                DeepSeekHarnessConfigurator.DshCommandRunner = _ => (0, "ok");

                var configurator = new DeepSeekHarnessConfigurator(_tempDir);
                configurator.Configure();

                Assert.AreEqual(McpStatus.Configured, configurator.Status);
                Assert.AreEqual(userRow, File.ReadAllText(rootPatch), "root patch must be untouched when no legacy block exists");

                configurator.Unregister();

                Assert.AreEqual(McpStatus.NotConfigured, configurator.Status);
                Assert.AreEqual(userRow, File.ReadAllText(rootPatch), "unregister must not touch user rows");
                Assert.IsFalse(Directory.Exists(Path.Combine(_tempDir, "bundles", "unity-mcp")), "bundle dir must be deleted");
            });
        }

        [Test]
        public void Unregister_CallsRemoveAndDeletesBundle()
        {
            WithHttpTransport(true, () =>
            {
                string capturedArgs = null;
                DeepSeekHarnessConfigurator.DshCommandRunner = args =>
                {
                    capturedArgs = string.Join(" ", args);
                    return (0, "ok");
                };

                var configurator = new DeepSeekHarnessConfigurator(_tempDir);
                configurator.Configure();
                string bundleDir = Path.Combine(_tempDir, "bundles", "unity-mcp");
                Assert.IsTrue(Directory.Exists(bundleDir));

                configurator.Unregister();

                StringAssert.Contains("remove", capturedArgs);
                StringAssert.Contains(DshBundleWriter.BundleName, capturedArgs);
                Assert.IsFalse(Directory.Exists(bundleDir), "bundle directory must be deleted");
            });
        }

        // ---------------------------------------------------------------- helpers

        private static string BuildHttpBlock(string url)
        {
            return DshConfigHelper.BuildUnityMcpBlock(
                useHttp: true, httpUrl: url,
                uvxCommand: null, uvxArgs: null,
                apiKeyHeader: null, apiKey: null, windowsSystemRoot: null);
        }

        private static void WriteBundleFiles(string dshHome, string patchBlock)
        {
            DshBundleWriter.WriteBundle(dshHome, patchBlock);
        }

        private static void WriteProfileBundleEntry(string dshHome, string profile)
        {
            string profileDir = Path.Combine(dshHome, "profiles", profile);
            Directory.CreateDirectory(profileDir);
            string packageJson = "{\n  \"dsh\": {\n    \"profile\": {\n      \"bundles\": [\"" + DshBundleWriter.BundleName + "\"]\n    }\n  }\n}\n";
            File.WriteAllText(Path.Combine(profileDir, "package.json"), packageJson);
        }

        private static void WithHttpTransport(bool useHttp, Action body)
        {
            bool original = EditorConfigurationCache.Instance.UseHttpTransport;
            try
            {
                EditorConfigurationCache.Instance.SetUseHttpTransport(useHttp);
                body();
            }
            finally
            {
                EditorConfigurationCache.Instance.SetUseHttpTransport(original);
            }
        }

        private static int CountOccurrences(string text, string needle)
        {
            int count = 0;
            int idx = 0;
            while ((idx = text.IndexOf(needle, idx, StringComparison.Ordinal)) >= 0)
            {
                count++;
                idx += needle.Length;
            }
            return count;
        }
    }
}
