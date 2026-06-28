using System;
using System.IO;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Security;
using UnityEditor;
using UnityEngine;

namespace MCPForUnity.Editor.Services.AssetGen.Import
{
    /// <summary>
    /// Imports a downloaded model file (already under Assets/) into the project and applies
    /// import settings. GLB/glTF require the optional glTFast package (installed from the
    /// Dependencies tab); FBX/OBJ use Unity's built-in ModelImporter. Optionally normalizes
    /// the model's scale to a target size.
    /// </summary>
    public static class ModelImportPipeline
    {
        public static AssetGenJob ImportInto(AssetGenJob job, string localFilePath)
        {
            if (job == null) return null;
            try
            {
                if (string.IsNullOrEmpty(localFilePath))
                    return Fail(job, "No file to import.");

                string rel = ToProjectRelative(localFilePath);
                if (string.IsNullOrEmpty(rel) || !rel.Replace('\\', '/').StartsWith("Assets"))
                    return Fail(job, "Generated file is not under the Assets folder.");

                string ext = Path.GetExtension(rel).ToLowerInvariant();
                bool isGltf = ext == ".glb" || ext == ".gltf";

                if (isGltf && !IsGltfastAvailable())
                {
                    return Fail(job,
                        "GLB import requires glTFast. Install it from the MCP for Unity → Dependencies tab, or choose FBX output.");
                }

                AssetDatabase.ImportAsset(rel, ImportAssetOptions.ForceUpdate);

                if (!isGltf)
                    ApplyModelImporterSettings(rel, job);

                job.AssetPath = rel;
                job.AssetGuid = AssetDatabase.AssetPathToGUID(rel);
                if (string.IsNullOrEmpty(job.AssetGuid))
                    return Fail(job, "Imported the file but Unity did not register it as an asset.");

                if (job.State != AssetGenJobState.Failed)
                    job.State = AssetGenJobState.Done;
                return job;
            }
            catch (Exception e)
            {
                return Fail(job, SecretRedactor.Scrub(e.Message));
            }
        }

        private static void ApplyModelImporterSettings(string rel, AssetGenJob job)
        {
            if (!(AssetImporter.GetAtPath(rel) is ModelImporter importer)) return;
            importer.useFileScale = true;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            importer.animationType = ModelImporterAnimationType.None;

            if (AssetGenPrefs.AutoNormalize && job.TargetSize > 0f)
            {
                float maxDim = ComputeMaxDimension(rel);
                if (maxDim > 0.0001f)
                {
                    float scale = job.TargetSize / maxDim;
                    if (scale > 0f && Math.Abs(scale - 1f) > 0.01f)
                    {
                        importer.useFileScale = false;
                        importer.globalScale = Mathf.Clamp(importer.globalScale * scale, 0.0001f, 1_000_000f);
                    }
                }
            }
            importer.SaveAndReimport();
        }

        private static float ComputeMaxDimension(string rel)
        {
            try
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(rel);
                if (go == null) return 0f;

                bool any = false;
                Bounds acc = new Bounds(Vector3.zero, Vector3.zero);
                foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (mf.sharedMesh == null) continue;
                    if (!any) { acc = mf.sharedMesh.bounds; any = true; }
                    else acc.Encapsulate(mf.sharedMesh.bounds);
                }
                foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (smr.sharedMesh == null) continue;
                    if (!any) { acc = smr.sharedMesh.bounds; any = true; }
                    else acc.Encapsulate(smr.sharedMesh.bounds);
                }
                if (!any) return 0f;
                Vector3 s = acc.size;
                return Mathf.Max(s.x, Mathf.Max(s.y, s.z));
            }
            catch { return 0f; }
        }

        private static bool IsGltfastAvailable()
        {
            if (Type.GetType("GLTFast.GltfImport, glTFast") != null) return true;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try { if (asm.GetType("GLTFast.GltfImport") != null) return true; }
                catch { /* dynamic/!resolvable assembly */ }
            }
            return false;
        }

        private static string ToProjectRelative(string path)
        {
            string p = path.Replace('\\', '/');
            if (p.StartsWith("Assets")) return p;
            string dataPath = Application.dataPath.Replace('\\', '/');
            if (p.StartsWith(dataPath)) return "Assets" + p.Substring(dataPath.Length);
            return p;
        }

        private static AssetGenJob Fail(AssetGenJob job, string message)
        {
            job.State = AssetGenJobState.Failed;
            job.Error = message;
            return job;
        }
    }
}
