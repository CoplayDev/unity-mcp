using System;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditorInternal;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MCPForUnity.Editor.Services
{
    /// <summary>
    /// Maintains a cached readiness snapshot (v2) so status reads remain fast even when Unity is busy.
    /// Updated on the main thread via Editor callbacks and periodic update ticks.
    /// </summary>
    [InitializeOnLoad]
    internal static class EditorStateCache
    {
        private static readonly object LockObj = new();
        private static long _sequence;
        private static long _observedUnixMs;

        private static bool _lastIsCompiling;
        private static long? _lastCompileStartedUnixMs;
        private static long? _lastCompileFinishedUnixMs;

        private static bool _domainReloadPending;
        private static long? _domainReloadBeforeUnixMs;
        private static long? _domainReloadAfterUnixMs;

        private static double _lastUpdateTimeSinceStartup;
        private const double MinUpdateIntervalSeconds = 0.25;

        private static JObject _cached;

        static EditorStateCache()
        {
            try
            {
                _sequence = 0;
                _observedUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                _cached = BuildSnapshot("init");

                EditorApplication.update += OnUpdate;
                EditorApplication.playModeStateChanged += _ => ForceUpdate("playmode");

                AssemblyReloadEvents.beforeAssemblyReload += () =>
                {
                    _domainReloadPending = true;
                    _domainReloadBeforeUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    ForceUpdate("before_domain_reload");
                };
                AssemblyReloadEvents.afterAssemblyReload += () =>
                {
                    _domainReloadPending = false;
                    _domainReloadAfterUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    ForceUpdate("after_domain_reload");
                };
            }
            catch (Exception ex)
            {
                McpLog.Error($"[EditorStateCache] Failed to initialise: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private static void OnUpdate()
        {
            // Throttle to reduce overhead while keeping the snapshot fresh enough for polling clients.
            double now = EditorApplication.timeSinceStartup;
            if (now - _lastUpdateTimeSinceStartup < MinUpdateIntervalSeconds)
            {
                // Still update on compilation edge transitions to keep timestamps meaningful.
                bool isCompiling = EditorApplication.isCompiling;
                if (isCompiling == _lastIsCompiling)
                {
                    return;
                }
            }

            _lastUpdateTimeSinceStartup = now;
            ForceUpdate("tick");
        }

        private static void ForceUpdate(string reason)
        {
            lock (LockObj)
            {
                _cached = BuildSnapshot(reason);
            }
        }

        private static JObject BuildSnapshot(string reason)
        {
            _sequence++;
            _observedUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            bool isCompiling = EditorApplication.isCompiling;
            if (isCompiling && !_lastIsCompiling)
            {
                _lastCompileStartedUnixMs = _observedUnixMs;
            }
            else if (!isCompiling && _lastIsCompiling)
            {
                _lastCompileFinishedUnixMs = _observedUnixMs;
            }
            _lastIsCompiling = isCompiling;

            var scene = EditorSceneManager.GetActiveScene();
            string scenePath = string.IsNullOrEmpty(scene.path) ? null : scene.path;
            string sceneGuid = !string.IsNullOrEmpty(scenePath) ? AssetDatabase.AssetPathToGUID(scenePath) : null;

            bool testsRunning = TestRunStatus.IsRunning;
            var testsMode = TestRunStatus.Mode?.ToString();
            string currentJobId = TestJobManager.CurrentJobId;
            bool isFocused = InternalEditorUtility.isApplicationActive;

            var activityPhase = "idle";
            if (testsRunning)
            {
                activityPhase = "running_tests";
            }
            else if (isCompiling)
            {
                activityPhase = "compiling";
            }
            else if (_domainReloadPending)
            {
                activityPhase = "domain_reload";
            }
            else if (EditorApplication.isUpdating)
            {
                activityPhase = "asset_import";
            }
            else if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                activityPhase = "playmode_transition";
            }

            // Keep this as a plain JSON object for minimal friction with transports.
            return JObject.FromObject(new
            {
                schema_version = "unity-mcp/editor_state@2",
                observed_at_unix_ms = _observedUnixMs,
                sequence = _sequence,
                unity = new
                {
                    instance_id = (string)null,
                    unity_version = Application.unityVersion,
                    project_id = (string)null,
                    platform = Application.platform.ToString(),
                    is_batch_mode = Application.isBatchMode
                },
                editor = new
                {
                    is_focused = isFocused,
                    play_mode = new
                    {
                        is_playing = EditorApplication.isPlaying,
                        is_paused = EditorApplication.isPaused,
                        is_changing = EditorApplication.isPlayingOrWillChangePlaymode
                    },
                    active_scene = new
                    {
                        path = scenePath,
                        guid = sceneGuid,
                        name = scene.name ?? string.Empty
                    }
                },
                activity = new
                {
                    phase = activityPhase,
                    since_unix_ms = _observedUnixMs,
                    reasons = new[] { reason }
                },
                compilation = new
                {
                    is_compiling = isCompiling,
                    is_domain_reload_pending = _domainReloadPending,
                    last_compile_started_unix_ms = _lastCompileStartedUnixMs,
                    last_compile_finished_unix_ms = _lastCompileFinishedUnixMs,
                    last_domain_reload_before_unix_ms = _domainReloadBeforeUnixMs,
                    last_domain_reload_after_unix_ms = _domainReloadAfterUnixMs
                },
                assets = new
                {
                    is_updating = EditorApplication.isUpdating,
                    external_changes_dirty = false,
                    external_changes_last_seen_unix_ms = (long?)null,
                    refresh = new
                    {
                        is_refresh_in_progress = false,
                        last_refresh_requested_unix_ms = (long?)null,
                        last_refresh_finished_unix_ms = (long?)null
                    }
                },
                tests = new
                {
                    is_running = testsRunning,
                    mode = testsMode,
                    current_job_id = string.IsNullOrEmpty(currentJobId) ? null : currentJobId,
                    started_unix_ms = TestRunStatus.StartedUnixMs,
                    started_by = "unknown",
                    last_run = TestRunStatus.FinishedUnixMs.HasValue
                        ? new
                        {
                            finished_unix_ms = TestRunStatus.FinishedUnixMs,
                            result = "unknown",
                            counts = (object)null
                        }
                        : null
                },
                transport = new
                {
                    unity_bridge_connected = (bool?)null,
                    last_message_unix_ms = (long?)null
                }
            });
        }

        public static JObject GetSnapshot()
        {
            lock (LockObj)
            {
                // Defensive: if something went wrong early, rebuild once.
                if (_cached == null)
                {
                    _cached = BuildSnapshot("rebuild");
                }
                return (JObject)_cached.DeepClone();
            }
        }
    }
}


