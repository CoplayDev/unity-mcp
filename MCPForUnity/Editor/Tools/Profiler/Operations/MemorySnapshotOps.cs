using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace MCPForUnity.Editor.Tools.Profiler
{
    internal static class MemorySnapshotOps
    {
        private static readonly Type MemoryProfilerType =
            Type.GetType("Unity.MemoryProfiler.MemoryProfiler, Unity.MemoryProfiler.Editor");

        private static bool HasPackage => MemoryProfilerType != null;

        internal static async Task<object> TakeSnapshotAsync(JObject @params)
        {
            if (!HasPackage)
                return PackageMissingError();

            var p = new ToolParams(@params);
            string snapshotPath = p.Get("snapshot_path");

            if (string.IsNullOrEmpty(snapshotPath))
            {
                string dir = Path.Combine(Application.temporaryCachePath, "MemoryCaptures");
                Directory.CreateDirectory(dir);
                snapshotPath = Path.Combine(dir, $"snapshot_{DateTime.Now:yyyyMMdd_HHmmss}.snap");
            }

            var tcs = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);

            try
            {
                var takeMethod = MemoryProfilerType.GetMethod("TakeSnapshot",
                    new[] { typeof(string), typeof(Action<string, bool>), typeof(Action<string, bool, UnityEditor.DebugScreenCapture>), typeof(uint) });

                if (takeMethod == null)
                    takeMethod = MemoryProfilerType.GetMethod("TakeSnapshot");

                if (takeMethod == null)
                    return new ErrorResponse("Could not find TakeSnapshot method on MemoryProfiler. API may have changed.");

                Action<string, bool> callback = (path, result) =>
                {
                    if (result)
                    {
                        var fi = new FileInfo(path);
                        tcs.TrySetResult(new SuccessResponse("Memory snapshot captured.", new
                        {
                            path,
                            size_bytes = fi.Exists ? fi.Length : 0,
                            size_mb = fi.Exists ? Math.Round(fi.Length / (1024.0 * 1024.0), 2) : 0,
                        }));
                    }
                    else
                    {
                        tcs.TrySetResult(new ErrorResponse($"Snapshot capture failed for path: {path}"));
                    }
                };

                takeMethod.Invoke(null, new object[] { snapshotPath, callback, null, 0u });
            }
            catch (Exception ex)
            {
                return new ErrorResponse($"Failed to take snapshot: {ex.Message}");
            }

            var timeout = Task.Delay(TimeSpan.FromSeconds(30));
            var completed = await Task.WhenAny(tcs.Task, timeout);
            if (completed == timeout)
                return new ErrorResponse("Snapshot timed out after 30 seconds.");

            return await tcs.Task;
        }

        internal static object ListSnapshots(JObject @params)
        {
            if (!HasPackage)
                return PackageMissingError();

            var p = new ToolParams(@params);
            string searchPath = p.Get("search_path");

            var dirs = new List<string>();
            if (!string.IsNullOrEmpty(searchPath))
            {
                dirs.Add(searchPath);
            }
            else
            {
                dirs.Add(Path.Combine(Application.temporaryCachePath, "MemoryCaptures"));
                dirs.Add(Path.Combine(Application.dataPath, "..", "MemoryCaptures"));
            }

            var snapshots = new List<object>();
            foreach (string dir in dirs)
            {
                if (!Directory.Exists(dir)) continue;
                foreach (string file in Directory.GetFiles(dir, "*.snap"))
                {
                    var fi = new FileInfo(file);
                    snapshots.Add(new
                    {
                        path = fi.FullName,
                        size_bytes = fi.Length,
                        size_mb = Math.Round(fi.Length / (1024.0 * 1024.0), 2),
                        created = fi.CreationTimeUtc.ToString("o"),
                    });
                }
            }

            return new SuccessResponse($"Found {snapshots.Count} snapshot(s).", new
            {
                snapshots,
                searched_dirs = dirs,
            });
        }

        internal static object CompareSnapshots(JObject @params)
        {
            if (!HasPackage)
                return PackageMissingError();

            var p = new ToolParams(@params);
            var pathAResult = p.GetRequired("snapshot_a");
            if (!pathAResult.IsSuccess)
                return new ErrorResponse(pathAResult.ErrorMessage);

            var pathBResult = p.GetRequired("snapshot_b");
            if (!pathBResult.IsSuccess)
                return new ErrorResponse(pathBResult.ErrorMessage);

            string pathA = pathAResult.Value;
            string pathB = pathBResult.Value;

            if (!File.Exists(pathA))
                return new ErrorResponse($"Snapshot file not found: {pathA}");
            if (!File.Exists(pathB))
                return new ErrorResponse($"Snapshot file not found: {pathB}");

            var fiA = new FileInfo(pathA);
            var fiB = new FileInfo(pathB);

            return new SuccessResponse("Snapshot comparison (file-level metadata).", new
            {
                snapshot_a = new
                {
                    path = fiA.FullName,
                    size_bytes = fiA.Length,
                    size_mb = Math.Round(fiA.Length / (1024.0 * 1024.0), 2),
                    created = fiA.CreationTimeUtc.ToString("o"),
                },
                snapshot_b = new
                {
                    path = fiB.FullName,
                    size_bytes = fiB.Length,
                    size_mb = Math.Round(fiB.Length / (1024.0 * 1024.0), 2),
                    created = fiB.CreationTimeUtc.ToString("o"),
                },
                delta = new
                {
                    size_delta_bytes = fiB.Length - fiA.Length,
                    size_delta_mb = Math.Round((fiB.Length - fiA.Length) / (1024.0 * 1024.0), 2),
                    time_delta_seconds = (fiB.CreationTimeUtc - fiA.CreationTimeUtc).TotalSeconds,
                },
                note = "For detailed object-level comparison, open both snapshots in the Memory Profiler window.",
            });
        }

        private static ErrorResponse PackageMissingError()
        {
            return new ErrorResponse(
                "Package com.unity.memoryprofiler is required. "
                + "Install via Package Manager or: manage_packages action=add_package package_id=com.unity.memoryprofiler");
        }
    }
}
