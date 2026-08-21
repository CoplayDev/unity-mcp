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
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
                return new ErrorResponse($"No TextureImporter found at '{path}'. Is it a texture/sprite?");

            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            int w = texture != null ? texture.width  : 0;
            int h = texture != null ? texture.height : 0;

            var existingSlices = importer.spritesheet.Select(s => new
            {
                name   = s.name,
                x      = (int)s.rect.x,
                y      = (int)s.rect.y,
                width  = (int)s.rect.width,
                height = (int)s.rect.height,
            }).ToArray();

            // Base64 payload so a vision-capable caller can read the grid off the image.
            string imageBase64 = null;
            try
            {
                string fullPath = Path.Combine(
                    Application.dataPath.Replace("/Assets", ""),
                    path
                );
                if (File.Exists(fullPath))
                {
                    byte[] bytes = File.ReadAllBytes(fullPath);
                    string ext = Path.GetExtension(path).ToLowerInvariant();
                    string mime = (ext == ".jpg" || ext == ".jpeg") ? "image/jpeg" : "image/png";
                    imageBase64 = $"data:{mime};base64," + Convert.ToBase64String(bytes);
                }
            }
            catch { /* The base64 payload is optional; leaving it null is a valid answer. */ }

            var result = new
            {
                success       = true,
                path,
                width         = w,
                height        = h,
                sprite_mode   = importer.spriteImportMode.ToString(),
                pixels_per_unit = importer.spritePixelsPerUnit,
                filter_mode   = importer.filterMode.ToString(),
                slice_count   = existingSlices.Length,
                slices        = existingSlices,
                image_base64  = imageBase64,
            };

            return result;
        }

        // ── SliceSheet ───────────────────────────────────────────────────────

        public static object SliceSheet(JObject @params, SpriteDiagnosticBuilder diagnostics)
        {
            string path = @params["path"]?.ToString();
            if (string.IsNullOrEmpty(path))
                return new ErrorResponse("'path' is required.");

            path = AssetPathUtility.SanitizeAssetPath(path);
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
                return new ErrorResponse($"No TextureImporter found at '{path}'.");

            // Measure the texture only once it is imported the way a sprite sheet is.
            // A Default-type import rescales a non-power-of-two sheet (96px becomes 128px),
            // and a grid computed against that size puts the trailing frames outside the real
            // texture, where Unity drops them without an error. Measured on 6000.4.4f1: a
            // 96x16 sheet asked for 6 columns produced 4 sprites of 21px.
            if (importer.textureType != TextureImporterType.Sprite)
            {
                importer.textureType = TextureImporterType.Sprite;
                EditorUtility.SetDirty(importer);
                importer.SaveAndReimport();
            }

            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (texture == null)
                return new ErrorResponse($"Could not load texture at '{path}'.");

            int texW = texture.width;
            int texH = texture.height;

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

            if (frameW <= 0) frameW = texW / cols;
            if (frameH <= 0) frameH = texH / rows;
            if (cols  <= 0) cols   = texW / frameW;
            if (rows  <= 0) rows   = texH / frameH;

            int totalFrames = cols * rows;
            if (totalFrames == 0)
            {
                diagnostics.AddError(
                    "SLICE_EMPTY",
                    "The grid works out to 0 frames - cols/rows or the frame size is wrong.",
                    new { cols, rows, frame_width = frameW, frame_height = frameH, texture_width = texW, texture_height = texH },
                    new[] { "Check the cols and rows values", "Confirm the texture dimensions with get_info" }
                );
                return new { success = false, diagnostics = diagnostics.Build() };
            }

            string baseName = @params["base_name"]?.ToString()
                ?? Path.GetFileNameWithoutExtension(path);

            var metas = new SpriteMetaData[totalFrames];
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
