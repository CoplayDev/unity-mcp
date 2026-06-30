using System;
using System.IO;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Security;
using UnityEditor;

namespace MCPForUnity.Editor.Services.AssetGen.Import
{
    /// <summary>
    /// Imports a generated 2D image (PNG, already under Assets/) and applies TextureImporter
    /// settings: Sprite vs Default, alpha-is-transparency, and sRGB (color) vs linear (data maps).
    /// </summary>
    public static class ImageImportPipeline
    {
        public static AssetGenJob ImportInto(AssetGenJob job, string localFilePath, bool asSprite, bool transparent, bool isColor)
        {
            if (job == null) return null;
            try
            {
                if (string.IsNullOrEmpty(localFilePath))
                    return Fail(job, "No file to import.");

                if (!AssetGenPaths.TryGetAssetsRelativePath(localFilePath, out string rel))
                    return Fail(job, "Generated file is not under the Assets folder.");

                AssetDatabase.ImportAsset(rel, ImportAssetOptions.ForceUpdate);

                if (AssetImporter.GetAtPath(rel) is TextureImporter importer)
                {
                    importer.textureType = asSprite ? TextureImporterType.Sprite : TextureImporterType.Default;
                    importer.alphaIsTransparency = transparent;
                    importer.sRGBTexture = isColor; // color maps sRGB; normal/roughness/metallic would be linear
                    if (asSprite)
                    {
                        importer.spriteImportMode = SpriteImportMode.Single;
                        importer.mipmapEnabled = false;
                    }
                    importer.SaveAndReimport();
                }

                job.AssetPath = rel;
                job.AssetGuid = AssetDatabase.AssetPathToGUID(rel);
                if (string.IsNullOrEmpty(job.AssetGuid))
                    return Fail(job, "Imported the image but Unity did not register it as an asset.");

                if (job.State != AssetGenJobState.Failed)
                    job.State = AssetGenJobState.Done;
                return job;
            }
            catch (Exception e)
            {
                return Fail(job, SecretRedactor.Scrub(e.Message));
            }
        }

        private static AssetGenJob Fail(AssetGenJob job, string message)
        {
            job.State = AssetGenJobState.Failed;
            job.Error = message;
            return job;
        }
    }
}
