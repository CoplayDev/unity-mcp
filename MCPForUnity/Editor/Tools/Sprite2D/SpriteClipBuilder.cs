using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using MCPForUnity.Editor.Helpers;

namespace MCPForUnity.Editor.Tools.Sprite2D
{
    internal static class SpriteClipBuilder
    {
        /// <summary>
        /// Builds AnimationClips out of sliced sprites and saves them as .anim assets.
        /// params:
        ///   path         - sprite texture asset path
        ///   clips        - [{name, start_frame, end_frame, fps (opt, def=12), loop (opt)}]
        ///   output_dir   - where the clips are written (default: the sprite's own folder)
        ///   overwrite    - bool (default false); an existing clip is kept unless this is true
        /// </summary>
        public static object SetupClips(JObject @params, SpriteDiagnosticBuilder diagnostics)
        {
            string path = @params["path"]?.ToString();
            if (string.IsNullOrEmpty(path))
                return new ErrorResponse("'path' is required.");

            path = AssetPathUtility.SanitizeAssetPath(path);
            if (path == null)
                return new ErrorResponse("'path' must stay under Assets/ and cannot contain '..'.");

            var allSprites = AssetDatabase.LoadAllAssetsAtPath(path)
                .OfType<Sprite>()
                .OrderBy(s => NaturalSortKey(s.name))
                .ToArray();

            if (allSprites.Length == 0)
                return new ErrorResponse($"No sprites found at '{path}'. Run slice_sheet first.");

            var clipsToken = @params["clips"] as JArray;
            if (clipsToken == null || clipsToken.Count == 0)
                return new ErrorResponse("'clips' array is required.");

            string outputDir = @params["output_dir"]?.ToString()
                ?? Path.GetDirectoryName(path)?.Replace('\\', '/') ?? "Assets";

            // SanitizeAssetPath returns null when it refuses a path, so falling back to the
            // raw value would hand traversal sequences straight through.
            outputDir = AssetPathUtility.SanitizeAssetPath(outputDir);
            if (outputDir == null)
                return new ErrorResponse("'output_dir' must stay under Assets/ and cannot contain '..'.");
            if (!AssetDatabase.IsValidFolder(outputDir))
                CreateFolders(outputDir);

            bool overwrite = @params["overwrite"]?.ToObject<bool>() ?? false;

            var createdClips = new List<object>();

            foreach (JToken clipToken in clipsToken)
            {
                // Measured: the Python surface forwards a clips entry that is not an
                // object, and the typed foreach cast threw InvalidCastException on it.
                if (!(clipToken is JObject clipDef))
                {
                    diagnostics.AddWarning("CLIP_NOT_AN_OBJECT", "A clips entry is not an object - skipped.", null, new[] { "Each clip must be an object with a 'name'." });
                    continue;
                }

                string clipName = clipDef["name"]?.ToString();
                if (string.IsNullOrEmpty(clipName))
                { diagnostics.AddWarning("CLIP_NO_NAME", "Clip name is missing — skipped.", null, new[] { "Add a 'name' field to each clip definition." }); continue; }

                // Measured: a name like "nested/walk" composes into a path under a folder that
                // does not exist and AssetDatabase.CreateAsset throws an uncaught UnityException;
                // where the folder happens to exist the clip is written outside output_dir instead.
                if (clipName.Contains("/") || clipName.Contains("\\"))
                {
                    diagnostics.AddWarning("CLIP_BAD_NAME", $"Clip '{clipName}': the name cannot contain a path separator - skipped.", null, new[] { "Remove '..' and path separators from the clip name." });
                    continue;
                }

                // Through SpriteParams, not ToObject: measured 2026-08-21, start_frame at
                // 2147483648 raised an OverflowException that left the tool entirely, and
                // start_frame 2.7 was silently rounded to 3 and written into a clip. The
                // range check below only ever saw values that survived the conversion.
                // Sequential, not chained with ||: a short-circuited call leaves its out
                // parameter unassigned and the second value is used below.
                int endFrame = allSprites.Length - 1;
                bool rangeOk = SpriteParams.TryReadWholeNumber(clipDef, "start_frame", 0, out int startFrame, out string frameError);
                if (rangeOk) rangeOk = SpriteParams.TryReadWholeNumber(clipDef, "end_frame", allSprites.Length - 1, out endFrame, out frameError);
                if (!rangeOk)
                {
                    diagnostics.AddWarning("CLIP_BAD_RANGE", $"Clip '{clipName}': {frameError} - skipped.", null, new[] { "start_frame and end_frame must be whole numbers within a sprite index." });
                    continue;
                }
                if (endFrame > allSprites.Length - 1)
                {
                    // Skip/Take clamps silently, so an end_frame past the last sprite produced
                    // a shorter clip and reported success - the caller asked for frames that
                    // do not exist and had no way to notice they were missing.
                    diagnostics.AddWarning("CLIP_BAD_RANGE", $"Clip '{clipName}': end_frame {endFrame} is past the last sprite index {allSprites.Length - 1} - skipped.", null, new[] { $"This sheet has {allSprites.Length} sprites, so end_frame must be at most {allSprites.Length - 1}." });
                    continue;
                }
                if (startFrame < 0 || endFrame < startFrame)
                {
                    // Enumerable.Skip yields everything for a negative count, so start_frame=-2
                    // with end_frame=3 wrote frames 0..5 and called it a success. A reversed
                    // range already lands on CLIP_EMPTY, but naming it here says which input
                    // was wrong instead of which result was empty.
                    diagnostics.AddWarning("CLIP_BAD_RANGE", $"Clip '{clipName}': frame range [{startFrame},{endFrame}] is invalid - skipped.", null, new[] { "start_frame must be 0 or more, and end_frame must not be below start_frame." });
                    continue;
                }
                // NaN passes every comparison, so `fps <= 0f` was false for it and a clip
                // was written whose keyframe times were all NaN - measured, and reported as
                // a success. TryReadFiniteFloat refuses NaN and both infinities by name.
                if (!SpriteParams.TryReadFiniteFloat(clipDef, "fps", 12f, out float fps, out string fpsError))
                {
                    diagnostics.AddWarning("CLIP_BAD_FPS", $"Clip '{clipName}': {fpsError} - skipped.", null, new[] { "Leave fps out to use the default of 12." });
                    continue;
                }
                if (fps <= 0f)
                {
                    // Keyframe times are i / fps, so a non-positive rate writes a clip whose
                    // keys sit at infinity - accepted by Unity, useless to play.
                    diagnostics.AddWarning("CLIP_BAD_FPS", $"Clip '{clipName}': fps must be greater than 0, got {fps} - skipped.", null, new[] { "Leave fps out to use the default of 12." });
                    continue;
                }

                var entry      = SpriteNamingDetector.Detect(clipName);
                if (!SpriteParams.TryReadBool(clipDef, "loop", entry.Loop, out bool loop, out string loopError))
                {
                    diagnostics.AddWarning("CLIP_BAD_LOOP", $"Clip '{clipName}': {loopError} - skipped.", null, new[] { "Leave loop out to let the clip name decide." });
                    continue;
                }

                var frameSprites = allSprites.Skip(startFrame).Take(endFrame - startFrame + 1).ToArray();
                if (frameSprites.Length == 0)
                {
                    diagnostics.AddWarning("CLIP_EMPTY", $"Clip '{clipName}': no frames in range [{startFrame},{endFrame}].", null, new[] { "Check start_frame/end_frame against total sprite count." });
                    continue;
                }

                if (frameSprites.Length <= 2)
                    diagnostics.AddWarning("LOW_FRAME_COUNT", $"Clip '{clipName}' has only {frameSprites.Length} frame(s) — animation may not be visible.", null, new string[0]);

                // Both refusals below come before the clip is allocated: a `new AnimationClip`
                // that never becomes an asset is a leaked UnityEngine.Object, not a collected one.
                // The delete stays down next to CreateAsset, so nothing is destroyed until the
                // replacement has actually been built.
                string clipPath = AssetPathUtility.SanitizeAssetPath($"{outputDir}/{clipName}.anim");
                if (clipPath == null)
                {
                    diagnostics.AddWarning("CLIP_BAD_NAME", $"Clip '{clipName}': the name cannot be used as a file name - skipped.", null, new[] { "Remove '..' and path separators from the clip name." });
                    continue;
                }

                var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
                if (existing != null && !overwrite)
                {
                    // Measured: an unrelated clip already at this path was deleted and replaced by a
                    // request that carried no overwrite field. The sibling controller builder refuses
                    // instead, so clips follow the same policy: destruction needs authorisation.
                    diagnostics.AddWarning("CLIP_EXISTS", $"Clip '{clipName}': an animation clip already exists at '{clipPath}' - skipped.", new { path = clipPath }, new[] { "Set overwrite=true to replace it.", "Choose a different clip name or output_dir." });
                    continue;
                }

                var clip = new AnimationClip { frameRate = fps };

                var binding = new EditorCurveBinding
                {
                    type         = typeof(SpriteRenderer),
                    path         = "",
                    propertyName = "m_Sprite",
                };

                var keyframes = new ObjectReferenceKeyframe[frameSprites.Length];
                for (int i = 0; i < frameSprites.Length; i++)
                {
                    keyframes[i] = new ObjectReferenceKeyframe
                    {
                        time  = i / fps,
                        value = frameSprites[i],
                    };
                }

                AnimationUtility.SetObjectReferenceCurve(clip, binding, keyframes);

                var settings = AnimationUtility.GetAnimationClipSettings(clip);
                settings.loopTime = loop;
                AnimationUtility.SetAnimationClipSettings(clip, settings);

                if (existing != null) AssetDatabase.DeleteAsset(clipPath);
                AssetDatabase.CreateAsset(clip, clipPath);

                createdClips.Add(new
                {
                    name        = clipName,
                    path        = clipPath,
                    frame_count = frameSprites.Length,
                    fps,
                    loop,
                    duration    = frameSprites.Length / fps,
                });
            }

            AssetDatabase.SaveAssets();

            return new
            {
                success     = true,
                sprite_path = path,
                clip_count  = createdClips.Count,
                clips       = createdClips,
                diagnostics = diagnostics.Build(),
            };
        }

        // ── Internal helper ──────────────────────────────────────────────────

        internal static AnimationClip LoadClip(string clipPath) =>
            AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);

        // Plain string sort puts hero_10 before hero_2, which reorders the animation.
        private static string NaturalSortKey(string name)
        {
            var sb = new System.Text.StringBuilder();
            int i = 0;
            while (i < name.Length)
            {
                if (char.IsDigit(name[i]))
                {
                    int start = i;
                    while (i < name.Length && char.IsDigit(name[i])) i++;
                    // Left-pad the run of digits so a lexicographic sort compares them numerically.
                    sb.Append(name.Substring(start, i - start).PadLeft(10, '0'));
                }
                else
                {
                    sb.Append(name[i++]);
                }
            }
            return sb.ToString();
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
