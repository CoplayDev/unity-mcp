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

            var clips = new List<(string name, string path, bool? loop)>();
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
                clips.Add((name, cd["path"]?.ToString() ?? "", null));
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

        /// <summary>Returns default when refused; the diagnostics say why. A non-null loop overrides the name guess.</summary>
        internal static (string path, int stateCount) BuildController(
            IEnumerable<(string name, string path, bool? loop)> clips, string controllerPath, bool overwrite,
            SpriteDiagnosticBuilder diagnostics)
        {
            controllerPath = string.IsNullOrWhiteSpace(controllerPath) ? null : AssetPathUtility.SanitizeAssetPath(controllerPath.Trim());
            if (controllerPath == null)
            {
                diagnostics.AddError("BAD_PARAM", "'controller_path' must stay under Assets/ and cannot contain '..'.");
                return default;
            }
            if (AssetDatabase.IsValidFolder(controllerPath))
            {
                diagnostics.AddError("BAD_PARAM", $"'controller_path' names the folder '{controllerPath}'; give the controller a file name inside it.");
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
            foreach (var (clipName, clipPath, loop) in clips)
            {
                string safeClipPath = AssetPathUtility.SanitizeAssetPath(clipPath);
                if (safeClipPath == null)
                { diagnostics.AddWarning("CLIP_BAD_PATH", $"Clip '{clipName}': path '{clipPath}' must stay under Assets/ and cannot contain '..' - skipped."); continue; }
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(safeClipPath);
                if (clip == null)
                { diagnostics.AddWarning("CLIP_NOT_FOUND", $"Clip '{clipName}' not found at '{clipPath}' — skipped."); continue; }
                var entry = SpriteNamingDetector.Detect(clipName);
                if (loop.HasValue) entry.Loop = loop.Value;
                entries.Add((entry, clip));
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
            // Reference equality, not a null check: a replacement that failed leaves the old
            // asset loadable at the same path.
            if (controller == null || AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath) != controller)
            {
                diagnostics.AddError("CONTROLLER_WRITE_FAILED", $"Unity did not write '{controllerPath}'.", "Check the Unity console for the AssetDatabase error.");
                return default;
            }
            var rootSM = controller.layers[0].stateMachine;
            // Every transition below gets duration 0: sprite keys are object references, which
            // cannot blend, so a blend time would only delay the visible sprite change.

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

            var idlePairs = entries.Where(e => e.entry.Category == SpriteAnimCategory.Idle).ToList();
            AnimatorState idleState = null;
            if (idlePairs.Count > 0)
            {
                idleState = rootSM.AddState("Idle");
                idleState.motion = idlePairs[0].clip;
                rootSM.defaultState = idleState;
            }
            // There is one Idle state, so a second idle clip is left out of the controller.
            foreach (var extra in idlePairs.Skip(1))
                diagnostics.AddWarning("IDLE_CLIP_UNUSED",
                    $"Clip '{extra.entry.ClipName}' is also an idle clip, and the one Idle state plays '{idlePairs[0].entry.ClipName}', so '{extra.entry.ClipName}' got no state.",
                    "Rename it to include an action word such as attack, jump or hurt, and neither idle nor stand, then rebuild with overwrite=true.",
                    "Put it in its own controller.");

            // ── Locomotion ────────────────────────────────────────────────────

            AnimatorState locomotionState = null;
            if (locomotionPairs.Count > 0)
            {
                if (locomotionPairs.Count == 1)
                {
                    var locoState = rootSM.AddState(locomotionPairs[0].entry.ClipName);
                    locoState.motion = locomotionPairs[0].clip;
                    locomotionState = locoState;
                    if (rootSM.defaultState == null) rootSM.defaultState = locoState;
                    if (idleState != null)
                    {
                        var t1 = idleState.AddTransition(locoState);
                        t1.AddCondition(AnimatorConditionMode.Greater, 0.1f, "Speed");
                        t1.hasExitTime = false;
                        t1.duration = 0f;
                        var t2 = locoState.AddTransition(idleState);
                        t2.AddCondition(AnimatorConditionMode.Less, 0.1f, "Speed");
                        t2.hasExitTime = false;
                        t2.duration = 0f;
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
                    locomotionState = blendState;
                    if (rootSM.defaultState == null) rootSM.defaultState = blendState;

                    if (idleState != null)
                    {
                        var t1 = idleState.AddTransition(blendState);
                        t1.AddCondition(AnimatorConditionMode.Greater, 0.1f, "Speed");
                        t1.hasExitTime = false;
                        t1.duration = 0f;
                        var t2 = blendState.AddTransition(idleState);
                        t2.AddCondition(AnimatorConditionMode.Less, 0.1f, "Speed");
                        t2.hasExitTime = false;
                        t2.duration = 0f;
                    }
                }
            }

            // ── Trigger states (combat, jump, object) ─────────────────────────

            var triggerPairs = entries.Where(e =>
                e.entry.Category == SpriteAnimCategory.Combat ||
                e.entry.Category == SpriteAnimCategory.Jump   ||
                e.entry.Category == SpriteAnimCategory.Object).ToList();

            // Trigger -> the clip whose state it enters. Two Any State transitions on one trigger
            // always resolve to the same one, so the second could never fire and is not built;
            // its clip keeps a state, with its exit, for a script to play.
            var triggerOwners = new Dictionary<string, string>();
            foreach (var pair in triggerPairs)
            {
                var state = rootSM.AddState(pair.entry.ClipName);
                state.motion = pair.clip;

                string trigger = pair.entry.TriggerName ?? pair.entry.ClipName;

                if (triggerOwners.TryGetValue(trigger, out string owner))
                {
                    diagnostics.AddWarning("TRIGGER_SHARED",
                        $"Clips '{owner}' and '{pair.entry.ClipName}' share the trigger '{trigger}', which plays '{owner}': no transition leads to '{pair.entry.ClipName}', so it plays only from a script.",
                        "Give each clip its own action word (attack, slash and punch are three different triggers), then rebuild with overwrite=true.");
                }
                else
                {
                    triggerOwners.Add(trigger, pair.entry.ClipName);
                    var tr = rootSM.AddAnyStateTransition(state);
                    tr.AddCondition(AnimatorConditionMode.If, 0, trigger);
                    tr.hasExitTime = false;
                    tr.duration = 0f;
                    // On, a repeated trigger restarts the clip. Off, Unity would leave that trigger
                    // set, and it would replay the state as soon as the Animator left it.
                    tr.canTransitionToSelf = true;
                }

                // A one-shot state hands control back to idle, else locomotion. With
                // neither, the default is another one-shot, and exiting into it would
                // just chain one stuck state into the next. A death gets no exit and holds
                // its last frame; a trigger the game fires still leaves it, from Any State.
                var exitTarget = idleState ?? locomotionState;
                if (exitTarget != null && !pair.entry.Loop && !pair.entry.Terminal)
                {
                    var exitTr = state.AddTransition(exitTarget);
                    exitTr.hasExitTime = true;
                    exitTr.exitTime     = 1f;
                    exitTr.hasFixedDuration = false;
                    exitTr.duration     = 0f;
                }
            }

            // ── Generic / single animation ───────────────────────────────────────

            foreach (var pair in entries.Where(e => e.entry.Category == SpriteAnimCategory.Generic))
            {
                var state = rootSM.AddState(pair.entry.ClipName);
                state.motion = pair.clip;
                if (rootSM.defaultState == null)
                    rootSM.defaultState = state;
                if (rootSM.defaultState != state)
                    diagnostics.AddWarning("STATE_UNREACHABLE",
                        $"Clip '{pair.entry.ClipName}' matches no action word, so no transition leads to its state: it plays only from a script, or after you rename the clip to an action word.",
                        "Rename the clip to include an action word such as attack, jump or hurt, then rebuild with overwrite=true.");
            }

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();

            return (controllerPath, rootSM.states.Length);
        }
    }
}
