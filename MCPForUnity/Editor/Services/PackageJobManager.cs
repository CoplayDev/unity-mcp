using System;
using System.Collections.Generic;
using System.Linq;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.PackageManager;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace MCPForUnity.Editor.Services
{
    internal enum PackageJobStatus { Running, Succeeded, Failed }

    internal sealed class PackageJob
    {
        public string JobId { get; set; }
        public PackageJobStatus Status { get; set; }
        public string Operation { get; set; }
        public string Package { get; set; }
        public long StartedUnixMs { get; set; }
        public long? FinishedUnixMs { get; set; }
        public long LastUpdateUnixMs { get; set; }
        public string Error { get; set; }
        public string ResultVersion { get; set; }
        public string ResultName { get; set; }
    }

    internal static class PackageJobManager
    {
        private const string SessionKeyJobs = "MCPForUnity.PackageJobsV1";
        private const int MaxJobsToKeep = 10;
        private const long DomainReloadTimeoutMs = 120_000;

        private static readonly object LockObj = new();
        private static readonly Dictionary<string, PackageJob> Jobs = new();

        static PackageJobManager()
        {
            TryRestoreFromSessionState();
        }

        private sealed class PersistedState
        {
            public List<PersistedJob> jobs { get; set; }
        }

        private sealed class PersistedJob
        {
            public string job_id { get; set; }
            public string status { get; set; }
            public string operation { get; set; }
            public string package_ { get; set; }
            public long started_unix_ms { get; set; }
            public long? finished_unix_ms { get; set; }
            public long last_update_unix_ms { get; set; }
            public string error { get; set; }
            public string result_version { get; set; }
            public string result_name { get; set; }
        }

        private static PackageJobStatus ParseStatus(string status)
        {
            if (string.IsNullOrWhiteSpace(status))
                return PackageJobStatus.Running;

            return status.Trim().ToLowerInvariant() switch
            {
                "succeeded" => PackageJobStatus.Succeeded,
                "failed" => PackageJobStatus.Failed,
                _ => PackageJobStatus.Running
            };
        }

        private static void TryRestoreFromSessionState()
        {
            try
            {
                string json = SessionState.GetString(SessionKeyJobs, string.Empty);
                if (string.IsNullOrWhiteSpace(json))
                    return;

                var state = JsonConvert.DeserializeObject<PersistedState>(json);
                if (state?.jobs == null)
                    return;

                lock (LockObj)
                {
                    Jobs.Clear();
                    long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                    foreach (var pj in state.jobs)
                    {
                        if (pj == null || string.IsNullOrWhiteSpace(pj.job_id))
                            continue;

                        var job = new PackageJob
                        {
                            JobId = pj.job_id,
                            Status = ParseStatus(pj.status),
                            Operation = pj.operation,
                            Package = pj.package_,
                            StartedUnixMs = pj.started_unix_ms,
                            FinishedUnixMs = pj.finished_unix_ms,
                            LastUpdateUnixMs = pj.last_update_unix_ms,
                            Error = pj.error,
                            ResultVersion = pj.result_version,
                            ResultName = pj.result_name
                        };

                        // Domain reload recovery for running jobs
                        if (job.Status == PackageJobStatus.Running)
                        {
                            TryRecoverJob(job, now);
                        }

                        Jobs[pj.job_id] = job;
                    }
                }
            }
            catch (Exception ex)
            {
                McpLog.Warn($"[PackageJobManager] Failed to restore SessionState: {ex.Message}");
            }
        }

        private static void TryRecoverJob(PackageJob job, long nowMs)
        {
            try
            {
                string packageName = ExtractPackageName(job.Package);
                var allPackages = PackageInfo.GetAllRegisteredPackages();
                var info = FindPackageInfo(allPackages, packageName, job.Package);

                if (job.Operation == "add")
                {
                    if (info != null)
                    {
                        job.Status = PackageJobStatus.Succeeded;
                        job.FinishedUnixMs = nowMs;
                        job.LastUpdateUnixMs = nowMs;
                        job.ResultVersion = info.version;
                        job.ResultName = info.name;
                        McpLog.Info($"[PackageJobManager] Recovered add job {job.JobId}: {info.name}@{info.version} installed.");
                    }
                    else if (nowMs - job.StartedUnixMs > DomainReloadTimeoutMs)
                    {
                        job.Status = PackageJobStatus.Failed;
                        job.FinishedUnixMs = nowMs;
                        job.LastUpdateUnixMs = nowMs;
                        job.Error = "Package installation timed out after domain reload.";
                        McpLog.Warn($"[PackageJobManager] Timed out add job {job.JobId} for '{job.Package}'.");
                    }
                }
                else if (job.Operation == "remove")
                {
                    if (info == null)
                    {
                        job.Status = PackageJobStatus.Succeeded;
                        job.FinishedUnixMs = nowMs;
                        job.LastUpdateUnixMs = nowMs;
                        McpLog.Info($"[PackageJobManager] Recovered remove job {job.JobId}: '{packageName}' is no longer installed.");
                    }
                    else if (nowMs - job.StartedUnixMs > DomainReloadTimeoutMs)
                    {
                        job.Status = PackageJobStatus.Failed;
                        job.FinishedUnixMs = nowMs;
                        job.LastUpdateUnixMs = nowMs;
                        job.Error = "Package removal timed out after domain reload.";
                        McpLog.Warn($"[PackageJobManager] Timed out remove job {job.JobId} for '{job.Package}'.");
                    }
                }
            }
            catch (Exception ex)
            {
                McpLog.Warn($"[PackageJobManager] Recovery check failed for job {job.JobId}: {ex.Message}");
            }
        }

        /// <summary>
        /// Find a PackageInfo by name, falling back to packageId or git/local source for non-standard identifiers.
        /// </summary>
        private static PackageInfo FindPackageInfo(PackageInfo[] allPackages, string packageName, string originalIdentifier)
        {
            // Direct name match (handles normal com.company.package identifiers)
            var info = allPackages.FirstOrDefault(p =>
                string.Equals(p.name, packageName, StringComparison.OrdinalIgnoreCase));
            if (info != null)
                return info;

            // For git URLs / file: paths, packageName == originalIdentifier and won't match .name.
            // Try matching by packageId or source (git/local).
            bool isGitOrFile = originalIdentifier.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                               || originalIdentifier.StartsWith("git", StringComparison.OrdinalIgnoreCase)
                               || originalIdentifier.StartsWith("file:", StringComparison.OrdinalIgnoreCase)
                               || originalIdentifier.EndsWith(".git", StringComparison.OrdinalIgnoreCase);

            if (!isGitOrFile)
                return null;

            return allPackages.FirstOrDefault(p =>
                p.source == PackageSource.Git || p.source == PackageSource.Local
                    ? p.packageId != null && p.packageId.Contains(originalIdentifier)
                      || p.resolvedPath != null && p.resolvedPath.Contains(originalIdentifier)
                    : false);
        }

        internal static string ExtractPackageName(string packageIdentifier)
        {
            if (string.IsNullOrEmpty(packageIdentifier))
                return packageIdentifier;

            // Strip version: "com.unity.foo@1.0.0" -> "com.unity.foo"
            int atIndex = packageIdentifier.IndexOf('@');
            if (atIndex > 0)
                return packageIdentifier.Substring(0, atIndex);

            // Git URLs and file: paths — can't reliably extract name, return as-is
            return packageIdentifier;
        }

        private static void PersistToSessionState()
        {
            try
            {
                PersistedState snapshot;
                lock (LockObj)
                {
                    var jobs = Jobs.Values
                        .OrderByDescending(j => j.LastUpdateUnixMs)
                        .Take(MaxJobsToKeep)
                        .Select(j => new PersistedJob
                        {
                            job_id = j.JobId,
                            status = j.Status.ToString().ToLowerInvariant(),
                            operation = j.Operation,
                            package_ = j.Package,
                            started_unix_ms = j.StartedUnixMs,
                            finished_unix_ms = j.FinishedUnixMs,
                            last_update_unix_ms = j.LastUpdateUnixMs,
                            error = j.Error,
                            result_version = j.ResultVersion,
                            result_name = j.ResultName
                        })
                        .ToList();

                    snapshot = new PersistedState { jobs = jobs };
                }

                SessionState.SetString(SessionKeyJobs, JsonConvert.SerializeObject(snapshot));
            }
            catch (Exception ex)
            {
                McpLog.Warn($"[PackageJobManager] Failed to persist SessionState: {ex.Message}");
            }
        }

        public static string StartJob(string operation, string package)
        {
            string jobId = Guid.NewGuid().ToString("N");
            long started = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            var job = new PackageJob
            {
                JobId = jobId,
                Status = PackageJobStatus.Running,
                Operation = operation,
                Package = package,
                StartedUnixMs = started,
                FinishedUnixMs = null,
                LastUpdateUnixMs = started,
                Error = null,
                ResultVersion = null,
                ResultName = null
            };

            lock (LockObj)
            {
                Jobs[jobId] = job;
            }
            PersistToSessionState();
            return jobId;
        }

        public static void CompleteJob(string jobId, bool success, string error = null,
            string version = null, string name = null)
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            lock (LockObj)
            {
                if (!Jobs.TryGetValue(jobId, out var job))
                    return;

                job.Status = success ? PackageJobStatus.Succeeded : PackageJobStatus.Failed;
                job.FinishedUnixMs = now;
                job.LastUpdateUnixMs = now;
                job.Error = error;
                job.ResultVersion = version;
                job.ResultName = name;
            }
            PersistToSessionState();
        }

        public static PackageJob GetJob(string jobId)
        {
            if (string.IsNullOrWhiteSpace(jobId))
                return null;

            lock (LockObj)
            {
                Jobs.TryGetValue(jobId, out var job);
                return job;
            }
        }

        public static PackageJob GetLatestJob()
        {
            lock (LockObj)
            {
                return Jobs.Values
                    .OrderByDescending(j => j.StartedUnixMs)
                    .FirstOrDefault();
            }
        }

        public static object ToSerializable(PackageJob job)
        {
            if (job == null)
                return null;

            return new
            {
                job_id = job.JobId,
                status = job.Status.ToString().ToLowerInvariant(),
                operation = job.Operation,
                package_ = job.Package,
                started_unix_ms = job.StartedUnixMs,
                finished_unix_ms = job.FinishedUnixMs,
                last_update_unix_ms = job.LastUpdateUnixMs,
                error = job.Error,
                result_version = job.ResultVersion,
                result_name = job.ResultName
            };
        }
    }
}
