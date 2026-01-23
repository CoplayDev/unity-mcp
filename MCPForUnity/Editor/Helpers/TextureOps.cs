using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace MCPForUnity.Editor.Helpers
{
    public static class TextureOps
    {
        public static byte[] EncodeTexture(Texture2D texture, string assetPath)
        {
            if (texture == null)
                return null;

            string extension = Path.GetExtension(assetPath);
            if (string.IsNullOrEmpty(extension))
            {
                McpLog.Warn($"[TextureOps] No file extension for '{assetPath}', defaulting to PNG.");
                return texture.EncodeToPNG();
            }

            switch (extension.ToLowerInvariant())
            {
                case ".png":
                    return texture.EncodeToPNG();
                case ".jpg":
                case ".jpeg":
                    return texture.EncodeToJPG();
                default:
                    McpLog.Warn($"[TextureOps] Unsupported extension '{extension}' for '{assetPath}', defaulting to PNG.");
                    return texture.EncodeToPNG();
            }
        }

        public static void FillTexture(Texture2D texture, Color32 color)
        {
            if (texture == null)
                return;

            Color32[] pixels = new Color32[texture.width * texture.height];
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = color;
            }
            texture.SetPixels32(pixels);
        }

        public static Color32 ParseColor32(JArray colorArray)
        {
            if (colorArray == null || colorArray.Count < 3)
                return new Color32(255, 255, 255, 255);

            byte r = (byte)Mathf.Clamp(colorArray[0].ToObject<int>(), 0, 255);
            byte g = (byte)Mathf.Clamp(colorArray[1].ToObject<int>(), 0, 255);
            byte b = (byte)Mathf.Clamp(colorArray[2].ToObject<int>(), 0, 255);
            byte a = colorArray.Count > 3 ? (byte)Mathf.Clamp(colorArray[3].ToObject<int>(), 0, 255) : (byte)255;

            return new Color32(r, g, b, a);
        }

        public static List<Color32> ParsePalette(JArray paletteArray)
        {
            if (paletteArray == null)
                return null;

            List<Color32> palette = new List<Color32>();
            foreach (var item in paletteArray)
            {
                if (item is JArray colorArray)
                {
                    palette.Add(ParseColor32(colorArray));
                }
            }
            return palette.Count > 0 ? palette : null;
        }

        public static void ApplyPixelData(Texture2D texture, JToken pixelsToken, int width, int height)
        {
            ApplyPixelDataToRegion(texture, pixelsToken, 0, 0, width, height);
        }

        public static void ApplyPixelDataToRegion(Texture2D texture, JToken pixelsToken, int offsetX, int offsetY, int regionWidth, int regionHeight)
        {
            if (texture == null || pixelsToken == null)
                return;

            int textureWidth = texture.width;
            int textureHeight = texture.height;

            if (pixelsToken is JArray pixelArray)
            {
                int index = 0;
                for (int y = 0; y < regionHeight && index < pixelArray.Count; y++)
                {
                    for (int x = 0; x < regionWidth && index < pixelArray.Count; x++)
                    {
                        var pixelColor = pixelArray[index] as JArray;
                        if (pixelColor != null)
                        {
                            int px = offsetX + x;
                            int py = offsetY + y;
                            if (px >= 0 && px < textureWidth && py >= 0 && py < textureHeight)
                            {
                                texture.SetPixel(px, py, ParseColor32(pixelColor));
                            }
                        }
                        index++;
                    }
                }

                int expectedCount = regionWidth * regionHeight;
                if (pixelArray.Count != expectedCount)
                {
                    McpLog.Warn($"[TextureOps] Pixel array size mismatch: expected {expectedCount} entries, got {pixelArray.Count}");
                }
            }
            else if (pixelsToken.Type == JTokenType.String)
            {
                string pixelString = pixelsToken.ToString();
                string base64 = pixelString.StartsWith("base64:") ? pixelString.Substring(7) : pixelString;
                if (!pixelString.StartsWith("base64:"))
                {
                    McpLog.Warn("[TextureOps] Base64 pixel data missing 'base64:' prefix; attempting to decode.");
                }

                byte[] rawData = Convert.FromBase64String(base64);

                // Assume RGBA32 format: 4 bytes per pixel
                int expectedBytes = regionWidth * regionHeight * 4;
                if (rawData.Length == expectedBytes)
                {
                    int pixelIndex = 0;
                    for (int y = 0; y < regionHeight; y++)
                    {
                        for (int x = 0; x < regionWidth; x++)
                        {
                            int px = offsetX + x;
                            int py = offsetY + y;
                            if (px >= 0 && px < textureWidth && py >= 0 && py < textureHeight)
                            {
                                int byteIndex = pixelIndex * 4;
                                Color32 color = new Color32(
                                    rawData[byteIndex],
                                    rawData[byteIndex + 1],
                                    rawData[byteIndex + 2],
                                    rawData[byteIndex + 3]
                                );
                                texture.SetPixel(px, py, color);
                            }
                            pixelIndex++;
                        }
                    }
                }
                else
                {
                    McpLog.Warn($"[TextureOps] Base64 data size mismatch: expected {expectedBytes} bytes, got {rawData.Length}");
                }
            }
        }
    }
}
