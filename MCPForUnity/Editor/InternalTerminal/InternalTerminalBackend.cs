using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using MCPForUnity.Editor.Services.Transport.Transports;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace WTL.InternalTerminal.Editor
{
    internal sealed class InternalTerminalBackend : IDisposable
    {
        private Process process;
        private int port;
        private bool dependenciesInstalled;
        private bool attached;
        private IDisposable unityTcpLease;

        public bool IsRunning => process != null && !process.HasExited || attached && port > 0;
        public int Port => port;
        public string Url => port > 0 ? $"http://127.0.0.1:{port}/" : string.Empty;

        public event Action<string> LogReceived;
        public event Action Exited;

        public void Start()
        {
            if (IsRunning)
            {
                return;
            }

            EnsureBackendExists();
            EnsureDependencies();

            port = InternalTerminalSettings.instance.PreferredPort;
            if (port == 0 || !IsPortFree(port))
            {
                port = GetFreePort();
            }

            var settings = InternalTerminalSettings.instance;
            var startInfo = InternalTerminalProcess.CreateStartInfo(
                settings.NodeExecutable,
                $"\"{InternalTerminalPaths.BackendEntry}\" --port {port} --cwd \"{settings.WorkingDirectory}\"{BuildShellArgument(settings.ShellExecutable)}",
                InternalTerminalPaths.BackendRoot,
                true);

            try
            {
                EnsureUnityTcpLease();
                var mcpConfig = InternalTerminalMcpConfig.Prepare(StdioBridgeHost.GetCurrentPort());
                InjectUnityTcpEnvironment(startInfo);
                InternalTerminalMcpConfig.Inject(startInfo, mcpConfig);

                process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
                var startedProcess = process;
                process.OutputDataReceived += (_, e) => EmitLog(e.Data);
                process.ErrorDataReceived += (_, e) => EmitLog(e.Data);
                process.Exited += (_, _) => EditorApplication.delayCall += () => OnProcessExited(startedProcess);

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                attached = false;
            }
            catch
            {
                try
                {
                    if (process != null && !process.HasExited)
                    {
                        process.Kill();
                        process.WaitForExit(1500);
                    }
                }
                catch { }
                try { process?.Dispose(); } catch { }
                process = null;
                ReleaseUnityTcpLease();
                throw;
            }

            EmitLog($"Started terminal backend on {Url}");
        }

        public void Stop()
        {
            ReleaseUnityTcpLease();

            if (process == null)
            {
                if (attached && port > 0)
                {
                    RequestBackendShutdown(port);
                }

                attached = false;
                port = 0;
                return;
            }

            try
            {
                if (!process.HasExited)
                {
                    process.Kill();
                    process.WaitForExit(1500);
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Failed to stop terminal backend: {exception.Message}");
            }
            finally
            {
                process.Dispose();
                process = null;
                attached = false;
                port = 0;
            }
        }

        public void Dispose()
        {
            Stop();
        }

        public bool Attach(int existingPort)
        {
            if (IsRunning)
            {
                return true;
            }

            if (existingPort <= 0 || !IsBackendHealthy(existingPort))
            {
                return false;
            }

            EnsureUnityTcpLease();
            if (!IsBackendUsingCurrentUnityTcp(existingPort))
            {
                EmitLog("Reconnected terminal backend; Unity MCP bridge port changed, so this shell keeps its previous agent environment until the terminal is restarted.");
            }

            port = existingPort;
            attached = true;
            EmitLog($"Reconnected terminal backend on {Url}");
            return true;
        }

        private static string BuildShellArgument(string shellExecutable)
        {
            return string.IsNullOrWhiteSpace(shellExecutable) ? string.Empty : $" --shell \"{shellExecutable}\"";
        }

        private static void InjectUnityTcpEnvironment(ProcessStartInfo startInfo)
        {
            startInfo.EnvironmentVariables["UNITY_MCP_INTERNAL_HOST"] = "127.0.0.1";
            startInfo.EnvironmentVariables["UNITY_MCP_INTERNAL_PORT"] = StdioBridgeHost.GetCurrentPort().ToString();
            startInfo.EnvironmentVariables["UNITY_MCP_INTERNAL_ROLE"] = "internal";
            startInfo.EnvironmentVariables["UNITY_MCP_INTERNAL_CLIENT_ID"] = "wtl-internal-terminal";
            startInfo.EnvironmentVariables["UNITY_MCP_LEGACY_PORT"] = StdioBridgeHost.GetCurrentPort().ToString();
        }

        private static bool IsBackendUsingCurrentUnityTcp(int candidatePort)
        {
            try
            {
                var request = WebRequest.CreateHttp($"http://127.0.0.1:{candidatePort}/health");
                request.Timeout = 500;
                using (var response = (HttpWebResponse)request.GetResponse())
                using (var reader = new StreamReader(response.GetResponseStream()))
                {
                    var body = reader.ReadToEnd();
                    return body.Contains("\"UNITY_MCP_INTERNAL_PORT\"")
                        && body.Contains("\"" + StdioBridgeHost.GetCurrentPort() + "\"");
                }
            }
            catch
            {
                return false;
            }
        }

        private void EnsureUnityTcpLease()
        {
            unityTcpLease ??= StdioBridgeHost.AcquireInternalLease();
        }

        private void ReleaseUnityTcpLease()
        {
            unityTcpLease?.Dispose();
            unityTcpLease = null;
        }

        private void OnProcessExited(Process exitedProcess)
        {
            if (!ReferenceEquals(process, exitedProcess))
            {
                try { exitedProcess?.Dispose(); } catch { }
                return;
            }

            ReleaseUnityTcpLease();
            try { exitedProcess?.Dispose(); } catch { }
            process = null;
            attached = false;
            port = 0;
            Exited?.Invoke();
        }

        private static bool IsPortFree(int candidatePort)
        {
            TcpListener listener = null;
            try
            {
                listener = new TcpListener(IPAddress.Loopback, candidatePort);
                listener.Start();
                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                if (listener != null)
                {
                    listener.Stop();
                }
            }
        }

        private static int GetFreePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            try
            {
                listener.Start();
                return ((IPEndPoint)listener.LocalEndpoint).Port;
            }
            finally
            {
                listener.Stop();
            }
        }

        private static bool IsBackendHealthy(int candidatePort)
        {
            try
            {
                var request = WebRequest.CreateHttp($"http://127.0.0.1:{candidatePort}/health");
                request.Timeout = 500;
                using (var response = (HttpWebResponse)request.GetResponse())
                {
                    return response.StatusCode == HttpStatusCode.OK;
                }
            }
            catch
            {
                return false;
            }
        }

        private static void RequestBackendShutdown(int candidatePort)
        {
            try
            {
                var request = WebRequest.CreateHttp($"http://127.0.0.1:{candidatePort}/shutdown");
                request.Timeout = 500;
                using (request.GetResponse())
                {
                    // Backend exits itself after acknowledging the request.
                }
            }
            catch
            {
                // The backend may already be gone.
            }
        }

        private static void EnsureBackendExists()
        {
            if (File.Exists(InternalTerminalPaths.BackendEntry))
            {
                return;
            }

            throw new FileNotFoundException("Internal Terminal package files are missing. Reinstall the package.");
        }

        private void EnsureDependencies()
        {
            var nodeModules = Path.Combine(InternalTerminalPaths.BackendRoot, "node_modules");
            if (dependenciesInstalled || Directory.Exists(nodeModules))
            {
                dependenciesInstalled = true;
                return;
            }

            EmitLog("Installing terminal backend npm dependencies...");
            var startInfo = InternalTerminalProcess.CreateStartInfo(
                InternalTerminalSettings.instance.NpmExecutable,
                "install --no-audit --no-fund",
                InternalTerminalPaths.BackendRoot,
                true);

            using (var install = Process.Start(startInfo))
            {
                if (install == null)
                {
                    throw new InvalidOperationException("Failed to start npm install.");
                }

                var output = install.StandardOutput.ReadToEnd();
                var error = install.StandardError.ReadToEnd();
                install.WaitForExit();

                if (!string.IsNullOrWhiteSpace(output))
                {
                    EmitLog(output.Trim());
                }

                if (install.ExitCode != 0)
                {
                    throw new InvalidOperationException($"npm install failed:\n{error}");
                }
            }

            dependenciesInstalled = true;
        }

        private void EmitLog(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            EditorApplication.delayCall += () => LogReceived?.Invoke(message);
        }
    }
}
