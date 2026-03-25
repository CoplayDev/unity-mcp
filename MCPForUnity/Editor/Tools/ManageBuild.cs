using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Tools.Build;

namespace MCPForUnity.Editor.Tools
{
    [McpForUnityTool("manage_build", AutoRegister = false, Group = "core",
        RequiresPolling = true, PollAction = "status", MaxPollSeconds = 1800)]
    public static class ManageBuild
    {
        private static readonly string[] ValidActions =
            { "build", "status", "platform", "settings", "scenes", "profiles", "batch", "cancel" };

        public static object HandleCommand(JObject @params)
        {
            if (@params == null)
                return new ErrorResponse("Parameters cannot be null.");

            var p = new ToolParams(@params);
            var actionResult = p.GetRequired("action");
            if (!actionResult.IsSuccess)
                return new ErrorResponse(actionResult.ErrorMessage);

            string action = actionResult.Value.ToLowerInvariant();

            if (!ValidActions.Contains(action))
                return new ErrorResponse(
                    $"Unknown action '{action}'. Valid actions: {string.Join(", ", ValidActions)}");

            try
            {
                switch (action)
                {
                    case "build": return HandleBuild(p);
                    case "status": return HandleStatus(p);
                    case "platform": return HandlePlatform(p);
                    case "settings": return HandleSettings(p);
                    case "scenes": return HandleScenes(p);
                    case "profiles": return HandleProfiles(p);
                    case "batch": return HandleBatch(p);
                    case "cancel": return HandleCancel(p);
                    default:
                        return new ErrorResponse($"Unknown action: '{action}'");
                }
            }
            catch (Exception ex)
            {
                return new ErrorResponse(ex.Message, new { stackTrace = ex.StackTrace });
            }
        }

        // ── build ──────────────────────────────────────────────────────

        private static object HandleBuild(ToolParams p)
        {
            if (BuildPipeline.isBuildingPlayer)
                return new ErrorResponse("A build is already in progress.");

            string targetName = p.Get("target");
            if (!BuildTargetMapping.TryResolveBuildTarget(targetName, out var target))
                return new ErrorResponse($"Unknown target '{targetName}'.");

            var group = BuildTargetMapping.GetTargetGroup(target);
            if (!BuildPipeline.IsBuildTargetSupported(group, target))
                return new ErrorResponse(
                    $"Platform '{target}' is not installed. Install it via Unity Hub.");

            string outputPath = p.Get("output_path")
                ?? BuildTargetMapping.GetDefaultOutputPath(target, PlayerSettings.productName);
            string[] scenes = p.GetStringArray("scenes");
            bool development = p.GetBool("development");
            string[] optionNames = p.GetStringArray("options");
            string subtargetStr = p.Get("subtarget");
            string scriptingBackend = p.Get("scripting_backend");

            // Apply scripting backend if specified (persistent change)
            if (!string.IsNullOrEmpty(scriptingBackend))
            {
                string backendLower = scriptingBackend.ToLowerInvariant();
                if (backendLower != "il2cpp" && backendLower != "mono")
                    return new ErrorResponse(
                        $"Unknown scripting_backend '{scriptingBackend}'. Valid: mono, il2cpp");
                var namedTarget = BuildTargetMapping.GetNamedBuildTarget(target);
                var impl = backendLower == "il2cpp"
                    ? ScriptingImplementation.IL2CPP
                    : ScriptingImplementation.Mono2x;
                PlayerSettings.SetScriptingBackend(namedTarget, impl);
            }

#if UNITY_6000_0_OR_NEWER
            string profilePath = p.Get("profile");
            if (!string.IsNullOrEmpty(profilePath))
                return HandleProfileBuild(p, profilePath, outputPath, development, optionNames);
#else
            string profilePath = p.Get("profile");
            if (!string.IsNullOrEmpty(profilePath))
                McpLog.Warn($"Build Profile param ignored — requires Unity 6+. Current: {UnityEngine.Application.unityVersion}");
#endif

            var buildOptions = BuildRunner.ParseBuildOptions(optionNames, development);
            int subtarget = BuildTargetMapping.ResolveSubtarget(subtargetStr);
            var options = BuildRunner.CreateBuildOptions(target, outputPath, scenes, buildOptions, subtarget);

            string jobId = BuildJobStore.CreateJobId();
            var job = new BuildJob(jobId, target, outputPath);
            return BuildRunner.ScheduleBuild(job, options);
        }

#if UNITY_6000_0_OR_NEWER
        private static object HandleProfileBuild(ToolParams p, string profilePath, string outputPath,
            bool development, string[] optionNames)
        {
            var profile = UnityEditor.AssetDatabase.LoadAssetAtPath<
                UnityEditor.Build.Profile.BuildProfile>(profilePath);
            if (profile == null)
                return new ErrorResponse($"Build profile not found at: {profilePath}");

            var buildOptions = BuildRunner.ParseBuildOptions(optionNames, development);
            var options = new BuildPlayerWithProfileOptions
            {
                buildProfile = profile,
                locationPathName = outputPath,
                options = buildOptions
            };

            // BuildPlayerWithProfileOptions derives the actual target from the profile,
            // but we use activeBuildTarget for job metadata/status display
            var target = EditorUserBuildSettings.activeBuildTarget;
            string jobId = BuildJobStore.CreateJobId();
            var job = new BuildJob(jobId, target, outputPath);
            return BuildRunner.ScheduleProfileBuild(job, options);
        }
#endif

        // ── status ─────────────────────────────────────────────────────

        private static object HandleStatus(ToolParams p)
        {
            string jobId = p.Get("job_id");

            if (string.IsNullOrEmpty(jobId))
            {
                // Prefer active (pending/building) job — needed for polling middleware
                var last = BuildJobStore.LastCompletedJob;
                if (last != null && (last.State == BuildJobState.Building || last.State == BuildJobState.Pending))
                {
                    return new PendingResponse(
                        $"Build {last.State.ToString().ToLowerInvariant()}...",
                        pollIntervalSeconds: 5.0,
                        data: last.ToStatusResponse()
                    );
                }

                if (last != null)
                    return new SuccessResponse("Last completed build.", last.ToStatusResponse());

#if UNITY_6000_0_OR_NEWER
                var latestReport = BuildReport.GetLatestReport();
                if (latestReport != null)
                {
                    var s = latestReport.summary;
                    return new SuccessResponse("Last build report from Unity.", new
                    {
                        result = s.result.ToString().ToLowerInvariant(),
                        platform = s.platform.ToString(),
                        output_path = s.outputPath,
                        total_size_mb = Math.Round(s.totalSize / (1024.0 * 1024.0), 2),
                        duration_seconds = s.totalTime.TotalSeconds,
                        errors = s.totalErrors,
                        warnings = s.totalWarnings
                    });
                }
#endif
                return new ErrorResponse("No build jobs found.");
            }

            // Check batch jobs first
            var batchJob = BuildJobStore.GetBatchJob(jobId);
            if (batchJob != null)
                return new SuccessResponse($"Batch {batchJob.State}.", batchJob.ToStatusResponse());

            var buildJob = BuildJobStore.GetBuildJob(jobId);
            if (buildJob == null)
                return new ErrorResponse($"No job found with ID: {jobId}");

            if (buildJob.State == BuildJobState.Building || buildJob.State == BuildJobState.Pending)
            {
                return new PendingResponse(
                    $"Build {buildJob.State.ToString().ToLowerInvariant()}...",
                    pollIntervalSeconds: 5.0,
                    data: buildJob.ToStatusResponse()
                );
            }

            return new SuccessResponse($"Build {buildJob.State}.", buildJob.ToStatusResponse());
        }

        // ── platform ───────────────────────────────────────────────────

        private static object HandlePlatform(ToolParams p)
        {
            string targetName = p.Get("target");

            if (string.IsNullOrEmpty(targetName))
            {
                // Read current platform
                return new SuccessResponse("Current platform.", new
                {
                    target = EditorUserBuildSettings.activeBuildTarget.ToString(),
                    target_group = BuildTargetMapping.GetTargetGroup(
                        EditorUserBuildSettings.activeBuildTarget).ToString(),
                    subtarget = EditorUserBuildSettings.standaloneBuildSubtarget.ToString()
                });
            }

            // Switch platform
            if (!BuildTargetMapping.TryResolveBuildTarget(targetName, out var target))
                return new ErrorResponse($"Unknown target '{targetName}'.");

            var group = BuildTargetMapping.GetTargetGroup(target);
            if (!BuildPipeline.IsBuildTargetSupported(group, target))
                return new ErrorResponse(
                    $"Platform '{target}' is not installed. Install it via Unity Hub.");

            if (EditorUserBuildSettings.activeBuildTarget == target)
                return new SuccessResponse("Already on this platform.", new
                {
                    target = target.ToString()
                });

            // Capture previous target before switching
            string previousTarget = EditorUserBuildSettings.activeBuildTarget.ToString();

            string subtargetStr = p.Get("subtarget");
            if (!string.IsNullOrEmpty(subtargetStr))
            {
                string subtargetLower = subtargetStr.ToLowerInvariant();
                if (subtargetLower == "server")
                    EditorUserBuildSettings.standaloneBuildSubtarget = StandaloneBuildSubtarget.Server;
                else if (subtargetLower == "player")
                    EditorUserBuildSettings.standaloneBuildSubtarget = StandaloneBuildSubtarget.Player;
            }

            // SwitchActiveBuildTarget is synchronous — blocks until reimport completes
            EditorUserBuildSettings.SwitchActiveBuildTarget(group, target);

            return new SuccessResponse(
                $"Switched to {target}. Assets reimported for new platform.",
                new { target = target.ToString(), previous = previousTarget }
            );
        }

        // ── settings ───────────────────────────────────────────────────

        private static object HandleSettings(ToolParams p)
        {
            var propertyResult = p.GetRequired("property");
            if (!propertyResult.IsSuccess)
                return new ErrorResponse(propertyResult.ErrorMessage);

            string property = propertyResult.Value.ToLowerInvariant();
            string targetName = p.Get("target");
            string value = p.Get("value");

            // Resolve target
            string err = BuildTargetMapping.TryResolveNamedBuildTarget(targetName, out var namedTarget);
            if (err != null)
                return new ErrorResponse(err);

            if (string.IsNullOrEmpty(value))
            {
                // Read
                var result = BuildSettingsHelper.ReadProperty(property, namedTarget);
                if (result == null)
                    return new ErrorResponse(
                        $"Unknown property '{property}'. Valid: {string.Join(", ", BuildSettingsHelper.ValidProperties)}");
                return new SuccessResponse($"Read {property}.", result);
            }

            // Write
            string writeErr = BuildSettingsHelper.WriteProperty(property, value, namedTarget);
            if (writeErr != null)
                return new ErrorResponse(writeErr);
            return new SuccessResponse($"Set {property} = {value}.",
                BuildSettingsHelper.ReadProperty(property, namedTarget));
        }

        // ── scenes ─────────────────────────────────────────────────────

        private static object HandleScenes(ToolParams p)
        {
            var scenesRaw = p.GetRaw("scenes");

            if (scenesRaw == null || scenesRaw.Type == JTokenType.Null)
            {
                // Read current scene list
                var scenes = EditorBuildSettings.scenes.Select(s => new
                {
                    path = s.path,
                    enabled = s.enabled,
                    guid = s.guid.ToString()
                }).ToArray();

                return new SuccessResponse($"Build scenes ({scenes.Length}).", new { scenes });
            }

            // Write scene list
            var sceneArray = scenesRaw as JArray;
            if (sceneArray == null)
                return new ErrorResponse("'scenes' must be an array of {path, enabled} objects.");

            var newScenes = new List<EditorBuildSettingsScene>();
            foreach (var item in sceneArray)
            {
                string path = item["path"]?.ToString();
                if (string.IsNullOrEmpty(path))
                    return new ErrorResponse("Each scene must have a 'path' field.");
                bool enabled = item["enabled"]?.Value<bool>() ?? true;
                newScenes.Add(new EditorBuildSettingsScene(path, enabled));
            }

            EditorBuildSettings.scenes = newScenes.ToArray();
            return new SuccessResponse($"Updated build scenes ({newScenes.Count}).", new
            {
                scenes = newScenes.Select(s => new { path = s.path, enabled = s.enabled }).ToArray()
            });
        }

        // ── profiles ───────────────────────────────────────────────────

        private static object HandleProfiles(ToolParams p)
        {
#if UNITY_6000_0_OR_NEWER
            string profilePath = p.Get("profile");
            bool activate = p.GetBool("activate");

            if (string.IsNullOrEmpty(profilePath))
            {
                // List all profiles
                var guids = AssetDatabase.FindAssets("t:BuildProfile");
                var profiles = guids.Select(guid =>
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    return new { path, name = System.IO.Path.GetFileNameWithoutExtension(path) };
                }).ToArray();

                var active = UnityEditor.Build.Profile.BuildProfile.GetActiveBuildProfile();
                return new SuccessResponse($"Found {profiles.Length} build profiles.", new
                {
                    profiles,
                    active_profile = active != null ? AssetDatabase.GetAssetPath(active) : null
                });
            }

            var loadedProfile = AssetDatabase.LoadAssetAtPath<
                UnityEditor.Build.Profile.BuildProfile>(profilePath);
            if (loadedProfile == null)
                return new ErrorResponse($"Build profile not found at: {profilePath}");

            if (activate)
            {
                UnityEditor.Build.Profile.BuildProfile.SetActiveBuildProfile(loadedProfile);
                return new SuccessResponse($"Activated build profile: {profilePath}", new
                {
                    profile = profilePath,
                    activated = true
                });
            }

            // Get profile details
            var profileScenes = loadedProfile.GetScenesForBuild()
                .Select(s => s.path).ToArray();
            return new SuccessResponse($"Profile: {profilePath}", new
            {
                profile = profilePath,
                scenes = profileScenes
            });
#else
            string version = UnityEngine.Application.unityVersion;
            return new ErrorResponse(
                $"Build Profiles require Unity 6 (6000.0+). Current version: {version}");
#endif
        }

        // ── batch ──────────────────────────────────────────────────────

        private static object HandleBatch(ToolParams p)
        {
            if (BuildPipeline.isBuildingPlayer)
                return new ErrorResponse("A build is already in progress.");

            string[] targets = p.GetStringArray("targets");
            string[] profiles = p.GetStringArray("profiles");
            string outputDir = p.Get("output_dir") ?? "Builds";
            bool development = p.GetBool("development");
            string[] optionNames = p.GetStringArray("options");

            if ((targets == null || targets.Length == 0) && (profiles == null || profiles.Length == 0))
                return new ErrorResponse("'targets' or 'profiles' is required for batch builds.");
            if (targets != null && targets.Length > 0 && profiles != null && profiles.Length > 0)
                return new ErrorResponse("Provide 'targets' or 'profiles', not both.");

            // Validate all targets/profiles upfront before creating store entries
            if (targets != null && targets.Length > 0)
            {
                var resolvedTargets = new List<(BuildTarget bt, string path)>();
                foreach (var t in targets)
                {
                    if (!BuildTargetMapping.TryResolveBuildTarget(t, out var bt))
                        return new ErrorResponse($"Unknown target '{t}' in batch.");
                    var btGroup = BuildTargetMapping.GetTargetGroup(bt);
                    if (!BuildPipeline.IsBuildTargetSupported(btGroup, bt))
                        return new ErrorResponse(
                            $"Platform '{bt}' is not installed. Install it via Unity Hub.");
                    string defaultPath = BuildTargetMapping.GetDefaultOutputPath(bt, PlayerSettings.productName);
                    string path = defaultPath.StartsWith("Builds/")
                        ? $"{outputDir}/{defaultPath.Substring(7)}"
                        : $"{outputDir}/{defaultPath}";
                    resolvedTargets.Add((bt, path));
                }

                string batchId = BuildJobStore.CreateBatchId();
                var batch = new BatchJob(batchId);
                batch.State = BuildJobState.Building;
                BuildJobStore.AddBatchJob(batch);

                foreach (var (bt, path) in resolvedTargets)
                {
                    var child = new BuildJob(BuildJobStore.CreateJobId(), bt, path);
                    batch.Children.Add(child);
                }

                var buildOpts = BuildRunner.ParseBuildOptions(optionNames, development);

                BuildRunner.ScheduleNextBatchBuild(batch, index =>
                {
                    var child = batch.Children[index];
                    var group = BuildTargetMapping.GetTargetGroup(child.Target);

                    // Platform switch is required — ensures correct shader variants,
                    // asset import settings, and scripting defines for the target
                    if (EditorUserBuildSettings.activeBuildTarget != child.Target)
                        EditorUserBuildSettings.SwitchActiveBuildTarget(group, child.Target);

                    int subtarget = (int)StandaloneBuildSubtarget.Player;
                    var options = BuildRunner.CreateBuildOptions(
                        child.Target, child.OutputPath, null, buildOpts, subtarget);
                    BuildRunner.ScheduleBuild(child, options);
                    return child;
                });

                return new PendingResponse(
                    $"Batch build started ({batch.Children.Count} builds).",
                    pollIntervalSeconds: 10.0,
                    data: new { job_id = batchId, total = batch.Children.Count }
                );
            }
#if UNITY_6000_0_OR_NEWER
            if (profiles != null && profiles.Length > 0)
            {
                // Validate all profiles exist before creating any store entries
                var loadedProfiles = new List<UnityEditor.Build.Profile.BuildProfile>();
                foreach (var profilePath in profiles)
                {
                    var profile = AssetDatabase.LoadAssetAtPath<
                        UnityEditor.Build.Profile.BuildProfile>(profilePath);
                    if (profile == null)
                        return new ErrorResponse($"Profile not found: {profilePath}");
                    loadedProfiles.Add(profile);
                }

                string batchId = BuildJobStore.CreateBatchId();
                var batch = new BatchJob(batchId);
                batch.State = BuildJobState.Building;
                BuildJobStore.AddBatchJob(batch);

                for (int i = 0; i < profiles.Length; i++)
                {
                    var target = EditorUserBuildSettings.activeBuildTarget;
                    string name = System.IO.Path.GetFileNameWithoutExtension(profiles[i]);
                    string path = $"{outputDir}/{name}/{PlayerSettings.productName}";
                    var child = new BuildJob(BuildJobStore.CreateJobId(), target, path);
                    batch.Children.Add(child);
                }

                var buildOpts = BuildRunner.ParseBuildOptions(optionNames, development);

                BuildRunner.ScheduleNextBatchBuild(batch, index =>
                {
                    var child = batch.Children[index];
                    var opts = new BuildPlayerWithProfileOptions
                    {
                        buildProfile = loadedProfiles[index],
                        locationPathName = child.OutputPath,
                        options = buildOpts
                    };
                    BuildRunner.ScheduleProfileBuild(child, opts);
                    return child;
                });

                return new PendingResponse(
                    $"Batch build started ({batch.Children.Count} builds).",
                    pollIntervalSeconds: 10.0,
                    data: new { job_id = batchId, total = batch.Children.Count }
                );
            }
#else
            if (profiles != null && profiles.Length > 0)
            {
                return new ErrorResponse(
                    $"Profile-based batch requires Unity 6+. Current: {UnityEngine.Application.unityVersion}");
            }
#endif

            return new ErrorResponse("'targets' or 'profiles' is required for batch builds.");
        }

        // ── cancel ─────────────────────────────────────────────────────

        private static object HandleCancel(ToolParams p)
        {
            var jobIdResult = p.GetRequired("job_id");
            if (!jobIdResult.IsSuccess)
                return new ErrorResponse(jobIdResult.ErrorMessage);

            string jobId = jobIdResult.Value;

            var batchJob = BuildJobStore.GetBatchJob(jobId);
            if (batchJob != null)
            {
                if (batchJob.State == BuildJobState.Building)
                {
                    batchJob.State = BuildJobState.Cancelled;
                    return new SuccessResponse(
                        "Batch cancelled. The current build will finish but no more builds will start.",
                        new { job_id = jobId, state = "cancelled" });
                }
                return new ErrorResponse($"Batch is already {batchJob.State}.");
            }

            var buildJob = BuildJobStore.GetBuildJob(jobId);
            if (buildJob != null)
            {
                if (buildJob.State == BuildJobState.Building)
                    return new ErrorResponse(
                        "Cannot cancel a single build in progress. BuildPipeline.BuildPlayer is " +
                        "synchronous and blocks the editor until completion.");
                return new ErrorResponse($"Build is already {buildJob.State}.");
            }

            return new ErrorResponse($"No job found with ID: {jobId}");
        }
    }
}
