using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Security;
using MCPForUnity.Editor.Services.AssetGen.Http;
using MCPForUnity.Editor.Services.AssetGen.Import;
using MCPForUnity.Editor.Services.AssetGen.Providers;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace MCPForUnity.Editor.Services.AssetGen
{
    public enum AssetGenJobState { Queued, Running, Importing, Done, Failed, Canceled }

    /// <summary>
    /// Snapshot of a generation/import job. Persisted to SessionState so a `status` query
    /// still works after an unrelated domain reload. NEVER carries a key or secret.
    /// </summary>
    public sealed class AssetGenJob
    {
        public string JobId;
        public string Kind;       // model | image | marketplace
        public string Provider;
        public string Action;
        public AssetGenJobState State;
        public float Progress;
        public string Format;
        public float TargetSize = 1f;
        public string AssetPath;
        public string AssetGuid;
        public string Error;
    }

    /// <summary>
    /// Drives asset-generation jobs on the Unity main thread via EditorApplication.update.
    /// Each job runs a submit → poll → download → import state machine using a provider
    /// adapter and an injectable HTTP transport. Because UnityWebRequest completes on the
    /// main thread, polling Task.IsCompleted from the update loop is main-thread-safe and we
    /// never block or use threadpool waits. The provider key is read once at submit time and
    /// held only in memory — never persisted, never logged, never put on the job record.
    /// </summary>
    [InitializeOnLoad]
    public static class AssetGenJobManager
    {
        private const string JobKeyPrefix = "MCPForUnity.AssetGen.Job.";
        private const string JobIndexKey = "MCPForUnity.AssetGen.JobIndex";

        // Test seams (overridable; defaults are the production implementations).
        internal static IHttpTransport TransportOverrideForTests;
        internal static Func<AssetGenJob, string, AssetGenJob> ImportOverrideForTests;
        internal static double PollIntervalSeconds = 3.0;
        internal static double TimeoutSeconds = 300.0;

        private static readonly Dictionary<string, AssetGenJob> Jobs = new();
        private static readonly Dictionary<string, Runner> Runners = new();
        private static bool _ticking;

        static AssetGenJobManager()
        {
            // Restore job records after a domain reload so `status` keeps working. In-flight
            // runners cannot resume (their Tasks are gone), so mark non-terminal jobs as failed.
            try
            {
                string index = SessionState.GetString(JobIndexKey, string.Empty);
                if (string.IsNullOrEmpty(index)) return;
                foreach (string id in index.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string json = SessionState.GetString(JobKeyPrefix + id, string.Empty);
                    if (string.IsNullOrEmpty(json)) continue;
                    var job = JsonConvert.DeserializeObject<AssetGenJob>(json);
                    if (job == null) continue;
                    if (job.State != AssetGenJobState.Done && job.State != AssetGenJobState.Failed && job.State != AssetGenJobState.Canceled)
                    {
                        job.State = AssetGenJobState.Failed;
                        job.Error = "Interrupted by an editor reload; please retry.";
                        Persist(job);
                    }
                    Jobs[id] = job;
                }
            }
            catch { /* recovery is best-effort */ }
        }

        public static AssetGenJob StartModelGeneration(ModelGenRequest req)
        {
            if (req == null) throw new ArgumentNullException(nameof(req));
            string provider = string.IsNullOrEmpty(req.Provider) ? "tripo" : req.Provider;
            IModelProviderAdapter adapter = AssetGenProviders.Model(provider); // throws NotSupportedException for unimplemented

            var job = NewJob("model", provider, "generate");
            job.Format = string.IsNullOrEmpty(req.Format) ? "glb" : req.Format;
            job.TargetSize = req.TargetSize <= 0 ? 1f : req.TargetSize;

            if (!SecureKeyStore.Current.TryGet(provider, out string apiKey) || string.IsNullOrEmpty(apiKey))
            {
                job.State = AssetGenJobState.Failed;
                job.Error = $"No API key configured for '{provider}'. Add it in the MCP for Unity → Asset Generation tab.";
                Jobs[job.JobId] = job;
                Persist(job);
                return job;
            }

            var runner = new Runner
            {
                Job = job,
                Request = req,
                ApiKey = apiKey,
                Adapter = adapter,
                Transport = TransportOverrideForTests ?? new UnityWebRequestTransport(),
                Cts = new CancellationTokenSource(),
                Phase = RunnerPhase.Submit,
                StartedAt = Now(),
            };
            Jobs[job.JobId] = job;
            Runners[job.JobId] = runner;
            Persist(job);
            EnsureTicking();
            return job;
        }

        public static AssetGenJob StartImageGeneration(ImageGenRequest req)
            => throw new NotSupportedException("Image generation arrives in a later phase.");

        public static AssetGenJob StartMarketplaceImport(string uid, float targetSize, string name, string outputFolder)
            => throw new NotSupportedException("Marketplace import arrives in a later phase.");

        public static AssetGenJob GetJob(string jobId)
            => string.IsNullOrEmpty(jobId) ? null : (Jobs.TryGetValue(jobId, out var j) ? j : null);

        public static bool Cancel(string jobId)
        {
            if (string.IsNullOrEmpty(jobId)) return false;
            if (Runners.TryGetValue(jobId, out var r))
            {
                r.Canceled = true;
                try { r.Cts.Cancel(); } catch { }
                return true;
            }
            if (Jobs.TryGetValue(jobId, out var job) && job.State == AssetGenJobState.Queued)
            {
                job.State = AssetGenJobState.Canceled;
                Persist(job);
                return true;
            }
            return false;
        }

        // ---------- runner ----------

        private enum RunnerPhase { Submit, AwaitSubmit, Poll, AwaitPoll, Download, AwaitDownload, Import }

        private sealed class Runner
        {
            public AssetGenJob Job;
            public ModelGenRequest Request;
            public string ApiKey;
            public IModelProviderAdapter Adapter;
            public IHttpTransport Transport;
            public CancellationTokenSource Cts;
            public RunnerPhase Phase;
            public double StartedAt;
            public double NextPollAt;
            public string ProviderJobId;
            public string DownloadUrl;
            public string LocalPath;
            public Task<string> SubmitTask;
            public Task<ProviderPollResult> PollTask;
            public Task<HttpResult> DownloadTask;
            public bool Canceled;
        }

        private static void EnsureTicking()
        {
            if (_ticking) return;
            EditorApplication.update += Tick;
            _ticking = true;
        }

        private static void Tick()
        {
            if (Runners.Count == 0)
            {
                EditorApplication.update -= Tick;
                _ticking = false;
                return;
            }
            // Snapshot keys to allow mutation during iteration.
            var ids = new List<string>(Runners.Keys);
            foreach (string id in ids)
            {
                if (Runners.TryGetValue(id, out var r)) Advance(r);
            }
        }

        /// <summary>Advance one job one step. Returns true when the job is terminal.</summary>
        internal static bool TryAdvanceForTests(string jobId)
        {
            if (Runners.TryGetValue(jobId, out var r))
            {
                Advance(r);
                return IsTerminal(r.Job.State);
            }
            return Jobs.TryGetValue(jobId, out var j) && IsTerminal(j.State);
        }

        private static void Advance(Runner r)
        {
            if (IsTerminal(r.Job.State)) { Finalize(r); return; }
            if (r.Canceled) { r.Job.State = AssetGenJobState.Canceled; Persist(r.Job); Finalize(r); return; }
            if (Now() - r.StartedAt > TimeoutSeconds) { Fail(r, $"Timed out after {TimeoutSeconds:0}s."); return; }

            try
            {
                switch (r.Phase)
                {
                    case RunnerPhase.Submit:
                        r.Job.State = AssetGenJobState.Running;
                        Persist(r.Job);
                        r.SubmitTask = r.Adapter.SubmitAsync(r.Request, r.ApiKey, r.Transport, r.Cts.Token);
                        r.Phase = RunnerPhase.AwaitSubmit;
                        break;

                    case RunnerPhase.AwaitSubmit:
                        if (!r.SubmitTask.IsCompleted) break;
                        if (Faulted(r.SubmitTask, out string subErr)) { Fail(r, subErr); break; }
                        r.ProviderJobId = r.SubmitTask.Result;
                        if (string.IsNullOrEmpty(r.ProviderJobId)) { Fail(r, "Provider returned no job id."); break; }
                        r.NextPollAt = Now();
                        r.Phase = RunnerPhase.Poll;
                        break;

                    case RunnerPhase.Poll:
                        if (Now() < r.NextPollAt) break;
                        r.PollTask = r.Adapter.PollAsync(r.ProviderJobId, r.ApiKey, r.Transport, r.Cts.Token);
                        r.Phase = RunnerPhase.AwaitPoll;
                        break;

                    case RunnerPhase.AwaitPoll:
                        if (!r.PollTask.IsCompleted) break;
                        if (Faulted(r.PollTask, out string pollErr)) { Fail(r, pollErr); break; }
                        ProviderPollResult pr = r.PollTask.Result;
                        r.Job.Progress = Mathf.Clamp01(pr.Progress);
                        Persist(r.Job);
                        if (pr.State == ProviderPollState.Succeeded)
                        {
                            if (string.IsNullOrEmpty(pr.DownloadUrl)) { Fail(r, "Provider succeeded but returned no download url."); break; }
                            r.DownloadUrl = pr.DownloadUrl;
                            r.Phase = RunnerPhase.Download;
                        }
                        else if (pr.State == ProviderPollState.Failed)
                        {
                            Fail(r, string.IsNullOrEmpty(pr.Error) ? "Provider reported failure." : pr.Error);
                        }
                        else
                        {
                            r.NextPollAt = Now() + PollIntervalSeconds;
                            r.Phase = RunnerPhase.Poll;
                        }
                        break;

                    case RunnerPhase.Download:
                        r.DownloadTask = r.Transport.SendAsync(
                            new HttpRequestSpec { Method = "GET", Url = r.DownloadUrl }, r.Cts.Token);
                        r.Phase = RunnerPhase.AwaitDownload;
                        break;

                    case RunnerPhase.AwaitDownload:
                        if (!r.DownloadTask.IsCompleted) break;
                        if (Faulted(r.DownloadTask, out string dlErr)) { Fail(r, dlErr); break; }
                        HttpResult res = r.DownloadTask.Result;
                        if (res == null || !res.IsSuccess || res.Body == null || res.Body.Length == 0)
                        {
                            Fail(r, $"Download failed (HTTP {res?.Status}).");
                            break;
                        }
                        r.LocalPath = WriteModelFile(r, res.Body);
                        r.Job.State = AssetGenJobState.Importing;
                        Persist(r.Job);
                        r.Phase = RunnerPhase.Import;
                        break;

                    case RunnerPhase.Import:
                        Func<AssetGenJob, string, AssetGenJob> import = ImportOverrideForTests ?? ModelImportPipeline.ImportInto;
                        AssetGenJob imported = import(r.Job, r.LocalPath);
                        if (imported != null) r.Job = imported;
                        if (r.Job.State != AssetGenJobState.Failed)
                        {
                            r.Job.State = AssetGenJobState.Done;
                            r.Job.Progress = 1f;
                        }
                        Persist(r.Job);
                        Finalize(r);
                        break;
                }
            }
            catch (Exception e)
            {
                Fail(r, SecretRedactor.Scrub(e.Message));
            }
        }

        private static string WriteModelFile(Runner r, byte[] bytes)
        {
            string ext = string.IsNullOrEmpty(r.Job.Format) ? "glb" : r.Job.Format.TrimStart('.').ToLowerInvariant();
            string root = !string.IsNullOrEmpty(r.Request.OutputFolder) ? r.Request.OutputFolder
                                                                        : (AssetGenPrefs.OutputRoot + "/Models");
            if (!root.Replace('\\', '/').StartsWith("Assets")) root = AssetGenPrefs.OutputRoot + "/Models";
            string absRoot = ToAbsolute(root);
            Directory.CreateDirectory(absRoot);
            string baseName = SanitizeName(!string.IsNullOrEmpty(r.Request.Name) ? r.Request.Name
                              : (!string.IsNullOrEmpty(r.Request.Prompt) ? r.Request.Prompt : "model_" + r.Job.JobId.Substring(0, 8)));
            string fileName = baseName + "." + ext;
            string abs = Path.Combine(absRoot, fileName);
            int n = 1;
            while (File.Exists(abs)) { fileName = baseName + "_" + n++ + "." + ext; abs = Path.Combine(absRoot, fileName); }
            File.WriteAllBytes(abs, bytes);
            return (root.TrimEnd('/') + "/" + fileName).Replace('\\', '/');
        }

        private static string ToAbsolute(string projectRelative)
        {
            string dataPath = Application.dataPath; // ".../Assets"
            string projectRoot = dataPath.Substring(0, dataPath.Length - "Assets".Length);
            return Path.Combine(projectRoot, projectRelative);
        }

        private static string SanitizeName(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "model";
            var sb = new System.Text.StringBuilder();
            foreach (char c in raw.Trim())
            {
                if (char.IsLetterOrDigit(c) || c == '_' || c == '-') sb.Append(c);
                else if (c == ' ') sb.Append('_');
                if (sb.Length >= 48) break;
            }
            string s = sb.ToString().Trim('_', '-');
            return string.IsNullOrEmpty(s) ? "model" : s;
        }

        private static void Fail(Runner r, string message)
        {
            r.Job.State = AssetGenJobState.Failed;
            r.Job.Error = SecretRedactor.Scrub(string.IsNullOrEmpty(message) ? "Generation failed." : message);
            Persist(r.Job);
            Finalize(r);
        }

        private static void Finalize(Runner r)
        {
            Runners.Remove(r.Job.JobId);
            try { r.Cts?.Dispose(); } catch { }
        }

        private static AssetGenJob NewJob(string kind, string provider, string action)
        {
            return new AssetGenJob
            {
                JobId = Guid.NewGuid().ToString("N"),
                Kind = kind,
                Provider = provider,
                Action = action,
                State = AssetGenJobState.Queued,
                Progress = 0f,
            };
        }

        private static void Persist(AssetGenJob job)
        {
            try
            {
                SessionState.SetString(JobKeyPrefix + job.JobId, JsonConvert.SerializeObject(job));
                string index = SessionState.GetString(JobIndexKey, string.Empty);
                var ids = new HashSet<string>(index.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
                if (ids.Add(job.JobId))
                    SessionState.SetString(JobIndexKey, string.Join(",", ids));
            }
            catch { /* persistence is best-effort */ }
        }

        private static bool Faulted(Task t, out string error)
        {
            if (t.IsFaulted)
            {
                Exception ex = t.Exception?.GetBaseException();
                error = SecretRedactor.Scrub(ex?.Message ?? "request failed");
                return true;
            }
            if (t.IsCanceled) { error = "Canceled."; return true; }
            error = null;
            return false;
        }

        private static bool IsTerminal(AssetGenJobState s)
            => s == AssetGenJobState.Done || s == AssetGenJobState.Failed || s == AssetGenJobState.Canceled;

        private static double Now() => EditorApplication.timeSinceStartup;

        internal static void ResetForTests()
        {
            foreach (var r in new List<Runner>(Runners.Values))
            {
                try { r.Cts?.Cancel(); r.Cts?.Dispose(); } catch { }
            }
            Runners.Clear();
            string index = SessionState.GetString(JobIndexKey, string.Empty);
            foreach (string id in index.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                SessionState.EraseString(JobKeyPrefix + id);
            SessionState.EraseString(JobIndexKey);
            Jobs.Clear();
            TransportOverrideForTests = null;
            ImportOverrideForTests = null;
            PollIntervalSeconds = 3.0;
            TimeoutSeconds = 300.0;
        }
    }
}
