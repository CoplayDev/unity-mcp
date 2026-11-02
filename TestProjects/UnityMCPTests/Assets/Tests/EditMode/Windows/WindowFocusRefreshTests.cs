using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using MCPForUnity.Editor.Clients;
using MCPForUnity.Editor.Constants;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Models;
using MCPForUnity.Editor.Services;
using MCPForUnity.Editor.Services.Transport;
using MCPForUnity.Editor.Windows.Components.Advanced;
using MCPForUnity.Editor.Windows.Components.ClientConfig;
using MCPForUnity.Editor.Windows.Components.Connection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace MCPForUnityTests.Editor.Windows
{
    public class FocusRefreshTestWindow : EditorWindow { }

    public class WindowFocusRefreshTests : TransportPreferenceTestBase
    {
        private FocusRefreshTestWindow window;
        private string fixtureDir;
        private readonly Dictionary<string, string> savedStrings = new();
        private readonly HashSet<string> existingPrefs = new();
        private bool savedAllowLan, hadAllowLan;

        [SetUp]
        public void Setup()
        {
            hadAllowLan = EditorPrefs.HasKey(EditorPrefKeys.AllowLanHttpBind);
            savedAllowLan = EditorPrefs.GetBool(EditorPrefKeys.AllowLanHttpBind, false);
            string[] keys = { EditorPrefKeys.GitUrlOverride, EditorPrefKeys.UvxPathOverride,
                EditorPrefKeys.ClaudeCliPathOverride, EditorPrefKeys.HttpBaseUrl, EditorPrefKeys.HttpTransportScope,
                EditorPrefKeys.ClientProjectDirOverride };
            foreach (string key in keys)
            {
                if (EditorPrefs.HasKey(key)) existingPrefs.Add(key);
                savedStrings[key] = EditorPrefs.GetString(key, string.Empty);
            }
            fixtureDir = Path.Combine(Path.GetTempPath(), "mcp-window-refresh-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(fixtureDir, "Server"));
            File.WriteAllText(Path.Combine(fixtureDir, "Server", "pyproject.toml"), "[project]\nname = 'refresh-fixture'\n");
            EditorPrefs.SetString(EditorPrefKeys.GitUrlOverride, fixtureDir);
            EditorPrefs.SetString(EditorPrefKeys.UvxPathOverride, "fixture-uvx");
            EditorPrefs.SetString(EditorPrefKeys.ClaudeCliPathOverride, Path.Combine(fixtureDir, "missing-claude"));
            EditorPrefs.SetString(EditorPrefKeys.HttpBaseUrl, "http://127.0.0.1:8080");
            EditorPrefs.SetString(EditorPrefKeys.HttpTransportScope, "local");
            EditorPrefs.SetString(EditorPrefKeys.ClientProjectDirOverride, string.Empty);
            EditorPrefs.SetBool(EditorPrefKeys.UseHttpTransport, true);
            EditorConfigurationCache.Instance.Refresh();
        }

        [TearDown]
        public void Cleanup()
        {
            if (window != null) window.Close();
            MCPServiceLocator.Reset();
            foreach (var pair in savedStrings)
            {
                if (existingPrefs.Contains(pair.Key)) EditorPrefs.SetString(pair.Key, pair.Value);
                else EditorPrefs.DeleteKey(pair.Key);
            }
            savedStrings.Clear();
            existingPrefs.Clear();
            if (hadAllowLan) EditorPrefs.SetBool(EditorPrefKeys.AllowLanHttpBind, savedAllowLan);
            else EditorPrefs.DeleteKey(EditorPrefKeys.AllowLanHttpBind);
            if (Directory.Exists(fixtureDir)) Directory.Delete(fixtureDir, true);
        }

        [UnityTest]
        public IEnumerator AdvancedRefresh_ReturnsBeforeSlowExecutableValidation()
        {
            var paths = new SlowPathResolver();
            MCPServiceLocator.Register<IPathResolverService>(paths);
            window = ScriptableObject.CreateInstance<FocusRefreshTestWindow>();
            window.Show();
            var asset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
                "Packages/com.coplaydev.unity-mcp/Editor/Windows/Components/Advanced/McpAdvancedSection.uxml");
            Assert.IsNotNull(asset);
            var root = asset.CloneTree();
            window.rootVisualElement.Add(root);
            // Build only the controls needed by refresh, without setup/configuration callbacks.
            var section = (McpAdvancedSection)FormatterServices.GetUninitializedObject(typeof(McpAdvancedSection));
            const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(McpAdvancedSection).GetField("<Root>k__BackingField", Flags).SetValue(section, root);
            typeof(McpAdvancedSection).GetMethod("CacheUIElements", Flags).Invoke(section, null);
            int mainThread = Thread.CurrentThread.ManagedThreadId;
            var watch = Stopwatch.StartNew();
            section.UpdatePathOverrides();
            watch.Stop();
            Assert.Less(watch.ElapsedMilliseconds, 200, "Focus refresh waited for an executable probe on the UI thread.");
            double deadline = EditorApplication.timeSinceStartup + 5;
            while (!paths.validationCompleted && EditorApplication.timeSinceStartup < deadline) yield return null;
            Assert.IsTrue(paths.validationCompleted, "Asynchronous validation did not finish.");
            Assert.AreNotEqual(mainThread, paths.validationThreadId, "Executable validation ran on the UI thread.");
        }

        [UnityTest]
        public IEnumerator AdvancedRefresh_UsesSilentDisplayUpdates_AndDoesNotNormalizeStoredSource()
        {
            MCPServiceLocator.Register<IPathResolverService>(new SlowPathResolver());
            var root = CreateRoot("Advanced", "McpAdvancedSection");
            var section = UninitializedSection<McpAdvancedSection>(root);
            int callbacks = 0;
            root.Q<TextField>("git-url-override").RegisterValueChangedCallback(_ => callbacks++);
            root.Q<Toggle>("debug-logs-toggle").RegisterValueChangedCallback(_ => callbacks++);
            root.Q<TextField>("deploy-source-path").RegisterValueChangedCallback(_ => callbacks++);
            section.UpdatePathOverrides();
            yield return EditorAsyncRefreshTests.WaitFor(() => root.Q<TextField>("uv-path-override").value == "fixture-uvx");
            Assert.AreEqual(0, callbacks);
            Assert.AreEqual(fixtureDir, EditorPrefs.GetString(EditorPrefKeys.GitUrlOverride), "Read-only refresh normalized a preference.");
        }

        [UnityTest]
        public IEnumerator ClientRefresh_IsReadOnly_AndLiveModelChangesOnlyAfterWorkerCompletes()
        {
            MCPServiceLocator.Register<IPathResolverService>(new SlowPathResolver());
            string path = Path.Combine(fixtureDir, "client.json");
            const string original = "{\"mcpServers\":{\"unityMCP\":{\"url\":\"http://127.0.0.1:1/mcp\"}}}";
            File.WriteAllText(path, original);
            var model = new McpClient { name = "Refresh Fixture", windowsConfigPath = path, macConfigPath = path, linuxConfigPath = path };
            var client = new FixtureJsonClient(model);
            MCPServiceLocator.Register<IClientConfigurationService>(new FixtureClientService(client));
            var root = CreateRoot("ClientConfig", "McpClientConfigSection");
            var watch = Stopwatch.StartNew();
            var section = new McpClientConfigSection(root);
            section.RefreshSelectedClient();
            watch.Stop();
            Assert.Less(watch.ElapsedMilliseconds, 200);
            Assert.AreEqual(McpStatus.NotConfigured, model.status);
            Assert.AreEqual(ConfiguredTransport.Unknown, model.configuredTransport);
            yield return EditorAsyncRefreshTests.WaitFor(() => root.Q<Label>("client-status").text != "Checking...");
            Assert.AreEqual(McpStatus.IncorrectPath, model.status);
            Assert.AreEqual(ConfiguredTransport.Http, model.configuredTransport);
            Assert.AreEqual(original, File.ReadAllText(path), "Window refresh rewrote a mismatched client configuration.");
            Assert.AreEqual(fixtureDir, EditorPrefs.GetString(EditorPrefKeys.GitUrlOverride));
        }

        [UnityTest]
        public IEnumerator ConnectionRefresh_ReturnsBeforeSlowProbe_AndCachedFailuresDoNotAccumulate()
        {
            var bridge = new FixtureBridge { Running = true };
            MCPServiceLocator.Register<IBridgeControlService>(bridge);
            var root = CreateRoot("Connection", "McpConnectionSection");
            var section = UninitializedSection<McpConnectionSection>(root);
            var field = root.Q<EnumField>("transport-dropdown");
            var enumType = typeof(McpConnectionSection).GetNestedType("TransportProtocol", BindingFlags.NonPublic);
            field.Init((Enum)Enum.Parse(enumType, "HTTPLocal"));
            int mainThread = Thread.CurrentThread.ManagedThreadId, probeThread = 0;
            section.LocalServerProbe = _ => { probeThread = Thread.CurrentThread.ManagedThreadId; Thread.Sleep(500); return false; };
            var watch = Stopwatch.StartNew();
            section.UpdateConnectionStatus();
            watch.Stop();
            Assert.Less(watch.ElapsedMilliseconds, 200);
            yield return EditorAsyncRefreshTests.WaitFor(() => GetDownPolls(section) == 1);
            for (int i = 0; i < 30; i++) section.UpdateConnectionStatus();
            Assert.AreEqual(1, GetDownPolls(section));
            Assert.AreNotEqual(mainThread, probeThread);
            Assert.AreEqual(0, bridge.Stops);
            EditorPrefs.SetString(EditorPrefKeys.HttpBaseUrl, "http://127.0.0.1:8081");
            section.UpdateConnectionStatus();
            Assert.AreEqual(0, GetDownPolls(section), "Endpoint change retained a failure streak.");
        }

        [UnityTest]
        public IEnumerator ProductionPathResolution_CapturesOnMain_AndRunsSlowExecutableOnWorker()
        {
            if (Application.platform == RuntimePlatform.WindowsEditor) Assert.Ignore("Unix fixture; Windows uses the same captured worker API.");
            string script = Path.Combine(fixtureDir, "uvx-slow");
            File.WriteAllText(script, "#!/bin/sh\nsleep 1\nprintf 'uvx 0.8.0\\n'\n");
            using (var chmod = Process.Start(new ProcessStartInfo("/bin/chmod", $"700 \"{script}\"") { UseShellExecute = false }))
                Assert.IsTrue(chmod.WaitForExit(1000));
            EditorPrefs.SetString(EditorPrefKeys.UvxPathOverride, script);
            var watch = Stopwatch.StartNew();
            var operation = new PathResolverService().CaptureWindowResolution();
            var task = Task.Run(operation);
            watch.Stop();
            Assert.Less(watch.ElapsedMilliseconds, 200);
            int frames = 0;
            double deadline = EditorApplication.timeSinceStartup + 10;
            while (!task.IsCompleted && EditorApplication.timeSinceStartup < deadline) { frames++; yield return null; }
            Assert.IsTrue(task.IsCompleted);
            Assert.GreaterOrEqual(frames, 3, "Editor updates stopped while the executable ran.");
            Assert.IsTrue(task.Result.UvxValid);
            Assert.AreEqual(script, task.Result.UvxPath);
        }

        [UnityTest]
        public IEnumerator WorkerTcpProbe_DetectsListeningLoopbackServer()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                int port = ((IPEndPoint)listener.LocalEndpoint).Port;
                var task = Task.Run(() => ServerManagementService.IsLocalHttpServerReachable($"http://127.0.0.1:{port}"));
                yield return EditorAsyncRefreshTests.WaitFor(() => task.IsCompleted);
                Assert.IsTrue(task.Result);
            }
            finally { listener.Stop(); }
        }

        [UnityTest]
        public IEnumerator CommandRefresh_DoesNotRestoreCommandAfterLanPolicyWasDisabled()
        {
            var paths = new SlowPathResolver();
            MCPServiceLocator.Register<IPathResolverService>(paths);
            EditorPrefs.SetString(EditorPrefKeys.HttpBaseUrl, "http://0.0.0.0:8080");
            EditorPrefs.SetBool(EditorPrefKeys.AllowLanHttpBind, true);
            var root = CreateRoot("Connection", "McpConnectionSection");
            var section = UninitializedSection<McpConnectionSection>(root);
            InitializeLocalTransport(root);
            section.UpdateHttpServerCommandDisplay();
            EditorPrefs.SetBool(EditorPrefKeys.AllowLanHttpBind, false);
            section.UpdateHttpServerCommandDisplay();
            yield return EditorAsyncRefreshTests.WaitFor(() => paths.validationCompleted);
            for (int i = 0; i < 5; i++) yield return null;
            Assert.IsEmpty(root.Q<TextField>("http-server-command").value);
            Assert.IsFalse(root.Q<Button>("copy-http-server-command-button").enabledSelf);
        }

        [UnityTest]
        public IEnumerator BridgeVerification_DiscardsResultAfterAutomaticSessionReplacement()
        {
            var verification = new TaskCompletionSource<BridgeVerificationResult>();
            var bridge = new FixtureBridge { Running = true, Verification = verification.Task };
            MCPServiceLocator.Register<IBridgeControlService>(bridge);
            var transport = new FixtureTransport { SessionId = "fixture-session-before" };
            var manager = new TransportManager();
            manager.Configure(() => transport, () => transport);
            MCPServiceLocator.Register<TransportManager>(manager);
            yield return EditorAsyncRefreshTests.WaitFor(() => manager.StartAsync(TransportMode.Http).IsCompleted);
            var root = CreateRoot("Connection", "McpConnectionSection");
            var section = UninitializedSection<McpConnectionSection>(root);
            string reportedHealth = null;
            section.SetHealthStatusUpdateCallback((_, status) => reportedHealth = status);
            var pending = section.VerifyBridgeConnectionAsync();
            Assert.IsFalse(pending.IsCompleted);
            transport.SessionId = "fixture-session-after";
            verification.SetResult(new BridgeVerificationResult { HandshakeValid = true, PingSucceeded = false });
            yield return EditorAsyncRefreshTests.WaitFor(() => pending.IsCompleted);
            Assert.IsNull(reportedHealth, "The old session's failed verification changed the new session's health.");
        }

        [Test]
        public void TransportWarning_UsesResolvedProjectOverrideFlag()
        {
            var root = CreateRoot("Connection", "McpConnectionSection");
            var section = UninitializedSection<McpConnectionSection>(root);
            section.UpdateTransportMismatchWarning("Fixture", ConfiguredTransport.Stdio, hasProjectDirOverride: true);
            Assert.IsFalse(root.Q<VisualElement>("transport-mismatch-warning").ClassListContains("visible"));
            section.UpdateTransportMismatchWarning("Fixture", ConfiguredTransport.Stdio, hasProjectDirOverride: false);
            Assert.IsTrue(root.Q<VisualElement>("transport-mismatch-warning").ClassListContains("visible"));
        }

        [UnityTest]
        public IEnumerator AllBuiltInClientDisplays_RunFromCapturedInputs_WithoutChangingLiveModels()
        {
            var paths = new SlowPathResolver(delay: 0);
            MCPServiceLocator.Register<IPathResolverService>(paths);
            var clients = new ClientConfigurationService().GetAllClients().OfType<McpClientConfiguratorBase>().ToArray();
            Assert.Greater(clients.Length, 10);
            var tasks = new List<Task<McpClientConfiguratorBase.ClientRefreshResult>>();
            foreach (var client in clients)
            {
                string path = Path.Combine(fixtureDir, client.Id + ".json");
                client.Client.windowsConfigPath = client.Client.macConfigPath = client.Client.linuxConfigPath = path;
                tasks.Add(Task.Run(client.CaptureRefresh(WindowRefreshContext.Capture())));
            }
            var all = Task.WhenAll(tasks);
            yield return EditorAsyncRefreshTests.WaitFor(() => all.IsCompleted);
            Assert.IsFalse(all.IsFaulted, "A built-in display read a Unity API on its worker.");
            foreach (var client in clients)
            {
                Assert.AreEqual(McpStatus.NotConfigured, client.Status, client.Id);
                Assert.AreEqual(ConfiguredTransport.Unknown, client.ConfiguredTransport, client.Id);
                Assert.IsFalse(File.Exists(client.Client.macConfigPath), "Refresh created a client configuration.");
            }
            foreach (var result in all.Result) Assert.IsNotEmpty(result.Snippet);
            Assert.AreEqual(0, paths.synchronousPathCalls);
        }

        private static void InitializeLocalTransport(VisualElement root)
        {
            var type = typeof(McpConnectionSection).GetNestedType("TransportProtocol", BindingFlags.NonPublic);
            root.Q<EnumField>("transport-dropdown").Init((Enum)Enum.Parse(type, "HTTPLocal"));
        }

        private VisualElement CreateRoot(string directory, string name)
        {
            if (window == null)
            {
                window = ScriptableObject.CreateInstance<FocusRefreshTestWindow>();
                window.Show();
            }
            var asset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
                $"Packages/com.coplaydev.unity-mcp/Editor/Windows/Components/{directory}/{name}.uxml");
            Assert.IsNotNull(asset);
            var root = asset.CloneTree();
            window.rootVisualElement.Add(root);
            return root;
        }

        private static T UninitializedSection<T>(VisualElement root)
        {
            var type = typeof(T);
            var section = (T)FormatterServices.GetUninitializedObject(type);
            const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
            type.GetField("<Root>k__BackingField", Flags).SetValue(section, root);
            type.GetMethod("CacheUIElements", Flags).Invoke(section, null);
            return section;
        }

        private static int GetDownPolls(McpConnectionSection section) => (int)typeof(McpConnectionSection)
            .GetField("consecutiveServerDownPolls", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(section);

        private sealed class FixtureJsonClient : JsonFileMcpConfigurator
        {
            public FixtureJsonClient(McpClient model) : base(model) { }
            public override IList<string> GetInstallationSteps() => new[] { "Use the fixture config." };
        }

        private sealed class FixtureClientService : IClientConfigurationService
        {
            private readonly IMcpClientConfigurator client;
            public FixtureClientService(IMcpClientConfigurator client) { this.client = client; }
            public IReadOnlyList<IMcpClientConfigurator> GetAllClients() => new[] { client };
            public void ConfigureClient(IMcpClientConfigurator _) => Assert.Fail("Refresh attempted configuration.");
            public ClientConfigurationSummary ConfigureAllDetectedClients() => throw new InvalidOperationException();
            public bool CheckClientStatus(IMcpClientConfigurator _, bool attemptAutoRewrite = true) => throw new InvalidOperationException();
        }

        private sealed class FixtureBridge : IBridgeControlService
        {
            internal bool Running;
            internal int Stops;
            internal Task<BridgeVerificationResult> Verification;
            public bool IsRunning => Running;
            public int CurrentPort => 6400;
            public bool IsAutoConnectMode => false;
            public TransportMode? ActiveMode => TransportMode.Http;
            public Task<bool> StartAsync() { Running = true; return Task.FromResult(true); }
            public Task StopAsync() { Stops++; Running = false; return Task.CompletedTask; }
            public BridgeVerificationResult Verify(int port) => new BridgeVerificationResult();
            public Task<BridgeVerificationResult> VerifyAsync() => Verification ?? Task.FromResult(new BridgeVerificationResult());
        }

        private sealed class FixtureTransport : IMcpTransportClient
        {
            internal string SessionId;
            public bool IsConnected => true;
            public string TransportName => "fixture-http";
            public TransportState State => TransportState.Connected(TransportName, sessionId: SessionId);
            public Task<bool> StartAsync() => Task.FromResult(true);
            public Task StopAsync() => Task.CompletedTask;
            public Task<bool> VerifyAsync() => Task.FromResult(true);
            public Task ReregisterToolsAsync() => Task.CompletedTask;
        }

        private sealed class SlowPathResolver : IPathResolverService, IWindowPathResolver
        {
            private readonly int delay;
            public SlowPathResolver(int delay = 500) { this.delay = delay; }
            internal volatile bool validationCompleted;
            internal int validationThreadId;
            internal int synchronousPathCalls;
            public bool HasUvxPathOverride => true;
            public bool HasClaudeCliPathOverride => false;
            public bool HasUvxPathFallback => false;
            public Func<WindowPathResolution> CaptureWindowResolution() => () =>
            {
                bool valid = TryValidateUvxExecutable("fixture-uvx", out _);
                return new WindowPathResolution { HasOverride = true, UvxPath = "fixture-uvx", UvxValid = valid };
            };
            public string GetUvxPath() { synchronousPathCalls++; return "fixture-uvx"; }
            public string GetClaudeCliPath() { synchronousPathCalls++; return null; }
            public bool IsPythonDetected() => false;
            public bool IsClaudeCliDetected() => false;
            public void SetUvxPathOverride(string path) { }
            public void SetClaudeCliPathOverride(string path) { }
            public void ClearUvxPathOverride() { }
            public void ClearClaudeCliPathOverride() { }
            public bool TryValidateUvxExecutable(string path, out string version)
            {
                validationThreadId = Thread.CurrentThread.ManagedThreadId;
                Thread.Sleep(delay);
                version = "0.8.0";
                validationCompleted = true;
                return true;
            }
        }
    }
}
