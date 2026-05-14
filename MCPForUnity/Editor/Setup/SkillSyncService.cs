using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace MCPForUnity.Editor.Setup
{
    public static class SkillSyncService
    {
        private const string DefaultRepoUrl = "https://github.com/CoplayDev/unity-mcp";
        private const string PackageName = "com.coplaydev.unity-mcp";
        private const string SkillSubdir = ".claude/skills/unity-mcp-skill";
        private const string FallbackSkillSubdir = "unity-mcp-skill";
        private const string SyncOwnershipMarker = ".unity-mcp-skill-sync";
        private const string LastSyncedCommitKeyPrefix = "UnityMcpSkillSync.LastSyncedCommit";

        public sealed class SyncResult
        {
            public bool Success { get; set; }
            public int Added { get; set; }
            public int Updated { get; set; }
            public int Deleted { get; set; }
            public string CommitSha { get; set; }
            public string Error { get; set; }
        }

        public static void SyncAsync(string installDir, string branch, Action<string> log, Action<SyncResult> onComplete)
        {
            SyncAsync(GetDefaultRepoUrl(), installDir, branch, log, onComplete);
        }

        public static void SyncAsync(string repoUrl, string installDir, string branch, Action<string> log, Action<SyncResult> onComplete)
        {
            repoUrl = NormalizeGitPackageUrl(string.IsNullOrWhiteSpace(repoUrl) ? GetDefaultRepoUrl() : repoUrl);
            var lastSyncedCommitKey = GetLastSyncedCommitKey(repoUrl, branch);
            var lastSyncedCommit = EditorPrefs.GetString(lastSyncedCommitKey, string.Empty);

            Task.Run(() =>
            {
                try
                {
                    var result = RunSync(repoUrl, installDir, branch, lastSyncedCommit, log);
                    EditorApplication.delayCall += () =>
                    {
                        if (result.Success && !string.IsNullOrEmpty(result.CommitSha))
                        {
                            EditorPrefs.SetString(lastSyncedCommitKey, result.CommitSha);
                        }
                        onComplete?.Invoke(result);
                    };
                }
                catch (Exception ex)
                {
                    EditorApplication.delayCall += () =>
                    {
                        onComplete?.Invoke(new SyncResult { Success = false, Error = ex.Message });
                    };
                }
            });
        }

        private static SyncResult RunSync(string repoUrl, string installDir, string branch, string lastSyncedCommit, Action<string> log)
        {
            log?.Invoke("=== Sync Start ===");
            repoUrl = NormalizeGitPackageUrl(repoUrl);

            RemoteSnapshot snapshot = default;
            GitHubRepoInfo repoInfo = default;
            try
            {
                if (TryParseGitHubRepository(repoUrl, out repoInfo))
                {
                    log?.Invoke($"Target repository: {repoInfo.Owner}/{repoInfo.Repo}@{branch}");
                    try
                    {
                        snapshot = FetchRemoteSnapshot(repoInfo, branch, SkillSubdir, log);
                    }
                    catch (Exception ex)
                    {
                        log?.Invoke($"GitHub API sync unavailable ({ex.Message}). Falling back to git clone.");
                        snapshot = FetchGitSnapshot(repoUrl, branch, SkillSubdir, log);
                    }
                }
                else
                {
                    log?.Invoke($"Target git repository: {repoUrl}@{branch}");
                    snapshot = FetchGitSnapshot(repoUrl, branch, SkillSubdir, log);
                }

                var installPath = ResolveAndValidateInstallPath(installDir);

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

                log?.Invoke($"Remote Commit: {ShortCommit(lastSyncedCommit)} -> {ShortCommit(snapshot.CommitSha)}");
                log?.Invoke(commitChanged
                    ? $"Commit: detected newer commit on {branch}."
                    : $"Commit: no new commit on {branch} since last sync.");
                log?.Invoke($"Plan => Added:{plan.Added.Count} Updated:{plan.Updated.Count} Deleted:{plan.Deleted.Count}");
                LogPlanDetails(plan, log);

                ApplyPlan(repoInfo, snapshot, installPath, plan, pathComparison, log);
                log?.Invoke("Files mirrored to install directory.");

                ValidateFileHashes(installPath, snapshot.Files, pathComparison, log);
                log?.Invoke($"Synced to commit: {snapshot.CommitSha}");
                log?.Invoke("=== Sync Done ===");

                return new SyncResult
                {
                    Success = true,
                    Added = plan.Added.Count,
                    Updated = plan.Updated.Count,
                    Deleted = plan.Deleted.Count,
                    CommitSha = snapshot.CommitSha
                };
            }
            finally
            {
                CleanupSnapshot(snapshot, log);
            }
        }

        internal static string GetDefaultRepoUrl()
        {
            return TryGetPackageRepoUrlFromManifest(out var packageRepoUrl)
                ? packageRepoUrl
                : DefaultRepoUrl;
        }

        internal static string NormalizeGitPackageUrl(string repoUrl)
        {
            if (string.IsNullOrWhiteSpace(repoUrl))
            {
                return string.Empty;
            }

            var trimmed = repoUrl.Trim();
            var delimiterIndex = -1;
            foreach (var delimiter in new[] { '?', '#' })
            {
                var index = trimmed.IndexOf(delimiter);
                if (index >= 0 && (delimiterIndex < 0 || index < delimiterIndex))
                {
                    delimiterIndex = index;
                }
            }

            var withoutPackageSelectors = delimiterIndex >= 0
                ? trimmed.Substring(0, delimiterIndex)
                : trimmed;
            return withoutPackageSelectors.TrimEnd('/');
        }

        private static bool TryGetPackageRepoUrlFromManifest(out string repoUrl)
        {
            repoUrl = string.Empty;
            try
            {
                var manifestPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Packages", "manifest.json"));
                if (!File.Exists(manifestPath))
                {
                    return false;
                }

                var manifest = JObject.Parse(File.ReadAllText(manifestPath));
                var dependencyValue = manifest["dependencies"]?[PackageName]?.Value<string>();
                if (string.IsNullOrWhiteSpace(dependencyValue) || !LooksLikeGitPackageUrl(dependencyValue))
                {
                    return false;
                }

                repoUrl = NormalizeGitPackageUrl(dependencyValue);
                return !string.IsNullOrWhiteSpace(repoUrl);
            }
            catch
            {
                repoUrl = string.Empty;
                return false;
            }
        }

        private static bool LooksLikeGitPackageUrl(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            var trimmed = value.Trim();
            if (trimmed.StartsWith("git@", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
            {
                return false;
            }

            if (string.Equals(uri.Scheme, "git", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(uri.Scheme, "ssh", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(uri.Scheme, "file", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!string.Equals(uri.Scheme, "http", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return uri.AbsolutePath.EndsWith(".git", StringComparison.OrdinalIgnoreCase) ||
                   uri.Host.IndexOf("github", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   uri.Host.IndexOf("gitlab", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string GetLastSyncedCommitKey(string repoUrl, string branch)
        {
            var scope = $"{repoUrl}|{branch}|{NormalizeRemotePath(SkillSubdir)}";
            using var sha256 = SHA256.Create();
            var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(scope));
            var suffix = BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
            return $"{LastSyncedCommitKeyPrefix}.{suffix}";
        }

        internal static bool TryParseGitHubRepository(string url, out GitHubRepoInfo repoInfo)
        {
            repoInfo = default;
            if (string.IsNullOrWhiteSpace(url))
            {
                return false;
            }

            var trimmed = NormalizeGitPackageUrl(url);
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

        private static RemoteSnapshot FetchRemoteSnapshot(GitHubRepoInfo repoInfo, string branch, string subdir, Action<string> log)
        {
            using var client = CreateGitHubClient();
            var commitSha = FetchBranchHeadCommitSha(client, repoInfo, branch, log);
            var treeApiUrl = BuildTreeApiUrl(repoInfo, commitSha);
            log?.Invoke($"Fetching remote directory tree at commit {ShortCommit(commitSha)}...");
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
                    log?.Invoke($"Skip unsafe remote path: {remotePath}");
                    continue;
                }

                remoteFiles[safeRelativePath] = entry.sha.Trim().ToLowerInvariant();
            }

            if (remoteFiles.Count == 0)
            {
                throw new InvalidOperationException($"Remote directory not found: {normalizedSubdir}");
            }

            log?.Invoke($"Remote file count: {remoteFiles.Count}");
            return new RemoteSnapshot(commitSha, normalizedSubdir, remoteFiles);
        }

        private static RemoteSnapshot FetchGitSnapshot(string repoUrl, string branch, string subdir, Action<string> log)
        {
            if (string.IsNullOrWhiteSpace(repoUrl))
            {
                throw new InvalidOperationException("Repo URL is empty.");
            }

            var tempRoot = Path.Combine(Path.GetTempPath(), $"unity-mcp-skill-sync-{Guid.NewGuid():N}");
            var repoDir = Path.Combine(tempRoot, "repo");
            Directory.CreateDirectory(tempRoot);

            try
            {
                log?.Invoke("Cloning skill source with git sparse checkout...");
                RunGit(
                    $"-c core.longpaths=true clone --depth 1 --filter=blob:none --sparse --branch {QuoteProcessArgument(branch)} --single-branch {QuoteProcessArgument(repoUrl)} {QuoteProcessArgument(repoDir)}",
                    tempRoot,
                    log);
                RunGit(
                    $"sparse-checkout set {QuoteProcessArgument(SkillSubdir)} {QuoteProcessArgument(FallbackSkillSubdir)}",
                    repoDir,
                    log);
                var commitSha = RunGit("rev-parse HEAD", repoDir, log).Trim();
                if (string.IsNullOrWhiteSpace(commitSha))
                {
                    throw new InvalidOperationException("Failed to resolve cloned repository HEAD.");
                }

                var normalizedSubdir = NormalizeRemotePath(subdir);
                var sourceRoot = ResolveExistingSkillSourceRoot(repoDir, normalizedSubdir);
                var remoteFiles = new Dictionary<string, string>(StringComparer.Ordinal);
                var sourceRootFullPath = Path.GetFullPath(sourceRoot);

                foreach (var filePath in Directory.GetFiles(sourceRootFullPath, "*", SearchOption.AllDirectories))
                {
                    var relativePath = Path.GetRelativePath(sourceRootFullPath, filePath).Replace('\\', '/');
                    if (!TryNormalizeRelativePath(relativePath, out var safeRelativePath))
                    {
                        log?.Invoke($"Skip unsafe git path: {relativePath}");
                        continue;
                    }

                    remoteFiles[safeRelativePath] = ComputeGitBlobSha1(filePath);
                }

                if (remoteFiles.Count == 0)
                {
                    throw new InvalidOperationException($"Remote directory not found: {normalizedSubdir}");
                }

                var selectedSubdir = NormalizeRemotePath(Path.GetRelativePath(repoDir, sourceRootFullPath));
                log?.Invoke($"Git sparse checkout source: {selectedSubdir}");
                log?.Invoke($"Remote file count: {remoteFiles.Count}");
                return new RemoteSnapshot(commitSha, selectedSubdir, remoteFiles, sourceRootFullPath, tempRoot);
            }
            catch
            {
                TryDeleteDirectory(tempRoot);
                throw;
            }
        }

        private static string ResolveExistingSkillSourceRoot(string repoDir, string preferredSubdir)
        {
            var pathComparison = GetPathComparison(repoDir);
            var candidates = new[]
            {
                preferredSubdir,
                FallbackSkillSubdir
            };

            foreach (var candidate in candidates)
            {
                if (string.IsNullOrWhiteSpace(candidate))
                {
                    continue;
                }

                var sourceRoot = ResolvePathUnderRoot(repoDir, candidate, pathComparison);
                if (Directory.Exists(sourceRoot))
                {
                    return sourceRoot;
                }
            }

            throw new InvalidOperationException(
                $"Remote skill directory not found. Checked: {preferredSubdir}, {FallbackSkillSubdir}");
        }

        private static string FetchBranchHeadCommitSha(HttpClient client, GitHubRepoInfo repoInfo, string branch, Action<string> log)
        {
            var branchApiUrl = BuildBranchApiUrl(repoInfo, branch);
            log?.Invoke($"Fetching branch head commit...");
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

        internal static HttpClient CreateGitHubClient()
        {
            var client = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(60)
            };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("UnityMcpSkillSync/1.0");
            client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            return client;
        }

        internal static string DownloadString(HttpClient client, string url)
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

        internal static string NormalizeRemotePath(string path)
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

        internal static bool TryNormalizeRelativePath(string relativePath, out string normalizedPath)
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

        internal static string ResolvePathUnderRoot(string root, string relativePath, StringComparison pathComparison)
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

        internal static SyncPlan BuildPlan(Dictionary<string, string> remoteFiles, Dictionary<string, string> localFiles, StringComparer pathComparer)
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

        private static void ApplyPlan(GitHubRepoInfo repoInfo, RemoteSnapshot snapshot, string targetRoot, SyncPlan plan, StringComparison pathComparison, Action<string> log)
        {
            using var client = string.IsNullOrEmpty(snapshot.LocalSourceRoot) ? CreateGitHubClient() : null;
            var sourcePathComparison = string.IsNullOrEmpty(snapshot.LocalSourceRoot)
                ? pathComparison
                : GetPathComparison(snapshot.LocalSourceRoot);

            foreach (var relativePath in plan.Added.Concat(plan.Updated))
            {
                var targetFile = ResolvePathUnderRoot(targetRoot, relativePath, pathComparison);
                var targetDirectory = Path.GetDirectoryName(targetFile);
                if (!string.IsNullOrEmpty(targetDirectory))
                {
                    Directory.CreateDirectory(targetDirectory);
                }

                byte[] bytes;
                if (!string.IsNullOrEmpty(snapshot.LocalSourceRoot))
                {
                    log?.Invoke($"Copy: {relativePath}");
                    var sourceFile = ResolvePathUnderRoot(snapshot.LocalSourceRoot, relativePath, sourcePathComparison);
                    bytes = File.ReadAllBytes(sourceFile);
                }
                else
                {
                    var remoteFilePath = CombineRemotePath(snapshot.SubdirPath, relativePath);
                    var downloadUrl = BuildRawFileUrl(repoInfo, snapshot.CommitSha, remoteFilePath);
                    log?.Invoke($"Download: {relativePath}");
                    bytes = DownloadBytes(client, downloadUrl);
                }

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

        private static string RunGit(string arguments, string workingDir, Action<string> log)
        {
            return RunProcess("git", arguments, workingDir, log);
        }

        private static string RunProcess(string fileName, string arguments, string workingDir, Action<string> log)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = string.IsNullOrEmpty(workingDir) ? Environment.CurrentDirectory : workingDir,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = false };
            var stdout = new StringBuilder();
            var stderr = new StringBuilder();
            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data != null)
                {
                    stdout.AppendLine(e.Data);
                }
            };
            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data != null)
                {
                    stderr.AppendLine(e.Data);
                }
            };

            if (!process.Start())
            {
                throw new InvalidOperationException($"Failed to start process: {fileName}");
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            if (!process.WaitForExit(120000))
            {
                try
                {
                    process.Kill();
                }
                catch
                {
                }

                throw new TimeoutException($"Process timed out: {fileName} {arguments}");
            }

            process.WaitForExit();
            var stderrText = stderr.ToString().Trim();
            if (!string.IsNullOrWhiteSpace(stderrText))
            {
                log?.Invoke(stderrText);
            }

            if (process.ExitCode != 0)
            {
                var stdoutText = stdout.ToString().Trim();
                var details = string.Join("\n", new[] { stdoutText, stderrText }.Where(s => !string.IsNullOrWhiteSpace(s)));
                throw new InvalidOperationException(
                    string.IsNullOrWhiteSpace(details)
                        ? $"Process failed with exit code {process.ExitCode}: {fileName} {arguments}"
                        : $"Process failed with exit code {process.ExitCode}: {fileName} {arguments}\n{details}");
            }

            return stdout.ToString();
        }

        private static string QuoteProcessArgument(string argument)
        {
            if (string.IsNullOrEmpty(argument))
            {
                return "\"\"";
            }

            if (argument.IndexOfAny(new[] { ' ', '\t', '\n', '\r', '"' }) < 0)
            {
                return argument;
            }

            return $"\"{argument.Replace("\"", "\\\"")}\"";
        }

        private static void ValidateFileHashes(string installRoot, Dictionary<string, string> remoteFiles, StringComparison pathComparison, Action<string> log)
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

            log?.Invoke($"Hash checks passed ({checkedCount}/{remoteFiles.Count}).");
        }

        internal static string ComputeGitBlobSha1(string filePath)
        {
            var bytes = File.ReadAllBytes(filePath);
            return ComputeGitBlobSha1(bytes);
        }

        internal static string ComputeGitBlobSha1(byte[] bytes)
        {
            var headerBytes = Encoding.UTF8.GetBytes($"blob {bytes.Length}\0");
            using var sha1 = SHA1.Create();
            sha1.TransformBlock(headerBytes, 0, headerBytes.Length, null, 0);
            sha1.TransformFinalBlock(bytes, 0, bytes.Length);
            return BitConverter.ToString(sha1.Hash ?? Array.Empty<byte>()).Replace("-", string.Empty).ToLowerInvariant();
        }

        internal static Dictionary<string, string> ListFiles(string root)
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

        internal static StringComparison GetPathComparison(string root)
        {
            return IsCaseSensitiveFileSystem(root) ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        }

        internal static StringComparer GetPathComparer(StringComparison pathComparison)
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

        private static void CleanupSnapshot(RemoteSnapshot snapshot, Action<string> log)
        {
            if (string.IsNullOrWhiteSpace(snapshot.TempRoot))
            {
                return;
            }

            try
            {
                TryDeleteDirectory(snapshot.TempRoot);
            }
            catch (Exception ex)
            {
                log?.Invoke($"Failed to clean temporary skill sync directory: {ex.Message}");
            }
        }

        private static void TryDeleteDirectory(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            {
                return;
            }

            Directory.Delete(path, true);
        }

        internal static string ResolveAndValidateInstallPath(string installDir)
        {
            if (string.IsNullOrWhiteSpace(installDir))
            {
                throw new InvalidOperationException("Install Dir is empty.");
            }

            var trimmed = installDir.Trim();
            if (trimmed.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            {
                throw new InvalidOperationException($"Install Dir contains invalid path characters: {installDir}");
            }

            string expandedPath;
            try
            {
                expandedPath = ExpandPath(trimmed);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Install Dir is invalid and cannot be resolved: {installDir}", ex);
            }

            if (string.IsNullOrWhiteSpace(expandedPath))
            {
                throw new InvalidOperationException("Install Dir resolved to an empty path.");
            }

            return expandedPath;
        }

        internal static string ExpandPath(string path)
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

        private static void LogPlanDetails(SyncPlan plan, Action<string> log)
        {
            if (plan.Added.Count == 0 && plan.Updated.Count == 0 && plan.Deleted.Count == 0)
            {
                log?.Invoke("No file changes.");
                return;
            }

            foreach (var path in plan.Added)
            {
                log?.Invoke($"+ {path}");
            }

            foreach (var path in plan.Updated)
            {
                log?.Invoke($"~ {path}");
            }

            foreach (var path in plan.Deleted)
            {
                log?.Invoke($"- {path}");
            }
        }

        internal readonly struct GitHubRepoInfo
        {
            public GitHubRepoInfo(string owner, string repo)
            {
                Owner = owner;
                Repo = repo;
            }

            public string Owner { get; }
            public string Repo { get; }
        }

        internal readonly struct RemoteSnapshot
        {
            public RemoteSnapshot(
                string commitSha,
                string subdirPath,
                Dictionary<string, string> files,
                string localSourceRoot = null,
                string tempRoot = null)
            {
                CommitSha = commitSha;
                SubdirPath = subdirPath;
                Files = files;
                LocalSourceRoot = localSourceRoot;
                TempRoot = tempRoot;
            }

            public string CommitSha { get; }
            public string SubdirPath { get; }
            public Dictionary<string, string> Files { get; }
            public string LocalSourceRoot { get; }
            public string TempRoot { get; }
        }

        [Serializable]
        internal sealed class GitHubTreeResponse
        {
            public string sha;
            public GitHubTreeEntry[] tree;
            public bool truncated;
        }

        [Serializable]
        internal sealed class GitHubBranchResponse
        {
            public GitHubBranchCommit commit;
        }

        [Serializable]
        internal sealed class GitHubBranchCommit
        {
            public string sha;
        }

        [Serializable]
        internal sealed class GitHubTreeEntry
        {
            public string path;
            public string type;
            public string sha;
        }

        internal sealed class SyncPlan
        {
            public List<string> Added { get; } = new();
            public List<string> Updated { get; } = new();
            public List<string> Deleted { get; } = new();
        }
    }
}
