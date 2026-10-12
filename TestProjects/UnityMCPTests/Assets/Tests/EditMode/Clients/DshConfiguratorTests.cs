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

        /// <summary>Creates a fresh temp DSH home and saves the default command runner.</summary>
        [SetUp]
        public void SetUp()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "UnityMCPTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
            _originalRunner = DeepSeekHarnessConfigurator.DshCommandRunner;
        }

        /// <summary>Restores the command runner and removes the temp DSH home.</summary>
        [TearDown]
        public void TearDown()
        {
            DeepSeekHarnessConfigurator.DshCommandRunner = _originalRunner;
            try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); } catch { }
        }

        // ---------------------------------------------------------------- helper: build

        /// <summary>HTTP block emits the expected insert row and url.</summary>
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

        /// <summary>HTTP block emits an auth header row when an API key is configured.</summary>
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

        /// <summary>stdio block emits command, args, and the Windows SystemRoot env row.</summary>
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

        /// <summary>TryParseManagedBlock round-trips the HTTP block shape.</summary>
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

        /// <summary>TryParseManagedBlock round-trips stdio rows containing Windows paths.</summary>
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

        /// <summary>TryParseManagedBlock rejects an empty block.</summary>
        [Test]
        public void TryParse_RejectsEmptyBlock()
        {
            bool parsed = DshConfigHelper.TryParseManagedBlock(
                "", out _, out _, out _, out _, out _, out _);
            Assert.IsFalse(parsed);
        }

        // ---------------------------------------------------------------- helper: upsert/remove

        /// <summary>Upsert into an empty file produces just the managed block.</summary>
        [Test]
        public void Upsert_IntoEmptyFile_ProducesValidListDocument()
        {
            string block = BuildHttpBlock("http://127.0.0.1:8090/mcp");
            string result = DshConfigHelper.UpsertManagedBlock(null, block);

            Assert.IsTrue(result.StartsWith(DshConfigHelper.BeginMarker), "file must remain a top-level YAML list");
            StringAssert.EndsWith("\n", result);
        }

        /// <summary>Upserting the same block twice is idempotent.</summary>
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

        /// <summary>Upsert preserves unrelated user patch content byte-for-byte.</summary>
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

        /// <summary>Upsert appends cleanly when the file lacks a trailing newline.</summary>
        [Test]
        public void Upsert_AppendsWithoutTrailingNewlineInput()
        {
            string existing = "- insert:\n    - id: x\n      name: pkg-x";
            string result = DshConfigHelper.UpsertManagedBlock(existing, BuildHttpBlock("http://127.0.0.1:8090/mcp"));

            StringAssert.StartsWith(existing + "\n", result);
        }

        /// <summary>Remove deletes only the managed block.</summary>
        [Test]
        public void Remove_DeletesOnlyManagedBlock()
        {
            string userPatches = "- insert:\n    - id: mcp-memory\n      name: '@deepseek-ai/dsh-mcp-client'\n";
            string file = userPatches + BuildHttpBlock("http://127.0.0.1:8090/mcp");

            string result = DshConfigHelper.RemoveManagedBlock(file);

            Assert.AreEqual(userPatches, result, "only the managed block may be removed");
        }

        /// <summary>Remove leaves text unchanged when no block exists.</summary>
        [Test]
        public void Remove_WithoutBlock_ReturnsTextUnchanged()
        {
            string text = "- insert: []\n";
            Assert.AreEqual(text, DshConfigHelper.RemoveManagedBlock(text));
        }

        /// <summary>Extract returns null without markers and the rows when markers exist.</summary>
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

        /// <summary>Config path points at the bundle patch inside the overridden DSH home.</summary>
        [Test]
        public void ConfigPath_UsesBundlePatchInsideOverrideHome()
        {
            var configurator = new DeepSeekHarnessConfigurator(_tempDir);
            string expected = Path.Combine(_tempDir, "bundles", "desktop", "unity-mcp", "cordis.patch.yml");
            Assert.AreEqual(expected, configurator.GetConfigPath());
        }

        /// <summary>ResolveDshHome precedence: explicit override, then DSH_HOME, then ~/.dsh.</summary>
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

        /// <summary>ResolveDshProfile precedence: explicit override, then DSH_PROFILE, then desktop.</summary>
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

        /// <summary>IsInstalled follows existence of the DSH home directory.</summary>
        [Test]
        public void IsInstalled_FollowsParentDirectory()
        {
            string missing = Path.Combine(_tempDir, "does-not-exist");
            Assert.IsFalse(new DeepSeekHarnessConfigurator(missing).IsInstalled);
            Assert.IsTrue(new DeepSeekHarnessConfigurator(_tempDir).IsInstalled);
        }

        /// <summary>A missing bundle reports NotConfigured and performs no writes or CLI calls.</summary>
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
            string bundleDir = Path.Combine(_tempDir, "bundles", "desktop", "unity-mcp");
            Assert.IsFalse(Directory.Exists(bundleDir), "CheckStatus must never write bundle files");
        }

        /// <summary>A bundle matching the current HTTP endpoint reports Configured.</summary>
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

        /// <summary>A stale HTTP url reports IncorrectPath without rewriting anything.</summary>
        [Test]
        public void CheckStatus_HttpMismatch_ReturnsIncorrectPath_WithoutRewrite()
        {
            WithHttpTransport(true, () =>
            {
                string bundlePatch = Path.Combine(_tempDir, "bundles", "desktop", "unity-mcp", "cordis.patch.yml");
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
        public void CheckStatus_StdioMatch_Configured()
        {
            WithHttpTransport(false, () =>
            {
                // The --from spec that Configure would really write, so the test matches
                // in any environment (prerelease range, beta pin, or local override).
                string expected = AssetPathUtility.GetMcpServerPackageSource();
                WriteProfileBundleEntry(_tempDir, "desktop");
                WriteBundleFiles(_tempDir, BuildStdioBlock(expected));

                var configurator = new DeepSeekHarnessConfigurator(_tempDir);
                var status = configurator.CheckStatus(attemptAutoRewrite: false);

                Assert.AreEqual(McpStatus.Configured, status,
                    "identical --from spec must validate (specs are text, not file paths)");
            });
        }

        [Test]
        public void CheckStatus_StdioSpecMismatch_NotConfigured()
        {
            WithHttpTransport(false, () =>
            {
                string expected = AssetPathUtility.GetMcpServerPackageSource();
                WriteProfileBundleEntry(_tempDir, "desktop");
                WriteBundleFiles(_tempDir, BuildStdioBlock(expected + ".x"));

                var configurator = new DeepSeekHarnessConfigurator(_tempDir);
                var status = configurator.CheckStatus(attemptAutoRewrite: false);

                Assert.AreNotEqual(McpStatus.Configured, status,
                    "a different --from spec must not read as Configured");
            });
        }

        [Test]
        public void PackageSourcesEqual_SpecsCompareAsText()
        {
            // Regression: PathsEqual compared specs as file paths, and Path.GetFullPath
            // throws on '>' — so identical specs compared unequal and status was always
            // IncorrectPath (masked only by auto-rewrite).
            Assert.IsTrue(McpConfigurationHelper.PackageSourcesEqual(
                "mcpforunityserver>=0.0.0a0", "mcpforunityserver>=0.0.0a0"));
            Assert.IsTrue(McpConfigurationHelper.PackageSourcesEqual(
                "mcpforunityserver>=0.0.0a0", "  MCPforUnityServer>=0.0.0a0  "));
            Assert.IsFalse(McpConfigurationHelper.PackageSourcesEqual(
                "mcpforunityserver>=0.0.0a0", "mcpforunityserver>=0.0.1a0"));
            Assert.IsFalse(McpConfigurationHelper.PackageSourcesEqual(null, "x"));
            Assert.IsFalse(McpConfigurationHelper.PackageSourcesEqual("x", " "));
        }

        [Test]
        public void PackageSourcesEqual_PathsCompareNormalized()
        {
            Assert.IsTrue(McpConfigurationHelper.PackageSourcesEqual("file:C:\\dev\\Server", "file:C:\\dev\\Server"));
            Assert.IsTrue(McpConfigurationHelper.PackageSourcesEqual("C:\\dev\\Server", "c:/dev/Server"));
            Assert.IsFalse(McpConfigurationHelper.PackageSourcesEqual("C:\\dev\\Server", "C:\\dev\\Other"));
        }

        /// <summary>Configure writes the bundle and invokes dsh plugin add through the seam.</summary>
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

                WriteProfilePackage(_tempDir, "desktop");
                var configurator = new DeepSeekHarnessConfigurator(_tempDir);
                configurator.Configure();

                string bundleDir = Path.Combine(_tempDir, "bundles", "desktop", "unity-mcp");
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

        /// <summary>Configure removes a legacy managed block from the root home patch.</summary>
        [Test]
        public void Configure_MigrationRemovesLegacyRootPatchBlock()
        {
            WithHttpTransport(true, () =>
            {
                string rootPatch = Path.Combine(_tempDir, "cordis.patch.yml");
                string userRow = "- insert:\n    - id: mcp-memory\n      name: '@deepseek-ai/dsh-mcp-client'\n";
                File.WriteAllText(rootPatch, userRow + BuildHttpBlock("http://127.0.0.1:1/mcp"));

                DeepSeekHarnessConfigurator.DshCommandRunner = _ => (0, "ok");

                WriteProfilePackage(_tempDir, "desktop");
                var configurator = new DeepSeekHarnessConfigurator(_tempDir);
                configurator.Configure();

                string after = File.ReadAllText(rootPatch);
                Assert.AreEqual(userRow, after, "legacy managed block must be removed from root patch");
                Assert.AreEqual(0, CountOccurrences(after, DshConfigHelper.BeginMarker));
            });
        }

        /// <summary>Configure and Unregister leave user patch rows untouched.</summary>
        [Test]
        public void Configure_ThenUnregister_LeavesUserPatchesIntact()
        {
            WithHttpTransport(true, () =>
            {
                string rootPatch = Path.Combine(_tempDir, "cordis.patch.yml");
                string userRow = "- insert:\n    - id: mcp-memory\n      name: '@deepseek-ai/dsh-mcp-client'\n";
                File.WriteAllText(rootPatch, userRow);

                DeepSeekHarnessConfigurator.DshCommandRunner = _ => (0, "ok");

                WriteProfilePackage(_tempDir, "desktop");
                var configurator = new DeepSeekHarnessConfigurator(_tempDir);
                configurator.Configure();

                Assert.AreEqual(McpStatus.Configured, configurator.Status);
                Assert.AreEqual(userRow, File.ReadAllText(rootPatch), "root patch must be untouched when no legacy block exists");

                configurator.Unregister();

                Assert.AreEqual(McpStatus.NotConfigured, configurator.Status);
                Assert.AreEqual(userRow, File.ReadAllText(rootPatch), "unregister must not touch user rows");
                Assert.IsFalse(Directory.Exists(Path.Combine(_tempDir, "bundles", "desktop", "unity-mcp")), "bundle dir must be deleted");
            });
        }

        /// <summary>Regression: a failed dsh plugin remove must keep the bundle (no dangling link) and report the failure.</summary>
        [Test]
        public void Unregister_RemovalFailure_KeepsBundleAndThrows()
        {
            WithHttpTransport(true, () =>
            {
                DeepSeekHarnessConfigurator.DshCommandRunner = args =>
                    string.Join(" ", args).Contains("remove") ? (1, "remove failed") : (0, "ok");
                WriteProfilePackage(_tempDir, "desktop");

                var configurator = new DeepSeekHarnessConfigurator(_tempDir);
                configurator.Configure();
                WriteProfileBundleEntry(_tempDir, "desktop");

                Assert.Throws<InvalidOperationException>(() => configurator.Unregister());

                Assert.IsTrue(Directory.Exists(Path.Combine(_tempDir, "bundles", "desktop", "unity-mcp")),
                    "bundle must be kept while the profile still links it");
                Assert.AreNotEqual(McpStatus.NotConfigured, configurator.Status,
                    "a failed removal must not report NotConfigured");
            });
        }

        /// <summary>Unregister deletes leftover bundle files without invoking the CLI when the profile does not list the bundle.</summary>
        [Test]
        public void Unregister_Unregistered_DeletesFilesWithoutRemove()
        {
            WithHttpTransport(true, () =>
            {
                var calls = new List<string>();
                DeepSeekHarnessConfigurator.DshCommandRunner = args =>
                {
                    calls.Add(string.Join(" ", args));
                    return (0, "ok");
                };
                WriteProfilePackage(_tempDir, "desktop");

                var configurator = new DeepSeekHarnessConfigurator(_tempDir);
                configurator.Configure();
                calls.Clear();

                configurator.Unregister();

                Assert.IsFalse(calls.Exists(c => c.Contains("remove")),
                    "CLI remove must not run when the profile does not list the bundle");
                Assert.IsFalse(Directory.Exists(Path.Combine(_tempDir, "bundles", "desktop", "unity-mcp")));
                Assert.AreEqual(McpStatus.NotConfigured, configurator.Status);
            });
        }

        /// <summary>
        /// Regression: a disabled-but-installed bundle (dependency present, absent from
        /// dsh.profile.bundles after DSH's toggle) must still go through dsh plugin remove.
        /// </summary>
        [Test]
        public void Unregister_DisabledButInstalled_StillRunsRemove()
        {
            WithHttpTransport(true, () =>
            {
                var calls = new List<string>();
                DeepSeekHarnessConfigurator.DshCommandRunner = args =>
                {
                    calls.Add(string.Join(" ", args));
                    return (0, "ok");
                };
                WriteProfilePackage(_tempDir, "desktop");

                var configurator = new DeepSeekHarnessConfigurator(_tempDir);
                configurator.Configure();
                // Simulate the plugin-manager toggle OFF: the dependency stays installed
                // while the entry disappears from dsh.profile.bundles.
                WriteProfileDependencyOnly(_tempDir, "desktop");
                calls.Clear();

                configurator.Unregister();

                Assert.IsTrue(calls.Exists(c => c.Contains("remove") && c.Contains(DshBundleWriter.BundleName)),
                    "remove must run while the dependency is still declared");
                Assert.IsFalse(Directory.Exists(Path.Combine(_tempDir, "bundles", "desktop", "unity-mcp")));
            });
        }

        /// <summary>
        /// Regression: unregistering one profile must not delete or dangle another
        /// profile's bundle (dsh plugin add symlinks the bundle directory).
        /// </summary>
        [Test]
        public void Unregister_OnlyRemovesTargetProfileBundle()
        {
            WithHttpTransport(true, () =>
            {
                DeepSeekHarnessConfigurator.DshCommandRunner = _ => (0, "ok");

                WriteProfilePackage(_tempDir, "desktop");
                WriteProfilePackage(_tempDir, "web");

                var desktop = new DeepSeekHarnessConfigurator(_tempDir, "desktop");
                desktop.Configure();
                var web = new DeepSeekHarnessConfigurator(_tempDir, "web");
                web.Configure();

                // Simulate the registration effect of the real `dsh plugin add`, which the
                // stubbed seam does not perform.
                WriteProfileBundleEntry(_tempDir, "desktop");
                WriteProfileBundleEntry(_tempDir, "web");

                desktop.Unregister();

                Assert.IsFalse(Directory.Exists(Path.Combine(_tempDir, "bundles", "desktop", "unity-mcp")),
                    "target profile bundle must be deleted");
                Assert.IsTrue(Directory.Exists(Path.Combine(_tempDir, "bundles", "web", "unity-mcp")),
                    "other profile bundle must survive");
                Assert.AreEqual(McpStatus.Configured, web.CheckStatus(attemptAutoRewrite: false),
                    "other profile must stay configured");
            });
        }

        /// <summary>Unregister calls dsh plugin remove and deletes the bundle directory.</summary>
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

                WriteProfilePackage(_tempDir, "desktop");
                var configurator = new DeepSeekHarnessConfigurator(_tempDir);
                configurator.Configure();
                // Simulate the registration effect of the real `dsh plugin add`, which the
                // stubbed seam does not perform; Unregister only runs `remove` when the
                // profile lists the bundle.
                WriteProfileBundleEntry(_tempDir, "desktop");
                string bundleDir = Path.Combine(_tempDir, "bundles", "desktop", "unity-mcp");
                Assert.IsTrue(Directory.Exists(bundleDir));

                configurator.Unregister();

                StringAssert.Contains("remove", capturedArgs);
                StringAssert.Contains(DshBundleWriter.BundleName, capturedArgs);
                Assert.IsFalse(Directory.Exists(bundleDir), "bundle directory must be deleted");
            });
        }

        /// <summary>Configure fails fast on a missing profile without creating it or calling the CLI.</summary>
        [Test]
        public void Configure_MissingProfile_ThrowsWithoutCreatingProfile()
        {
            WithHttpTransport(true, () =>
            {
                bool runnerCalled = false;
                DeepSeekHarnessConfigurator.DshCommandRunner = args =>
                {
                    runnerCalled = true;
                    return (0, "ok");
                };

                var configurator = new DeepSeekHarnessConfigurator(_tempDir);
                var ex = Assert.Throws<InvalidOperationException>(() => configurator.Configure());

                StringAssert.Contains("desktop", ex.Message);
                Assert.IsFalse(runnerCalled, "dsh CLI must not run when the profile is missing");
                Assert.IsFalse(Directory.Exists(Path.Combine(_tempDir, "profiles", "desktop")),
                    "guard must not create the profile");
            });
        }

        /// <summary>The resolver finds dsh.cmd on PATH and reports it as a cmd shim.</summary>
        [Test]
        public void TryResolveDshExecutable_FindsCmdShimOnPath()
        {
            if (Path.DirectorySeparatorChar != '\\') Assert.Ignore("cmd shim resolution is Windows-specific");

            string shimDir = Path.Combine(_tempDir, "fakebin");
            Directory.CreateDirectory(shimDir);
            string shim = Path.Combine(shimDir, "dsh.cmd");
            File.WriteAllText(shim, "@echo off\r\n");

            string originalPath = Environment.GetEnvironmentVariable("PATH");
            string originalCli = Environment.GetEnvironmentVariable("DSH_CLI");
            try
            {
                Environment.SetEnvironmentVariable("DSH_CLI", null);
                Environment.SetEnvironmentVariable("PATH", shimDir);

                Assert.IsTrue(DeepSeekHarnessConfigurator.TryResolveDshExecutable(out string path, out bool isCmdShim));
                Assert.AreEqual(shim, path);
                Assert.IsTrue(isCmdShim, ".cmd must be reported as a cmd shim");
            }
            finally
            {
                Environment.SetEnvironmentVariable("PATH", originalPath);
                Environment.SetEnvironmentVariable("DSH_CLI", originalCli);
            }
        }

        /// <summary>RunDshCommand executes a .cmd shim through cmd.exe and forwards its arguments.</summary>
        [Test]
        public void RunDshCommand_RoutesCmdShimThroughCmdExe()
        {
            if (Path.DirectorySeparatorChar != '\\') Assert.Ignore("cmd shim routing is Windows-specific");

            string shim = Path.Combine(_tempDir, "dsh.cmd");
            File.WriteAllText(shim, "@echo off\r\necho shim-ok %*\r\nexit /b 0\r\n");

            string originalCli = Environment.GetEnvironmentVariable("DSH_CLI");
            try
            {
                Environment.SetEnvironmentVariable("DSH_CLI", shim);
                var (exitCode, output) = DeepSeekHarnessConfigurator.RunDshCommand(
                    new[] { "plugin", "--profile", "R&D", "add", "C:\\bundle dir" });

                Assert.AreEqual(0, exitCode);
                StringAssert.Contains("shim-ok", output);
                StringAssert.Contains("plugin", output);
                // cmd.exe metacharacters must round-trip as data, not be interpreted.
                StringAssert.Contains("R&D", output);
                StringAssert.Contains("C:\\bundle dir", output);
            }
            finally
            {
                Environment.SetEnvironmentVariable("DSH_CLI", originalCli);
            }
        }

        // ---------------------------------------------------------------- helpers

        /// <summary>Builds an HTTP managed block for the given url.</summary>
        private static string BuildHttpBlock(string url)
        {
            return DshConfigHelper.BuildUnityMcpBlock(
                useHttp: true, httpUrl: url,
                uvxCommand: null, uvxArgs: null,
                apiKeyHeader: null, apiKey: null, windowsSystemRoot: null);
        }

        /// <summary>Builds a stdio managed block with the given --from package spec.</summary>
        private static string BuildStdioBlock(string fromSpec)
        {
            string[] uvxArgs = { "--from", fromSpec, "mcp-for-unity", "--transport", "stdio" };
            return DshConfigHelper.BuildUnityMcpBlock(
                useHttp: false, httpUrl: null,
                uvxCommand: "uvx.exe", uvxArgs: uvxArgs,
                apiKeyHeader: null, apiKey: null, windowsSystemRoot: "C:\\WINDOWS");
        }

        /// <summary>Writes the bundle files for the default test profile.</summary>
        private static void WriteBundleFiles(string dshHome, string patchBlock)
        {
            DshBundleWriter.WriteBundle(dshHome, "desktop", patchBlock);
        }

        /// <summary>Creates a bare profile directory with a package.json (no bundle entry).</summary>
        private static void WriteProfilePackage(string dshHome, string profile)
        {
            string profileDir = Path.Combine(dshHome, "profiles", profile);
            Directory.CreateDirectory(profileDir);
            File.WriteAllText(Path.Combine(profileDir, "package.json"), "{\n  \"name\": \"test-profile\"\n}\n");
        }

        /// <summary>Creates a profile package.json declaring the bundle only as a dependency (disabled state).</summary>
        private static void WriteProfileDependencyOnly(string dshHome, string profile)
        {
            string profileDir = Path.Combine(dshHome, "profiles", profile);
            Directory.CreateDirectory(profileDir);
            string packageJson = "{\n  \"dependencies\": {\n    \"" + DshBundleWriter.BundleName + "\": \"link:./somewhere\"\n  }\n}\n";
            File.WriteAllText(Path.Combine(profileDir, "package.json"), packageJson);
        }

        /// <summary>Creates a profile package.json that lists the bundle as installed.</summary>
        private static void WriteProfileBundleEntry(string dshHome, string profile)
        {
            string profileDir = Path.Combine(dshHome, "profiles", profile);
            Directory.CreateDirectory(profileDir);
            string packageJson = "{\n  \"dsh\": {\n    \"profile\": {\n      \"bundles\": [\"" + DshBundleWriter.BundleName + "\"]\n    }\n  }\n}\n";
            File.WriteAllText(Path.Combine(profileDir, "package.json"), packageJson);
        }

        /// <summary>Runs a body under the given transport preference, restoring the previous one afterwards.</summary>
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

        /// <summary>Counts non-overlapping occurrences of a substring.</summary>
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
