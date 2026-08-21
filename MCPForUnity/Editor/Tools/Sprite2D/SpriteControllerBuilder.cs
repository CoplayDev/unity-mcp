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
                return new ErrorResponse("'clips' array is required.");

            string controllerPath = @params["controller_path"]?.ToString();
            if (string.IsNullOrEmpty(controllerPath))
                return new ErrorResponse("'controller_path' is required.");

            controllerPath = AssetPathUtility.SanitizeAssetPath(controllerPath);
            if (controllerPath == null)
                return new ErrorResponse("'controller_path' must stay under Assets/ and cannot contain '..'.");
            if (!controllerPath.EndsWith(".controller"))
                controllerPath += ".controller";

            bool overwrite = @params["overwrite"]?.ToObject<bool>() ?? false;

            var entries = new List<(SpriteAnimEntry entry, AnimationClip clip)>();
            foreach (JToken clipToken in clipsToken)
            {
                // Measured: the Python surface forwards a clips entry that is not an
                // object, and the typed foreach cast threw InvalidCastException on it.
                if (!(clipToken is JObject cd))
                {
                    diagnostics.AddWarning("CLIP_NOT_AN_OBJECT", "A clips entry is not an object - skipped.", null, new[] { "Each clip must be an object with a 'name'." });
                    continue;
                }

                string clipName = cd["name"]?.ToString() ?? "";
                string clipPath = cd["path"]?.ToString() ?? "";
                string safeClipPath = AssetPathUtility.SanitizeAssetPath(clipPath);
                if (safeClipPath == null)
                { diagnostics.AddWarning("CLIP_BAD_PATH", $"Clip '{clipName}': path '{clipPath}' must stay under Assets/ and cannot contain '..' - skipped.", null, new string[0]); continue; }
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(safeClipPath);
                if (clip == null)
                { diagnostics.AddWarning("CLIP_NOT_FOUND", $"Clip '{clipName}' not found at '{clipPath}' — skipped.", null, new string[0]); continue; }
                entries.Add((SpriteNamingDetector.Detect(clipName), clip));
            }

            if (entries.Count == 0)
                // The diagnostics travel in ErrorResponse's data field rather than in a
                // diagnostics-carrying anonymous object: SpriteFullSetup stops on
                // `is ErrorResponse`, and CLIP_NOT_AN_OBJECT is a warning, so HasErrors would
                // not catch it - changing the type here would let a failed controller step
                // fall through to the scene step again.
                return new ErrorResponse("No valid clips loaded.", new { diagnostics = diagnostics.Build() });

            // The existing controller is only removed once the replacement is known to be
            // buildable: deleting first left a failed rebuild with no controller at all.
            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath) != null)
            {
                if (!overwrite)
                {
                    diagnostics.AddError(
                        "CONTROLLER_EXISTS",
                        $"Controller already exists at '{controllerPath}'.",
                        new { path = controllerPath },
                        new[] { "Set overwrite=true to replace it." }
                    );
                    return new { success = false, diagnostics = diagnostics.Build() };
                }
                AssetDatabase.DeleteAsset(controllerPath);
            }

            string dir = Path.GetDirectoryName(controllerPath)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(dir) && !AssetDatabase.IsValidFolder(dir))
                CreateFolders(dir);

            var complexity = SpriteNamingDetector.DecideComplexity(entries.Select(e => e.entry));
            var controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            var rootSM     = controller.layers[0].stateMachine;

            // ── Parameters ──────────────────────────────────────────────────

            if (complexity == ControllerComplexity.BlendTree1D || complexity == ControllerComplexity.Full)
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

            var locomotionPairs = entries.Where(e => e.entry.Category == SpriteAnimCategory.Locomotion).ToList();
            if (locomotionPairs.Count > 0)
            {
                if (locomotionPairs.Count == 1)
                {
                    // A single locomotion clip: one plain state.
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
                    // More than one locomotion clip: a 1D blend tree.
                    var blendState = rootSM.AddState("Locomotion");
                    var blendTree  = new BlendTree { name = "LocomotionTree", blendType = BlendTreeType.Simple1D, blendParameter = "Speed" };
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

            return new
            {
                success         = true,
                controller_path = controllerPath,
                complexity      = complexity.ToString(),
                state_count     = rootSM.states.Length,
                diagnostics     = diagnostics.Build(),
            };
        }

        private static void CreateFolders(string path)
        {
            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/') ?? "Assets";
            if (!AssetDatabase.IsValidFolder(parent))
                CreateFolders(parent);
            string folderName = Path.GetFileName(path);
            if (!string.IsNullOrEmpty(folderName))
                AssetDatabase.CreateFolder(parent, folderName);
        }
    }
}
