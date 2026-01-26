using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using MCPForUnity.Editor.Helpers;

namespace MCPForUnity.Editor.Tools
{
    /// <summary>
    /// Handles procedural texture generation operations.
    /// Supports patterns (checkerboard, stripes, dots, grid, brick),
    /// gradients, noise, and direct pixel manipulation.
    /// </summary>
    [McpForUnityTool("manage_texture", AutoRegister = false)]
    public static class ManageTexture
    {
        private const int MaxTextureDimension = 1024;
        private const int MaxTexturePixels = 1024 * 1024;
        private const int MaxNoiseWork = 4000000;
        private static readonly List<string> ValidActions = new List<string>
        {
            "create",
            "modify",
            "delete",
            "create_sprite",
            "apply_pattern",
            "apply_gradient",
            "apply_noise"
        };

        private static ErrorResponse ValidateDimensions(int width, int height, List<string> warnings)
        {
            if (width <= 0 || height <= 0)
                return new ErrorResponse($"Invalid dimensions: {width}x{height}. Must be positive.");
            if (width > MaxTextureDimension || height > MaxTextureDimension)
                warnings.Add($"Dimensions exceed recommended max {MaxTextureDimension} per side (got {width}x{height}).");
            long totalPixels = (long)width * height;
            if (totalPixels > MaxTexturePixels)
                warnings.Add($"Total pixels exceed recommended max {MaxTexturePixels} (got {width}x{height}).");
            return null;
        }


        public static object HandleCommand(JObject @params)
        {
            string action = @params["action"]?.ToString()?.ToLowerInvariant();
            if (string.IsNullOrEmpty(action))
            {
                return new ErrorResponse("Action parameter is required.");
            }

            if (!ValidActions.Contains(action))
            {
                string validActionsList = string.Join(", ", ValidActions);
                return new ErrorResponse(
                    $"Unknown action: '{action}'. Valid actions are: {validActionsList}"
                );
            }

            string path = @params["path"]?.ToString();

            try
            {
                switch (action)
                {
                    case "create":
                    case "create_sprite":
                        return CreateTexture(@params, action == "create_sprite");
                    case "modify":
                        return ModifyTexture(@params);
                    case "delete":
                        return DeleteTexture(path);
                    case "apply_pattern":
                        return ApplyPattern(@params);
                    case "apply_gradient":
                        return ApplyGradient(@params);
                    case "apply_noise":
                        return ApplyNoise(@params);
                    default:
                        return new ErrorResponse($"Unknown action: '{action}'");
                }
            }
            catch (Exception e)
            {
                McpLog.Error($"[ManageTexture] Action '{action}' failed: {e}");
                return new ErrorResponse($"Internal error processing action '{action}': {e.Message}");
            }
        }

        // --- Action Implementations ---

        private static object CreateTexture(JObject @params, bool asSprite)
        {
            string path = @params["path"]?.ToString();
            if (string.IsNullOrEmpty(path))
                return new ErrorResponse("'path' is required for create.");

            string imagePath = @params["imagePath"]?.ToString();
            bool hasImage = !string.IsNullOrEmpty(imagePath);

            int width = @params["width"]?.ToObject<int>() ?? 64;
            int height = @params["height"]?.ToObject<int>() ?? 64;
            List<string> warnings = new List<string>();

            // Validate dimensions
            if (!hasImage)
            {
                var dimensionError = ValidateDimensions(width, height, warnings);
                if (dimensionError != null)
                    return dimensionError;
            }

            string fullPath = AssetPathUtility.SanitizeAssetPath(path);
            EnsureDirectoryExists(fullPath);

            try
            {
                var fillColorToken = @params["fillColor"];
                var patternToken = @params["pattern"];
                var pixelsToken = @params["pixels"];

                if (hasImage && (fillColorToken != null || patternToken != null || pixelsToken != null))
                {
                    return new ErrorResponse("imagePath cannot be combined with fillColor, pattern, or pixels.");
                }

                int patternSize = 8;
                if (!hasImage && patternToken != null)
                {
                    patternSize = @params["patternSize"]?.ToObject<int>() ?? 8;
                    if (patternSize <= 0)
                        return new ErrorResponse("patternSize must be greater than 0.");
                }

                Texture2D texture;
                if (hasImage)
                {
                    string resolvedImagePath = ResolveImagePath(imagePath);
                    if (!File.Exists(resolvedImagePath))
                        return new ErrorResponse($"Image file not found at '{imagePath}'.");

                    byte[] imageBytes = File.ReadAllBytes(resolvedImagePath);
                    texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    if (!texture.LoadImage(imageBytes))
                    {
                        UnityEngine.Object.DestroyImmediate(texture);
                        return new ErrorResponse($"Failed to load image from '{imagePath}'.");
                    }

                    width = texture.width;
                    height = texture.height;
                    var imageDimensionError = ValidateDimensions(width, height, warnings);
                    if (imageDimensionError != null)
                    {
                        UnityEngine.Object.DestroyImmediate(texture);
                        return imageDimensionError;
                    }
                }
                else
                {
                    texture = new Texture2D(width, height, TextureFormat.RGBA32, false);

                    // Check for fill color
                    if (fillColorToken != null && fillColorToken.Type == JTokenType.Array)
                    {
                        Color32 fillColor = TextureOps.ParseColor32(fillColorToken as JArray);
                        TextureOps.FillTexture(texture, fillColor);
                    }

                    // Check for pattern
                    if (patternToken != null)
                    {
                        string pattern = patternToken.ToString();
                        var palette = TextureOps.ParsePalette(@params["palette"] as JArray);
                        ApplyPatternToTexture(texture, pattern, palette, patternSize);
                    }

                    // Check for direct pixel data
                    if (pixelsToken != null)
                    {
                        TextureOps.ApplyPixelData(texture, pixelsToken, width, height);
                    }

                    // If nothing specified, create transparent texture
                    if (fillColorToken == null && patternToken == null && pixelsToken == null)
                    {
                        TextureOps.FillTexture(texture, new Color32(0, 0, 0, 0));
                    }
                }

                texture.Apply();

                // Save to disk
                byte[] imageData = TextureOps.EncodeTexture(texture, fullPath);
                if (imageData == null || imageData.Length == 0)
                {
                    UnityEngine.Object.DestroyImmediate(texture);
                    return new ErrorResponse($"Failed to encode texture for '{fullPath}'");
                }
                File.WriteAllBytes(GetAbsolutePath(fullPath), imageData);

                AssetDatabase.ImportAsset(fullPath, ImportAssetOptions.ForceUpdate);

                // Configure texture importer settings if provided
                JToken importSettingsToken = @params["importSettings"];
                JToken spriteSettingsToken = @params["spriteSettings"];

                if (importSettingsToken != null)
                {
                    ConfigureTextureImporter(fullPath, importSettingsToken);
                }
                else if (asSprite || spriteSettingsToken != null)
                {
                    // Legacy sprite configuration
                    ConfigureAsSprite(fullPath, spriteSettingsToken);
                }

                // Clean up memory
                UnityEngine.Object.DestroyImmediate(texture);
                foreach (var warning in warnings)
                {
                    McpLog.Warn($"[ManageTexture] {warning}");
                }

                return new SuccessResponse(
                    $"Texture created at '{fullPath}' ({width}x{height})" + (asSprite ? " as sprite" : ""),
                    new
                    {
                        path = fullPath,
                        width,
                        height,
                        asSprite = asSprite || spriteSettingsToken != null || (importSettingsToken?["textureType"]?.ToString() == "Sprite"),
                        warnings = warnings.Count > 0 ? warnings : null
                    }
                );
            }
            catch (Exception e)
            {
                return new ErrorResponse($"Failed to create texture: {e.Message}");
            }
        }

        private static object ModifyTexture(JObject @params)
        {
            string path = @params["path"]?.ToString();
            if (string.IsNullOrEmpty(path))
                return new ErrorResponse("'path' is required for modify.");

            string fullPath = AssetPathUtility.SanitizeAssetPath(path);
            if (!AssetExists(fullPath))
                return new ErrorResponse($"Texture not found at path: {fullPath}");

            try
            {
                Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(fullPath);
                if (texture == null)
                    return new ErrorResponse($"Failed to load texture at path: {fullPath}");

                // Make the texture readable
                string absolutePath = GetAbsolutePath(fullPath);
                byte[] fileData = File.ReadAllBytes(absolutePath);
                Texture2D editableTexture = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false);
                editableTexture.LoadImage(fileData);

                // Apply modifications
                var setPixelsToken = @params["setPixels"] as JObject;
                if (setPixelsToken != null)
                {
                    int x = setPixelsToken["x"]?.ToObject<int>() ?? 0;
                    int y = setPixelsToken["y"]?.ToObject<int>() ?? 0;
                    int w = setPixelsToken["width"]?.ToObject<int>() ?? 1;
                    int h = setPixelsToken["height"]?.ToObject<int>() ?? 1;

                    if (w <= 0 || h <= 0)
                    {
                        UnityEngine.Object.DestroyImmediate(editableTexture);
                        return new ErrorResponse("setPixels width and height must be positive.");
                    }

                    var pixelsToken = setPixelsToken["pixels"];
                    var colorToken = setPixelsToken["color"];

                    if (pixelsToken != null)
                    {
                        TextureOps.ApplyPixelDataToRegion(editableTexture, pixelsToken, x, y, w, h);
                    }
                    else if (colorToken != null)
                    {
                        Color32 color = TextureOps.ParseColor32(colorToken as JArray);
                        int startX = Mathf.Max(0, x);
                        int startY = Mathf.Max(0, y);
                        int endX = Mathf.Min(x + w, editableTexture.width);
                        int endY = Mathf.Min(y + h, editableTexture.height);

                        for (int py = startY; py < endY; py++)
                        {
                            for (int px = startX; px < endX; px++)
                            {
                                editableTexture.SetPixel(px, py, color);
                            }
                        }
                    }
                    else
                    {
                        UnityEngine.Object.DestroyImmediate(editableTexture);
                        return new ErrorResponse("setPixels requires 'color' or 'pixels'.");
                    }
                }

                editableTexture.Apply();

                // Save back to disk
                byte[] imageData = TextureOps.EncodeTexture(editableTexture, fullPath);
                if (imageData == null || imageData.Length == 0)
                {
                    UnityEngine.Object.DestroyImmediate(editableTexture);
                    return new ErrorResponse($"Failed to encode texture for '{fullPath}'");
                }
                File.WriteAllBytes(absolutePath, imageData);

                AssetDatabase.ImportAsset(fullPath, ImportAssetOptions.ForceUpdate);

                UnityEngine.Object.DestroyImmediate(editableTexture);

                return new SuccessResponse($"Texture modified: {fullPath}");
            }
            catch (Exception e)
            {
                return new ErrorResponse($"Failed to modify texture: {e.Message}");
            }
        }

        private static object DeleteTexture(string path)
        {
            if (string.IsNullOrEmpty(path))
                return new ErrorResponse("'path' is required for delete.");

            string fullPath = AssetPathUtility.SanitizeAssetPath(path);
            if (!AssetExists(fullPath))
                return new ErrorResponse($"Texture not found at path: {fullPath}");

            try
            {
                bool success = AssetDatabase.DeleteAsset(fullPath);
                if (success)
                    return new SuccessResponse($"Texture deleted: {fullPath}");
                else
                    return new ErrorResponse($"Failed to delete texture: {fullPath}");
            }
            catch (Exception e)
            {
                return new ErrorResponse($"Error deleting texture: {e.Message}");
            }
        }

        private static object ApplyPattern(JObject @params)
        {
            // Reuse CreateTexture with pattern
            return CreateTexture(@params, false);
        }

        private static object ApplyGradient(JObject @params)
        {
            string path = @params["path"]?.ToString();
            if (string.IsNullOrEmpty(path))
                return new ErrorResponse("'path' is required for apply_gradient.");

            int width = @params["width"]?.ToObject<int>() ?? 64;
            int height = @params["height"]?.ToObject<int>() ?? 64;
            List<string> warnings = new List<string>();
            var dimensionError = ValidateDimensions(width, height, warnings);
            if (dimensionError != null)
                return dimensionError;
            string gradientType = @params["gradientType"]?.ToString() ?? "linear";
            float angle = @params["gradientAngle"]?.ToObject<float>() ?? 0f;

            var palette = TextureOps.ParsePalette(@params["palette"] as JArray);
            if (palette == null || palette.Count < 2)
            {
                // Default gradient palette
                palette = new List<Color32> { new Color32(0, 0, 0, 255), new Color32(255, 255, 255, 255) };
            }

            string fullPath = AssetPathUtility.SanitizeAssetPath(path);
            EnsureDirectoryExists(fullPath);

            Texture2D texture = null;
            try
            {
                texture = new Texture2D(width, height, TextureFormat.RGBA32, false);

                if (gradientType == "radial")
                {
                    ApplyRadialGradient(texture, palette);
                }
                else
                {
                    ApplyLinearGradient(texture, palette, angle);
                }

                texture.Apply();

                byte[] imageData = TextureOps.EncodeTexture(texture, fullPath);
                if (imageData == null || imageData.Length == 0)
                {
                    return new ErrorResponse($"Failed to encode texture for '{fullPath}'");
                }
                File.WriteAllBytes(GetAbsolutePath(fullPath), imageData);

                AssetDatabase.ImportAsset(fullPath, ImportAssetOptions.ForceUpdate);

                // Configure as sprite if requested
                JToken spriteSettingsToken = @params["spriteSettings"];
                if (spriteSettingsToken != null)
                {
                    ConfigureAsSprite(fullPath, spriteSettingsToken);
                }

                foreach (var warning in warnings)
                {
                    McpLog.Warn($"[ManageTexture] {warning}");
                }

                return new SuccessResponse(
                    $"Gradient texture created at '{fullPath}' ({width}x{height})",
                    new
                    {
                        path = fullPath,
                        width,
                        height,
                        gradientType,
                        warnings = warnings.Count > 0 ? warnings : null
                    }
                );
            }
            catch (Exception e)
            {
                return new ErrorResponse($"Failed to create gradient texture: {e.Message}");
            }
            finally
            {
                if (texture != null)
                    UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        private static object ApplyNoise(JObject @params)
        {
            string path = @params["path"]?.ToString();
            if (string.IsNullOrEmpty(path))
                return new ErrorResponse("'path' is required for apply_noise.");

            int width = @params["width"]?.ToObject<int>() ?? 64;
            int height = @params["height"]?.ToObject<int>() ?? 64;
            List<string> warnings = new List<string>();
            var dimensionError = ValidateDimensions(width, height, warnings);
            if (dimensionError != null)
                return dimensionError;
            float scale = @params["noiseScale"]?.ToObject<float>() ?? 0.1f;
            int octaves = @params["octaves"]?.ToObject<int>() ?? 1;
            if (octaves <= 0)
                return new ErrorResponse("octaves must be greater than 0.");
            long noiseWork = (long)width * height * octaves;
            if (noiseWork > MaxNoiseWork)
                warnings.Add($"Noise workload exceeds recommended max {MaxNoiseWork} (got {width}x{height}x{octaves}).");

            var palette = TextureOps.ParsePalette(@params["palette"] as JArray);
            if (palette == null || palette.Count < 2)
            {
                palette = new List<Color32> { new Color32(0, 0, 0, 255), new Color32(255, 255, 255, 255) };
            }

            string fullPath = AssetPathUtility.SanitizeAssetPath(path);
            EnsureDirectoryExists(fullPath);

            Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            try
            {
                ApplyPerlinNoise(texture, palette, scale, octaves);

                texture.Apply();

                byte[] imageData = TextureOps.EncodeTexture(texture, fullPath);
                if (imageData == null || imageData.Length == 0)
                {
                    return new ErrorResponse($"Failed to encode texture for '{fullPath}'");
                }
                File.WriteAllBytes(GetAbsolutePath(fullPath), imageData);

                AssetDatabase.ImportAsset(fullPath, ImportAssetOptions.ForceUpdate);

                // Configure as sprite if requested
                JToken spriteSettingsToken = @params["spriteSettings"];
                if (spriteSettingsToken != null)
                {
                    ConfigureAsSprite(fullPath, spriteSettingsToken);
                }

                foreach (var warning in warnings)
                {
                    McpLog.Warn($"[ManageTexture] {warning}");
                }

                return new SuccessResponse(
                    $"Noise texture created at '{fullPath}' ({width}x{height})",
                    new
                    {
                        path = fullPath,
                        width,
                        height,
                        noiseScale = scale,
                        octaves,
                        warnings = warnings.Count > 0 ? warnings : null
                    }
                );
            }
            catch (Exception e)
            {
                return new ErrorResponse($"Failed to create noise texture: {e.Message}");
            }
            finally
            {
                if (texture != null)
                    UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        // --- Pattern Helpers ---

        private static void ApplyPatternToTexture(Texture2D texture, string pattern, List<Color32> palette, int patternSize)
        {
            if (palette == null || palette.Count == 0)
            {
                palette = new List<Color32> { new Color32(255, 255, 255, 255), new Color32(0, 0, 0, 255) };
            }

            int width = texture.width;
            int height = texture.height;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    Color32 color = GetPatternColor(x, y, pattern, palette, patternSize, width, height);
                    texture.SetPixel(x, y, color);
                }
            }
        }

        private static Color32 GetPatternColor(int x, int y, string pattern, List<Color32> palette, int size, int width, int height)
        {
            int colorIndex = 0;

            switch (pattern.ToLower())
            {
                case "checkerboard":
                    colorIndex = ((x / size) + (y / size)) % 2;
                    break;

                case "stripes":
                case "stripes_v":
                    colorIndex = (x / size) % palette.Count;
                    break;

                case "stripes_h":
                    colorIndex = (y / size) % palette.Count;
                    break;

                case "stripes_diag":
                    colorIndex = ((x + y) / size) % palette.Count;
                    break;

                case "dots":
                    int cx = (x % (size * 2)) - size;
                    int cy = (y % (size * 2)) - size;
                    bool inDot = (cx * cx + cy * cy) < (size * size / 4);
                    colorIndex = inDot ? 1 : 0;
                    break;

                case "grid":
                    bool onGridLine = (x % size == 0) || (y % size == 0);
                    colorIndex = onGridLine ? 1 : 0;
                    break;

                case "brick":
                    int row = y / size;
                    int offset = (row % 2) * (size / 2);
                    bool onBorder = ((x + offset) % size == 0) || (y % size == 0);
                    colorIndex = onBorder ? 1 : 0;
                    break;

                default:
                    colorIndex = 0;
                    break;
            }

            return palette[Mathf.Clamp(colorIndex, 0, palette.Count - 1)];
        }

        // --- Gradient Helpers ---

        private static void ApplyLinearGradient(Texture2D texture, List<Color32> palette, float angle)
        {
            int width = texture.width;
            int height = texture.height;
            float radians = angle * Mathf.Deg2Rad;
            Vector2 dir = new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
            float denomX = Mathf.Max(1, width - 1);
            float denomY = Mathf.Max(1, height - 1);

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float nx = x / denomX;
                    float ny = y / denomY;
                    float t = Vector2.Dot(new Vector2(nx, ny), dir);
                    t = Mathf.Clamp01((t + 1f) / 2f);

                    Color32 color = LerpPalette(palette, t);
                    texture.SetPixel(x, y, color);
                }
            }
        }

        private static void ApplyRadialGradient(Texture2D texture, List<Color32> palette)
        {
            int width = texture.width;
            int height = texture.height;
            float cx = width / 2f;
            float cy = height / 2f;
            float maxDist = Mathf.Sqrt(cx * cx + cy * cy);

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float dx = x - cx;
                    float dy = y - cy;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    float t = Mathf.Clamp01(dist / maxDist);

                    Color32 color = LerpPalette(palette, t);
                    texture.SetPixel(x, y, color);
                }
            }
        }

        private static Color32 LerpPalette(List<Color32> palette, float t)
        {
            if (palette.Count == 1) return palette[0];
            if (t <= 0) return palette[0];
            if (t >= 1) return palette[palette.Count - 1];

            float scaledT = t * (palette.Count - 1);
            int index = Mathf.FloorToInt(scaledT);
            float localT = scaledT - index;

            if (index >= palette.Count - 1)
                return palette[palette.Count - 1];

            Color c1 = palette[index];
            Color c2 = palette[index + 1];
            return Color.Lerp(c1, c2, localT);
        }

        // --- Noise Helpers ---

        private static void ApplyPerlinNoise(Texture2D texture, List<Color32> palette, float scale, int octaves)
        {
            int width = texture.width;
            int height = texture.height;

            // Random offset to ensure different patterns
            float offsetX = UnityEngine.Random.Range(0f, 1000f);
            float offsetY = UnityEngine.Random.Range(0f, 1000f);

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float noiseValue = 0f;
                    float amplitude = 1f;
                    float frequency = 1f;
                    float maxValue = 0f;

                    for (int o = 0; o < octaves; o++)
                    {
                        float sampleX = (x + offsetX) * scale * frequency;
                        float sampleY = (y + offsetY) * scale * frequency;
                        noiseValue += Mathf.PerlinNoise(sampleX, sampleY) * amplitude;
                        maxValue += amplitude;
                        amplitude *= 0.5f;
                        frequency *= 2f;
                    }

                    float t = Mathf.Clamp01(noiseValue / maxValue);
                    Color32 color = LerpPalette(palette, t);
                    texture.SetPixel(x, y, color);
                }
            }
        }

        private static void ConfigureAsSprite(string path, JToken spriteSettings)
        {
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
            {
                McpLog.Warn($"[ManageTexture] Could not get TextureImporter for {path}");
                return;
            }

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;

            if (spriteSettings != null && spriteSettings.Type == JTokenType.Object)
            {
                var settings = spriteSettings as JObject;

                // Pivot
                var pivotToken = settings["pivot"];
                if (pivotToken is JArray pivotArray && pivotArray.Count >= 2)
                {
                    importer.spritePivot = new Vector2(
                        pivotArray[0].ToObject<float>(),
                        pivotArray[1].ToObject<float>()
                    );
                }

                // Pixels per unit
                var ppuToken = settings["pixelsPerUnit"];
                if (ppuToken != null)
                {
                    importer.spritePixelsPerUnit = ppuToken.ToObject<float>();
                }
            }

            importer.SaveAndReimport();
        }

        private static void ConfigureTextureImporter(string path, JToken importSettings)
        {
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
            {
                McpLog.Warn($"[ManageTexture] Could not get TextureImporter for {path}");
                return;
            }

            if (importSettings == null || importSettings.Type != JTokenType.Object)
            {
                return;
            }

            var settings = importSettings as JObject;

            // Texture Type
            var textureTypeToken = settings["textureType"];
            if (textureTypeToken != null)
            {
                string typeStr = textureTypeToken.ToString();
                if (TryParseEnum<TextureImporterType>(typeStr, out var textureType))
                {
                    importer.textureType = textureType;
                }
            }

            // Texture Shape
            var textureShapeToken = settings["textureShape"];
            if (textureShapeToken != null)
            {
                string shapeStr = textureShapeToken.ToString();
                if (TryParseEnum<TextureImporterShape>(shapeStr, out var textureShape))
                {
                    importer.textureShape = textureShape;
                }
            }

            // sRGB
            var srgbToken = settings["sRGBTexture"];
            if (srgbToken != null)
            {
                importer.sRGBTexture = srgbToken.ToObject<bool>();
            }

            // Alpha Source
            var alphaSourceToken = settings["alphaSource"];
            if (alphaSourceToken != null)
            {
                string alphaStr = alphaSourceToken.ToString();
                if (TryParseEnum<TextureImporterAlphaSource>(alphaStr, out var alphaSource))
                {
                    importer.alphaSource = alphaSource;
                }
            }

            // Alpha Is Transparency
            var alphaTransToken = settings["alphaIsTransparency"];
            if (alphaTransToken != null)
            {
                importer.alphaIsTransparency = alphaTransToken.ToObject<bool>();
            }

            // Readable
            var readableToken = settings["isReadable"];
            if (readableToken != null)
            {
                importer.isReadable = readableToken.ToObject<bool>();
            }

            // Mipmaps
            var mipmapToken = settings["mipmapEnabled"];
            if (mipmapToken != null)
            {
                importer.mipmapEnabled = mipmapToken.ToObject<bool>();
            }

            // Mipmap Filter
            var mipmapFilterToken = settings["mipmapFilter"];
            if (mipmapFilterToken != null)
            {
                string filterStr = mipmapFilterToken.ToString();
                if (TryParseEnum<TextureImporterMipFilter>(filterStr, out var mipmapFilter))
                {
                    importer.mipmapFilter = mipmapFilter;
                }
            }

            // Wrap Mode
            var wrapModeToken = settings["wrapMode"];
            if (wrapModeToken != null)
            {
                string wrapStr = wrapModeToken.ToString();
                if (TryParseEnum<TextureWrapMode>(wrapStr, out var wrapMode))
                {
                    importer.wrapMode = wrapMode;
                }
            }

            // Wrap Mode U
            var wrapModeUToken = settings["wrapModeU"];
            if (wrapModeUToken != null)
            {
                string wrapStr = wrapModeUToken.ToString();
                if (TryParseEnum<TextureWrapMode>(wrapStr, out var wrapMode))
                {
                    importer.wrapModeU = wrapMode;
                }
            }

            // Wrap Mode V
            var wrapModeVToken = settings["wrapModeV"];
            if (wrapModeVToken != null)
            {
                string wrapStr = wrapModeVToken.ToString();
                if (TryParseEnum<TextureWrapMode>(wrapStr, out var wrapMode))
                {
                    importer.wrapModeV = wrapMode;
                }
            }

            // Filter Mode
            var filterModeToken = settings["filterMode"];
            if (filterModeToken != null)
            {
                string filterStr = filterModeToken.ToString();
                if (TryParseEnum<FilterMode>(filterStr, out var filterMode))
                {
                    importer.filterMode = filterMode;
                }
            }

            // Aniso Level
            var anisoToken = settings["anisoLevel"];
            if (anisoToken != null)
            {
                importer.anisoLevel = anisoToken.ToObject<int>();
            }

            // Max Texture Size
            var maxSizeToken = settings["maxTextureSize"];
            if (maxSizeToken != null)
            {
                importer.maxTextureSize = maxSizeToken.ToObject<int>();
            }

            // Compression
            var compressionToken = settings["textureCompression"];
            if (compressionToken != null)
            {
                string compStr = compressionToken.ToString();
                if (TryParseEnum<TextureImporterCompression>(compStr, out var compression))
                {
                    importer.textureCompression = compression;
                }
            }

            // Crunched Compression
            var crunchedToken = settings["crunchedCompression"];
            if (crunchedToken != null)
            {
                importer.crunchedCompression = crunchedToken.ToObject<bool>();
            }

            // Compression Quality
            var qualityToken = settings["compressionQuality"];
            if (qualityToken != null)
            {
                importer.compressionQuality = qualityToken.ToObject<int>();
            }

            // --- Sprite-specific settings ---

            // Sprite Import Mode
            var spriteModeToken = settings["spriteImportMode"];
            if (spriteModeToken != null)
            {
                string modeStr = spriteModeToken.ToString();
                if (TryParseEnum<SpriteImportMode>(modeStr, out var spriteMode))
                {
                    importer.spriteImportMode = spriteMode;
                }
            }

            // Sprite Pixels Per Unit
            var ppuToken = settings["spritePixelsPerUnit"];
            if (ppuToken != null)
            {
                importer.spritePixelsPerUnit = ppuToken.ToObject<float>();
            }

            // Sprite Pivot
            var pivotToken = settings["spritePivot"];
            if (pivotToken is JArray pivotArray && pivotArray.Count >= 2)
            {
                importer.spritePivot = new Vector2(
                    pivotArray[0].ToObject<float>(),
                    pivotArray[1].ToObject<float>()
                );
            }

            // Apply sprite settings using TextureImporterSettings helper
            TextureImporterSettings importerSettings = new TextureImporterSettings();
            importer.ReadTextureSettings(importerSettings);

            bool settingsChanged = false;

            // Sprite Mesh Type
            var meshTypeToken = settings["spriteMeshType"];
            if (meshTypeToken != null)
            {
                string meshStr = meshTypeToken.ToString();
                if (TryParseEnum<SpriteMeshType>(meshStr, out var meshType))
                {
                    importerSettings.spriteMeshType = meshType;
                    settingsChanged = true;
                }
            }

            // Sprite Extrude
            var extrudeToken = settings["spriteExtrude"];
            if (extrudeToken != null)
            {
                importerSettings.spriteExtrude = (uint)extrudeToken.ToObject<int>();
                settingsChanged = true;
            }
            
            if (settingsChanged)
            {
                importer.SetTextureSettings(importerSettings);
            }

            importer.SaveAndReimport();
        }

        private static bool TryParseEnum<T>(string value, out T result) where T : struct
        {
            // Try exact match first
            if (Enum.TryParse<T>(value, true, out result))
            {
                return true;
            }

            // Try without common prefixes/suffixes
            string cleanValue = value.Replace("_", "").Replace("-", "");
            if (Enum.TryParse<T>(cleanValue, true, out result))
            {
                return true;
            }

            result = default;
            return false;
        }

        private static bool AssetExists(string path)
        {
            return !string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(path));
        }

        private static void EnsureDirectoryExists(string assetPath)
        {
            string directory = Path.GetDirectoryName(assetPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(GetAbsolutePath(directory)))
            {
                Directory.CreateDirectory(GetAbsolutePath(directory));
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            }
        }

        private static string GetAbsolutePath(string assetPath)
        {
            return Path.Combine(Directory.GetCurrentDirectory(), assetPath);
        }

        private static string ResolveImagePath(string imagePath)
        {
            if (Path.IsPathRooted(imagePath))
                return imagePath;

            return Path.Combine(Directory.GetCurrentDirectory(), imagePath);
        }
    }
}
