using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace MCPForUnity.Editor.Setup
{
    public class McpForUnitySkillInstaller : EditorWindow
    {
        private const string RepoUrlKey = "UnityMcpSkillSync.RepoUrl";
        private const string BranchKey = "UnityMcpSkillSync.Branch";
        private const string CliKey = "UnityMcpSkillSync.Cli";
        private const string InstallDirKey = "UnityMcpSkillSync.InstallDir";
        private const string CodexCli = "codex";
        private const string ClaudeCli = "claude";
        private static readonly string[] BranchOptions = { "beta", "main" };
        private static readonly string[] CliOptions = { CodexCli, ClaudeCli };

        private string _repoUrl;
        private string _targetBranch;
        private string _cliType;
        private string _installDir;
        private Vector2 _scroll;
        private volatile bool _isRunning;
        private readonly ConcurrentQueue<string> _pendingLogs = new();
        private readonly StringBuilder _logBuilder = new(4096);

        [MenuItem("Window/MCP For Unity/Install(Sync) MCP Skill")]
        public static void OpenWindow()
        {
            GetWindow<McpForUnitySkillInstaller>("Unity MCP Skill Install(Sync)");
        }

        private void OnEnable()
        {
            var userHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            _repoUrl = EditorPrefs.GetString(RepoUrlKey, "https://github.com/CoplayDev/unity-mcp");
            _targetBranch = EditorPrefs.GetString(BranchKey, "beta");
            if (!BranchOptions.Contains(_targetBranch))
            {
                _targetBranch = "beta";
            }
            _cliType = EditorPrefs.GetString(CliKey, CodexCli);
            if (!CliOptions.Contains(_cliType))
            {
                _cliType = CodexCli;
            }
            _installDir = EditorPrefs.GetString(InstallDirKey, GetDefaultInstallDir(userHome, _cliType));
            EditorApplication.update += OnEditorUpdate;
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
            EditorPrefs.SetString(RepoUrlKey, _repoUrl);
            EditorPrefs.SetString(BranchKey, _targetBranch);
            EditorPrefs.SetString(CliKey, _cliType);
            EditorPrefs.SetString(InstallDirKey, _installDir);
        }

        private void OnGUI()
        {
            FlushPendingLogs();
            EditorGUILayout.HelpBox("Sync Unity MCP Skill to the latest on the selected branch and output the changed file list.", MessageType.Info);
            EditorGUILayout.Space(4f);

            EditorGUILayout.LabelField("Config", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(_isRunning))
            {
                _repoUrl = EditorGUILayout.TextField("Repo URL", _repoUrl);
                var branchIndex = Array.IndexOf(BranchOptions, _targetBranch);
                if (branchIndex < 0)
                {
                    branchIndex = 0;
                }

                var selectedBranchIndex = EditorGUILayout.Popup("Branch", branchIndex, BranchOptions);
                _targetBranch = BranchOptions[selectedBranchIndex];

                var cliIndex = Array.IndexOf(CliOptions, _cliType);
                if (cliIndex < 0)
                {
                    cliIndex = 0;
                }

                var selectedCliIndex = EditorGUILayout.Popup("CLI", cliIndex, CliOptions);
                if (selectedCliIndex != cliIndex)
                {
                    var previousCli = _cliType;
                    _cliType = CliOptions[selectedCliIndex];
                    TryApplyCliDefaultInstallPath(previousCli, _cliType);
                }

                _installDir = EditorGUILayout.TextField("Install Dir", _installDir);
            }

            EditorGUILayout.Space(8f);
            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(_isRunning))
            {
                if (GUILayout.Button($"Sync Latest ({_targetBranch})", GUILayout.Height(32f)))
                {
                    AppendLineImmediate("Sync task queued...");
                    AppendLineImmediate("Will use GitHub API to read the remote directory tree and perform incremental sync (no repository clone).");
                    RunSyncLatest();
                }
            }

            if (GUILayout.Button("Clear Log", GUILayout.Width(100f), GUILayout.Height(32f)))
            {
                _logBuilder.Clear();
                while (_pendingLogs.TryDequeue(out _))
                {
                }
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Output", EditorStyles.boldLabel);
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            EditorGUILayout.TextArea(_logBuilder.ToString(), GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
        }

        private void OnEditorUpdate()
        {
            var changed = FlushPendingLogs();
            if (_isRunning || changed)
            {
                Repaint();
            }
        }

        private void RunSyncLatest()
        {
            if (_isRunning)
            {
                return;
            }

            _isRunning = true;
            SkillSyncService.SyncAsync(_repoUrl, _installDir, _targetBranch,
                line => _pendingLogs.Enqueue($"[{DateTime.Now:HH:mm:ss}] {SanitizeLogLine(line)}"),
                result =>
                {
                    _isRunning = false;
                    if (result.Success)
                    {
                        _pendingLogs.Enqueue($"[{DateTime.Now:HH:mm:ss}] Sync complete: +{result.Added} ~{result.Updated} -{result.Deleted}");
                    }
                    else
                    {
                        _pendingLogs.Enqueue($"[{DateTime.Now:HH:mm:ss}] [ERROR] {result.Error}");
                    }
                });
        }

        private void TryApplyCliDefaultInstallPath(string previousCli, string currentCli)
        {
            var userHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var previousDefaultInstall = GetDefaultInstallDir(userHome, previousCli);
            var currentDefaultInstall = GetDefaultInstallDir(userHome, currentCli);

            if (string.IsNullOrWhiteSpace(_installDir) || PathsEqual(_installDir, previousDefaultInstall))
            {
                _installDir = currentDefaultInstall;
            }
        }

        private static string GetDefaultInstallDir(string userHome, string cliType)
        {
            var baseDir = IsClaudeCli(cliType) ? ".claude" : ".codex";
            return Path.Combine(userHome, baseDir, "skills/unity-mcp-skill");
        }

        private static bool IsClaudeCli(string cliType)
        {
            return string.Equals(cliType, ClaudeCli, StringComparison.Ordinal);
        }

        private static bool PathsEqual(string left, string right)
        {
            if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            {
                return false;
            }

            try
            {
                return string.Equals(
                    SkillSyncService.ExpandPath(left),
                    SkillSyncService.ExpandPath(right),
                    StringComparison.Ordinal);
            }
            catch
            {
                return false;
            }
        }

        private void AppendLineImmediate(string line)
        {
            var sanitized = SanitizeLogLine(line);
            if (string.IsNullOrWhiteSpace(sanitized))
            {
                return;
            }

            _logBuilder.AppendLine($"[{DateTime.Now:HH:mm:ss}] {sanitized}");
            _scroll.y = float.MaxValue;
            Repaint();
        }

        private bool FlushPendingLogs()
        {
            var hasNewLine = false;
            while (_pendingLogs.TryDequeue(out var line))
            {
                _logBuilder.AppendLine(line);
                hasNewLine = true;
            }

            if (hasNewLine)
            {
                _scroll.y = float.MaxValue;
            }

            return hasNewLine;
        }

        private static string SanitizeLogLine(string line)
        {
            if (string.IsNullOrEmpty(line))
            {
                return string.Empty;
            }

            var sb = new StringBuilder(line.Length);
            var inEscape = false;
            foreach (var ch in line)
            {
                if (inEscape)
                {
                    if (ch >= '@' && ch <= '~')
                    {
                        inEscape = false;
                    }
                    continue;
                }

                if (ch == '\u001b')
                {
                    inEscape = true;
                    continue;
                }

                if (ch == '\t' || (ch >= ' ' && ch != 127))
                {
                    sb.Append(ch);
                }
            }

            return sb.ToString().Trim();
        }
    }
}
