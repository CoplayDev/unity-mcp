using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Security;
using MCPForUnity.Editor.Services.AssetGen.Http;
using MCPForUnity.Editor.Services.AssetGen.Providers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace MCPForUnity.Editor.Services.AssetGen
{
    /// <summary>
    /// Public fal discovery, optionally using a configured key for higher rate limits. Each kind
    /// is committed only after all metadata pages and shortlisted schemas succeed.
    /// Failures preserve the last successful snapshot.
    /// Invoke on the editor thread; UnityWebRequest and its continuations stay on that thread.
    /// </summary>
    public static class FalModelCatalog
    {
        private const string ApiUrl = "https://api.fal.ai/v1/models";
        private const int CandidateLimit = 5;
        private static readonly TimeSpan Lifetime = TimeSpan.FromHours(24);
        private static readonly Dictionary<string, Snapshot> Snapshots = new();
        private static readonly Dictionary<string, Task<bool>> Refreshes = new();
        private static readonly Dictionary<string, string> Errors = new();
        private static readonly Dictionary<string, DateTime> Attempts = new();
        private static bool loaded;
        private static readonly SemaphoreSlim RequestGate = new(1, 1);
        private static DateTime nextRequestAt;
        public static event Action<string> Changed;

        internal static IHttpTransport TransportOverrideForTests;
        internal static string CachePathOverrideForTests;
        internal static Func<DateTime> UtcNow = () => DateTime.UtcNow;
        internal static Func<TimeSpan, CancellationToken, Task> DelayOverrideForTests;

        private sealed class Snapshot
        {
            public string Kind;
            public DateTime CheckedAt;
            public List<ModelEntry> Entries;
            [JsonIgnore] public bool FromDisk;
        }

        private sealed class Cache
        {
            public int Version = 1;
            public List<Snapshot> Snapshots;
        }

        public static bool IsRefreshing(string kind) => Refreshes.TryGetValue(kind, out var task) && !task.IsCompleted;
        public static string LastError(string kind) => Errors.TryGetValue(kind, out var error) ? error : null;

        public static string Source(string kind)
        {
            Load();
            return Snapshots.TryGetValue(kind, out var snapshot) ? snapshot.FromDisk ? "cache" : "live" : "bundled";
        }

        public static string VerifiedAt(string kind)
        {
            Load();
            return Snapshots.TryGetValue(kind, out var snapshot) ? snapshot.CheckedAt.ToString("O") : null;
        }

        public static bool IsStale(string kind)
        {
            Load();
            return !Snapshots.TryGetValue(kind, out var snapshot) || UtcNow() - snapshot.CheckedAt >= Lifetime;
        }

        internal static bool TryGet(string kind, out IReadOnlyList<ModelEntry> entries)
        {
            Load();
            if (Snapshots.TryGetValue(kind, out var snapshot))
            {
                entries = snapshot.Entries;
                return true;
            }
            entries = null;
            return false;
        }

        /// <summary>Coalesces concurrent refreshes. Automatic retries back off for two minutes.</summary>
        public static Task<bool> RefreshAsync(string kind, bool force = false)
        {
            if (kind != "audio" && kind != "image") throw new ArgumentException("fal discovery supports audio and image.", nameof(kind));
            Load();
            if (IsRefreshing(kind)) return Refreshes[kind];
            if (!force && !IsStale(kind)) return Task.FromResult(true);
            if (!force && Attempts.TryGetValue(kind, out var attempted) && UtcNow() - attempted < TimeSpan.FromMinutes(2))
                return Task.FromResult(false);
            Attempts[kind] = UtcNow();
            var task = RefreshCore(kind);
            Refreshes[kind] = task;
            return task;
        }

        private static async Task<bool> RefreshCore(string kind)
        {
            string apiKey = null;
            try
            {
                // Public discovery works without credentials; a configured key grants higher limits.
                if (TransportOverrideForTests == null)
                    try { SecureKeyStore.Current.TryGet("fal", out apiKey); } catch { /* public fallback */ }
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
                var http = TransportOverrideForTests ?? new UnityWebRequestTransport();
                var all = await ListMetadata(kind, http, timeout.Token, apiKey);
                var bundled = AssetGenModelCatalog.Bundled("fal", kind);
                var preferred = bundled.Select(e => e.Id).ToList();
                string selected = AssetGenPrefs.GetSelectedModel(kind, "fal");
                if (FalModelSchema.SafeId(selected)) preferred.Insert(0, selected);
                // Known profiles and user selections first; vendor highlights and recency discover
                // new candidates. Recency is not presented as a measured quality score.
                var candidates = all.Values
                    .Where(model => FalModelSchema.IsCandidate(model, kind))
                    .OrderBy(model => preferred.Contains((string)model["endpoint_id"]) ? preferred.IndexOf((string)model["endpoint_id"]) : int.MaxValue)
                    .ThenByDescending(model => (bool?)model["metadata"]?["highlighted"] == true || (bool?)model["metadata"]?["pinned"] == true)
                    .ThenByDescending(model => (string)model["metadata"]?["updated_at"] ?? (string)model["metadata"]?["date"])
                    .ThenBy(model => (string)model["endpoint_id"], StringComparer.Ordinal)
                    .Take(CandidateLimit + preferred.Count).ToArray();
                var ids = candidates.Select(model => (string)model["endpoint_id"]).ToList();
                if (kind == "image") ids.AddRange(ids.Select(id => id + "/edit").ToArray());
                var details = await Details(ids, http, timeout.Token, apiKey);
                string checkedAt = UtcNow().ToString("O");
                var entries = new List<ModelEntry>();
                foreach (var candidate in candidates)
                {
                    string id = (string)candidate["endpoint_id"];
                    if (!details.TryGetValue(id, out var model)) continue; // removed between list and find
                    var entry = FalModelSchema.Parse(model, kind, checkedAt);
                    if (entry == null) continue;
                    if (kind == "image" && details.TryGetValue(id + "/edit", out var editModel))
                    {
                        var edit = FalModelSchema.Parse(editModel, "image", checkedAt, edit: true);
                        if (edit != null)
                        {
                            entry.EditModelId = edit.Id;
                            entry.ImageInputField = edit.ImageInputField;
                            entry.ImageInputIsArray = edit.ImageInputIsArray;
                            entry.EditSupportsNumImages = edit.SupportsNumImages;
                            entry.EditOutputFormat = edit.OutputFormat;
                        }
                    }
                    entries.Add(entry);
                }
                // A selected non-default model must not silently become the automatic default.
                var order = bundled.Select((entry, index) => new { entry.Id, Index = index }).ToDictionary(entry => entry.Id, entry => entry.Index);
                var ordered = entries.OrderBy(entry => order.TryGetValue(entry.Id, out int index) ? index : int.MaxValue).ToList();
                Snapshots[kind] = new Snapshot { Kind = kind, CheckedAt = UtcNow(), Entries = ordered };
                Errors.Remove(kind);
                Save();
                Changed?.Invoke(kind);
                return true;
            }
            catch (Exception error)
            {
                Errors[kind] = error is OperationCanceledException ? "Model refresh timed out. Previous catalog retained." : SecretRedactor.Scrub(error.Message, apiKey);
                return false;
            }
        }

        private static async Task<Dictionary<string, JObject>> ListMetadata(string kind, IHttpTransport http, CancellationToken ct, string apiKey = null)
        {
            var models = new Dictionary<string, JObject>(StringComparer.Ordinal);
            var cursors = new HashSet<string>();
            string cursor = null;
            for (int page = 0; page < 50; page++)
            {
                string url = ApiUrl + "?category=" + (kind == "audio" ? "text-to-audio" : "text-to-image") + "&status=active&limit=100";
                if (cursor != null) url += "&cursor=" + Uri.EscapeDataString(cursor);
                var json = await Get(url, http, ct, apiKey);
                foreach (JObject model in (JArray)json["models"])
                {
                    string id = (string)model["endpoint_id"];
                    if (FalModelSchema.SafeId(id) && (string)model["metadata"]?["status"] == "active") models[id] = model;
                }
                if ((bool?)json["has_more"] != true && string.IsNullOrEmpty((string)json["next_cursor"])) return models;
                cursor = (string)json["next_cursor"];
                if (string.IsNullOrEmpty(cursor) || !cursors.Add(cursor)) throw new InvalidOperationException("Incomplete model pagination. Previous catalog retained.");
            }
            throw new InvalidOperationException("Model pagination exceeded its limit. Previous catalog retained.");
        }

        private static async Task<Dictionary<string, JObject>> Details(IEnumerable<string> ids, IHttpTransport http, CancellationToken ct, string apiKey = null)
        {
            var result = new Dictionary<string, JObject>(StringComparer.Ordinal);
            string[] wanted = ids.Distinct(StringComparer.Ordinal).Where(FalModelSchema.SafeId).ToArray();
            // Expanded schemas are large; small batches respect the provider's expansion limits.
            for (int offset = 0; offset < wanted.Length; offset += 5)
            {
                string query = string.Join("&", wanted.Skip(offset).Take(5).Select(id => "endpoint_id=" + Uri.EscapeDataString(id)));
                string cursor = null;
                var cursors = new HashSet<string>();
                for (int page = 0; page < 10; page++)
                {
                    string url = ApiUrl + "?" + query + "&expand=openapi-3.0&limit=5";
                    if (cursor != null) url += "&cursor=" + Uri.EscapeDataString(cursor);
                    var json = await Get(url, http, ct, apiKey, allowMissing: true);
                    foreach (JObject model in (JArray)json["models"])
                    {
                        string id = (string)model["endpoint_id"];
                        if (!wanted.Contains(id, StringComparer.Ordinal)) throw new InvalidOperationException("Unexpected model in schema response. Previous catalog retained.");
                        if ((string)model["metadata"]?["status"] == "active" && (!(model["openapi"] is JObject api) || api["error"] != null))
                            throw new InvalidOperationException("Model schema expansion failed. Previous catalog retained.");
                        result[id] = model;
                    }
                    if ((bool?)json["has_more"] != true && string.IsNullOrEmpty((string)json["next_cursor"])) break;
                    cursor = (string)json["next_cursor"];
                    if (page == 9 || string.IsNullOrEmpty(cursor) || !cursors.Add(cursor))
                        throw new InvalidOperationException("Incomplete model schema pagination. Previous catalog retained.");
                }
            }
            return result;
        }

        private static async Task<JObject> Get(string url, IHttpTransport http, CancellationToken ct, string apiKey = null, bool allowMissing = false)
        {
            ProviderHttp.RequireHost(url, "api.fal.ai", apiKey, "fal catalog");
            HttpResult response = null;
            for (int attempt = 0; attempt < 3; attempt++)
            {
                await RequestGate.WaitAsync(ct);
                try
                {
                    TimeSpan wait = nextRequestAt - UtcNow();
                    if (wait > TimeSpan.Zero) await Delay(wait, ct);
                    ct.ThrowIfCancellationRequested();
                    var request = new HttpRequestSpec { Method = "GET", Url = url };
                    request.Headers["User-Agent"] = "MCPForUnity/ModelCatalog";
                    if (!string.IsNullOrEmpty(apiKey)) request.Headers["Authorization"] = "Key " + apiKey;
                    response = await http.SendAsync(request, ct);
                    // Public discovery has a much smaller allowance than authenticated calls.
                    // Keep background refreshes from exhausting it before a preflight query.
                    nextRequestAt = UtcNow().AddSeconds(string.IsNullOrEmpty(apiKey) ? 7 : 1);
                }
                finally { RequestGate.Release(); }
                if (response?.Status != 429 || attempt == 2) break;
                await Delay(TimeSpan.FromSeconds(Math.Min(10, Math.Max(1, response.RetryAfterSeconds ?? (2 << attempt)))), ct);
            }
            // Find mode returns 404 when every requested endpoint is absent. In particular,
            // missing edit endpoints are an expected negative discovery result, not an outage.
            if (allowMissing && response?.Status == 404)
            {
                var missing = JObject.Parse(ProviderHttp.BodyText(response));
                if ((string)missing["error"]?["type"] == "not_found")
                    return new JObject { ["models"] = new JArray(), ["has_more"] = false };
            }
            // Do not persist provider error bodies or credentials.
            if (response == null || response.Status < 200 || response.Status >= 300)
                throw new InvalidOperationException($"Model catalog request failed (HTTP {response?.Status}). Previous catalog retained.");
            var json = JObject.Parse(ProviderHttp.BodyText(response));
            if (!(json["models"] is JArray)) throw new InvalidOperationException("Invalid model catalog response. Previous catalog retained.");
            return json;
        }

        private static Task Delay(TimeSpan duration, CancellationToken ct)
            => DelayOverrideForTests?.Invoke(duration, ct) ?? Task.Delay(duration, ct);

        /// <summary>Recheck the exact endpoint before a paid submit, and capture its live profile.</summary>
        internal static async Task<ModelEntry> VerifyForGeneration(string id, string kind, string mode, CancellationToken ct, string apiKey = null)
        {
            if (!FalModelSchema.SafeId(id)) throw new InvalidOperationException("Invalid fal model ID.");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            var ids = kind == "image" && mode == "image" ? new[] { id, id + "/edit" } : new[] { id };
            var models = await Details(ids, TransportOverrideForTests ?? new UnityWebRequestTransport(), timeout.Token, apiKey);
            if (!models.TryGetValue(id, out var model)) throw new InvalidOperationException($"Model '{id}' is unavailable. Refresh models and choose another model.");
            var entry = FalModelSchema.Parse(model, kind, UtcNow().ToString("O"));
            if (entry == null) throw new InvalidOperationException($"Model '{id}' is unavailable or incompatible with this tool. Refresh models and choose another model.");
            if (kind == "image" && mode == "image")
            {
                var edit = models.TryGetValue(id + "/edit", out var editModel) ? FalModelSchema.Parse(editModel, "image", entry.VerifiedAt, edit: true) : null;
                if (edit == null) throw new InvalidOperationException($"Model '{id}' has no compatible image editing endpoint.");
                entry.EditModelId = edit.Id;
                entry.ImageInputField = edit.ImageInputField;
                entry.ImageInputIsArray = edit.ImageInputIsArray;
                entry.EditSupportsNumImages = edit.SupportsNumImages;
                entry.EditOutputFormat = edit.OutputFormat;
            }
            return entry;
        }

        private static string CachePath => CachePathOverrideForTests ?? Path.Combine(Path.GetDirectoryName(Application.dataPath), "Library", "MCPForUnity", "fal-model-catalog.json");

        private static void Load()
        {
            if (loaded) return;
            loaded = true;
            try
            {
                if (!File.Exists(CachePath) || new FileInfo(CachePath).Length > 2 * 1024 * 1024) return;
                var cache = JsonConvert.DeserializeObject<Cache>(File.ReadAllText(CachePath));
                if (cache?.Version != 1 || cache.Snapshots == null) return;
                foreach (var snapshot in cache.Snapshots)
                {
                    if ((snapshot.Kind != "audio" && snapshot.Kind != "image") || snapshot.Entries == null
                        || snapshot.CheckedAt.Kind != DateTimeKind.Utc || snapshot.CheckedAt > UtcNow().AddMinutes(5)
                        || snapshot.Entries.Any(e => e == null || e.Provider != "fal" || e.Kind != snapshot.Kind || !FalModelSchema.SafeId(e.Id))) continue;
                    snapshot.FromDisk = true;
                    Snapshots[snapshot.Kind] = snapshot;
                }
            }
            catch { /* Corrupt or old caches are ignored; never erase the bundled catalog. */ }
        }

        private static void Save()
        {
            try
            {
                string path = CachePath;
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path + ".tmp", JsonConvert.SerializeObject(new Cache { Snapshots = Snapshots.Values.ToList() }));
                if (File.Exists(path)) File.Replace(path + ".tmp", path, null);
                else File.Move(path + ".tmp", path);
            }
            catch { /* A read-only/full disk does not invalidate the in-memory refresh. */ }
        }

        internal static void ReloadCacheForTests()
        {
            Snapshots.Clear();
            loaded = false;
        }

        internal static void ResetForTests(bool isolate = false)
        {
            Snapshots.Clear();
            Refreshes.Clear();
            Errors.Clear();
            Attempts.Clear();
            loaded = false;
            TransportOverrideForTests = null;
            CachePathOverrideForTests = isolate ? Path.Combine(Path.GetTempPath(), "unused_fal_catalog_" + Guid.NewGuid().ToString("N"), "catalog.json") : null;
            UtcNow = () => DateTime.UtcNow;
            nextRequestAt = DateTime.MinValue;
            DelayOverrideForTests = isolate ? (_, __) => Task.CompletedTask : (Func<TimeSpan, CancellationToken, Task>)null;
            Changed = null;
        }
    }
}
