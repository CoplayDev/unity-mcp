using System;
using System.IO;

namespace MCPForUnity.Editor.Services.AssetGen
{
    internal static class ImageResultFormat
    {
        internal static string FromMetadata(string mediaType, string url = null)
        {
            switch (mediaType?.Split(';')[0].ToLowerInvariant())
            {
                case "image/png": return "png";
                case "image/jpeg": return "jpg";
                case "image/webp": return "webp";
                case "image/svg+xml": return "svg";
            }
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                string ext = Path.GetExtension(uri.AbsolutePath).TrimStart('.').ToLowerInvariant();
                if (ext == "png" || ext == "jpg" || ext == "jpeg" || ext == "webp" || ext == "svg") return ext;
            }
            return null;
        }

        internal static string FromBytes(byte[] bytes)
        {
            if (bytes == null) return null;
            if (bytes.Length >= 8 && bytes[0] == 137 && bytes[1] == 80 && bytes[2] == 78 && bytes[3] == 71
                && bytes[4] == 13 && bytes[5] == 10 && bytes[6] == 26 && bytes[7] == 10) return "png";
            if (bytes.Length >= 3 && bytes[0] == 255 && bytes[1] == 216 && bytes[2] == 255) return "jpg";
            if (bytes.Length >= 12 && bytes[0] == 82 && bytes[1] == 73 && bytes[2] == 70 && bytes[3] == 70
                && bytes[8] == 87 && bytes[9] == 69 && bytes[10] == 66 && bytes[11] == 80) return "webp";
            return null;
        }
    }
}
