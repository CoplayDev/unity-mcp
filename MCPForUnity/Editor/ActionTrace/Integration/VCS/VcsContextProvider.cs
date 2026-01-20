using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEditor;
using UnityEngine;
using MCPForUnity.Editor.Helpers;

namespace MCPForUnity.Editor.ActionTrace.Integration.VCS
{
    /// <summary>
    /// Version Control System (VCS) integration for ActionTrace events.
    ///
    /// Purpose (from ActionTrace-enhancements.md P2.2):
    /// - Track Git commit and branch information
    /// - Mark events as "dirty" if they occurred after last commit
    /// - Help AI understand "dirty state" (uncommitted changes)
    ///
    /// Implementation:
    /// - Polls Git status periodically (via EditorApplication.update)
    /// - Injects vcs_context into event payloads
    /// - Supports Git-only (Unity Collaborate, SVN, Perforce not implemented)
    ///
    /// Unity 6 Compatibility:
    /// - Uses [InitializeOnLoad] to ensure EditorApplication.update is re-registered after domain reloads
    /// - Static constructor is called by Unity on domain reload
    ///
    /// Event payload format:
    /// {
    ///   "sequence": 123,
    ///   "summary": "Added Rigidbody to Player",
    ///   "vcs_context": {
    ///     "commit_id": "abc123",
    ///     "branch": "feature/player-movement",
    ///     "is_dirty": true
    ///   }
    /// }
    /// </summary>
    [InitializeOnLoad]
    public static class VcsContextProvider
    {
        // Configuration
        private const float PollIntervalSeconds = 5.0f;  // Poll every 5 seconds

        // State
        private static VcsContext _currentContext;
        private static double _lastPollTime;

        /// <summary>
        /// Initializes the VCS context provider and starts polling.
        /// </summary>
        static VcsContextProvider()
        {
            _currentContext = GetInitialContext();
            EditorApplication.update += OnUpdate;
        }

        /// <summary>
        /// Periodic update to refresh Git status.
        /// </summary>
        private static void OnUpdate()
        {
            if (EditorApplication.timeSinceStartup - _lastPollTime > PollIntervalSeconds)
            {
                RefreshContext();
                _lastPollTime = EditorApplication.timeSinceStartup;
            }
        }

        /// <summary>
        /// Gets the current VCS context for event injection.
        /// Thread-safe (called from any thread during event recording).
        /// </summary>
        public static VcsContext GetCurrentContext()
        {
            if (_currentContext == null)
            {
                _currentContext = GetInitialContext();
            }

            return _currentContext;
        }

        /// <summary>
        /// Refreshes the VCS context by polling Git status.
        /// </summary>
        private static void RefreshContext()
        {
            try
            {
                _currentContext = QueryGitStatus();
            }
            catch (System.Exception ex)
            {
                McpLog.Warn($"[VcsContextProvider] Failed to query Git status: {ex.Message}");
                // Fall back to default context
                _currentContext = VcsContext.CreateDefault();
            }
        }

        /// <summary>
        /// Queries Git status using git command.
        /// Returns current commit, branch, and dirty state.
        /// </summary>
        private static VcsContext QueryGitStatus()
        {
            // Check if this is a Git repository
            if (!IsGitRepository())
            {
                return VcsContext.CreateDefault();
            }

            // Get current commit
            var commitId = RunGitCommand("rev-parse HEAD");
            var shortCommit = commitId?.Length > 8 ? commitId.Substring(0, 8) : commitId;

            // Get current branch
            var branch = RunGitCommand("rev-parse --abbrev-ref HEAD");

            // Check if working tree is dirty
            var statusOutput = RunGitCommand("status --porcelain");
            var isDirty = !string.IsNullOrEmpty(statusOutput);

            return new VcsContext
            {
                CommitId = shortCommit ?? "unknown",
                Branch = branch ?? "unknown",
                IsDirty = isDirty
            };
        }

        /// <summary>
        /// Gets initial VCS context on startup.
        /// </summary>
        private static VcsContext GetInitialContext()
        {
            try
            {
                return QueryGitStatus();
            }
            catch
            {
                return VcsContext.CreateDefault();
            }
        }

        /// <summary>
        /// Checks if the current project is under Git version control.
        /// </summary>
        private static bool IsGitRepository()
        {
            try
            {
                var projectPath = System.IO.Path.GetDirectoryName(UnityEngine.Application.dataPath);
                var gitPath = System.IO.Path.Combine(projectPath, ".git");

                return System.IO.Directory.Exists(gitPath);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Runs a Git command and returns stdout.
        /// Returns null if command fails.
        /// </summary>
        private static string RunGitCommand(string arguments)
        {
            try
            {
                var projectPath = System.IO.Path.GetDirectoryName(UnityEngine.Application.dataPath);
                var gitPath = System.IO.Path.Combine(projectPath, ".git");

                // Find git executable
                string gitExe = FindGitExecutable();
                if (string.IsNullOrEmpty(gitExe))
                    return null;

                var startInfo = new ProcessStartInfo
                {
                    FileName = gitExe,
                    Arguments = $"-C \"{projectPath}\" {arguments}",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using (var process = Process.Start(startInfo))
                {
                    // Read both StandardOutput and StandardError simultaneously to avoid buffer blocking
                    var outputTask = System.Threading.Tasks.Task.Run(() => process.StandardOutput.ReadToEnd());
                    var errorTask = System.Threading.Tasks.Task.Run(() => process.StandardError.ReadToEnd());

                    // Add timeout protection (5 seconds) to prevent editor freeze
                    if (!process.WaitForExit(5000))
                    {
                        // Timeout exceeded - kill the process
                        try
                        {
                            process.Kill();
                            // Wait for process to actually exit after Kill
                            process.WaitForExit(1000);
                        }
                        catch { }
                        McpLog.Warn("[VcsContextProvider] Git command timeout after 5 seconds");
                        return null;
                    }

                    // Wait for both read tasks to complete (with short timeout to avoid hanging)
                    if (!System.Threading.Tasks.Task.WaitAll(new[] { outputTask, errorTask }, 1000))
                    {
                        McpLog.Warn("[VcsContextProvider] Git output read timeout");
                        return null;
                    }

                    var output = outputTask.Result;
                    var error = errorTask.Result;

                    // Log if there is error output
                    if (!string.IsNullOrEmpty(error))
                    {
                        McpLog.Warn($"[VcsContextProvider] Git error: {error.Trim()}");
                    }

                    return output.Trim();
                }
            }
            catch (System.Exception ex)
            {
                McpLog.Warn($"[VcsContextProvider] Git command failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Finds the Git executable path.
        /// </summary>
        private static string FindGitExecutable()
        {
            // Try common Git locations
            string[] gitPaths = new[]
            {
                @"C:\Program Files\Git\bin\git.exe",
                @"C:\Program Files (x86)\Git\bin\git.exe",
                "/usr/bin/git",
                "/usr/local/bin/git"
            };

            foreach (var path in gitPaths)
            {
                if (System.IO.File.Exists(path))
                    return path;
            }

            // Try system PATH
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = "--version",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };

                using (var process = Process.Start(startInfo))
                {
                    process.WaitForExit();
                    if (process.ExitCode == 0)
                        return "git"; // Found in PATH
                }
            }
            catch
            {
                // Git executable not found in PATH
            }

            return null;
        }
    }

    /// <summary>
    /// Represents the VCS context at the time of event recording.
    /// </summary>
    public sealed class VcsContext
    {
        /// <summary>
        /// Current Git commit hash (short form, 8 characters).
        /// Example: "abc12345"
        /// </summary>
        public string CommitId { get; set; }

        /// <summary>
        /// Current Git branch name.
        /// Example: "feature/player-movement", "main"
        /// </summary>
        public string Branch { get; set; }

        /// <summary>
        /// Whether the working tree has uncommitted changes.
        /// True if there are modified/new/deleted files not yet committed.
        /// </summary>
        public bool IsDirty { get; set; }

        /// <summary>
        /// Creates a default Vcs context for non-Git repositories.
        /// </summary>
        public static VcsContext CreateDefault()
        {
            return new VcsContext
            {
                CommitId = "unknown",
                Branch = "unknown",
                IsDirty = false
            };
        }

        /// <summary>
        /// Converts this context to a dictionary for event payload injection.
        /// Returns a new dictionary on each call to prevent unintended mutations.
        /// </summary>
        public Dictionary<string, object> ToDictionary()
        {
            return new Dictionary<string, object>
            {
                ["commit_id"] = CommitId,
                ["branch"] = Branch,
                ["is_dirty"] = IsDirty
            };
        }
    }
}
