using System.IO;
using Newtonsoft.Json;
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
        public static object Run(JObject @params)
        {
            string path = @params["path"]?.ToString();
            if (string.IsNullOrEmpty(path))
                return new ErrorResponse("'path' is required.");

            path = AssetPathUtility.SanitizeAssetPath(path);
            if (!AssetDatabase.AssetPathExists(path))
                return new ErrorResponse($"Sprite not found: '{path}'");

            var diagnostics = new SpriteDiagnosticBuilder();

            // ── Step 1: Slice ──────────────────────────────────────────────────

            var sliceResult = SpriteImportSetup.SliceSheet(@params, diagnostics);
            if (sliceResult is ErrorResponse)
                return new { success = false, step = "slice_sheet", error = ((ErrorResponse)sliceResult).Error, diagnostics = diagnostics.Build() };
            if (diagnostics.HasErrors)
                return new { success = false, step = "slice_sheet", diagnostics = diagnostics.Build() };

            // ── Step 2: Clips ──────────────────────────────────────────────────

            string outputDir = @params["output_dir"]?.ToString()
                ?? Path.GetDirectoryName(path)?.Replace('\\', '/') ?? "Assets";

            var clipsToken = @params["clips"] as JArray;
            if (clipsToken == null || clipsToken.Count == 0)
            {
                // No clips given: one clip spanning every frame.
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

            bool overwrite = @params["overwrite"]?.ToObject<bool>() ?? false;

            var clipsParams = new JObject
            {
                ["path"]       = path,
                ["clips"]      = clipsToken,
                ["output_dir"] = outputDir,
                ["overwrite"]  = overwrite,
            };
            var clipResult = SpriteClipBuilder.SetupClips(clipsParams, diagnostics);
            if (clipResult is ErrorResponse)
                return new { success = false, step = "setup_clips", error = ((ErrorResponse)clipResult).Error, diagnostics = diagnostics.Build() };
            if (diagnostics.HasErrors)
                return new { success = false, step = "setup_clips", diagnostics = diagnostics.Build() };

            // ── Step 3: Controller ─────────────────────────────────────────────

            string controllerPath = @params["controller_path"]?.ToString()
                ?? $"{outputDir}/{Path.GetFileNameWithoutExtension(path)}_Controller.controller";

            // The builder suffixes its own local copy, so keeping the raw string here made the
            // scene step load '<dir>/S7' instead of '<dir>/S7.controller' and attach nothing.
            controllerPath = AssetPathUtility.SanitizeAssetPath(controllerPath);
            if (controllerPath == null)
                return new { success = false, step = "setup_controller",
                    error = "'controller_path' must stay under Assets/ and cannot contain '..'.",
                    diagnostics = diagnostics.Build() };
            if (!controllerPath.EndsWith(".controller"))
                controllerPath += ".controller";

            // Only the clips SetupClips really wrote may reach the controller: rebuilding the
            // list from the request counted refused clips and fed the controller stale assets.
            var createdClips = new JArray();
            var clipObj = AsJObject(clipResult);
            if (clipObj == null)
            {
                diagnostics.AddError("CLIP_RESULT_UNREADABLE",
                    "The clip step result could not be read back, so the created clips are unknown.",
                    null, new[] { "Run setup_clips on its own to see which clips were created." });
            }
            else
            {
                foreach (var c in clipObj["clips"] as JArray ?? new JArray())
                {
                    string cpath = c["path"]?.ToString();
                    if (!string.IsNullOrEmpty(cpath))
                        createdClips.Add(new JObject { ["name"] = c["name"]?.ToString(), ["path"] = cpath });
                }
            }

            var ctrlParams = new JObject
            {
                ["clips"]           = createdClips,
                ["controller_path"] = controllerPath,
                ["overwrite"]       = overwrite,
            };
            var ctrlResult = SpriteControllerBuilder.Build(ctrlParams, diagnostics);

            // The builder returns ErrorResponse (not a throw) for cases like "No valid clips
            // loaded", so the failure has to be checked for explicitly.
            if (ctrlResult is ErrorResponse)
                return new { success = false, step = "setup_controller",
                    error = ((ErrorResponse)ctrlResult).Error, diagnostics = diagnostics.Build() };
            // An existing-controller refusal arrives as an error diagnostic, not an ErrorResponse;
            // without this the scene step went on to attach the OLD controller.
            if (diagnostics.HasErrors)
                return new { success = false, step = "setup_controller", diagnostics = diagnostics.Build() };

            // ── Step 4: Add to scene ───────────────────────────────────────────

            bool addToScene  = @params["add_to_scene"]?.ToObject<bool>() ?? false;
            string sceneTarget = @params["scene_target"]?.ToString();

            // An attachment that was asked for but did not happen is not a success, so both
            // misses below are errors rather than a warning or nothing at all.
            if (addToScene && string.IsNullOrEmpty(sceneTarget))
            {
                diagnostics.AddError("SCENE_TARGET_MISSING",
                    "'add_to_scene' is true but 'scene_target' is empty.",
                    null,
                    new[] { "Pass 'scene_target' with the GameObject name.", "Set add_to_scene=false." });
            }
            else if (addToScene)
            {
                var go = UnityEngine.GameObject.Find(sceneTarget);
                if (go != null)
                {
                    var controller = AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(
                        AssetPathUtility.SanitizeAssetPath(controllerPath));
                    if (controller != null)
                    {
                        // `??` compares references and so never sees Unity's overloaded ==: a
                        // GameObject without an Animator yields an object that equals null but is
                        // not a null reference, so AddComponent was never called and the next line
                        // threw MissingComponentException. Measured: this path never once worked.
                        var animator = go.GetComponent<UnityEngine.Animator>();
                        if (animator == null)
                        {
                            UnityEditor.Undo.RecordObject(go, "Add Animator Component");
                            animator = UnityEditor.Undo.AddComponent<UnityEngine.Animator>(go);
                        }
                        // Recorded and dirtied like the sibling controller_assign path, so the
                        // change is undoable and survives a scene save.
                        UnityEditor.Undo.RecordObject(animator, "Assign AnimatorController");
                        animator.runtimeAnimatorController = controller;
                        EditorUtility.SetDirty(go);

                        diagnostics.AddInfo("SCENE_ANIMATOR_SET",
                            $"Animator set on '{sceneTarget}'.", new { target = sceneTarget });
                    }
                    else
                    {
                        diagnostics.AddError("SCENE_CONTROLLER_NOT_LOADED",
                            $"The controller at '{controllerPath}' could not be loaded, so '{sceneTarget}' was left unchanged.",
                            new { path = controllerPath },
                            new[] { "Check the controller_path in the response." });
                    }
                }
                else
                {
                    diagnostics.AddError("SCENE_TARGET_NOT_FOUND",
                        $"GameObject '{sceneTarget}' not found in scene.",
                        null,
                        new[] { "Check GameObject name or open the correct scene first." });
                }
            }

            var ctrlObj = AsJObject(ctrlResult);
            if (ctrlObj == null)
                diagnostics.AddWarning("CONTROLLER_RESULT_UNREADABLE",
                    "The controller step result could not be read back; complexity and state_count are unknown.",
                    null, new string[0]);

            return new
            {
                success               = !diagnostics.HasErrors,
                sprite_path           = path,
                controller_path       = controllerPath,
                controller_complexity = ctrlObj?["complexity"]?.ToString(),
                state_count           = ctrlObj?["state_count"]?.ToObject<int>() ?? 0,
                clip_count            = createdClips.Count,
                diagnostics           = diagnostics.Build(),
            };
        }

        /// <summary>Reads a builder's anonymous result back as JSON; null when it cannot be parsed.</summary>
        private static JObject AsJObject(object result)
        {
            try { return JObject.Parse(JsonConvert.SerializeObject(result)); }
            catch { return null; }
        }

        private static int GetSliceCount(string path)
        {
            var sprites = AssetDatabase.LoadAllAssetsAtPath(path);
            int count = 0;
            foreach (var a in sprites)
                if (a is UnityEngine.Sprite) count++;
            return count > 0 ? count : 1;
        }
    }
}
