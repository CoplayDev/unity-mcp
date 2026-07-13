using System;
using System.IO;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Security;
using UnityEditor;
using UnityEngine;

namespace MCPForUnity.Editor.Services.AssetGen.Import
{
    /// <summary>
    /// Imports a downloaded audio clip (already under Assets/) and applies AudioImporter settings.
    /// Load type is chosen by clip length: short one-shots (SFX) decompress into memory for
    /// zero-latency playback; medium clips stay compressed in memory; long BGM streams — so a
    /// generated soundtrack doesn't sit fully decompressed in RAM.
    /// </summary>
    public static class AudioImportPipeline
    {
        public static AssetGenJob ImportInto(AssetGenJob job, string localFilePath)
        {
            if (job == null) return null;
            try
            {
                if (string.IsNullOrEmpty(localFilePath))
                    return Fail(job, "No file to import.");

                if (!AssetGenPaths.TryGetAssetsRelativePath(localFilePath, out string rel))
                    return Fail(job, "Generated file is not under the Assets folder.");

                // Defense-in-depth: never import a non-audio file even if one slipped past WriteFile.
                if (!AssetGenJobManager.IsAllowedResultExtension("audio", Path.GetExtension(rel)))
                    return Fail(job, "Refusing to import a non-audio file type.");

                AssetDatabase.ImportAsset(rel, ImportAssetOptions.ForceUpdate);
                ApplyAudioImporterSettings(rel);

                job.AssetPath = rel;
                job.AssetGuid = AssetDatabase.AssetPathToGUID(rel);
                if (string.IsNullOrEmpty(job.AssetGuid))
                    return Fail(job, "Imported the audio but Unity did not register it as an asset.");

                if (job.State != AssetGenJobState.Failed)
                    job.State = AssetGenJobState.Done;
                return job;
            }
            catch (Exception e)
            {
                return Fail(job, SecretRedactor.Scrub(e.Message));
            }
        }

        private static void ApplyAudioImporterSettings(string rel)
        {
            if (!(AssetImporter.GetAtPath(rel) is AudioImporter importer)) return;

            AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(rel);
            float len = clip != null ? clip.length : 0f;

            AudioImporterSampleSettings s = importer.defaultSampleSettings;
            if (len > 30f) s.loadType = AudioClipLoadType.Streaming;              // long BGM
            else if (len > 10f) s.loadType = AudioClipLoadType.CompressedInMemory; // medium track
            else s.loadType = AudioClipLoadType.DecompressOnLoad;                  // short SFX one-shot
            importer.defaultSampleSettings = s;

            importer.forceToMono = false;
            importer.loadInBackground = false;
            importer.SaveAndReimport();
        }

        private static AssetGenJob Fail(AssetGenJob job, string message)
        {
            job.State = AssetGenJobState.Failed;
            job.Error = message;
            return job;
        }
    }
}
