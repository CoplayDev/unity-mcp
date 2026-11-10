using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using MCPForUnity.Editor.Helpers;
// TextureImporter.spritesheet is obsolete as of Unity 6, but the replacement
// (ISpriteEditorDataProvider) needs the 2D Sprite package for the same result.
#pragma warning disable CS0618

namespace MCPForUnity.Editor.Tools.Sprite2D
{
    internal static class SpriteImportSetup
    {
        // ── GetInfo ──────────────────────────────────────────────────────────

        public static object GetInfo(JObject @params, SpriteDiagnosticBuilder diagnostics)
        {
            if (!SpriteParams.TryReadAssetPath(@params, "path", out string path, out string pathError))
                return diagnostics.Fail("BAD_PARAM", pathError);
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
                return diagnostics.Fail("NOT_FOUND", $"No TextureImporter found at '{path}'. Is it a texture/sprite?");

            // Source pixels, not texture.width/height: slice_sheet cuts its grid in source
            // pixels, and the inline image below is the source file. Max Size, or NPOT
            // scaling on a Default-type import, makes the imported texture smaller than
            // the file, so its size would not match either.
            importer.GetSourceTextureWidthAndHeight(out int w, out int h);

            // Paged because this reads what is already on the asset: the 4096 ceiling
            // slice_sheet applies when WRITING never bounded a sheet sliced by hand.
            // Changing either number means changing the page_size description in
            // Server/src/services/tools/manage_sprite.py - that copy is the published promise.
            const int DefaultSlicePageSize = 512;
            const int MaxSlicePageSize = 4096;

            if (!SpriteParams.TryReadWholeNumber(@params, "page_size", DefaultSlicePageSize, out int pageSize, out string paramError))
                return diagnostics.Fail("BAD_PARAM", paramError);
            if (pageSize < 1 || pageSize > MaxSlicePageSize)
                return diagnostics.Fail("BAD_PARAM", $"'page_size' must be between 1 and {MaxSlicePageSize}; got {pageSize}.");

            int totalSlices = importer.spritesheet.Length;
            if (!SpriteParams.TryReadWholeNumber(@params, "cursor", 0, out int cursor, out paramError))
                return diagnostics.Fail("BAD_PARAM", paramError);
            // Skip yields everything for a negative count rather than throwing, so a negative
            // cursor would return page one as a success. Landing exactly on totalSlices is
            // legal: it is the end, and cursor 0 on an unsliced sheet is that same case.
            if (cursor < 0 || cursor > totalSlices)
                return diagnostics.Fail("BAD_PARAM", $"'cursor' must be between 0 and {totalSlices}; got {cursor}.");

            var existingSlices = importer.spritesheet.Skip(cursor).Take(pageSize).Select(s => new
            {
                name   = s.name,
                x      = (int)s.rect.x,
                y      = (int)s.rect.y,
                width  = (int)s.rect.width,
                height = (int)s.rect.height,
            }).ToArray();

            int nextIndex = cursor + existingSlices.Length;
            int? nextCursor = nextIndex < totalSlices ? nextIndex : (int?)null;

            // Base64 payload so a vision-capable caller can read the grid off the image.
            // Bounded by size rather than paged: an image split across cursors is not an image
            // any client can reassemble. 4 MB is a budget, not a protocol boundary. The bound
            // is on the ENCODED length - base64 emits 4 chars per 3 bytes, and bounding the
            // source instead let a measured 3.67 MB sheet through as a 4.89 MB payload.
            const int MaxInlinePayloadBytes = 4 * 1024 * 1024;
            const int MaxInlineSide = 8000;
            string imageBase64 = null;
            string imageOmittedReason = null;
            if (cursor > 0)
            {
                // First page only: repeating it would multiply what paging exists to cap.
                imageOmittedReason =
                    "The image is returned only on the first page. Request this path with " +
                    "cursor 0 (or omit cursor) if the image itself is needed.";
            }
            else
            {
                try
                {
                    // Not dataPath.Replace("/Assets", ""): Replace removes EVERY occurrence, so
                    // a project under /work/AssetsLab lost the wrong segment and missed the file.
                    string projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
                    string fullPath = projectRoot != null ? Path.Combine(projectRoot, path) : null;
                    if (fullPath == null)
                    {
                        imageOmittedReason = "The project root could not be resolved from Application.dataPath.";
                    }
                    else if (!File.Exists(fullPath))
                    {
                        // Asset path over the bridge, absolute path to the log only: the caller
                        // gains nothing from it and it discloses the machine's directory layout.
                        McpLog.Warn($"[Sprite2D] get_info found no file on disk at '{fullPath}'.");
                        imageOmittedReason = $"No file on disk for '{path}'.";
                    }
                    else
                    {
                        // Only bytes a client can decode as labelled: a PSD, TGA, GIF, BMP or
                        // TIFF source used to go out as image/png, and a client that checks
                        // the image fails the whole request rather than this one block.
                        string ext = Path.GetExtension(path).ToLowerInvariant();
                        string mime = ext == ".png" ? "image/png"
                                    : (ext == ".jpg" || ext == ".jpeg") ? "image/jpeg"
                                    : null;
                        if (mime == null)
                        {
                            imageOmittedReason =
                                $"The source is a '{ext}' file; only PNG and JPEG sources are sent inline. " +
                                "Read the file directly if the image itself is needed.";
                        }
                        // Image inputs commonly refuse anything over 8000 px on a side, and the
                        // inline image is the source file, so this is checked on source pixels.
                        else if (w > MaxInlineSide || h > MaxInlineSide)
                        {
                            imageOmittedReason =
                                $"The {w}x{h} source is over {MaxInlineSide} px on a side, which image " +
                                "inputs commonly refuse. Read the file directly if the image itself is needed.";
                        }
                        else
                        {
                            string prefix = $"data:{mime};base64,";
                            long size = new FileInfo(fullPath).Length;
                            long encoded = 4L * ((size + 2) / 3) + prefix.Length;
                            if (encoded > MaxInlinePayloadBytes)
                            {
                                imageOmittedReason =
                                    $"The {size}-byte source encodes to {encoded} base64 bytes, above the " +
                                    $"{MaxInlinePayloadBytes}-byte inline limit. Read the file directly if the " +
                                    "image itself is needed.";
                            }
                            else
                            {
                                imageBase64 = prefix + Convert.ToBase64String(File.ReadAllBytes(fullPath));
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    // A swallowed failure and a deliberate omission are different answers.
                    // Type over the bridge, not message: messages carry the path that threw.
                    McpLog.Warn($"[Sprite2D] get_info could not read '{path}': {ex}");
                    imageOmittedReason =
                        $"The image could not be read ({ex.GetType().Name}); the Unity console has the detail.";
                }
            }

            return new
            {
                success       = true,
                path,
                width         = w,
                height        = h,
                sprite_mode   = importer.spriteImportMode.ToString(),
                pixels_per_unit = importer.spritePixelsPerUnit,
                filter_mode   = importer.filterMode.ToString(),
                slice_count   = totalSlices,
                slices        = existingSlices,
                next_cursor   = nextCursor,
                image_base64  = imageBase64,
                image_omitted_reason = imageOmittedReason,
            };
        }

        /// <summary>Every importer field slice_sheet writes, so a refusal can put all of them back.</summary>
        private sealed class ImporterSnapshot
        {
            private readonly TextureImporterType textureType;
            private readonly TextureImporterNPOTScale npotScale;
            private readonly SpriteImportMode spriteImportMode;
            private readonly SpriteMetaData[] spritesheet;
            private readonly FilterMode filterMode;

            public ImporterSnapshot(TextureImporter importer)
            {
                textureType      = importer.textureType;
                npotScale        = importer.npotScale;
                spriteImportMode = importer.spriteImportMode;
                spritesheet      = importer.spritesheet.ToArray();
                filterMode       = importer.filterMode;
            }

            /// <summary>The frame names the sheet had before this call, in sheet order.</summary>
            public string[] FrameNames => spritesheet.Select(s => s.name).ToArray();

            public void Restore(TextureImporter importer)
            {
                bool changed = false;
                if (importer.textureType != textureType) { importer.textureType = textureType; changed = true; }
                if (importer.npotScale != npotScale) { importer.npotScale = npotScale; changed = true; }
                if (importer.spriteImportMode != spriteImportMode) { importer.spriteImportMode = spriteImportMode; changed = true; }
                if (!importer.spritesheet.SequenceEqual(spritesheet)) { importer.spritesheet = spritesheet; changed = true; }
                if (importer.filterMode != filterMode) { importer.filterMode = filterMode; changed = true; }
                if (!changed) return;
                EditorUtility.SetDirty(importer);
                importer.SaveAndReimport();
            }
        }

        // ── SliceSheet ───────────────────────────────────────────────────────

        public static object SliceSheet(JObject @params, SpriteDiagnosticBuilder diagnostics)
        {
            if (!SpriteParams.TryReadAssetPath(@params, "path", out string path, out string pathError))
                return diagnostics.Fail("BAD_PARAM", pathError);
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
                return diagnostics.Fail("NOT_FOUND", $"No TextureImporter found at '{path}'.");

            // Checked before the conversion below: a refused request used to leave the texture
            // already turned into a Sprite. Sequential rather than chained with ||, because a
            // short-circuited call leaves its out parameter unassigned.
            int rows = 0, frameW = 0, frameH = 0;
            bool gridOk = SpriteParams.TryReadWholeNumber(@params, "cols", 0, out int cols, out string gridError);
            if (gridOk) gridOk = SpriteParams.TryReadWholeNumber(@params, "rows", 0, out rows, out gridError);
            if (gridOk) gridOk = SpriteParams.TryReadWholeNumber(@params, "frame_width", 0, out frameW, out gridError);
            if (gridOk) gridOk = SpriteParams.TryReadWholeNumber(@params, "frame_height", 0, out frameH, out gridError);
            if (!gridOk)
                return diagnostics.Fail("BAD_PARAM", gridError);

            if (cols < 0 || rows < 0 || frameW < 0 || frameH < 0)
                return diagnostics.Fail("BAD_PARAM", "'cols', 'rows', 'frame_width' and 'frame_height' cannot be negative.");

            if (cols <= 0 && frameW <= 0)
                return diagnostics.Fail("BAD_PARAM", "Either 'cols' (1 or more) or 'frame_width' is required.");

            // Like cols, rows=0 means "derive it from frame_height"; an explicit 0 with nothing
            // to derive from would reach the texH / rows division below and throw. An absent
            // rows means one row unless frame_height is there to derive it from.
            bool rowsGiven = @params["rows"] != null && @params["rows"].Type != JTokenType.Null;
            if (rowsGiven && rows == 0 && frameH <= 0)
                return diagnostics.Fail("BAD_PARAM", "'rows' must be 1 or more; pass 'frame_height' instead if the row count is unknown.");
            if (!rowsGiven && frameH <= 0)
                rows = 1;

            // Point unless asked: it keeps pixel art sharp, and it was the only filter slice_sheet
            // set before this was a parameter. A switch rather than Enum.TryParse, which would
            // also take "7" or "Bilinear,Trilinear", neither of them a filter.
            FilterMode filterMode = FilterMode.Point;
            JToken filterToken = @params["filter_mode"];
            if (filterToken != null && filterToken.Type != JTokenType.Null)
            {
                switch (filterToken.ToString().ToLowerInvariant())
                {
                    case "point":     filterMode = FilterMode.Point; break;
                    case "bilinear":  filterMode = FilterMode.Bilinear; break;
                    case "trilinear": filterMode = FilterMode.Trilinear; break;
                    default:
                        return diagnostics.Fail("BAD_PARAM",
                            $"'filter_mode' must be point, bilinear or trilinear; got '{filterToken}'.");
                }
            }

            // Measure only once imported as a sprite sheet: a Default-type import rescales a
            // non-power-of-two sheet (96px to 128px) and the trailing frames then land outside
            // the real texture, where Unity drops them silently - measured on 6000.4.4f1, a
            // 96x16 sheet asked for 6 columns gave 4 sprites of 21px. Later refusals restore
            // the snapshot: a refused request must not leave a modified importer behind.
            var snapshot = new ImporterSnapshot(importer);
            try
            {
                // npotScale as well as the type: Unity refuses sprite generation outright on a
                // non-power-of-two texture that carries NPOT scaling ("Sprites can not be
                // generated from textures with NPOT scaling"), and the refusal is a console
                // message, not an exception - measured on 2021.3.45f2, a sheet already typed
                // Sprite skipped this block entirely, wrote its metadata, and reported six
                // frames with nothing on the asset. Sprite-mode textures cannot use NPOT
                // scaling at all, so clearing it takes nothing away.
                if (importer.textureType != TextureImporterType.Sprite
                    || importer.npotScale != TextureImporterNPOTScale.None)
                {
                    importer.textureType = TextureImporterType.Sprite;
                    importer.npotScale = TextureImporterNPOTScale.None;
                    EditorUtility.SetDirty(importer);
                    importer.SaveAndReimport();
                }
                return SliceConverted(@params, diagnostics, path, importer, snapshot, cols, rows, frameW, frameH, filterMode);
            }
            catch
            {
                // A restore that throws must not replace the exception that caused it.
                try { snapshot.Restore(importer); }
                catch (Exception restoreError) { McpLog.Error($"[ManageSprite] Could not restore the import settings of '{path}': {restoreError.Message}"); }
                throw;
            }
        }

        private static object SliceConverted(JObject @params, SpriteDiagnosticBuilder diagnostics, string path,
                                             TextureImporter importer, ImporterSnapshot snapshot,
                                             int cols, int rows, int frameW, int frameH, FilterMode filterMode)
        {
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (texture == null)
            {
                snapshot.Restore(importer);
                return diagnostics.Fail("NOT_FOUND", $"Could not load texture at '{path}'.");
            }

            // Sprite rects are in source pixels; texture.width/height is the imported size,
            // which Max Size shrinks. Measured on 6000.6.4f1: a 4096x256 sheet at the default
            // Max Size of 2048 imported at 2048x128, and an 8-column grid cut from that size
            // gave 8 sprites over the left half of the sheet, each half a frame, as a success.
            importer.GetSourceTextureWidthAndHeight(out int texW, out int texH);

            if (frameW <= 0) frameW = texW / cols;
            if (frameH <= 0) frameH = texH / rows;
            if (cols  <= 0) cols   = texW / frameW;
            if (rows  <= 0) rows   = texH / frameH;

            // Three ways to fail, only the first obvious. An oversized frame yields a non-zero
            // grid whose rects land outside the texture (measured: frame_height=4096 on a 16px
            // sheet, dropped silently, success). Integer division can drive a derived frame size
            // to zero (measured: 64 zero-width sprites, success). The product is long because
            // two large caller values wrap in 32-bit arithmetic and slip under the comparison.
            if (frameW <= 0 || frameH <= 0
                || (long)cols * frameW > texW || (long)rows * frameH > texH)
            {
                snapshot.Restore(importer);
                return diagnostics.Fail("SLICE_OUT_OF_BOUNDS",
                    $"A {cols}x{rows} grid of {frameW}x{frameH} frames does not fit inside the {texW}x{texH} texture, so some frames would fall outside it.",
                    "Reduce frame_width/frame_height, or cols/rows", "Confirm the texture dimensions with get_info");
            }

            // Fitting is not covering: the guard above only refuses a grid that is too BIG.
            // Measured on 6000.4.4f1 - a 100x16 sheet at 6 columns covered 96 of 100 pixels,
            // success, no diagnostic. Warns rather than refuses because a remainder is often
            // deliberate (a trailing margin, a separator, an intentional sub-region); silence
            // was the defect, not the behaviour.
            int uncoveredW = texW - cols * frameW;
            int uncoveredH = texH - rows * frameH;
            if (uncoveredW > 0 || uncoveredH > 0)
                diagnostics.AddWarning("SLICE_GRID_REMAINDER",
                    $"The grid covers {cols * frameW}x{rows * frameH} of a {texW}x{texH} texture, leaving {uncoveredW}px on the right and {uncoveredH}px at the bottom unused.",
                    "Deliberate if the sheet has a margin or a separator", "Otherwise check cols/rows against the texture size with get_info");

            // Every frame is allocated and reimported in one call, so this is a precaution
            // rather than a reproduction; far above any real sheet, it catches a cols/rows typo.
            const int MaxFrames = 4096;
            long totalFrames = (long)cols * rows;
            if (totalFrames > MaxFrames)
            {
                snapshot.Restore(importer);
                return diagnostics.Fail("SLICE_TOO_MANY_FRAMES",
                    $"The grid works out to {totalFrames} frames, above the {MaxFrames}-frame limit.",
                    "Increase frame_width/frame_height", "Slice the sheet in smaller pieces");
            }

            if (totalFrames == 0)
            {
                snapshot.Restore(importer);
                return diagnostics.Fail("SLICE_EMPTY",
                    $"A {cols}x{rows} grid works out to 0 frames - cols/rows or the frame size is wrong.",
                    "Check the cols and rows values", "Confirm the texture dimensions with get_info");
            }

            string baseName = @params["base_name"]?.ToString()
                ?? Path.GetFileNameWithoutExtension(path);

            var metas = new SpriteMetaData[(int)totalFrames];
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    int i = r * cols + c;
                    metas[i] = new SpriteMetaData
                    {
                        name      = $"{baseName}_{i}",
                        rect      = new Rect(c * frameW, texH - (r + 1) * frameH, frameW, frameH),
                        pivot     = new Vector2(0.5f, 0.5f),
                        alignment = 0,
                    };
                }
            }

            importer.spriteImportMode = SpriteImportMode.Multiple;
            importer.spritesheet      = metas;
            importer.filterMode       = filterMode;
            // Assigning spritesheet on an already-Multiple importer does not mark it dirty, so
            // SaveAndReimport would restore the old grid - measured, a second slice did nothing.
            EditorUtility.SetDirty(importer);
            importer.SaveAndReimport();

            // Unity can accept every SpriteMetaData entry and still emit no sprite for it, and
            // it says so in the console rather than throwing. NPOT scaling was one such path
            // and is closed above; an import that fails for any other reason would report the
            // same success over an empty asset. Counting what is actually on the asset is the
            // only answer that does not depend on knowing the causes in advance.
            // Sprites exist only after an import, so this check cannot run before the save;
            // the rollback is what keeps a refusal from leaving the asset modified.
            int generated = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().Count();
            if (generated != totalFrames)
            {
                snapshot.Restore(importer);
                return diagnostics.Fail("SLICE_NOT_GENERATED",
                    $"Unity accepted a {cols}x{rows} grid but generated {generated} of {totalFrames} sprites for '{path}'.",
                    "Check the Unity console for the import error",
                    "Confirm the texture's import settings allow sprite generation");
            }

            // A sprite's ID follows its name, so a re-slice keeps only the frames whose names the
            // new grid reuses. Measured on 2021.3.45f2: a clip of all eight frames of a 4x2 sheet
            // had six of them missing after a 2x1 re-slice, and the response said nothing;
            // slicing 4x2 again brought all eight back. After the generation check, because a
            // refusal restores the old frames and the warning would then be false.
            string[] before = snapshot.FrameNames;
            string[] removed = before.Except(metas.Select(m => m.name)).ToArray();
            if (removed.Length > 0)
            {
                const int MaxNamesListed = 10;
                string names = string.Join(", ", removed.Take(MaxNamesListed))
                             + (removed.Length > MaxNamesListed ? $" and {removed.Length - MaxNamesListed} more" : "");
                diagnostics.AddWarning("SLICE_REMOVED_FRAMES",
                    $"This slice removed {removed.Length} of the {before.Length} frames the sheet had ({names}); animation clips that used them lose those frames.",
                    "If the frames are still needed, slice again with the previous grid and base_name; clips pick them up again by name",
                    "Otherwise rebuild the clips that used them: setup_clips or full_setup, with overwrite=true");
            }

            return new
            {
                success      = true,
                path,
                cols,
                rows,
                frame_width  = frameW,
                frame_height = frameH,
                total_frames = totalFrames,
                diagnostics  = diagnostics.Build(),
            };
        }
    }
}
