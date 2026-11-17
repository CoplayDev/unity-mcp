using System;
using System.Collections.Generic;
using System.Linq;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Runtime.Helpers;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace MCPForUnity.Editor.Tools.Recording
{
    [InitializeOnLoad]
    [McpForUnityTool("manage_recording", AutoRegister = false, Group = "core")]
    public static class ManageRecording
    {
        private const string HistoryKey = "MCPForUnity.Recording.History";
        private const string RequestsKey = "MCPForUnity.Recording.Requests";
        private const int MaxJobs = 32;
        private static readonly Dictionary<string, RecordingSession> Jobs = new Dictionary<string, RecordingSession>();
        private static readonly Dictionary<string, string> Requests = new Dictionary<string, string>();
        private static readonly Dictionary<string, JObject> History = new Dictionary<string, JObject>();
        private static RecordingSession active;

        static ManageRecording()
        {
            try
            {
                foreach (JObject item in JArray.Parse(SessionState.GetString(HistoryKey, "[]")))
                {
                    if ((string)item["state"] == "recording" || (string)item["state"] == "prepared" || (string)item["state"] == "finalizing" || (string)item["state"] == "warming_up")
                    { item["state"] = "interrupted"; item["ready"] = false; item["error"] = "Editor domain reloaded. Prepare a new recording."; }
                    History[(string)item["recording_id"]] = item;
                }
            }
            catch (Exception ex) { McpLog.Warn("Could not restore recording history: " + ex.Message); }
            try
            {
                foreach (var entry in JObject.Parse(SessionState.GetString(RequestsKey, "{}")).Properties())
                    if (History.ContainsKey((string)entry.Value)) Requests[entry.Name] = (string)entry.Value;
            }
            catch (Exception ex) { McpLog.Warn("Could not restore recording request IDs: " + ex.Message); }
            EditorApplication.update += Tick;
            AssemblyReloadEvents.beforeAssemblyReload += () => Interrupt("Script assembly reload interrupted the recording.");
            EditorApplication.quitting += () => Interrupt("Editor quit interrupted the recording.");
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.ExitingPlayMode || state == PlayModeStateChange.ExitingEditMode)
                    Interrupt("Play Mode changed during recording.");
            };
        }

        /// <summary>Advance the single active job and release the active slot after it reaches a terminal state.</summary>
        private static void Tick() { if (active != null) { active.Tick(); if (!active.Active) active = null; } }
        /// <summary>Finalize the active encoder before Editor lifecycle changes and persist the interrupted result.</summary>
        private static void Interrupt(string reason) { if (active != null) active.Finish("interrupted", reason); SaveHistory(); }
        /// <summary>Persist bounded job snapshots and retry keys without invalidating unfinished jobs.</summary>
        private static void SaveHistory()
        {
            foreach (var job in Jobs.Values) History[job.Id] = JObject.FromObject(job.Describe());
            TrimHistory(MaxJobs);
            SessionState.SetString(HistoryKey, new JArray(History.Values).ToString(Newtonsoft.Json.Formatting.None));
            SessionState.SetString(RequestsKey, JObject.FromObject(Requests).ToString(Newtonsoft.Json.Formatting.None));
        }

        /// <summary>Evict the oldest terminal records only; prepared and active IDs remain client-addressable.</summary>
        private static void TrimHistory(int limit)
        {
            // Prepared and running jobs still own a client-visible ID. Only terminal
            // history may be discarded; otherwise a live recorder could become unreachable.
            var removable = History
                .Where(x => !Jobs.TryGetValue(x.Key, out var job) || (!job.Active && job.State != "prepared"))
                .OrderBy(x => (string)x.Value["created_at"])
                .Take(Math.Max(0, History.Count - limit))
                .Select(x => x.Key).ToArray();
            foreach (string oldest in removable)
            {
                History.Remove(oldest); Jobs.Remove(oldest);
                foreach (string key in Requests.Where(x => x.Value == oldest).Select(x => x.Key).ToArray()) Requests.Remove(key);
            }
        }

        /// <summary>Dispatch recording lifecycle actions on the Editor thread and return structured validation errors.</summary>
        public static object HandleCommand(JObject input)
        {
            try
            {
                RecordingPlan.Require(input != null, "Parameters are required.");
                string action = (string)input["action"];
                switch (action)
                {
                    case "capabilities": return new SuccessResponse("Recording capabilities.", new
                    {
                        sources = new[] { "game_view", "camera", "scene_view" },
                        playback = new[] { "none", "timeline", "animation_clip", "animator", "dotween", "custom" },
                        formats = Application.platform == RuntimePlatform.LinuxEditor ? new[] { "webm", "png_sequence" } : new[] { "mp4", "webm", "png_sequence" },
                        modes = new[] { "realtime", "fixed_step" }, dotween_available = DotweenPlayback.Available,
                        max_duration_seconds = 300, max_frames = 18000, max_fps = 120, max_pixels = 3840 * 2160, audio = false,
                        notes = new[] {
                            "game_view captures composited Play Mode UI; requested dimensions fit/letterbox the current viewport, they do not reflow it.",
                            "camera and scene_view render a camera and exclude Screen Space Overlay UI and editor chrome/gizmos.",
                            "Fixed-step sampling controls the selected playback source, not deterministic replay of the whole game.",
                            "Animator and tween callbacks can have project-specific side effects. Arbitrary script/physics state cannot be rolled back.",
                            "Video encoder availability depends on the installed Editor/platform; png_sequence is the portable fallback.",
                        },
                    });
                    case "discover":
                        var items = RecordingPlayback.Discover((string)input["query"]);
                        int size = RecordingPlan.ReadInt(input, "page_size", 25), cursor = RecordingPlan.ReadInt(input, "cursor", 0);
                        RecordingPlan.Require(size >= 1 && size <= 100 && cursor >= 0, "page_size must be 1..100 and cursor must be non-negative.");
                        return new SuccessResponse("Available playback targets and cameras.", new { items = items.Skip(cursor).Take(size).ToArray(), total = items.Count, next_cursor = cursor + size < items.Count ? (int?)(cursor + size) : null });
                    case "prepare": return Prepare(input);
                    case "start": return Start(input);
                    case "status":
                        string id = (string)input["recording_id"];
                        if (string.IsNullOrEmpty(id)) { SaveHistory(); return new SuccessResponse("Recent recording jobs (maximum 32).", new { jobs = History.Values.OrderByDescending(x => (string)x["created_at"]).ToArray() }); }
                        return Status(id);
                    case "stop":
                    case "cancel":
                        string jobId = (string)input["recording_id"];
                        RecordingPlan.Require(!string.IsNullOrEmpty(jobId), "recording_id is required.");
                        if (!Jobs.TryGetValue(jobId, out var job)) return Status(jobId);
                        if (job.State == "prepared" && action == "stop") throw new ArgumentException("The recording has not started. Use start or cancel.");
                        if (job.Active || job.State == "prepared") job.Finish(action == "stop" ? "completed" : "cancelled");
                        return Status(jobId);
                    default: return new ErrorResponse("Unknown recording action. Use capabilities, discover, prepare, start, status, stop or cancel.");
                }
            }
            catch (Exception ex) { return new ErrorResponse(ex.GetBaseException().Message); }
        }

        /// <summary>Validate and bind a source and camera without changing playback or creating capture artifacts.</summary>
        private static object Prepare(JObject input)
        {
            var playback = RecordingPlayback.Resolve(input["playback"] as JObject);
            var plan = RecordingPlan.Parse(input, playback.FrameRate);
            RecordingSession.ValidateEnvironment(plan);
            Camera camera = RecordingSession.ResolveCamera(plan);
            playback.Validate(plan);
            if (playback.TargetId != null) plan.Playback["target"] = playback.TargetId;
            if (camera != null) plan.Camera = camera.GetInstanceIDCompat().ToString();
            SaveHistory();
            TrimHistory(MaxJobs - 1);
            RecordingPlan.Require(History.Count < MaxJobs,
                "Recording job capacity reached (32 prepared or active jobs). Cancel an unused prepared job before preparing another recording.");
            var job = new RecordingSession(plan, playback, camera) { Changed = SaveHistory };
            Jobs.Add(job.Id, job); SaveHistory();
            return new SuccessResponse("Recording prepared without changing playback or creating output files. Start with recording_id.", job.Describe());
        }

        /// <summary>Deduplicate retries, enforce single-recorder ownership, and start the previously bound plan.</summary>
        private static object Start(JObject input)
        {
            string requestId = (string)input["request_id"];
            if (!string.IsNullOrEmpty(requestId) && Requests.TryGetValue(requestId, out string previous)) return Status(previous);
            string id = (string)input["recording_id"];
            if (!string.IsNullOrEmpty(id)) RecordingPlan.Require(!input.Properties().Any(p => p.Name != "action" && p.Name != "recording_id" && p.Name != "request_id"), "Start a prepared recording with recording_id only; prepare a new job to change its specification.");
            if (!string.IsNullOrEmpty(id) && !Jobs.ContainsKey(id)) return Status(id);
            if (!string.IsNullOrEmpty(id) && Jobs[id].State != "prepared") return Status(id);
            RecordingPlan.Require(active == null || !active.Active, "Another recording is active. Stop it before starting a new one.");
            if (string.IsNullOrEmpty(id))
            {
                var response = Prepare(input) as SuccessResponse;
                id = JObject.FromObject(response.Data).Value<string>("recording_id");
            }
            active = Jobs[id];
            if (!string.IsNullOrEmpty(requestId)) Requests[requestId] = id;
            active.Start();
            return active.State == "failed" ? new ErrorResponse(active.Error, active.Describe()) : new SuccessResponse("Recording started. Poll status with recording_id; ready is true only after encoding finishes.", active.Describe());
        }

        /// <summary>Return a live job or its retained snapshot, including the terminal state and artifact readiness.</summary>
        private static object Status(string id)
        {
            if (Jobs.TryGetValue(id, out var job)) return new SuccessResponse("Recording status.", job.Describe());
            if (History.TryGetValue(id, out var historical)) return new SuccessResponse("Recording status from the previous domain.", historical);
            return new ErrorResponse("Unknown recording_id. Jobs are retained for this Editor session, up to 32 recent jobs.");
        }
    }
}
