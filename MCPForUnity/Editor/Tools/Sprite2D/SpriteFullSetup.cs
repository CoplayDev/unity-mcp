using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using MCPForUnity.Editor.Helpers;

namespace MCPForUnity.Editor.Tools.Sprite2D
{
    internal static class SpriteFullSetup
    {
        /// <summary>
        /// params:
        ///   path             - sprite texture path (required)
        ///   cols             - grid columns (required)
        ///   rows             - grid rows (default 1)
        ///   frame_width      - alternative to cols: explicit frame size
        ///   frame_height     - alternative to rows: explicit frame size
        ///   clips            - [{name, start_frame, end_frame, fps, loop}];
        ///                      omitted means every frame becomes one clip named animation_name
        ///   animation_name   - used when clips is omitted (default: the file name)
        ///   controller_path  - default: the sprite's own folder
        ///   overwrite        - bool (default false)
        ///   add_to_scene     - add an Animator to a target GameObject
        ///   scene_target     - GameObject name
        /// </summary>
        public static object Run(JObject @params, SpriteDiagnosticBuilder diagnostics)
        {
            if (!SpriteParams.TryReadAssetPath(@params, "path", out string path, out string pathError))
                return diagnostics.Fail("BAD_PARAM", pathError);

            // ── Step 1: Slice ──────────────────────────────────────────────────

            SpriteImportSetup.SliceSheet(@params, diagnostics);
            if (diagnostics.HasErrors)
                return Stop("slice_sheet", diagnostics);

            // ── Step 2: Clips ──────────────────────────────────────────────────

            string outputDir = @params["output_dir"]?.ToString()
                ?? Path.GetDirectoryName(path)?.Replace('\\', '/') ?? "Assets";

            var clipsToken = @params["clips"] as JArray;
            if (clipsToken == null || clipsToken.Count == 0)
            {
                string animName = @params["animation_name"]?.ToString()
                    ?? Path.GetFileNameWithoutExtension(path);
                int totalFrames = GetSliceCount(path);
                clipsToken = new JArray(new JObject
                {
                    ["name"]        = animName,
                    ["start_frame"] = 0,
                    ["end_frame"]   = totalFrames - 1,
                    ["fps"]         = 12,
                });
            }

            bool overwrite = ParamCoercion.CoerceBool(@params["overwrite"], false);

            var clips = SpriteClipBuilder.CreateClips(path, clipsToken, outputDir, overwrite, diagnostics);
            if (diagnostics.HasErrors)
                return Stop("setup_clips", diagnostics);

            // ── Step 3: Controller ─────────────────────────────────────────────

            string controllerPath = @params["controller_path"]?.ToString()
                ?? $"{outputDir}/{Path.GetFileNameWithoutExtension(path)}_Controller.controller";

            var controller = SpriteControllerBuilder.BuildController(
                clips.Select(c => (c.name, c.path)), controllerPath, overwrite, diagnostics);
            if (diagnostics.HasErrors)
                return Stop("setup_controller", diagnostics);

            // ── Step 4: Add to scene ───────────────────────────────────────────

            bool addToScene  = ParamCoercion.CoerceBool(@params["add_to_scene"], false);
            string sceneTarget = @params["scene_target"]?.ToString();

            // An attachment asked for but not made is not a success, so both misses are errors.
            if (addToScene && string.IsNullOrEmpty(sceneTarget))
            {
                diagnostics.AddError("SCENE_TARGET_MISSING",
                    "'add_to_scene' is true but 'scene_target' is empty.",
                    "Pass 'scene_target' with the GameObject name.", "Set add_to_scene=false.");
            }
            else if (addToScene)
            {
                // The shared lookup, not GameObject.Find: Find skips inactive objects and
                // picks one of several with the same name without saying so.
                var matches = GameObjectLookup.SearchGameObjects("by_name", sceneTarget, includeInactive: true);
                var go = matches.Count == 1 ? GameObjectLookup.FindById(matches[0]) : null;
                if (matches.Count > 1)
                {
                    diagnostics.AddError("SCENE_TARGET_AMBIGUOUS",
                        $"{matches.Count} GameObjects are named '{sceneTarget}'; nothing was attached.",
                        "Rename the target or pick a unique name.");
                }
                else if (go != null)
                {
                    var asset = AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(controller.path);
                    if (asset != null)
                    {
                        // `??` compares references and never sees Unity's overloaded ==, so
                        // AddComponent was skipped and the next line threw. Measured: this path
                        // never once worked.
                        var animator = go.GetComponent<UnityEngine.Animator>();
                        if (animator == null)
                        {
                            UnityEditor.Undo.RecordObject(go, "Add Animator Component");
                            animator = UnityEditor.Undo.AddComponent<UnityEngine.Animator>(go);
                        }
                        // The clips bind to SpriteRenderer.m_Sprite: without one the Animator
                        // plays into nothing and the call still reports success.
                        if (go.GetComponent<UnityEngine.SpriteRenderer>() == null)
                        {
                            UnityEditor.Undo.AddComponent<UnityEngine.SpriteRenderer>(go);
                            diagnostics.AddWarning("SCENE_SPRITE_RENDERER_ADDED",
                                $"'{sceneTarget}' had no SpriteRenderer, so one was added for the clips to drive.");
                        }
                        // Recorded and dirtied like the sibling controller_assign path.
                        UnityEditor.Undo.RecordObject(animator, "Assign AnimatorController");
                        animator.runtimeAnimatorController = asset;
                        EditorUtility.SetDirty(go);
                    }
                    else
                    {
                        diagnostics.AddError("SCENE_CONTROLLER_NOT_LOADED",
                            $"The controller at '{controller.path}' could not be loaded, so '{sceneTarget}' was left unchanged.",
                            "Check the controller_path in the response.");
                    }
                }
                else
                {
                    diagnostics.AddError("SCENE_TARGET_NOT_FOUND",
                        $"GameObject '{sceneTarget}' not found in scene.",
                        "Check GameObject name or open the correct scene first.");
                }
            }

            // Shaped like the other three steps' refusals rather than like a success with the
            // flag flipped: a step-4 failure used to be the one refusal in the tool carrying
            // neither 'step' nor 'message', leaving the reason only inside the diagnostics
            // array. The asset fields stay on it, because by this point steps 1-3 have written
            // and the caller needs to know what is already on disk.
            if (diagnostics.HasErrors)
                return new
                {
                    success           = false,
                    step              = "add_to_scene",
                    message           = diagnostics.FirstError,
                    sprite_path       = path,
                    controller_path   = controller.path,
                    state_count       = controller.stateCount,
                    clip_count        = clips.Count,
                    diagnostics       = diagnostics.Build(),
                };

            return new
            {
                success               = true,
                sprite_path           = path,
                controller_path       = controller.path,
                state_count           = controller.stateCount,
                clip_count            = clips.Count,
                diagnostics           = diagnostics.Build(),
            };
        }

        private static object Stop(string step, SpriteDiagnosticBuilder diagnostics) =>
            new { success = false, step, message = diagnostics.FirstError, diagnostics = diagnostics.Build() };

        private static int GetSliceCount(string path)
        {
            int count = AssetDatabase.LoadAllAssetsAtPath(path).OfType<UnityEngine.Sprite>().Count();
            return count > 0 ? count : 1;
        }
    }
}
