using System;
using System.IO;
using System.Linq;
using System.Reflection;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;

namespace MCPForUnity.Editor.Tools
{
    [InitializeOnLoad]
    internal static class AddressablesBuildJobs
    {
        private const string SessionKey = "MCPForUnity.Addressables.LastBuild";
        private static JObject _job;

        static AddressablesBuildJobs()
        {
            string saved = SessionState.GetString(SessionKey, "");
            if (string.IsNullOrEmpty(saved)) return;
            _job = JObject.Parse(saved);
            if (IsBusy)
            {
                _job["state"] = "failed";
                _job["error"] = "Editor domain reloaded before a result was recorded. Inspect build outputs before retrying.";
                Store();
            }
        }

        internal static bool IsBusy => (string)_job?["state"] == "queued" || (string)_job?["state"] == "running";

        private static void Store() => SessionState.SetString(SessionKey, _job.ToString(Formatting.None));

        internal static object Status(string id)
        {
            if (_job == null || (id != null && id != (string)_job["job_id"]))
                return new ErrorResponse("ADDRESSABLES_BUILD_NOT_FOUND", new { message = "Only the latest content job is retained in this Editor session." });
            var data = _job.DeepClone();
            if (IsBusy) return new PendingResponse("Addressables content operation is pending.", 2, data);
            if ((string)_job["state"] == "failed") return new ErrorResponse("ADDRESSABLES_BUILD_FAILED", data);
            return new SuccessResponse("Addressables content operation completed.", data);
        }

        internal static object Start(object settings, string action, string statePath)
        {
            if (IsBusy || BuildPipeline.isBuildingPlayer || EditorApplication.isCompiling
                || EditorApplication.isPlayingOrWillChangePlaymode)
                throw new ArgumentException("Content operations require an idle Editor outside Play mode.");
            var type = settings.GetType();
            var updateType = type.Assembly.GetType("UnityEditor.AddressableAssets.Build.ContentUpdateScript");
            MethodInfo method;
            if (action == "build_content")
                method = type.GetMethods(BindingFlags.Public | BindingFlags.Static).SingleOrDefault(candidate =>
                    candidate.Name == "BuildPlayerContent" && candidate.GetParameters().Length == 1 && candidate.GetParameters()[0].IsOut);
            else if (action == "build_clean")
                method = type.GetMethods(BindingFlags.Public | BindingFlags.Static).SingleOrDefault(candidate =>
                    candidate.Name == "CleanPlayerContent" && candidate.GetParameters().Length == 1);
            else
            {
                if (string.IsNullOrWhiteSpace(statePath) || !File.Exists(statePath) || !statePath.EndsWith(".bin", StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException("build_update requires an existing content_state_path .bin file from the matching previous build.");
                statePath = Path.GetFullPath(statePath);
                method = updateType?.GetMethod("BuildContentUpdate", new[] { type, typeof(string) });
            }
            if (method == null) throw new MissingMethodException($"Addressables API for '{action}' is unavailable.");
            if (action == "build_update" && updateType.GetMethod("GatherModifiedEntries", new[] { type, typeof(string) }) == null)
                throw new MissingMethodException("GatherModifiedEntries");

            string settingsPath = AssetDatabase.GetAssetPath((UnityEngine.Object)settings);
            var target = EditorUserBuildSettings.activeBuildTarget;
            string profile = ReadProfileId(settings);
            if (string.IsNullOrEmpty(profile)) throw new ArgumentException("Select an active profile before building content.");
            var builder = type.GetProperty("ActivePlayerDataBuilder")?.GetValue(settings);
            if (builder == null) throw new ArgumentException("No active player data builder is configured.");
            _job = new JObject
            {
                ["job_id"] = Guid.NewGuid().ToString("N"), ["action"] = action,
                ["state"] = "queued", ["platform"] = target.ToString(),
                ["settings_path"] = settingsPath, ["profile_id"] = profile,
                ["content_state_input"] = statePath, ["queued_at"] = DateTime.UtcNow.ToString("O"),
                ["cancellation_supported"] = false
            };
            Store();
            // Native Addressables builds must run on Unity's main thread. Schedule after returning the job ID.
            void Run()
            {
                EditorApplication.update -= Run;
                _job["state"] = "running";
                Store();
                try
                {
                    if (BuildPipeline.isBuildingPlayer || EditorApplication.isCompiling
                        || EditorApplication.isPlayingOrWillChangePlaymode)
                        throw new InvalidOperationException("Editor became busy while the content operation was queued.");
                    if ((UnityEngine.Object)settings == null || settingsPath != AssetDatabase.GetAssetPath((UnityEngine.Object)settings)
                        || !ReferenceEquals(settings, type.Assembly.GetType("UnityEditor.AddressableAssets.AddressableAssetSettingsDefaultObject")
                            ?.GetMethod("GetSettings", new[] { typeof(bool) })?.Invoke(null, new object[] { false }))
                        || target != EditorUserBuildSettings.activeBuildTarget || profile != ReadProfileId(settings)
                        || !ReferenceEquals(builder, type.GetProperty("ActivePlayerDataBuilder")?.GetValue(settings)))
                        throw new InvalidOperationException("Build configuration changed while the job was queued.");
                    AssetDatabase.SaveAssets();
                    object result = null;
                    if (action == "build_update")
                    {
                        var changes = updateType.GetMethod("GatherModifiedEntries", new[] { type, typeof(string) })
                            .Invoke(null, new[] { settings, (object)statePath }) as System.Collections.IEnumerable;
                        if (changes == null) throw new InvalidOperationException("Unable to inspect content update restrictions.");
                        var restricted = changes.Cast<object>().ToList();
                        if (restricted.Count > 0)
                        {
                            _job["restricted_entries"] = new JArray(restricted.Take(200).Select(entry =>
                                entry.GetType().GetProperty("guid")?.GetValue(entry)?.ToString()));
                            throw new InvalidOperationException("Static-content restrictions require moving modified entries to a new update group before building.");
                        }
                        result = method.Invoke(null, new[] { settings, (object)statePath });
                    }
                    else if (action == "build_clean")
                        method.Invoke(null, new[] { builder });
                    else
                    {
                        object[] args = { null };
                        method.Invoke(null, args);
                        result = args[0];
                    }
                    if (action != "build_clean")
                    {
                        if (result == null) throw new InvalidOperationException("Addressables returned no build result.");
                        string error = result.GetType().GetProperty("Error")?.GetValue(result) as string;
                        if (!string.IsNullOrEmpty(error)) throw new InvalidOperationException(error);
                        _job["output_path"] = result.GetType().GetProperty("OutputPath")?.GetValue(result)?.ToString();
                        var stateMethod = updateType?.GetMethod("GetContentStateDataPath", new[] { typeof(bool), type });
                        // Older packages expose only the one-argument overload; the queued job still uses the active settings.
                        object[] stateArgs = { false, settings };
                        if (stateMethod == null)
                        {
                            stateMethod = updateType?.GetMethod("GetContentStateDataPath", new[] { typeof(bool) });
                            stateArgs = new object[] { false };
                        }
                        if (stateMethod == null) throw new MissingMethodException("GetContentStateDataPath");
                        _job["content_state_path"] = stateMethod.Invoke(null, stateArgs) as string;
                    }
                    _job["state"] = "succeeded";
                }
                catch (Exception ex)
                {
                    McpLog.Warn($"[Addressables] Content operation failed: {ex}");
                    _job["state"] = "failed";
                    _job["error"] = (ex is TargetInvocationException invocation ? invocation.InnerException : ex)?.Message;
                    _job["outputs_may_exist"] = true;
                }
                finally
                {
                    _job["completed_at"] = DateTime.UtcNow.ToString("O");
                    Store();
                }
            }
            EditorApplication.update += Run;
            return new PendingResponse("Content operation queued. Poll build_status using job_id.", 2, _job.DeepClone());
        }

        private static string ReadProfileId(object settings)
        {
            using var serialized = new SerializedObject((UnityEngine.Object)settings);
            var property = serialized.FindProperty("m_ActiveProfileId");
            if (property == null) throw new MissingMemberException("m_ActiveProfileId");
            return property.stringValue;
        }
    }
}
