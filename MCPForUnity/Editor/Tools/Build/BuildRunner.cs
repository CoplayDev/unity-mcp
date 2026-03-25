using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.Build;
using MCPForUnity.Editor.Helpers;

namespace MCPForUnity.Editor.Tools.Build
{
    public static class BuildRunner
    {
        public static object ScheduleBuild(BuildJob job, BuildPlayerOptions options)
        {
            job.State = BuildJobState.Pending;
            BuildJobStore.AddBuildJob(job);

            ScheduleOnNextUpdate(() =>
                RunBuildCore(job, () => BuildPipeline.BuildPlayer(options)));

            return new PendingResponse(
                $"Build scheduled for {job.Target}. Polling for completion...",
                pollIntervalSeconds: 5.0,
                data: new { job_id = job.JobId, platform = job.Target.ToString() }
            );
        }

#if UNITY_6000_0_OR_NEWER
        public static object ScheduleProfileBuild(BuildJob job, BuildPlayerWithProfileOptions options)
        {
            job.State = BuildJobState.Pending;
            BuildJobStore.AddBuildJob(job);

            ScheduleOnNextUpdate(() =>
                RunBuildCore(job, () => BuildPipeline.BuildPlayer(options)));

            return new PendingResponse(
                $"Profile build scheduled for {job.Target}. Polling for completion...",
                pollIntervalSeconds: 5.0,
                data: new { job_id = job.JobId, platform = job.Target.ToString() }
            );
        }
#endif

        public static BuildPlayerOptions CreateBuildOptions(
            BuildTarget target,
            string outputPath,
            string[] scenes,
            BuildOptions buildOptions,
            int subtarget)
        {
            return new BuildPlayerOptions
            {
                target = target,
                targetGroup = BuildTargetMapping.GetTargetGroup(target),
                locationPathName = outputPath,
                scenes = scenes ?? GetDefaultScenes(),
                options = buildOptions,
                subtarget = subtarget
            };
        }

        public static BuildOptions ParseBuildOptions(string[] optionNames, bool development)
        {
            var opts = BuildOptions.None;
            if (development)
                opts |= BuildOptions.Development;

            if (optionNames == null) return opts;

            foreach (var name in optionNames)
            {
                switch (name.ToLowerInvariant())
                {
                    case "clean_build": opts |= BuildOptions.CleanBuildCache; break;
                    case "auto_run": opts |= BuildOptions.AutoRunPlayer; break;
                    case "deep_profiling": opts |= BuildOptions.EnableDeepProfilingSupport; break;
                    case "compress_lz4": opts |= BuildOptions.CompressWithLz4; break;
                    case "strict_mode": opts |= BuildOptions.StrictMode; break;
                    case "detailed_report": opts |= BuildOptions.DetailedBuildReport; break;
                    case "allow_debugging": opts |= BuildOptions.AllowDebugging; break;
                    case "connect_profiler": opts |= BuildOptions.ConnectWithProfiler; break;
                    case "scripts_only": opts |= BuildOptions.BuildScriptsOnly; break;
                    case "show_player": opts |= BuildOptions.ShowBuiltPlayer; break;
                    case "include_tests": opts |= BuildOptions.IncludeTestAssemblies; break;
                }
            }
            return opts;
        }

        private static void RunBuildCore(BuildJob job, Func<BuildReport> buildFunc)
        {
            job.State = BuildJobState.Building;
            job.StartedAt = DateTime.UtcNow;

            try
            {
                BuildReport report = buildFunc();
                job.CompletedAt = DateTime.UtcNow;

                // Extract summary data immediately, then let the BuildReport be GC'd
                var summary = report.summary;
                job.TotalSizeMb = Math.Round(summary.totalSize / (1024.0 * 1024.0), 2);
                job.TotalErrors = summary.totalErrors;
                job.TotalWarnings = summary.totalWarnings;

                if (summary.result == BuildResult.Succeeded)
                {
                    job.State = BuildJobState.Succeeded;
                }
                else
                {
                    job.State = BuildJobState.Failed;
                    job.ErrorMessage = GetBuildErrorMessage(report);
                }
            }
            catch (Exception ex)
            {
                job.State = BuildJobState.Failed;
                job.CompletedAt = DateTime.UtcNow;
                job.ErrorMessage = ex.Message;
            }

            BuildJobStore.SetLastCompleted(job);

            // Emit console signal so the build result is visible in Unity's Console
            if (job.State == BuildJobState.Succeeded)
            {
                UnityEngine.Debug.Log(
                    $"[MCP Build] Build succeeded: {job.Target} → {job.OutputPath} " +
                    $"({job.TotalSizeMb} MB, {(job.CompletedAt.Value - job.StartedAt).TotalSeconds:F1}s)");
            }
            else
            {
                UnityEngine.Debug.LogError(
                    $"[MCP Build] ✗ Build failed: {job.Target} — {job.ErrorMessage}");
            }
        }

        public static void ScheduleNextBatchBuild(BatchJob batch, Func<int, BuildJob> createChildBuild)
        {
            batch.CurrentIndex++;

            if (batch.State == BuildJobState.Cancelled)
            {
                for (int i = batch.CurrentIndex; i < batch.Children.Count; i++)
                    batch.Children[i].State = BuildJobState.Skipped;
                return;
            }

            if (batch.CurrentIndex >= batch.Children.Count)
            {
                bool anyFailed = batch.Children.Any(c => c.State == BuildJobState.Failed);
                batch.State = anyFailed ? BuildJobState.Failed : BuildJobState.Succeeded;
                return;
            }

            var child = batch.Children[batch.CurrentIndex];
            createChildBuild(batch.CurrentIndex);

            // Safety timeout: if child never transitions out of Building/Pending after 2 hours,
            // unregister the delegate to prevent an orphaned update loop
            var watchStart = DateTime.UtcNow;
            var maxWait = TimeSpan.FromHours(2);

            void WaitForCompletion()
            {
                if (DateTime.UtcNow - watchStart > maxWait)
                {
                    EditorApplication.update -= WaitForCompletion;
                    if (child.State == BuildJobState.Building || child.State == BuildJobState.Pending)
                    {
                        child.State = BuildJobState.Failed;
                        child.ErrorMessage = "Build timed out after 2 hours.";
                    }
                    ScheduleNextBatchBuild(batch, createChildBuild);
                    return;
                }

                if (child.State == BuildJobState.Building || child.State == BuildJobState.Pending)
                    return;

                EditorApplication.update -= WaitForCompletion;
                ScheduleNextBatchBuild(batch, createChildBuild);
            }
            EditorApplication.update += WaitForCompletion;
        }

        /// <summary>
        /// Schedule an action to run on the next EditorApplication.update tick.
        /// Unlike delayCall, update callbacks are guaranteed to fire since the
        /// TransportCommandDispatcher processes commands through the same mechanism.
        /// </summary>
        private static void ScheduleOnNextUpdate(Action action)
        {
            bool executed = false;
            void RunOnce()
            {
                if (executed) return;
                executed = true;
                EditorApplication.update -= RunOnce;
                action();
            }
            EditorApplication.update += RunOnce;
        }

        private static string GetBuildErrorMessage(BuildReport report)
        {
            var summary = report.summary;

#if UNITY_2023_1_OR_NEWER
            var msg = report.SummarizeErrors();
            if (!string.IsNullOrEmpty(msg))
                return msg;
#endif

            if (summary.totalErrors > 0)
                return $"{summary.totalErrors} build error(s)";

            return $"Build failed with result: {summary.result}";
        }

        private static string[] GetDefaultScenes()
        {
            return EditorBuildSettings.scenes
                .Where(s => s.enabled)
                .Select(s => s.path)
                .ToArray();
        }
    }
}
