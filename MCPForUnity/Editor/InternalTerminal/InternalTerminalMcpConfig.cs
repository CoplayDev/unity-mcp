using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Services;
using UnityEngine;

namespace WTL.InternalTerminal.Editor
{
    internal static class InternalTerminalMcpConfig
    {
        private const string CodexServerName = "unityMCPInternal";
        private const string ExternalCodexServerName = "unityMCP";
        private const string ClaudeServerName = "UnityMCPInternal";
        private const string InternalClientId = "wtl-internal-terminal";

        public static PreparedConfig Prepare(int unityPort)
        {
            var root = Path.Combine(Application.temporaryCachePath, "WTLInternalTerminal", "Mcp");
            var binDir = Path.Combine(root, "bin");
            Directory.CreateDirectory(root);
            Directory.CreateDirectory(binDir);

            var uvxCommand = BuildUvxCommand();
            var env = BuildInternalEnv(unityPort);

            var codexProfileName = "unity-internal";
            var codexConfigPath = Path.Combine(GetCodexHome(), codexProfileName + ".config.toml");
            Directory.CreateDirectory(Path.GetDirectoryName(codexConfigPath));
            File.WriteAllText(codexConfigPath, BuildCodexConfig(uvxCommand, env), new UTF8Encoding(false));

            var claudeConfigPath = Path.Combine(root, "claude-mcp.json");
            File.WriteAllText(claudeConfigPath, BuildClaudeConfig(uvxCommand, env), new UTF8Encoding(false));

            WriteWrappers(binDir, codexProfileName, claudeConfigPath);

            return new PreparedConfig(root, binDir, codexConfigPath, codexProfileName, claudeConfigPath);
        }

        public static void Inject(ProcessStartInfo startInfo, PreparedConfig config)
        {
            if (startInfo == null || config == null)
            {
                return;
            }

            startInfo.EnvironmentVariables["UNITY_MCP_INTERNAL_CODEX_SERVER_NAME"] = CodexServerName;
            startInfo.EnvironmentVariables["UNITY_MCP_INTERNAL_CLAUDE_SERVER_NAME"] = ClaudeServerName;
            startInfo.EnvironmentVariables["UNITY_MCP_INTERNAL_CODEX_PROFILE"] = config.CodexProfileName;
            startInfo.EnvironmentVariables["UNITY_MCP_INTERNAL_CODEX_CONFIG"] = config.CodexConfigPath;
            startInfo.EnvironmentVariables["UNITY_MCP_INTERNAL_CLAUDE_CONFIG"] = config.ClaudeConfigPath;

            PrependPath(startInfo, config.BinDirectory);
        }

        private static UvxCommand BuildUvxCommand()
        {
            var (uvxPath, _, packageName) = AssetPathUtility.GetUvxCommandParts();
            var args = new List<string>();
            args.AddRange(AssetPathUtility.GetUvxDevFlagsList());
            args.AddRange(AssetPathUtility.GetBetaServerFromArgsList());
            args.Add(packageName);
            args.Add("--transport");
            args.Add("stdio");

            return new UvxCommand(string.IsNullOrWhiteSpace(uvxPath) ? "uvx" : uvxPath, args);
        }

        private static Dictionary<string, string> BuildInternalEnv(int unityPort)
        {
            var env = new Dictionary<string, string>
            {
                ["UNITY_MCP_INTERNAL_HOST"] = "127.0.0.1",
                ["UNITY_MCP_INTERNAL_PORT"] = unityPort.ToString(),
                ["UNITY_MCP_INTERNAL_ROLE"] = "internal",
                ["UNITY_MCP_INTERNAL_CLIENT_ID"] = InternalClientId,
                ["UNITY_MCP_LEGACY_PORT"] = unityPort.ToString(),
                ["FASTMCP_SHOW_SERVER_BANNER"] = "false",
                ["NO_COLOR"] = "1",
                ["PYTHONIOENCODING"] = "utf-8",
                ["PYTHONUTF8"] = "1",
            };

            var platformService = MCPServiceLocator.Platform;
            if (platformService.IsWindows())
            {
                env["SystemRoot"] = platformService.GetSystemRoot();
                env["ComSpec"] = Path.Combine(platformService.GetSystemRoot(), "System32", "cmd.exe");
                env["PATHEXT"] = ".COM;.EXE;.BAT;.CMD;.VBS;.VBE;.JS;.JSE;.WSF;.WSH;.MSC;.CPL";
                env["Path"] = BuildWindowsPath();
            }

            return env;
        }

        private static string BuildCodexConfig(UvxCommand uvxCommand, Dictionary<string, string> env)
        {
            var builder = new StringBuilder();
            builder.Append("[mcp_servers.");
            builder.Append(ExternalCodexServerName);
            builder.AppendLine("]");
            builder.AppendLine("enabled = false");
            builder.AppendLine();

            builder.Append("[mcp_servers.");
            builder.Append(CodexServerName);
            builder.AppendLine("]");
            builder.Append("command = ");
            builder.AppendLine(TomlString(uvxCommand.Command));
            builder.Append("args = [");
            builder.Append(string.Join(", ", uvxCommand.Args.Select(TomlString)));
            builder.AppendLine("]");
            builder.AppendLine("startup_timeout_sec = 60");
            builder.AppendLine();

            builder.Append("[mcp_servers.");
            builder.Append(CodexServerName);
            builder.AppendLine(".env]");
            foreach (var pair in env.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                if (string.IsNullOrEmpty(pair.Value))
                {
                    continue;
                }

                builder.Append(pair.Key);
                builder.Append(" = ");
                builder.AppendLine(TomlString(pair.Value));
            }

            return builder.ToString();
        }

        private static string BuildClaudeConfig(UvxCommand uvxCommand, Dictionary<string, string> env)
        {
            var builder = new StringBuilder();
            builder.AppendLine("{");
            builder.AppendLine("  \"mcpServers\": {");
            builder.Append("    ");
            builder.Append(JsonString(ClaudeServerName));
            builder.AppendLine(": {");
            builder.AppendLine("      \"type\": \"stdio\",");
            builder.Append("      \"command\": ");
            builder.Append(JsonString(uvxCommand.Command));
            builder.AppendLine(",");
            builder.Append("      \"args\": [");
            builder.Append(string.Join(", ", uvxCommand.Args.Select(JsonString)));
            builder.AppendLine("],");
            builder.AppendLine("      \"env\": {");

            var envPairs = env
                .Where(pair => !string.IsNullOrEmpty(pair.Value))
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .ToArray();
            for (var index = 0; index < envPairs.Length; index++)
            {
                var pair = envPairs[index];
                builder.Append("        ");
                builder.Append(JsonString(pair.Key));
                builder.Append(": ");
                builder.Append(JsonString(pair.Value));
                builder.AppendLine(index + 1 == envPairs.Length ? string.Empty : ",");
            }

            builder.AppendLine("      }");
            builder.AppendLine("    }");
            builder.AppendLine("  }");
            builder.AppendLine("}");
            return builder.ToString();
        }

        private static string GetCodexHome()
        {
            var codexHome = Environment.GetEnvironmentVariable("CODEX_HOME");
            if (!string.IsNullOrWhiteSpace(codexHome))
            {
                return codexHome;
            }

            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
        }

        private static void WriteWrappers(string binDir, string codexProfileName, string claudeConfigPath)
        {
            var codexPath = ResolveExecutable("codex");
            var claudePath = ResolveExecutable("claude");

            if (Application.platform == RuntimePlatform.WindowsEditor)
            {
                WriteWindowsWrapper(Path.Combine(binDir, "codex.cmd"), codexPath, $"--profile-v2 {QuoteCmd(codexProfileName)}");
                WriteWindowsWrapper(Path.Combine(binDir, "claude.cmd"), claudePath, $"--mcp-config {QuoteCmd(claudeConfigPath)} --strict-mcp-config");
                return;
            }

            WriteUnixWrapper(Path.Combine(binDir, "codex"), codexPath, $"--profile-v2 {QuoteShell(codexProfileName)}");
            WriteUnixWrapper(Path.Combine(binDir, "claude"), claudePath, $"--mcp-config {QuoteShell(claudeConfigPath)} --strict-mcp-config");
        }

        private static void WriteWindowsWrapper(string path, string executable, string injectedArgs)
        {
            if (string.IsNullOrEmpty(executable))
            {
                var missingName = Path.GetFileNameWithoutExtension(path);
                File.WriteAllText(path, $"@echo off\r\necho {missingName} CLI was not found when the internal terminal started.\r\nexit /b 1\r\n", new UTF8Encoding(false));
                return;
            }

            var content = "@echo off\r\n"
                + "setlocal\r\n"
                + BuildWindowsInvocation(executable, injectedArgs)
                + "\r\nexit /b %ERRORLEVEL%\r\n";
            File.WriteAllText(path, content, new UTF8Encoding(false));
        }

        private static string BuildWindowsInvocation(string executable, string injectedArgs)
        {
            if (executable.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase))
            {
                return "powershell.exe -NoProfile -ExecutionPolicy Bypass -File "
                    + QuoteCmd(executable)
                    + " "
                    + injectedArgs
                    + " %*";
            }

            return "call " + QuoteCmd(executable) + " " + injectedArgs + " %*";
        }

        private static void WriteUnixWrapper(string path, string executable, string injectedArgs)
        {
            if (string.IsNullOrEmpty(executable))
            {
                var missingName = Path.GetFileName(path);
                File.WriteAllText(path, "#!/usr/bin/env sh\n"
                    + $"echo '{missingName} CLI was not found when the internal terminal started.' >&2\n"
                    + "exit 1\n", new UTF8Encoding(false));
                TryChmodExecutable(path);
                return;
            }

            var content = "#!/usr/bin/env sh\n"
                + $"exec {QuoteShell(executable)} {injectedArgs} \"$@\"\n";
            File.WriteAllText(path, content, new UTF8Encoding(false));
            TryChmodExecutable(path);
        }

        private static string ResolveExecutable(string executable)
        {
            var candidates = new List<string>();
            var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            foreach (var directory in pathEnv.Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(directory))
                {
                    continue;
                }

                if (Application.platform == RuntimePlatform.WindowsEditor)
                {
                    candidates.Add(Path.Combine(directory, executable + ".exe"));
                    candidates.Add(Path.Combine(directory, executable + ".cmd"));
                    candidates.Add(Path.Combine(directory, executable + ".bat"));
                    candidates.Add(Path.Combine(directory, executable + ".ps1"));
                }
                else
                {
                    candidates.Add(Path.Combine(directory, executable));
                }
            }

            foreach (var candidate in candidates)
            {
                try
                {
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
                catch { }
            }

            return null;
        }

        private static void TryChmodExecutable(string path)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "chmod",
                    Arguments = "+x " + QuoteShell(path),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using var process = Process.Start(startInfo);
                process?.WaitForExit(1000);
            }
            catch
            {
                // Best effort; generated wrappers still document the launch flags.
            }
        }

        private static void PrependPath(ProcessStartInfo startInfo, string directory)
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                return;
            }

            var currentPath = GetExistingPath(startInfo);
            var updated = string.IsNullOrEmpty(currentPath)
                ? directory
                : directory + Path.PathSeparator + currentPath;

            startInfo.EnvironmentVariables["PATH"] = updated;
            if (Application.platform == RuntimePlatform.WindowsEditor)
            {
                startInfo.EnvironmentVariables["Path"] = updated;
            }
        }

        private static string GetExistingPath(ProcessStartInfo startInfo)
        {
            foreach (var key in new[] { "Path", "PATH", "path" })
            {
                var value = startInfo.EnvironmentVariables[key];
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            if (Application.platform == RuntimePlatform.WindowsEditor)
            {
                return BuildWindowsPath();
            }

            return Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        }

        private static string BuildWindowsPath()
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var directories = new List<string>();

            AddPathEntries(directories, seen, Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.Process));
            AddPathEntries(directories, seen, Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.Machine));
            AddPathEntries(directories, seen, Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.User));
            AddDirectory(directories, seen, Environment.SystemDirectory);
            AddDirectory(directories, seen, Path.GetDirectoryName(Environment.SystemDirectory));
            AddDirectory(directories, seen, Path.Combine(Environment.SystemDirectory, "Wbem"));
            AddDirectory(directories, seen, Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0"));
            AddDirectory(directories, seen, Path.Combine(Environment.SystemDirectory, "OpenSSH"));
            AddDirectory(directories, seen, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "nodejs"));
            AddDirectory(directories, seen, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm"));

            return string.Join(Path.PathSeparator.ToString(), directories);
        }

        private static void AddPathEntries(List<string> directories, HashSet<string> seen, string pathValue)
        {
            if (string.IsNullOrWhiteSpace(pathValue))
            {
                return;
            }

            foreach (var rawPath in pathValue.Split(Path.PathSeparator))
            {
                AddDirectory(directories, seen, rawPath);
            }
        }

        private static void AddDirectory(List<string> directories, HashSet<string> seen, string directory)
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                return;
            }

            var expanded = Environment.ExpandEnvironmentVariables(directory.Trim());
            if (Directory.Exists(expanded) && seen.Add(expanded))
            {
                directories.Add(expanded);
            }
        }

        private static string QuoteCmd(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "\"\"";
            }

            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        private static string QuoteShell(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "''";
            }

            return "'" + value.Replace("'", "'\\''") + "'";
        }

        private static string TomlString(string value)
        {
            return "\"" + EscapeCommon(value) + "\"";
        }

        private static string JsonString(string value)
        {
            return "\"" + EscapeCommon(value) + "\"";
        }

        private static string EscapeCommon(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(value.Length);
            foreach (var ch in value)
            {
                switch (ch)
                {
                    case '\\':
                        builder.Append("\\\\");
                        break;
                    case '"':
                        builder.Append("\\\"");
                        break;
                    case '\b':
                        builder.Append("\\b");
                        break;
                    case '\f':
                        builder.Append("\\f");
                        break;
                    case '\n':
                        builder.Append("\\n");
                        break;
                    case '\r':
                        builder.Append("\\r");
                        break;
                    case '\t':
                        builder.Append("\\t");
                        break;
                    default:
                        if (ch < ' ')
                        {
                            builder.Append("\\u");
                            builder.Append(((int)ch).ToString("x4"));
                        }
                        else
                        {
                            builder.Append(ch);
                        }
                        break;
                }
            }

            return builder.ToString();
        }

        private sealed class UvxCommand
        {
            public UvxCommand(string command, IReadOnlyList<string> args)
            {
                Command = command;
                Args = args;
            }

            public string Command { get; }
            public IReadOnlyList<string> Args { get; }
        }

        public sealed class PreparedConfig
        {
            public PreparedConfig(
                string rootDirectory,
                string binDirectory,
                string codexConfigPath,
                string codexProfileName,
                string claudeConfigPath)
            {
                RootDirectory = rootDirectory;
                BinDirectory = binDirectory;
                CodexConfigPath = codexConfigPath;
                CodexProfileName = codexProfileName;
                ClaudeConfigPath = claudeConfigPath;
            }

            public string RootDirectory { get; }
            public string BinDirectory { get; }
            public string CodexConfigPath { get; }
            public string CodexProfileName { get; }
            public string ClaudeConfigPath { get; }
        }
    }
}
