using System.Collections.Generic;
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

            var clipsParams = new JObject
            {
                ["path"]       = path,
                ["clips"]      = clipsToken,
                ["output_dir"] = outputDir,
            };
            var clipResult = SpriteClipBuilder.SetupClips(clipsParams, diagnostics);
            if (clipResult is ErrorResponse)
                return new { success = false, step = "setup_clips", error = ((ErrorResponse)clipResult).Error, diagnostics = diagnostics.Build() };
            if (diagnostics.HasErrors)
                return new { success = false, step = "setup_clips", diagnostics = diagnostics.Build() };

            // ── Step 3: Controller ─────────────────────────────────────────────

            string controllerPath = @params["controller_path"]?.ToString()
                ?? $"{outputDir}/{Path.GetFileNameWithoutExtension(path)}_Controller.controller";
            bool overwrite = @params["overwrite"]?.ToObject<bool>() ?? false;

            // Derive the paths from clipsToken directly - the builder's return value is an
            // anonymous object, and parsing it back would be the long way round.
            var clipPaths = SpriteClipBuilder.GetClipPaths(clipsToken, outputDir);
            var createdClips = new JArray();
            foreach (var (cname, cpath) in clipPaths)
                createdClips.Add(new JObject { ["name"] = cname, ["path"] = cpath });

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

            // ── Step 4: Add to scene ───────────────────────────────────────────

            bool addToScene  = @params["add_to_scene"]?.ToObject<bool>() ?? false;
            string sceneTarget = @params["scene_target"]?.ToString();

            if (addToScene && !string.IsNullOrEmpty(sceneTarget))
            {
                var go = UnityEngine.GameObject.Find(sceneTarget);
                if (go != null)
                {
                    var controller = AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(
                        AssetPathUtility.SanitizeAssetPath(controllerPath));
                    if (controller != null)
                    {
                        var animator = go.GetComponent<UnityEngine.Animator>()
                            ?? go.AddComponent<UnityEngine.Animator>();
                        animator.runtimeAnimatorController = controller;
                        diagnostics.AddInfo("SCENE_ANIMATOR_SET",
                            $"Animator set on '{sceneTarget}'.", new { target = sceneTarget });
                    }
                }
                else
                {
                    diagnostics.AddWarning("SCENE_TARGET_NOT_FOUND",
                        $"GameObject '{sceneTarget}' not found in scene.",
                        null,
                        new[] { "Check GameObject name or open the correct scene first." });
                }
            }

            // ctrlResult is an anonymous object, so round-trip it through JSON to read two fields.
            string complexity = null;
            int stateCount = 0;
            try
            {
                var ctrlJson = JsonConvert.SerializeObject(ctrlResult);
                var ctrlObj  = JObject.Parse(ctrlJson);
                complexity  = ctrlObj["complexity"]?.ToString();
                stateCount  = ctrlObj["state_count"]?.ToObject<int>() ?? 0;
            }
            catch { /* non-critical */ }

            return new
            {
                success               = !diagnostics.HasErrors,
                sprite_path           = path,
                controller_path       = controllerPath,
                controller_complexity = complexity,
                state_count           = stateCount,
                clip_count            = clipPaths.Count,
                diagnostics           = diagnostics.Build(),
            };
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
