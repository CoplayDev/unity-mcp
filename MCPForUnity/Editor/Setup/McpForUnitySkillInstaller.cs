using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
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
        private const string LastSyncedCommitKey = "UnityMcpSkillSync.LastSyncedCommit";
        private const string FixedSkillSubdir = "unity-mcp-skill";
        private const string SyncOwnershipMarker = ".unity-mcp-skill-sync";
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
        private readonly ConcurrentQueue<Action> _mainThreadActions = new();
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
            ExecuteMainThreadActions();
            var changed = FlushPendingLogs();
            if (_isRunning || changed)
            {
                Repaint();
            }
        }

        private void RunSyncLatest()
        {
            var lastSyncedCommitKey = GetLastSyncedCommitKey();
            var lastSyncedCommit = EditorPrefs.GetString(lastSyncedCommitKey, string.Empty);
            ExecuteWithGuard(() =>
            {
                AppendLine("=== Sync Start ===");
                if (!TryParseGitHubRepository(_repoUrl, out var repoInfo))
                {
                    throw new InvalidOperationException($"Repo URL is not a recognized GitHub repository URL: {_repoUrl}");
                }

                AppendLine($"Target repository: {repoInfo.Owner}/{repoInfo.Repo}@{_targetBranch}");
                var snapshot = FetchRemoteSnapshot(repoInfo, _targetBranch, FixedSkillSubdir);
                var installPath = GetInstallPath();

                if (!Directory.Exists(installPath))
                {
                    Directory.CreateDirectory(installPath);
                }

                var localFiles = ListFiles(installPath);
                var pathComparison = GetPathComparison(installPath);
                var pathComparer = GetPathComparer(pathComparison);
                EnsureManagedInstallRoot(installPath, localFiles.Keys, snapshot.Files.Keys, pathComparer);
                var plan = BuildPlan(snapshot.Files, localFiles, pathComparer);
                var commitChanged = !string.Equals(lastSyncedCommit, snapshot.CommitSha, StringComparison.Ordinal);

                AppendLine($"Remote Commit: {ShortCommit(lastSyncedCommit)} -> {ShortCommit(snapshot.CommitSha)}");
                AppendLine(commitChanged
                    ? $"Commit: detected newer commit on {_targetBranch}."
                    : $"Commit: no new commit on {_targetBranch} since last sync.");
                AppendLine($"Plan => Added:{plan.Added.Count} Updated:{plan.Updated.Count} Deleted:{plan.Deleted.Count}");
                AppendSummary(plan, commitChanged);
                LogPlanDetails(plan);

                ApplyPlan(repoInfo, snapshot.CommitSha, snapshot.SubdirPath, installPath, plan, pathComparison);
                AppendLine("Files mirrored to install directory.");

                ValidateFileHashes(installPath, snapshot.Files, pathComparison);
                EnqueueMainThreadAction(() => EditorPrefs.SetString(lastSyncedCommitKey, snapshot.CommitSha));
                AppendLine($"Synced to commit: {snapshot.CommitSha}");
                AppendLine("=== Sync Done ===");
            });
        }

        private void ExecuteWithGuard(Action action)
        {
            if (_isRunning)
            {
                return;
            }

            _isRunning = true;
            Task.Run(() =>
            {
                try
                {
                    action.Invoke();
                }
                catch (Exception ex)
                {
                    AppendLine($"[ERROR] {ex.Message}");
                }
                finally
                {
                    _isRunning = false;
                }
            });
        }

        private string GetLastSyncedCommitKey()
        {
            var scope = $"{_repoUrl}|{_targetBranch}|{NormalizeRemotePath(FixedSkillSubdir)}";
            using var sha256 = SHA256.Create();
            var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(scope));
            var suffix = BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
            return $"{LastSyncedCommitKey}.{suffix}";
        }

        private static bool TryParseGitHubRepository(string url, out GitHubRepoInfo repoInfo)
        {
            repoInfo = default;
            if (string.IsNullOrWhiteSpace(url))
            {
                return false;
            }

            var trimmed = url.Trim();
            if (trimmed.StartsWith("git@github.com:", StringComparison.OrdinalIgnoreCase))
            {
                var repoPath = trimmed.Substring("git@github.com:".Length).Trim('/');
                return TryParseOwnerAndRepo(repoPath, out repoInfo);
            }

            if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
            {
                return false;
            }

            if (!string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var repoPathFromUri = uri.AbsolutePath.Trim('/');
            return TryParseOwnerAndRepo(repoPathFromUri, out repoInfo);
        }

        private static bool TryParseOwnerAndRepo(string path, out GitHubRepoInfo repoInfo)
        {
            repoInfo = default;
            var segments = path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length < 2)
            {
                return false;
            }

            var owner = segments[0].Trim();
            var repo = segments[1].Trim();
            if (repo.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            {
                repo = repo.Substring(0, repo.Length - 4);
            }

            if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(repo))
            {
                return false;
            }

            repoInfo = new GitHubRepoInfo(owner, repo);
            return true;
        }

        private RemoteSnapshot FetchRemoteSnapshot(GitHubRepoInfo repoInfo, string branch, string subdir)
        {
            using var client = CreateGitHubClient();
            var commitSha = FetchBranchHeadCommitSha(client, repoInfo, branch);
            var treeApiUrl = BuildTreeApiUrl(repoInfo, commitSha);
            AppendLine($"Fetching remote directory tree at commit {ShortCommit(commitSha)}: {treeApiUrl}");
            var json = DownloadString(client, treeApiUrl);
            var treeResponse = JsonUtility.FromJson<GitHubTreeResponse>(json);
            if (treeResponse == null || treeResponse.tree == null)
            {
                throw new InvalidOperationException("Failed to parse GitHub directory tree response.");
            }

            if (treeResponse.truncated)
            {
                throw new InvalidOperationException(
                    "GitHub returned a truncated directory tree (incomplete snapshot). " +
                    "Sync was aborted to prevent accidental deletion of valid local files.");
            }

            var normalizedSubdir = NormalizeRemotePath(subdir);
            var subdirPrefix = string.IsNullOrEmpty(normalizedSubdir) ? string.Empty : $"{normalizedSubdir}/";
            var remoteFiles = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var entry in treeResponse.tree)
            {
                if (!string.Equals(entry.type, "blob", StringComparison.Ordinal))
                {
                    continue;
                }

                var remotePath = NormalizeRemotePath(entry.path);
                if (string.IsNullOrEmpty(remotePath))
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(subdirPrefix) &&
                    !remotePath.StartsWith(subdirPrefix, StringComparison.Ordinal))
                {
                    continue;
                }

                var relativePath = string.IsNullOrEmpty(subdirPrefix)
                    ? remotePath
                    : remotePath.Substring(subdirPrefix.Length);
                if (string.IsNullOrWhiteSpace(relativePath) || string.IsNullOrWhiteSpace(entry.sha))
                {
                    continue;
                }

                if (!TryNormalizeRelativePath(relativePath, out var safeRelativePath))
                {
                    AppendLine($"Skip unsafe remote path: {remotePath}");
                    continue;
                }

                remoteFiles[safeRelativePath] = entry.sha.Trim().ToLowerInvariant();
            }

            if (remoteFiles.Count == 0)
            {
                throw new InvalidOperationException($"Remote directory not found: {normalizedSubdir}");
            }

            AppendLine($"Remote file count: {remoteFiles.Count}");
            return new RemoteSnapshot(commitSha, normalizedSubdir, remoteFiles);
        }

        private string FetchBranchHeadCommitSha(HttpClient client, GitHubRepoInfo repoInfo, string branch)
        {
            var branchApiUrl = BuildBranchApiUrl(repoInfo, branch);
            AppendLine($"Fetching branch head commit: {branchApiUrl}");
            var branchJson = DownloadString(client, branchApiUrl);
            var branchResponse = JsonUtility.FromJson<GitHubBranchResponse>(branchJson);
            var commitSha = branchResponse?.commit?.sha?.Trim();
            if (string.IsNullOrWhiteSpace(commitSha))
            {
                throw new InvalidOperationException($"Failed to resolve branch head commit SHA for: {branch}");
            }

            return commitSha;
        }

        private static string BuildBranchApiUrl(GitHubRepoInfo repoInfo, string branch)
        {
            return $"https://api.github.com/repos/{Uri.EscapeDataString(repoInfo.Owner)}/{Uri.EscapeDataString(repoInfo.Repo)}/branches/{Uri.EscapeDataString(branch)}";
        }

        private static string BuildTreeApiUrl(GitHubRepoInfo repoInfo, string reference)
        {
            return $"https://api.github.com/repos/{Uri.EscapeDataString(repoInfo.Owner)}/{Uri.EscapeDataString(repoInfo.Repo)}/git/trees/{Uri.EscapeDataString(reference)}?recursive=1";
        }

        private static string BuildRawFileUrl(GitHubRepoInfo repoInfo, string commitSha, string remoteFilePath)
        {
            var encodedPath = string.Join("/",
                NormalizeRemotePath(remoteFilePath)
                    .Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(Uri.EscapeDataString));
            return $"https://raw.githubusercontent.com/{Uri.EscapeDataString(repoInfo.Owner)}/{Uri.EscapeDataString(repoInfo.Repo)}/{Uri.EscapeDataString(commitSha)}/{encodedPath}";
        }

        private static HttpClient CreateGitHubClient()
        {
            var client = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(60)
            };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("UnityMcpSkillSyncWindow/1.0");
            client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            return client;
        }

        private static string DownloadString(HttpClient client, string url)
        {
            using var response = client.GetAsync(url).GetAwaiter().GetResult();
            var body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"GitHub request failed: {(int)response.StatusCode} {response.ReasonPhrase} ({url})\n{body}");
            }

            return body;
        }

        private static byte[] DownloadBytes(HttpClient client, string url)
        {
            using var response = client.GetAsync(url).GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode)
            {
                var body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                throw new InvalidOperationException($"File download failed: {(int)response.StatusCode} {response.ReasonPhrase} ({url})\n{body}");
            }

            return response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
        }

        private static string NormalizeRemotePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            return path.Replace('\\', '/').Trim().Trim('/');
        }

        private static string CombineRemotePath(string left, string right)
        {
            var normalizedLeft = NormalizeRemotePath(left);
            var normalizedRight = NormalizeRemotePath(right);
            if (string.IsNullOrEmpty(normalizedLeft))
            {
                return normalizedRight;
            }

            if (string.IsNullOrEmpty(normalizedRight))
            {
                return normalizedLeft;
            }

            return $"{normalizedLeft}/{normalizedRight}";
        }

        private static bool TryNormalizeRelativePath(string relativePath, out string normalizedPath)
        {
            normalizedPath = NormalizeRemotePath(relativePath);
            if (string.IsNullOrWhiteSpace(normalizedPath) || Path.IsPathRooted(normalizedPath))
            {
                return false;
            }

            var segments = normalizedPath.Split('/');
            if (segments.Length == 0)
            {
                return false;
            }

            foreach (var segment in segments)
            {
                if (string.IsNullOrWhiteSpace(segment) ||
                    string.Equals(segment, ".", StringComparison.Ordinal) ||
                    string.Equals(segment, "..", StringComparison.Ordinal) ||
                    segment.IndexOf(':') >= 0)
                {
                    return false;
                }
            }

            normalizedPath = string.Join("/", segments);
            return true;
        }

        private static string ResolvePathUnderRoot(string root, string relativePath, StringComparison pathComparison)
        {
            if (!TryNormalizeRelativePath(relativePath, out var safeRelativePath))
            {
                throw new InvalidOperationException($"Unsafe relative path: {relativePath}");
            }

            var normalizedRoot = EnsureTrailingDirectorySeparator(Path.GetFullPath(root));
            var combinedPath = Path.Combine(normalizedRoot, safeRelativePath.Replace('/', Path.DirectorySeparatorChar));
            var fullPath = Path.GetFullPath(combinedPath);
            if (!fullPath.StartsWith(normalizedRoot, pathComparison))
            {
                throw new InvalidOperationException($"Path escapes install root: {relativePath}");
            }

            return fullPath;
        }

        private static string EnsureTrailingDirectorySeparator(string path)
        {
            return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        }

        private static SyncPlan BuildPlan(Dictionary<string, string> remoteFiles, Dictionary<string, string> localFiles, StringComparer pathComparer)
        {
            var plan = new SyncPlan();
            var localLookup = new Dictionary<string, string>(pathComparer);
            foreach (var localEntry in localFiles)
            {
                if (!localLookup.ContainsKey(localEntry.Key))
                {
                    localLookup[localEntry.Key] = localEntry.Value;
                }
            }

            foreach (var remoteEntry in remoteFiles)
            {
                if (!localLookup.TryGetValue(remoteEntry.Key, out var localPath))
                {
                    plan.Added.Add(remoteEntry.Key);
                    continue;
                }

                var localBlobSha = ComputeGitBlobSha1(localPath);
                if (!string.Equals(localBlobSha, remoteEntry.Value, StringComparison.Ordinal))
                {
                    plan.Updated.Add(remoteEntry.Key);
                }
            }

            var remoteLookup = new HashSet<string>(remoteFiles.Keys, pathComparer);
            foreach (var localRelativePath in localFiles.Keys)
            {
                if (!remoteLookup.Contains(localRelativePath))
                {
                    plan.Deleted.Add(localRelativePath);
                }
            }

            plan.Added.Sort(StringComparer.Ordinal);
            plan.Updated.Sort(StringComparer.Ordinal);
            plan.Deleted.Sort(StringComparer.Ordinal);
            return plan;
        }

        private void ApplyPlan(GitHubRepoInfo repoInfo, string commitSha, string remoteSubdir, string targetRoot, SyncPlan plan, StringComparison pathComparison)
        {
            using var client = CreateGitHubClient();
            foreach (var relativePath in plan.Added.Concat(plan.Updated))
            {
                var remoteFilePath = CombineRemotePath(remoteSubdir, relativePath);
                var downloadUrl = BuildRawFileUrl(repoInfo, commitSha, remoteFilePath);
                var targetFile = ResolvePathUnderRoot(targetRoot, relativePath, pathComparison);
                var targetDirectory = Path.GetDirectoryName(targetFile);
                if (!string.IsNullOrEmpty(targetDirectory))
                {
                    Directory.CreateDirectory(targetDirectory);
                }

                AppendLine($"Download: {relativePath}");
                var bytes = DownloadBytes(client, downloadUrl);
                File.WriteAllBytes(targetFile, bytes);
            }

            foreach (var relativePath in plan.Deleted)
            {
                var targetFile = ResolvePathUnderRoot(targetRoot, relativePath, pathComparison);
                if (File.Exists(targetFile))
                {
                    File.Delete(targetFile);
                }
            }

            RemoveEmptyDirectories(targetRoot);
        }

        private void ValidateFileHashes(string installRoot, Dictionary<string, string> remoteFiles, StringComparison pathComparison)
        {
            var checkedCount = 0;
            foreach (var remoteEntry in remoteFiles)
            {
                var localPath = ResolvePathUnderRoot(installRoot, remoteEntry.Key, pathComparison);
                if (!File.Exists(localPath))
                {
                    throw new InvalidOperationException($"Missing synced file: {remoteEntry.Key}");
                }

                var localBlobSha = ComputeGitBlobSha1(localPath);
                if (!string.Equals(localBlobSha, remoteEntry.Value, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"File hash mismatch: {remoteEntry.Key} ({ShortHash(localBlobSha)} != {ShortHash(remoteEntry.Value)})");
                }

                checkedCount++;
            }

            AppendLine($"Hash checks passed ({checkedCount}/{remoteFiles.Count}).");
        }

        private static string ComputeGitBlobSha1(string filePath)
        {
            var bytes = File.ReadAllBytes(filePath);
            return ComputeGitBlobSha1(bytes);
        }

        private static string ComputeGitBlobSha1(byte[] bytes)
        {
            var headerBytes = Encoding.UTF8.GetBytes($"blob {bytes.Length}\0");
            using var sha1 = SHA1.Create();
            sha1.TransformBlock(headerBytes, 0, headerBytes.Length, null, 0);
            sha1.TransformFinalBlock(bytes, 0, bytes.Length);
            return BitConverter.ToString(sha1.Hash ?? Array.Empty<byte>()).Replace("-", string.Empty).ToLowerInvariant();
        }

        private static Dictionary<string, string> ListFiles(string root)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            if (!Directory.Exists(root))
            {
                return map;
            }

            var normalizedRoot = Path.GetFullPath(root);
            foreach (var filePath in Directory.GetFiles(normalizedRoot, "*", SearchOption.AllDirectories))
            {
                var relativePath = Path.GetRelativePath(normalizedRoot, filePath).Replace('\\', '/');
                if (string.Equals(relativePath, SyncOwnershipMarker, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                map[relativePath] = filePath;
            }

            return map;
        }

        private static void EnsureManagedInstallRoot(
            string installPath,
            ICollection<string> localRelativePaths,
            ICollection<string> remoteRelativePaths,
            StringComparer pathComparer)
        {
            var markerPath = Path.Combine(installPath, SyncOwnershipMarker);
            if (File.Exists(markerPath))
            {
                return;
            }

            if (localRelativePaths.Count > 0 && !CanAdoptLegacyManagedRoot(localRelativePaths, remoteRelativePaths, pathComparer))
            {
                throw new InvalidOperationException(
                    "Install Dir contains unmanaged files. " +
                    "Please choose an empty folder or an existing unity-mcp-skill folder.");
            }

            File.WriteAllText(markerPath, "managed-by-unity-mcp-skill-sync");
        }

        private static bool CanAdoptLegacyManagedRoot(
            ICollection<string> localRelativePaths,
            ICollection<string> remoteRelativePaths,
            StringComparer pathComparer)
        {
            if (localRelativePaths.Count == 0)
            {
                return true;
            }

            var remoteTopLevels = new HashSet<string>(pathComparer);
            foreach (var remotePath in remoteRelativePaths)
            {
                var topLevel = GetTopLevelSegment(remotePath);
                if (!string.IsNullOrWhiteSpace(topLevel))
                {
                    remoteTopLevels.Add(topLevel);
                }
            }

            if (remoteTopLevels.Count == 0)
            {
                return false;
            }

            var hasSkillDefinition = false;
            foreach (var localPath in localRelativePaths)
            {
                if (pathComparer.Equals(localPath, "SKILL.md"))
                {
                    hasSkillDefinition = true;
                }

                var topLevel = GetTopLevelSegment(localPath);
                if (string.IsNullOrWhiteSpace(topLevel) || !remoteTopLevels.Contains(topLevel))
                {
                    return false;
                }
            }

            return hasSkillDefinition;
        }

        private static string GetTopLevelSegment(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                return string.Empty;
            }

            var normalized = NormalizeRemotePath(relativePath);
            var separatorIndex = normalized.IndexOf('/');
            return separatorIndex < 0 ? normalized : normalized.Substring(0, separatorIndex);
        }

        private static StringComparison GetPathComparison(string root)
        {
            return IsCaseSensitiveFileSystem(root) ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        }

        private static StringComparer GetPathComparer(StringComparison pathComparison)
        {
            return pathComparison == StringComparison.Ordinal
                ? StringComparer.Ordinal
                : StringComparer.OrdinalIgnoreCase;
        }

        private static bool IsCaseSensitiveFileSystem(string root)
        {
            try
            {
                var probeName = $".mcp-case-probe-{Guid.NewGuid():N}";
                var lowercasePath = Path.Combine(root, probeName.ToLowerInvariant());
                var uppercasePath = Path.Combine(root, probeName.ToUpperInvariant());
                File.WriteAllText(lowercasePath, string.Empty);
                try
                {
                    return !File.Exists(uppercasePath);
                }
                finally
                {
                    if (File.Exists(lowercasePath))
                    {
                        File.Delete(lowercasePath);
                    }
                }
            }
            catch
            {
                // Conservative fallback for security checks.
                return true;
            }
        }

        private static void RemoveEmptyDirectories(string root)
        {
            if (!Directory.Exists(root))
            {
                return;
            }

            var directories = Directory.GetDirectories(root, "*", SearchOption.AllDirectories);
            Array.Sort(directories, (a, b) => string.CompareOrdinal(b, a));
            foreach (var directory in directories)
            {
                if (Directory.EnumerateFileSystemEntries(directory).Any())
                {
                    continue;
                }

                Directory.Delete(directory, false);
            }
        }

        private string GetInstallPath()
        {
            return ExpandPath(_installDir);
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
                return string.Equals(ExpandPath(left), ExpandPath(right), StringComparison.Ordinal);
            }
            catch
            {
                return false;
            }
        }

        private static string ExpandPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            var expanded = path.Trim();
            if (expanded.StartsWith("~", StringComparison.Ordinal))
            {
                var userHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                expanded = Path.Combine(userHome, expanded.Substring(1).TrimStart('/', '\\'));
            }

            return Path.GetFullPath(expanded);
        }

        private void EnqueueMainThreadAction(Action action)
        {
            if (action == null)
            {
                return;
            }

            _mainThreadActions.Enqueue(action);
        }

        private void ExecuteMainThreadActions()
        {
            while (_mainThreadActions.TryDequeue(out var action))
            {
                try
                {
                    action.Invoke();
                }
                catch (Exception ex)
                {
                    AppendLineImmediate($"[ERROR] Main-thread action execution failed: {ex.Message}");
                }
            }
        }

        private void AppendLine(string line)
        {
            var sanitized = SanitizeLogLine(line);
            if (string.IsNullOrWhiteSpace(sanitized))
            {
                return;
            }

            _pendingLogs.Enqueue($"[{DateTime.Now:HH:mm:ss}] {sanitized}");
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
                    // End ANSI escape sequence on final byte.
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

        private void LogPlanDetails(SyncPlan plan)
        {
            if (plan.Added.Count == 0 && plan.Updated.Count == 0 && plan.Deleted.Count == 0)
            {
                AppendLine("No file changes.");
                return;
            }

            foreach (var path in plan.Added)
            {
                AppendLine($"+ {path}");
            }

            foreach (var path in plan.Updated)
            {
                AppendLine($"~ {path}");
            }

            foreach (var path in plan.Deleted)
            {
                AppendLine($"- {path}");
            }
        }

        private void AppendSummary(SyncPlan plan, bool commitChanged)
        {
            var added = plan.Added.Count;
            var updated = plan.Updated.Count;
            var deleted = plan.Deleted.Count;

            if (added == 0 && updated == 0 && deleted == 0)
            {
                AppendLine("Conclusion: No file changes in this run.");
                return;
            }

            if (added == 0 && updated == 0 && deleted > 0)
            {
                AppendLine(commitChanged
                    ? "Conclusion: A new commit was detected, but skill content was unchanged; only local redundant files were cleaned up."
                    : "Conclusion: Skill content was unchanged; only local redundant files were cleaned up.");
                return;
            }

            AppendLine($"Conclusion: Skill files were updated (added {added}, modified {updated}, deleted {deleted}).");
        }

        private static string ShortCommit(string commit)
        {
            if (string.IsNullOrWhiteSpace(commit))
            {
                return "(none)";
            }

            return commit.Length <= 8 ? commit : commit.Substring(0, 8);
        }

        private static string ShortHash(string hash)
        {
            if (string.IsNullOrWhiteSpace(hash))
            {
                return "(none)";
            }

            return hash.Length <= 6 ? hash : hash.Substring(0, 6);
        }

        private readonly struct GitHubRepoInfo
        {
            public GitHubRepoInfo(string owner, string repo)
            {
                Owner = owner;
                Repo = repo;
            }

            public string Owner { get; }
            public string Repo { get; }
        }

        private readonly struct RemoteSnapshot
        {
            public RemoteSnapshot(string commitSha, string subdirPath, Dictionary<string, string> files)
            {
                CommitSha = commitSha;
                SubdirPath = subdirPath;
                Files = files;
            }

            public string CommitSha { get; }
            public string SubdirPath { get; }
            public Dictionary<string, string> Files { get; }
        }

        [Serializable]
        private sealed class GitHubTreeResponse
        {
            public string sha;
            public GitHubTreeEntry[] tree;
            public bool truncated;
        }

        [Serializable]
        private sealed class GitHubBranchResponse
        {
            public GitHubBranchCommit commit;
        }

        [Serializable]
        private sealed class GitHubBranchCommit
        {
            public string sha;
        }

        [Serializable]
        private sealed class GitHubTreeEntry
        {
            public string path;
            public string type;
            public string sha;
        }

        private sealed class SyncPlan
        {
            public List<string> Added { get; } = new();
            public List<string> Updated { get; } = new();
            public List<string> Deleted { get; } = new();
        }
    }
}
