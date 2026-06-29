using System;
using System.Collections.Generic;
using System.IO;
using MCPForUnity.Editor.Helpers;

namespace MCPForUnity.Editor.Services.AssetGen.Providers
{
    /// <summary>
    /// Helpers for feeding a LOCAL on-disk image to a provider: resolve+verify the path, and encode
    /// it as a base64 <c>data:</c> URI — the inline form fal, Meshy, and OpenRouter accept for image
    /// input (no hosting/upload needed). Tripo does NOT accept data URIs and is handled separately.
    /// </summary>
    internal static class LocalImage
    {
        // Extensions that can be inlined as a data URI for provider image input.
        private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".png", ".jpg", ".jpeg", ".webp", ".gif" };

        /// <summary>
        /// Resolve an "Assets/..."-relative or absolute path to an existing absolute file of a
        /// supported image type. Returns false with a user-facing <paramref name="error"/> for a
        /// missing file or an unsupported extension, so the handler can fail fast (synchronously).
        /// </summary>
        public static bool ResolveExisting(string path, out string absPath, out string error)
        {
            absPath = null;
            error = null;
            if (string.IsNullOrWhiteSpace(path)) { error = "image_path is empty."; return false; }
            string p = path.Replace('\\', '/');
            string abs = (p == "Assets" || p.StartsWith("Assets/")) ? AssetGenPaths.ToAbsolute(p) : p;
            if (!File.Exists(abs)) { error = $"Source image not found: {path}"; return false; }
            if (!SupportedExtensions.Contains(Path.GetExtension(abs)))
            {
                error = $"Unsupported image type '{Path.GetExtension(abs)}'. Use .png, .jpg, .jpeg, .webp, or .gif.";
                return false;
            }
            absPath = abs;
            return true;
        }

        /// <summary>
        /// Read a local image and return a "data:image/&lt;mime&gt;;base64,..." URI. Throws
        /// <see cref="NotSupportedException"/> for an unsupported extension.
        /// </summary>
        public static string ToDataUri(string absPath)
        {
            string mime = MimeFromExtension(Path.GetExtension(absPath));
            byte[] bytes = File.ReadAllBytes(absPath);
            return "data:" + mime + ";base64," + Convert.ToBase64String(bytes);
        }

        private static string MimeFromExtension(string ext)
        {
            switch ((ext ?? string.Empty).ToLowerInvariant())
            {
                case ".png": return "image/png";
                case ".jpg":
                case ".jpeg": return "image/jpeg";
                case ".webp": return "image/webp";
                case ".gif": return "image/gif";
                default:
                    throw new NotSupportedException(
                        $"Unsupported image type '{ext}' for image input. Use .png, .jpg, .jpeg, .webp, or .gif.");
            }
        }
    }
}
