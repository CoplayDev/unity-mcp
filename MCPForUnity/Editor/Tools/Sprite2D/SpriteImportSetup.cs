using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using MCPForUnity.Editor.Helpers;
// TextureImporter.spritesheet is obsolete as of Unity 6, but the replacement
// (ISpriteEditorDataProvider) needs the 2D Sprite package and a good deal more setup for the
// same result. Revisit if the property is actually removed.
#pragma warning disable CS0618

namespace MCPForUnity.Editor.Tools.Sprite2D
{
    internal static class SpriteImportSetup
    {
        // ── GetInfo ──────────────────────────────────────────────────────────

        public static object GetInfo(JObject @params)
        {
            string path = @params["path"]?.ToString();
            if (string.IsNullOrEmpty(path))
                return new ErrorResponse("'path' is required.");

            path = AssetPathUtility.SanitizeAssetPath(path);
            // A refused path comes back null, and reporting that as "no TextureImporter here"
            // names the wrong problem: the path was never looked up.
            if (path == null)
                return new ErrorResponse("'path' must stay under Assets/ and cannot contain '..'.");
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
                return new ErrorResponse($"No TextureImporter found at '{path}'. Is it a texture/sprite?");

            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            int w = texture != null ? texture.width  : 0;
            int h = texture != null ? texture.height : 0;

            // The slice list is paged, unlike the image below. slice_sheet caps what it
            // GENERATES at 4096 frames, but this reads what is already on the asset, and a
            // sheet sliced by hand in the Sprite Editor carries as many entries as someone
            // drew - the ceiling on the writing end never bounded the reading end.
            // 512 is the default page because it clears any grid a caller would slice here
            // (a 32x16 sheet fits whole), so the common case gets one page, no cursor, and
            // never learns paging exists. The maximum is what stops page_size from being
            // used to ask for the unbounded result again.
            const int DefaultSlicePageSize = 512;
            const int MaxSlicePageSize = 4096;

            // ToObject<int?> rather than the ToObject<int> the slice_sheet parameters below
            // use: an explicit JSON null reaches here as a JValue, so `?.` does not
            // short-circuit and the non-nullable form would read it as 0 and refuse the
            // call. Nulling a parameter out means "unset", which is the default.
            int pageSize = @params["page_size"]?.ToObject<int?>() ?? DefaultSlicePageSize;
            if (pageSize < 1 || pageSize > MaxSlicePageSize)
                return new ErrorResponse($"'page_size' must be between 1 and {MaxSlicePageSize}; got {pageSize}.");

            int totalSlices = importer.spritesheet.Length;
            int cursor = @params["cursor"]?.ToObject<int?>() ?? 0;
            // Skip yields every element for a negative count rather than throwing, so a
            // negative cursor would silently return page one and call it a success - the
            // same trap that let start_frame=-2 write frames 0..5 in SpriteClipBuilder.
            // Landing exactly on totalSlices returns an empty page rather than an error.
            // next_cursor never points there, so this is for a caller walking the list by
            // adding page_size itself - and it is what makes cursor 0 legal on a sheet
            // with no slices at all, where 0 IS the end.
            if (cursor < 0 || cursor > totalSlices)
                return new ErrorResponse($"'cursor' must be between 0 and {totalSlices}; got {cursor}.");

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
            // It is bounded by size rather than paged: the point of the payload is that one
            // response carries one whole image a vision model can look at, and an image split
            // across cursors is not an image any client can reassemble. Over the ceiling the
            // payload is dropped and the reason is named - width, height and the slice list
            // still answer everything the caller needs to compute a grid.
            // 4 MB because that is a payload a single tool response can carry without the
            // transport or the model's context becoming the limiting factor; it is a budget,
            // not a measured protocol boundary, and moving it breaks the two fixture
            // assertions in ManageSpriteTests on purpose.
            // The ceiling is applied to the ENCODED length, not the file size. base64 emits
            // 4 characters per 3 bytes, so bounding the source let a 3.67 MB sheet through as
            // a 4.89 MB payload - measured, and the reason this arithmetic is written out.
            const int MaxInlinePayloadBytes = 4 * 1024 * 1024;
            string imageBase64 = null;
            string imageOmittedReason = null;
            if (cursor > 0)
            {
                // Only the first page carries the image. The picture does not change
                // between pages, and paging exists to bound the response - sending the
                // whole payload again with every page would multiply by the page count
                // the very thing the page size is there to cap.
                imageOmittedReason =
                    "The image is returned only on the first page. Request this path with " +
                    "cursor 0 (or omit cursor) if the image itself is needed.";
            }
            else
            {
                try
                {
                    // Not Application.dataPath.Replace("/Assets", ""): Replace removes EVERY
                    // occurrence, so a project under a directory like /work/AssetsLab lost the
                    // wrong segment and the lookup silently missed a file that was really there.
                    string projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
                    string fullPath = projectRoot != null ? Path.Combine(projectRoot, path) : null;
                    if (fullPath == null)
                    {
                        imageOmittedReason = "The project root could not be resolved from Application.dataPath.";
                    }
                    else if (!File.Exists(fullPath))
                    {
                        imageOmittedReason = $"No file on disk at '{fullPath}'.";
                    }
                    else
                    {
                        string ext = Path.GetExtension(path).ToLowerInvariant();
                        string mime = (ext == ".jpg" || ext == ".jpeg") ? "image/jpeg" : "image/png";
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
                catch (Exception ex)
                {
                    // The payload is optional, but a swallowed failure and a deliberate omission
                    // are different answers and the response now has a field that can tell them
                    // apart. Leaving it null was the whole complaint about `catch {}`.
                    imageOmittedReason = $"The image could not be read: {ex.GetType().Name}: {ex.Message}";
                }
            }

            var result = new
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

            return result;
        }

        /// <summary>Undoes the conversion above when the request is refused after it.</summary>
        private static void RestoreTextureType(TextureImporter importer, TextureImporterType previous)
        {
            if (importer.textureType == previous) return;
            importer.textureType = previous;
            EditorUtility.SetDirty(importer);
            importer.SaveAndReimport();
        }

        // ── SliceSheet ───────────────────────────────────────────────────────

        public static object SliceSheet(JObject @params, SpriteDiagnosticBuilder diagnostics)
        {
            string path = @params["path"]?.ToString();
            if (string.IsNullOrEmpty(path))
                return new ErrorResponse("'path' is required.");

            path = AssetPathUtility.SanitizeAssetPath(path);
            if (path == null)
                return new ErrorResponse("'path' must stay under Assets/ and cannot contain '..'.");
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
                return new ErrorResponse($"No TextureImporter found at '{path}'.");

            // These arguments need no texture, so they are checked before the conversion below:
            // a refused request used to return an error with the texture already turned into a Sprite.
            int cols = @params["cols"]?.ToObject<int>() ?? 0;
            int rows = @params["rows"]?.ToObject<int>() ?? 1;
            int frameW = @params["frame_width"]?.ToObject<int>() ?? 0;
            int frameH = @params["frame_height"]?.ToObject<int>() ?? 0;

            if (cols <= 0 && frameW <= 0)
                return new ErrorResponse("Either 'cols' or 'frame_width' is required.");

            // `?? 1` above only covers an absent key, so an explicit rows=0 reaches the
            // texH / rows division below and throws instead of answering.
            if (rows <= 0 && frameH <= 0)
                return new ErrorResponse("'rows' must be 1 or more; pass 'frame_height' instead if the row count is unknown.");

            // Measure the texture only once it is imported the way a sprite sheet is.
            // A Default-type import rescales a non-power-of-two sheet (96px becomes 128px),
            // and a grid computed against that size puts the trailing frames outside the real
            // texture, where Unity drops them without an error. Measured on 6000.4.4f1: a
            // 96x16 sheet asked for 6 columns produced 4 sprites of 21px.
            // Some refusals can only be reached after the texture has been measured - a frame
            // size larger than the sheet is one - so the previous type is kept and restored on
            // the way out. A request that was refused must not leave a converted texture behind.
            var previousType = importer.textureType;
            if (importer.textureType != TextureImporterType.Sprite)
            {
                importer.textureType = TextureImporterType.Sprite;
                EditorUtility.SetDirty(importer);
                importer.SaveAndReimport();
            }

            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (texture == null)
            {
                RestoreTextureType(importer, previousType);
                return new ErrorResponse($"Could not load texture at '{path}'.");
            }

            int texW = texture.width;
            int texH = texture.height;

            if (frameW <= 0) frameW = texW / cols;
            if (frameH <= 0) frameH = texH / rows;
            if (cols  <= 0) cols   = texW / frameW;
            if (rows  <= 0) rows   = texH / frameH;

            // A frame larger than the sheet still yields a non-zero grid, so the empty-grid
            // check below never sees it: the rects simply land outside the texture and Unity
            // drops them while the call reports success. Measured on 6000.4.4f1 with
            // frame_height=4096 on a 16px-tall sheet.
            // Three ways a grid fails to fit, and only the first is obvious. Integer division
            // can drive a DERIVED frame size to zero - 64 columns across 32 pixels gives 0-wide
            // frames - and the product is then 0, which passes any bounds test while the
            // metadata is degenerate: measured, 64 zero-width sprites reported as success. And
            // the product itself is computed in long, because two large caller-supplied values
            // wrap in 32-bit arithmetic and slip under the comparison.
            if (frameW <= 0 || frameH <= 0
                || (long)cols * frameW > texW || (long)rows * frameH > texH)
            {
                diagnostics.AddError(
                    "SLICE_OUT_OF_BOUNDS",
                    "The grid does not fit inside the texture, so some frames would fall outside it.",
                    new { cols, rows, frame_width = frameW, frame_height = frameH, texture_width = texW, texture_height = texH },
                    new[] { "Reduce frame_width/frame_height, or cols/rows", "Confirm the texture dimensions with get_info" }
                );
                RestoreTextureType(importer, previousType);
                return new { success = false, diagnostics = diagnostics.Build() };
            }

            // A 4096x4096 sheet cut into 1px frames is 16,777,216 entries, and this method
            // allocates and reimports every one of them in one call. That size was not run
            // here - the ceiling is a precaution, not a reproduction - but it sits far above
            // any real sheet (Unity's own Sprite Editor works in the hundreds), so what it
            // actually catches is a typo in cols/rows. The count is long because it is
            // compared before it is trusted.
            const int MaxFrames = 4096;
            long totalFrames = (long)cols * rows;
            if (totalFrames > MaxFrames)
            {
                diagnostics.AddError(
                    "SLICE_TOO_MANY_FRAMES",
                    $"The grid works out to {totalFrames} frames, above the {MaxFrames}-frame limit.",
                    new { cols, rows, frame_width = frameW, frame_height = frameH, total_frames = totalFrames, max_frames = MaxFrames },
                    new[] { "Increase frame_width/frame_height", "Slice the sheet in smaller pieces" }
                );
                RestoreTextureType(importer, previousType);
                return new { success = false, diagnostics = diagnostics.Build() };
            }

            if (totalFrames == 0)
            {
                diagnostics.AddError(
                    "SLICE_EMPTY",
                    "The grid works out to 0 frames - cols/rows or the frame size is wrong.",
                    new { cols, rows, frame_width = frameW, frame_height = frameH, texture_width = texW, texture_height = texH },
                    new[] { "Check the cols and rows values", "Confirm the texture dimensions with get_info" }
                );
                RestoreTextureType(importer, previousType);
                return new { success = false, diagnostics = diagnostics.Build() };
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
            importer.filterMode       = FilterMode.Point; // pixel-perfect default
            // Assigning spritesheet on an importer that is already Multiple does not mark it
            // dirty, so SaveAndReimport would re-import the previously serialised grid and the
            // new one would be silently dropped. Measured on 6000.4.4f1: without this, slicing
            // a second time leaves the first grid in place.
            EditorUtility.SetDirty(importer);
            importer.SaveAndReimport();

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
