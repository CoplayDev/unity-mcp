using AssetStoreTools.Previews.Data;
using UnityEditor;
using UnityEngine;

namespace AssetStoreTools.Previews.Utility
{
    internal static class PreviewConvertUtility
    {
        public static string ConvertFilename(Object asset, FileNameFormat format)
        {
            string fileName = string.Empty;

            switch (format)
            {
                case FileNameFormat.Guid:
                    AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset, out var guid, out long _);
                    fileName = guid;
                    break;
                case FileNameFormat.FullAssetPath:
                    var assetPath = AssetDatabase.GetAssetPath(asset);

                    if (assetPath.StartsWith("Assets/"))
                        fileName = assetPath.Substring("Assets/".Length);
                    else if (assetPath.StartsWith("Packages/"))
                        fileName = assetPath.Substring("Packages/".Length);

                    fileName = fileName.Replace("/", "_");
                    break;
                case FileNameFormat.AssetName:
                    fileName = asset.name;
                    break;
                default:
                    throw new System.Exception("Undefined format");
            }

            return fileName;
        }

        public static string ConvertExtension(PreviewFormat format)
        {
            switch (format)
            {
                case PreviewFormat.JPG:
                    return "jpg";
                case PreviewFormat.PNG:
                    return "png";
                default:
                    throw new System.Exception("Undefined format");
            }
        }

        public static string ConvertFilenameWithExtension(Object asset, FileNameFormat nameFormat, PreviewFormat imageFormat)
        {
            var filename = ConvertFilename(asset, nameFormat);
            var extension = ConvertExtension(imageFormat);
            return $"{filename}.{extension}";
        }

        public static byte[] ConvertTexture(Texture2D texture, PreviewFormat format)
        {
            switch (format)
            {
                case PreviewFormat.JPG:
                    return texture.EncodeToJPG();
                case PreviewFormat.PNG:
                    return texture.EncodeToPNG();
                default:
                    throw new System.Exception("Undefined format");
            }
        }
    }
}