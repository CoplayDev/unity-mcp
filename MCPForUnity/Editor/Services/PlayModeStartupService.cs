using System;
using System.Collections.Generic;
using System.Linq;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Tools;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MCPForUnity.Editor.Services
{
    /// <summary>Tracks play startup across domain reloads without changing Enter Play Mode options.</summary>
    [InitializeOnLoad]
    internal static class PlayModeStartupService
    {
        private const string JobsKey = "MCPForUnity.PlayStartup.Jobs";
        private const string RequestsKey = "MCPForUnity.PlayStartup.Requests";
        private const string FrameKey = "MCPForUnity.PlayStartup.EnteredFrame";
        private const int MaxJobs = 16;
        private static readonly List<StartupJob> Jobs = LoadJobs();
        private static readonly HashSet<string> Requests = LoadRequests();

        private sealed class StartupJob
        {
            public string Id;
            public string Status = "waiting";
            public string WaitUntil;
            public string Code;
            public double StartedAt;
            public double Timeout;
            public double? FinishedAt;
            public bool SceneLoaded;
            public bool FirstFrameComplete;
            public bool CodeAttempted;
            public JObject CodeResult;
            public string Error;
            public string SceneName;
        }

        static PlayModeStartupService()
        {
            // The code may itself trigger a reload. A durable execution marker prevents
            // replaying side effects when its original call/result did not survive.
            foreach (var job in Jobs.Where(item => item.Status == "executing"))
                Fail(job, "Startup code was interrupted by a domain reload and will not be repeated.");
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            EditorApplication.update += OnUpdate;
        }

        public static object Start(JObject args)
        {
            string waitUntil = args["waitUntil"]?.ToString();
            string code = args["onReadyCode"]?.Type == JTokenType.Null ? null : args["onReadyCode"]?.ToString();
            if (waitUntil != null && waitUntil != "scene_loaded" && waitUntil != "first_frame")
                return new ErrorResponse("waitUntil must be 'scene_loaded' or 'first_frame'.");
            if (code != null && (string.IsNullOrWhiteSpace(code) || code.Length > ExecuteCode.MaxCodeLength))
                return new ErrorResponse($"onReadyCode must contain a C# body of at most {ExecuteCode.MaxCodeLength} characters.");
            if (waitUntil == null && code == null)
            {
                if (args["waitTimeout"] != null) return new ErrorResponse("waitTimeout requires waitUntil or onReadyCode.");
                if (EditorApplication.isPlaying) return new SuccessResponse("Already in play mode.");
                EditorApplication.isPlaying = true;
                return new SuccessResponse("Entered play mode.");
            }
            double timeout = args["waitTimeout"]?.ToObject<double>() ?? 30;
            if (double.IsNaN(timeout) || double.IsInfinity(timeout) || timeout < 1 || timeout > 300)
                return new ErrorResponse("waitTimeout must be between 1 and 300 seconds.");
            string id = args["startupRequestId"]?.ToString() ?? Guid.NewGuid().ToString("N");
            if (!Guid.TryParse(id, out var parsedId)) return new ErrorResponse("startupRequestId must be a UUID.");
            id = parsedId.ToString("N");
            var previous = Jobs.FirstOrDefault(job => job.Id == id);
            if (previous != null) return Response(previous);
            if (Requests.Contains(id)) return new ErrorResponse("This startup request's result has expired; its code will not be repeated.");
            var active = Jobs.FirstOrDefault(job => job.Status == "waiting" || job.Status == "executing");
            if (active != null)
                return new ErrorResponse("A Play Mode startup job is already active. Poll its job_id before starting another.",
                    new { job_id = active.Id, status = active.Status });
            if (EditorApplication.isPlaying && !EditorApplication.isPlayingOrWillChangePlaymode)
                return new ErrorResponse("Play Mode is stopping; wait until it has stopped before starting a job.");

            var current = new StartupJob { Id = id, WaitUntil = code == null ? waitUntil : "first_frame",
                Code = code, StartedAt = EditorApplication.timeSinceStartup, Timeout = timeout };
            while (Jobs.Count >= MaxJobs) Jobs.RemoveAt(0);
            Jobs.Add(current);
            Requests.Add(id);
            Persist();
            if (!EditorApplication.isPlaying && !EditorApplication.isPlayingOrWillChangePlaymode)
            {
                SessionState.SetInt(FrameKey, -1);
                EditorApplication.isPlaying = true;
            }
            return Response(current);
        }

        public static object Get(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return new ErrorResponse("jobId is required for get_play_mode_startup.");
            if (!Guid.TryParse(id, out var parsedId)) return new ErrorResponse("jobId must be a UUID.");
            id = parsedId.ToString("N");
            var job = Jobs.FirstOrDefault(item => item.Id == id);
            return job == null ? new ErrorResponse("Play Mode startup job was not found in this Editor session.") : Response(job);
        }

        private static object Response(StartupJob job) => new SuccessResponse("Play Mode startup status.", new {
            job_id = job.Id, status = job.Status, wait_until = job.WaitUntil,
            scene_loaded = job.SceneLoaded, first_frame_complete = job.FirstFrameComplete,
            scene_name = job.SceneName, code_attempted = job.CodeAttempted, on_ready_result = job.CodeResult,
            error = job.Error, elapsed_seconds = (job.FinishedAt ?? EditorApplication.timeSinceStartup) - job.StartedAt,
            unity_instance = ProjectIdentityUtility.GetProjectHash()
        });

        private static void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingEditMode) SessionState.SetInt(FrameKey, -1);
            if (change == PlayModeStateChange.EnteredPlayMode) SessionState.SetInt(FrameKey, Time.frameCount);
            if (change == PlayModeStateChange.ExitingPlayMode || change == PlayModeStateChange.EnteredEditMode)
            {
                CancelWaiting();
            }
            if (change == PlayModeStateChange.EnteredEditMode) SessionState.SetInt(FrameKey, -1);
        }

        public static void CancelWaiting()
        {
            foreach (var job in Jobs.Where(item => item.Status == "waiting"))
                Fail(job, "Play Mode stopped before startup completed.");
        }

        private static void OnUpdate()
        {
            var job = Jobs.FirstOrDefault(item => item.Status == "waiting");
            if (job == null) return;
            if (EditorApplication.timeSinceStartup - job.StartedAt >= job.Timeout)
            {
                Fail(job, "Timed out waiting for Play Mode startup. Play Mode has not been stopped.");
                return;
            }
            int enteredFrame = SessionState.GetInt(FrameKey, -1);
            var scene = SceneManager.GetActiveScene();
            bool loaded = EditorApplication.isPlaying && Application.isPlaying && enteredFrame >= 0 && scene.isLoaded;
            // Frame count increments at the start of Update. Waiting for a subsequent
            // frame ensures a whole earlier frame (including Start) has completed.
            bool firstFrame = loaded && (long)Time.frameCount - enteredFrame >= 2;
            if (!loaded || (job.WaitUntil == "first_frame" && !firstFrame)) return;
            job.SceneLoaded = true;
            job.FirstFrameComplete = firstFrame;
            job.SceneName = scene.name;
            if (job.Code == null)
            {
                job.Status = "completed";
                job.FinishedAt = EditorApplication.timeSinceStartup;
                Persist();
                return;
            }
            job.Status = "executing";
            job.CodeAttempted = true;
            Persist();
            try
            {
                // Use the same compiler, safety checks, result format and history as execute_code.
                var result = ExecuteCode.HandleCommand(new JObject { ["action"] = "execute", ["code"] = job.Code });
                job.CodeResult = JObject.FromObject(result, UnityJsonSerializer.Instance);
                job.Status = job.CodeResult.Value<bool>("success") ? "completed" : "failed";
                job.Error = job.Status == "failed" ? job.CodeResult["error"]?.ToString() ?? "Startup code failed." : null;
            }
            catch (Exception)
            {
                job.Status = "failed";
                job.Error = "Startup code failed; it will not be repeated.";
            }
            job.Code = null;
            job.FinishedAt = EditorApplication.timeSinceStartup;
            Persist();
        }

        private static void Fail(StartupJob job, string reason)
        {
            job.Status = "failed";
            job.Error = reason;
            job.Code = null;
            job.FinishedAt = EditorApplication.timeSinceStartup;
            Persist();
        }

        private static List<StartupJob> LoadJobs()
        {
            try { return JsonConvert.DeserializeObject<List<StartupJob>>(SessionState.GetString(JobsKey, "[]")) ?? new List<StartupJob>(); }
            catch (JsonException) { return new List<StartupJob>(); }
        }

        private static HashSet<string> LoadRequests()
        {
            HashSet<string> requests;
            try { requests = JsonConvert.DeserializeObject<HashSet<string>>(SessionState.GetString(RequestsKey, "[]")) ?? new HashSet<string>(); }
            catch (JsonException) { requests = new HashSet<string>(); }
            requests.UnionWith(Jobs.Select(job => job.Id));
            return requests;
        }

        private static void Persist()
        {
            SessionState.SetString(JobsKey, JsonConvert.SerializeObject(Jobs));
            SessionState.SetString(RequestsKey, JsonConvert.SerializeObject(Requests));
        }
    }
}
