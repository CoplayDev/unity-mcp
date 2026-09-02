using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using MCPForUnity.Editor.Helpers;

namespace MCPForUnity.Editor.Tools.Sprite2D
{
    internal static class SpriteControllerBuilder
    {
        /// <summary>
        /// params:
        ///   clips            - [{name, path}] where path is an .anim asset path
        ///   controller_path  - output .controller path (required)
        ///   overwrite        - bool (default false)
        /// </summary>
        public static object Build(JObject @params, SpriteDiagnosticBuilder diagnostics)
        {
            var clipsToken = @params["clips"] as JArray;
            if (clipsToken == null || clipsToken.Count == 0)
                return diagnostics.Fail("BAD_PARAM", "'clips' array is required.");

            string controllerPath = @params["controller_path"]?.ToString();
            if (string.IsNullOrEmpty(controllerPath))
                return diagnostics.Fail("BAD_PARAM", "'controller_path' is required.");

            bool overwrite = ParamCoercion.CoerceBool(@params["overwrite"], false);

            var clips = new List<(string name, string path)>();
            foreach (JToken clipToken in clipsToken)
            {
                // Measured: a non-object clips entry threw InvalidCastException on a typed cast.
                if (!(clipToken is JObject cd))
                {
                    diagnostics.AddWarning("CLIP_NOT_AN_OBJECT", "A clips entry is not an object - skipped.", "Each clip must be an object with a 'name'.");
                    continue;
                }
                string name = cd["name"]?.ToString();
                if (string.IsNullOrEmpty(name))
                {
                    diagnostics.AddWarning("CLIP_NO_NAME", "A clips entry has no name - skipped.", "Each clip must be an object with a 'name'.");
                    continue;
                }
                clips.Add((name, cd["path"]?.ToString() ?? ""));
            }

            var built = BuildController(clips, controllerPath, overwrite, diagnostics);
            if (diagnostics.HasErrors)
                return diagnostics.Fail();

            return new
            {
                success         = true,
                controller_path = built.path,
                state_count     = built.stateCount,
                diagnostics     = diagnostics.Build(),
            };
        }

        /// <summary>Returns default when refused; the diagnostics say why.</summary>
        internal static (string path, int stateCount) BuildController(
            IEnumerable<(string name, string path)> clips, string controllerPath, bool overwrite,
            SpriteDiagnosticBuilder diagnostics)
        {
            controllerPath = AssetPathUtility.SanitizeAssetPath(controllerPath);
            if (controllerPath == null)
            {
                diagnostics.AddError("BAD_PARAM", "'controller_path' must stay under Assets/ and cannot contain '..'.");
                return default;
            }
            if (!controllerPath.EndsWith(".controller"))
                controllerPath += ".controller";
            // Checked after the suffix: a bare 'Assets' passes SanitizeAssetPath and then
            // becomes 'Assets.controller', a file at the project root; 'Assets/' becomes
            // 'Assets/.controller', a file with no name.
            if (!AssetPathUtility.IsValidAssetPath(controllerPath) || Path.GetFileName(controllerPath) == ".controller")
            {
                diagnostics.AddError("BAD_PARAM", "'controller_path' must name a file under Assets/ without characters like : * ? \" < > |.");
                return default;
            }

            var entries = new List<(SpriteAnimEntry entry, AnimationClip clip)>();
            foreach (var (clipName, clipPath) in clips)
            {
                string safeClipPath = AssetPathUtility.SanitizeAssetPath(clipPath);
                if (safeClipPath == null)
                { diagnostics.AddWarning("CLIP_BAD_PATH", $"Clip '{clipName}': path '{clipPath}' must stay under Assets/ and cannot contain '..' - skipped."); continue; }
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(safeClipPath);
                if (clip == null)
                { diagnostics.AddWarning("CLIP_NOT_FOUND", $"Clip '{clipName}' not found at '{clipPath}' — skipped."); continue; }
                entries.Add((SpriteNamingDetector.Detect(clipName), clip));
            }

            if (entries.Count == 0)
            {
                diagnostics.AddError("NO_CLIPS", "No valid clips loaded.");
                return default;
            }

            // Not deleted here: CreateAnimatorControllerAtPath replaces the asset itself, and
            // deleting first left a failed rebuild with no controller at all.
            if (!overwrite && AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath) != null)
            {
                diagnostics.AddError("CONTROLLER_EXISTS", $"Controller already exists at '{controllerPath}'.", "Set overwrite=true to replace it.");
                return default;
            }

            string dir = Path.GetDirectoryName(controllerPath)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(dir) && !AssetDatabase.IsValidFolder(dir))
                SpriteClipBuilder.CreateFolders(dir);

            var controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            var rootSM     = controller.layers[0].stateMachine;

            // ── Parameters ──────────────────────────────────────────────────

            var locomotionPairs = entries.Where(e => e.entry.Category == SpriteAnimCategory.Locomotion).ToList();
            if (locomotionPairs.Count > 0)
                controller.AddParameter("Speed", AnimatorControllerParameterType.Float);

            var triggerNames = entries
                .Where(e => !string.IsNullOrEmpty(e.entry.TriggerName) &&
                            (e.entry.Category == SpriteAnimCategory.Combat ||
                             e.entry.Category == SpriteAnimCategory.Jump   ||
                             e.entry.Category == SpriteAnimCategory.Object))
                .Select(e => e.entry.TriggerName)
                .Distinct();
            foreach (var t in triggerNames)
                controller.AddParameter(t, AnimatorControllerParameterType.Trigger);

            // ── Idle state ────────────────────────────────────────────────────

            var idlePair = entries.FirstOrDefault(e => e.entry.Category == SpriteAnimCategory.Idle);
            AnimatorState idleState = null;
            if (idlePair.clip != null)
            {
                idleState = rootSM.AddState("Idle");
                idleState.motion = idlePair.clip;
                rootSM.defaultState = idleState;
            }

            // ── Locomotion ────────────────────────────────────────────────────

            if (locomotionPairs.Count > 0)
            {
                if (locomotionPairs.Count == 1)
                {
                    var locoState = rootSM.AddState(locomotionPairs[0].entry.ClipName);
                    locoState.motion = locomotionPairs[0].clip;
                    if (rootSM.defaultState == null) rootSM.defaultState = locoState;
                    if (idleState != null)
                    {
                        var t1 = idleState.AddTransition(locoState);
                        t1.AddCondition(AnimatorConditionMode.Greater, 0.1f, "Speed");
                        t1.hasExitTime = false;
                        var t2 = locoState.AddTransition(idleState);
                        t2.AddCondition(AnimatorConditionMode.Less, 0.1f, "Speed");
                        t2.hasExitTime = false;
                    }
                }
                else
                {
                    var blendState = rootSM.AddState("Locomotion");
                    var blendTree  = new BlendTree { name = "LocomotionTree", blendType = BlendTreeType.Simple1D, blendParameter = "Speed" };
                    // Off, or Unity silently redistributes the thresholds and the BlendValues
                    // below never reach the asset - measured live: walk/run wrote 1/2, read back 0/1.
                    blendTree.useAutomaticThresholds = false;
                    AssetDatabase.AddObjectToAsset(blendTree, controllerPath);

                    foreach (var pair in locomotionPairs.OrderBy(p => p.entry.BlendValue))
                        blendTree.AddChild(pair.clip, pair.entry.BlendValue);

                    blendState.motion = blendTree;
                    if (rootSM.defaultState == null) rootSM.defaultState = blendState;

                    if (idleState != null)
                    {
                        var t1 = idleState.AddTransition(blendState);
                        t1.AddCondition(AnimatorConditionMode.Greater, 0.1f, "Speed");
                        t1.hasExitTime = false;
                        var t2 = blendState.AddTransition(idleState);
                        t2.AddCondition(AnimatorConditionMode.Less, 0.1f, "Speed");
                        t2.hasExitTime = false;
                    }
                }
            }

            // ── Trigger states (combat, jump, object) ─────────────────────────

            var triggerPairs = entries.Where(e =>
                e.entry.Category == SpriteAnimCategory.Combat ||
                e.entry.Category == SpriteAnimCategory.Jump   ||
                e.entry.Category == SpriteAnimCategory.Object).ToList();

            foreach (var pair in triggerPairs)
            {
                var state = rootSM.AddState(pair.entry.ClipName);
                state.motion = pair.clip;

                string trigger = pair.entry.TriggerName ?? pair.entry.ClipName;

                foreach (var existingState in rootSM.states.Select(s => s.state))
                {
                    if (existingState == state) continue;
                    var tr = existingState.AddTransition(state);
                    tr.AddCondition(AnimatorConditionMode.If, 0, trigger);
                    tr.hasExitTime = false;
                }

                // A one-shot state has to hand control back, so it exits to idle on its own.
                if (idleState != null && !pair.entry.Loop)
                {
                    var exitTr = state.AddTransition(idleState);
                    exitTr.hasExitTime = true;
                    exitTr.exitTime     = 1f;
                    exitTr.hasFixedDuration = false;
                }
            }

            // ── Generic / single animation ───────────────────────────────────────

            foreach (var pair in entries.Where(e => e.entry.Category == SpriteAnimCategory.Generic))
            {
                var state = rootSM.AddState(pair.entry.ClipName);
                state.motion = pair.clip;
                if (rootSM.defaultState == null)
                    rootSM.defaultState = state;
            }

            AssetDatabase.SaveAssets();
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();

            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath) == null)
            {
                diagnostics.AddError("CONTROLLER_WRITE_FAILED", $"Unity did not write '{controllerPath}'.", "Check the Unity console for the AssetDatabase error.");
                return default;
            }

            return (controllerPath, rootSM.states.Length);
        }
    }
}
