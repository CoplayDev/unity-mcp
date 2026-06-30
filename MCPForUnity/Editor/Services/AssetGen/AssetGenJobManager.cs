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
    /// Drives asset-generation jobs on the Unity main thread via EditorApplication.update. Each
    /// job runs a generic submit → poll → (download | inline) → import state machine; the model
    /// and image paths supply their own submit/poll/import delegates. Because UnityWebRequest
    /// completes on the main thread, polling Task.IsCompleted from the update loop is
    /// main-thread-safe — we never block or use threadpool waits. The provider key is read once at
    /// submit time, captured only inside the submit/poll closures — never stored on the job,
    /// persisted, or logged.
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
        internal static double TimeoutSeconds = 600.0;

        private static readonly Dictionary<string, AssetGenJob> Jobs = new();
        private static readonly Dictionary<string, Runner> Runners = new();
        private static readonly List<string> _tickIds = new();
        private static bool _ticking;

        static AssetGenJobManager()
        {
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
                    if (!IsTerminal(job.State))
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
            IModelProviderAdapter adapter = AssetGenProviders.Model(provider); // throws NotSupportedException if unimplemented

            var job = NewJob("model", provider, "generate");
            job.Format = string.IsNullOrEmpty(req.Format) ? "glb" : req.Format;
            job.TargetSize = req.TargetSize <= 0 ? 1f : req.TargetSize;

            if (!TryResolveKey(provider, job, out string apiKey)) return job;

            IHttpTransport transport = TransportOverrideForTests ?? new UnityWebRequestTransport();
            var runner = new Runner
            {
                Job = job,
                SubmitFn = ct => adapter.SubmitAsync(req, apiKey, transport, ct),
                PollFn = (pid, ct) => adapter.PollAsync(pid, apiKey, transport, ct),
                ImportFn = ImportOverrideForTests ?? ModelImportPipeline.ImportInto,
                Transport = transport,
                OutputFolder = req.OutputFolder,
                Ext = (job.Format ?? "glb").TrimStart('.'),
                Name = NameFrom(req.Name, req.Prompt, job.JobId),
                Subfolder = "Models",
            };
            Register(job, runner);
            return job;
        }

        public static AssetGenJob StartImageGeneration(ImageGenRequest req)
        {
            if (req == null) throw new ArgumentNullException(nameof(req));
            string provider = string.IsNullOrEmpty(req.Provider) ? "fal" : req.Provider;
            IImageProviderAdapter adapter = AssetGenProviders.Image(provider); // throws NotSupportedException if unimplemented

            var job = NewJob("image", provider, "generate");
            job.Format = "png";

            if (!TryResolveKey(provider, job, out string apiKey)) return job;

            IHttpTransport transport = TransportOverrideForTests ?? new UnityWebRequestTransport();
            bool asSprite = req.AsSprite;
            bool transparent = req.Transparent;
            var runner = new Runner
            {
                Job = job,
                SubmitFn = ct => adapter.SubmitAsync(req, apiKey, transport, ct),
                PollFn = (pid, ct) => adapter.PollAsync(pid, apiKey, transport, ct),
                ImportFn = ImportOverrideForTests ?? ((j, path) => ImageImportPipeline.ImportInto(j, path, asSprite, transparent, isColor: true)),
                Transport = transport,
                OutputFolder = req.OutputFolder,
                Ext = "png",
                Name = NameFrom(req.Name, req.Prompt, job.JobId),
                Subfolder = "Images",
            };
            Register(job, runner);
            return job;
        }

        public static AssetGenJob StartMarketplaceImport(string uid, float targetSize, string name, string outputFolder)
        {
            if (string.IsNullOrEmpty(uid)) throw new ArgumentException("uid required");
            var adapter = AssetGenProviders.Marketplace("sketchfab"); // throws NotSupported if unimplemented
            var job = NewJob("marketplace", "sketchfab", "import");
            job.TargetSize = targetSize <= 0 ? 1f : targetSize;
            if (!TryResolveKey("sketchfab", job, out string apiKey)) return job;
            var transport = TransportOverrideForTests ?? new UnityWebRequestTransport();
            var runner = new Runner
            {
                Job = job,
                SubmitFn = ct => adapter.ResolveDownloadUrlAsync(uid, apiKey, transport, ct),   // returns the zip/gltf URL as providerJobId
                PollFn = (pid, ct) => Task.FromResult(new ProviderPollResult { State = ProviderPollState.Succeeded, Progress = 1f, DownloadUrl = pid, ResultExt = "zip" }),
                ImportFn = ImportOverrideForTests ?? ModelImportPipeline.ImportInto,
                Transport = transport,
                OutputFolder = outputFolder,
                Ext = "zip",
                Name = NameFrom(name, uid, job.JobId),
                Subfolder = "Sketchfab",
            };
            Register(job, runner);
            return job;
        }

        public static AssetGenJob GetJob(string jobId)
            => string.IsNullOrEmpty(jobId) ? null : (Jobs.TryGetValue(jobId, out var j) ? j : null);

        /// <summary>Most-recent-first snapshot of known jobs (for the GUI readout). Never contains keys.</summary>
        public static IReadOnlyList<AssetGenJob> RecentJobs(int max = 20)
        {
            var all = new List<AssetGenJob>(Jobs.Values);
            int start = Math.Max(0, all.Count - max);
            var slice = all.GetRange(start, all.Count - start);
            slice.Reverse();
            return slice;
        }

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
            public Func<CancellationToken, Task<string>> SubmitFn;
            public Func<string, CancellationToken, Task<ProviderPollResult>> PollFn;
            public Func<AssetGenJob, string, AssetGenJob> ImportFn;
            public IHttpTransport Transport;
            public string OutputFolder;
            public string Ext;
            public string OverrideExt;
            public string Name;
            public string Subfolder;

            public CancellationTokenSource Cts = new();
            public RunnerPhase Phase = RunnerPhase.Submit;
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

        private static bool TryResolveKey(string provider, AssetGenJob job, out string apiKey)
        {
            if (!SecureKeyStore.Current.TryGet(provider, out apiKey) || string.IsNullOrEmpty(apiKey))
            {
                job.State = AssetGenJobState.Failed;
                job.Error = AssetGenProviders.MissingKeyMessage(provider);
                Jobs[job.JobId] = job;
                Persist(job);
                return false;
            }
            return true;
        }

        private static void Register(AssetGenJob job, Runner runner)
        {
            runner.StartedAt = Now();
            Jobs[job.JobId] = job;
            Runners[job.JobId] = runner;
            Persist(job);
            EnsureTicking();
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
            // Snapshot keys into a reused buffer so Advance can mutate Runners mid-iteration
            // without churning the GC on every editor-update frame.
            _tickIds.Clear();
            _tickIds.AddRange(Runners.Keys);
            foreach (string id in _tickIds)
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
                        r.SubmitTask = r.SubmitFn(r.Cts.Token);
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
                        r.PollTask = r.PollFn(r.ProviderJobId, r.Cts.Token);
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
                            r.OverrideExt = pr.ResultExt;
                            if (pr.InlineData != null && pr.InlineData.Length > 0)
                            {
                                r.LocalPath = WriteFile(r, pr.InlineData);
                                r.Job.State = AssetGenJobState.Importing;
                                Persist(r.Job);
                                r.Phase = RunnerPhase.Import;
                            }
                            else if (!string.IsNullOrEmpty(pr.DownloadUrl))
                            {
                                r.DownloadUrl = pr.DownloadUrl;
                                r.Phase = RunnerPhase.Download;
                            }
                            else
                            {
                                Fail(r, "Provider succeeded but returned no result data.");
                            }
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
                        // The download URL comes from an untrusted provider response. Only fetch
                        // http(s) — refuse file://, ftp://, etc. so a malicious response can't read
                        // a local file into the project or hit an internal host.
                        if (!IsAllowedDownloadUrl(r.DownloadUrl))
                        {
                            Fail(r, "Refusing to fetch a non-http(s) download URL returned by the provider.");
                            break;
                        }
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
                        r.LocalPath = WriteFile(r, res.Body);
                        r.Job.State = AssetGenJobState.Importing;
                        Persist(r.Job);
                        r.Phase = RunnerPhase.Import;
                        break;

                    case RunnerPhase.Import:
                        // The result file was just written via File.WriteAllBytes (outside the
                        // AssetDatabase). Refresh so Unity registers it before we import it,
                        // mirroring ImportModelFile. Skipped under the test import seam.
                        if (ImportOverrideForTests == null) AssetDatabase.Refresh();
                        AssetGenJob imported = r.ImportFn(r.Job, r.LocalPath);
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

        private static string WriteFile(Runner r, byte[] bytes)
        {
            string chosen = !string.IsNullOrEmpty(r.OverrideExt) ? r.OverrideExt : r.Ext;
            string ext = string.IsNullOrEmpty(chosen) ? "bin" : chosen.TrimStart('.').ToLowerInvariant();
            string requestedRoot = !string.IsNullOrEmpty(r.OutputFolder) ? r.OutputFolder
                                                                         : (AssetGenPrefs.OutputRoot + "/" + r.Subfolder);
            if (!AssetGenPaths.TryGetAssetsFolder(requestedRoot, out string root))
                root = AssetGenPrefs.DefaultOutputRoot + "/" + r.Subfolder;
            string absRoot = AssetGenPaths.ToAbsolute(root);
            Directory.CreateDirectory(absRoot);
            string baseName = SanitizeName(r.Name);
            string fileName = baseName + "." + ext;
            string abs = Path.Combine(absRoot, fileName);
            int n = 1;
            while (File.Exists(abs)) { fileName = baseName + "_" + n++ + "." + ext; abs = Path.Combine(absRoot, fileName); }
            File.WriteAllBytes(abs, bytes);
            return (root.TrimEnd('/') + "/" + fileName).Replace('\\', '/');
        }

        private static string NameFrom(string explicitName, string prompt, string jobId)
        {
            if (!string.IsNullOrWhiteSpace(explicitName)) return explicitName;
            if (!string.IsNullOrWhiteSpace(prompt)) return prompt;
            return "asset_" + jobId.Substring(0, 8);
        }

        private static string SanitizeName(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "asset";
            var sb = new System.Text.StringBuilder();
            foreach (char c in raw.Trim())
            {
                if (char.IsLetterOrDigit(c) || c == '_' || c == '-') sb.Append(c);
                else if (c == ' ') sb.Append('_');
                if (sb.Length >= 48) break;
            }
            string s = sb.ToString().Trim('_', '-');
            return string.IsNullOrEmpty(s) ? "asset" : s;
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

        /// <summary>Only http(s) download URLs are allowed; provider responses are untrusted.</summary>
        private static bool IsAllowedDownloadUrl(string url)
            => Uri.TryCreate(url, UriKind.Absolute, out Uri u)
               && (u.Scheme == Uri.UriSchemeHttps || u.Scheme == Uri.UriSchemeHttp);

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
            TimeoutSeconds = 600.0;
        }
    }
}
