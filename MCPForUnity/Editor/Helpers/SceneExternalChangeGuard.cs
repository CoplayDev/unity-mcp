using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace MCPForUnity.Editor.Helpers
{
    /// <summary>
    /// Detects that a scene open in the Editor was modified on disk behind Unity's back — the usual
    /// cause being an agent editing the <c>.unity</c> YAML directly — and resolves it before an
    /// asset refresh can raise Unity's blocking "Scene(s) Have Been Modified" prompt.
    ///
    /// The prompt is modal, so it stalls the Editor's main thread and with it every MCP command
    /// until a human dismisses it. This guard removes the condition that raises it instead of
    /// answering it: the on-disk and in-memory copies are reconciled first, in whichever direction
    /// the caller asked for.
    /// </summary>
    [InitializeOnLoad]
    internal static class SceneExternalChangeGuard
    {
        internal const string ModeAuto = "auto";
        internal const string ModeReload = "reload";
        internal const string ModeKeepEditor = "keep_editor";

        private const string BaselineKey = "MCPForUnity.OpenSceneMtimeBaseline";

        static SceneExternalChangeGuard()
        {
            // Without a baseline from scene-open time the first refresh of a session has nothing to
            // compare against, so the edit reaches AssetDatabase.Refresh — which is where the modal
            // comes from. Each callback updates only the scene it names: rewriting the whole map
            // would stamp the current mtime onto an unrelated open scene that was edited but not yet
            // reconciled, erasing the change this guard exists to catch.
            EditorSceneManager.sceneOpened += (scene, _) => UpsertBaseline(scene.path);
            EditorSceneManager.sceneSaved += scene => UpsertBaseline(scene.path);
            EditorApplication.delayCall += FillMissingBaselines;
        }

        internal sealed class Outcome
        {
            public bool Blocked { get; set; }
            public string Error { get; set; }
            public List<string> ChangedScenes { get; } = new List<string>();
            public List<string> ReloadedScenes { get; } = new List<string>();
            public List<string> OverwrittenScenes { get; } = new List<string>();
        }

        /// <summary>
        /// Reconcile any open scene whose file changed on disk since we last recorded it.
        /// Returns an outcome whose <see cref="Outcome.Blocked"/> flag means the caller must not
        /// refresh: doing so would raise the modal prompt.
        /// </summary>
        internal static Outcome Reconcile(string mode)
        {
            var outcome = new Outcome();
            mode = string.IsNullOrWhiteSpace(mode) ? ModeAuto : mode.Trim().ToLowerInvariant();

            var baseline = LoadBaseline();
            var openScenes = OpenScenes();

            var changed = new List<Scene>();
            foreach (var scene in openScenes)
            {
                if (string.IsNullOrEmpty(scene.path) || !File.Exists(scene.path))
                {
                    continue;
                }

                long diskMtime = MtimeOf(scene.path);
                if (baseline.TryGetValue(scene.path, out long known) && diskMtime > known)
                {
                    changed.Add(scene);
                    outcome.ChangedScenes.Add(scene.path);
                }
            }

            if (changed.Count == 0)
            {
                RecordBaseline(openScenes);
                return outcome;
            }

            if (mode == ModeKeepEditor)
            {
                foreach (var scene in changed)
                {
                    // Capture before saving: the path is read back off the Scene struct, and
                    // anything that reopens or replaces the scene invalidates that handle.
                    string path = scene.path;
                    EditorSceneManager.SaveScene(scene);
                    outcome.OverwrittenScenes.Add(path);
                }

                RecordBaseline(OpenScenes());
                return outcome;
            }

            bool anyDirty = changed.Any(s => s.isDirty);
            bool multiScene = openScenes.Count > 1;

            if (mode == ModeAuto && (anyDirty || multiScene))
            {
                outcome.Blocked = true;
                outcome.Error = BuildBlockedMessage(changed, anyDirty, multiScene);
                return outcome;
            }

            // mode == reload, or auto with a single clean scene: take the on-disk version.
            if (multiScene)
            {
                // Reopening one scene of a multi-scene setup as Single would unload the others, and
                // reopening additively would reorder them. Refuse rather than rearrange the setup.
                outcome.Blocked = true;
                outcome.Error = BuildBlockedMessage(changed, anyDirty, true);
                return outcome;
            }

            // Reopening a scene invalidates every Scene struct describing it, so the paths are read
            // out before any of them are used.
            var reloadPaths = changed.Select(s => s.path).ToList();
            foreach (var path in reloadPaths)
            {
                EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                outcome.ReloadedScenes.Add(path);
            }

            RecordBaseline(OpenScenes());
            return outcome;
        }

        /// <summary>
        /// Record the current on-disk timestamps as the known-good baseline. Called after any
        /// operation that syncs Unity with disk, so the next check only sees genuinely new edits.
        /// </summary>
        internal static void RecordBaseline()
        {
            RecordBaseline(OpenScenes());
        }

        private static string BuildBlockedMessage(List<Scene> changed, bool anyDirty, bool multiScene)
        {
            string names = string.Join(", ", changed.Select(s => s.path).ToArray());

            // Reloading one scene of a multi-scene setup would unload or reorder the others, so
            // only keep_editor is offered there.
            return multiScene
                ? $"Scene(s) changed on disk ({names}) and several scenes are open. "
                  + $"Set on_external_scene_change to \"{ModeKeepEditor}\", or close the other scenes."
                : $"Scene(s) changed on disk ({names}) with unsaved Editor changes. "
                  + $"Set on_external_scene_change to \"{ModeReload}\" or \"{ModeKeepEditor}\".";
        }

        private static List<Scene> OpenScenes()
        {
            var scenes = new List<Scene>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (scene.IsValid())
                {
                    scenes.Add(scene);
                }
            }

            return scenes;
        }

        private static long MtimeOf(string path)
        {
            try
            {
                return File.GetLastWriteTimeUtc(path).Ticks;
            }
            catch
            {
                return 0;
            }
        }

        private static Dictionary<string, long> LoadBaseline()
        {
            try
            {
                string raw = SessionState.GetString(BaselineKey, null);
                if (!string.IsNullOrEmpty(raw))
                {
                    var parsed = JsonConvert.DeserializeObject<Dictionary<string, long>>(raw);
                    if (parsed != null)
                    {
                        return parsed;
                    }
                }
            }
            catch
            {
                // Corrupt baseline is not worth failing a refresh over; rebuild it below.
            }

            return new Dictionary<string, long>(StringComparer.Ordinal);
        }

        /// <summary>Record one scene's current mtime, leaving every other entry alone.</summary>
        private static void UpsertBaseline(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                return;
            }

            var map = LoadBaseline();
            map[path] = MtimeOf(path);
            SaveBaseline(map);
        }

        /// <summary>
        /// Seed baselines for open scenes that have none, without overwriting the ones already
        /// recorded — an existing entry may be the only evidence of an unreconciled edit.
        /// </summary>
        private static void FillMissingBaselines()
        {
            var map = LoadBaseline();
            bool changed = false;
            foreach (var scene in OpenScenes())
            {
                if (string.IsNullOrEmpty(scene.path) || !File.Exists(scene.path))
                {
                    continue;
                }

                if (!map.ContainsKey(scene.path))
                {
                    map[scene.path] = MtimeOf(scene.path);
                    changed = true;
                }
            }

            if (changed)
            {
                SaveBaseline(map);
            }
        }

        private static void SaveBaseline(Dictionary<string, long> map)
        {
            try
            {
                SessionState.SetString(BaselineKey, JsonConvert.SerializeObject(map));
            }
            catch (Exception ex)
            {
                McpLog.Warn($"Failed to record open-scene baseline: {ex.Message}");
            }
        }

        private static void RecordBaseline(List<Scene> scenes)
        {
            var map = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (var scene in scenes)
            {
                if (!string.IsNullOrEmpty(scene.path) && File.Exists(scene.path))
                {
                    map[scene.path] = MtimeOf(scene.path);
                }
            }

            SaveBaseline(map);
        }
    }
}
