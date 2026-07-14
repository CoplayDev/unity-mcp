using System;
using System.Collections.Generic;
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
        // Inert asset types permitted out of an UNTRUSTED provider archive (Sketchfab et al.).
        // Anything else — scripts, assemblies, asmdefs — is skipped on extraction so it can never
        // compile or load inside the Editor. See SafeZipExtractor for the enforcement.
        private static readonly HashSet<string> ArchiveAllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".gltf", ".glb", ".bin", ".fbx", ".obj", ".mtl",
            ".png", ".jpg", ".jpeg", ".tga", ".bmp", ".tif", ".tiff", ".webp", ".exr", ".hdr",
            ".ktx", ".ktx2", ".basis", ".dds",
        };

        public static AssetGenJob ImportInto(AssetGenJob job, string localFilePath)
        {
            if (job == null) return null;
            try
            {
                if (string.IsNullOrEmpty(localFilePath))
                    return Fail(job, "No file to import.");

                if (!AssetGenPaths.TryGetAssetsRelativePath(localFilePath, out string rel))
                    return Fail(job, "Generated file is not under the Assets folder.");

                string ext = Path.GetExtension(rel).ToLowerInvariant();
                if (ext == ".zip")
                    return ImportArchive(job, rel);

                return ImportModelFile(job, rel, ext);
            }
            catch (Exception e)
            {
                return Fail(job, SecretRedactor.Scrub(e.Message));
            }
        }

        private static AssetGenJob ImportModelFile(AssetGenJob job, string rel, string ext)
        {
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

        /// <summary>
        /// Unpack a downloaded archive (Sketchfab ships .zip) into a sibling folder named
        /// after the archive, import it, then locate the first model file inside and import that.
        /// FBX/OBJ are preferred over glTF; a glTF-only archive still requires glTFast.
        /// </summary>
        private static AssetGenJob ImportArchive(AssetGenJob job, string zipRel)
        {
            string zipAbs = AssetGenPaths.ToAbsolute(zipRel);
            if (!File.Exists(zipAbs))
                return Fail(job, "Downloaded archive was not found on disk.");

            string folderRel = zipRel.Substring(0, zipRel.Length - ".zip".Length);
            string folderAbs = AssetGenPaths.ToAbsolute(folderRel);

            Directory.CreateDirectory(folderAbs);
            // Provider archives are untrusted: only inert model/texture files are written under
            // Assets/ — scripts/assemblies are skipped so they can't be compiled on import.
            SafeZipExtractor.ExtractTo(zipAbs, folderAbs, ArchiveAllowedExtensions);

            AssetDatabase.Refresh();
            AssetDatabase.ImportAsset(folderRel, ImportAssetOptions.ImportRecursive | ImportAssetOptions.ForceUpdate);

            string modelRel = FindFirstModel(folderAbs);
            if (string.IsNullOrEmpty(modelRel))
                return Fail(job, "Archive extracted but no model file (.fbx/.obj/.glb/.gltf) was found inside.");

            string ext = Path.GetExtension(modelRel).ToLowerInvariant();
            bool isGltf = ext == ".glb" || ext == ".gltf";
            if (isGltf && !IsGltfastAvailable())
            {
                return Fail(job,
                    "This model is glTF (.glb/.gltf), which requires glTFast. Install it from the MCP for Unity → Dependencies tab.");
            }

            AssetDatabase.ImportAsset(modelRel, ImportAssetOptions.ForceUpdate);

            if (!isGltf)
                ApplyModelImporterSettings(modelRel, job);

            job.AssetPath = modelRel;
            job.AssetGuid = AssetDatabase.AssetPathToGUID(modelRel);
            if (string.IsNullOrEmpty(job.AssetGuid))
                return Fail(job, "Imported the extracted model but Unity did not register it as an asset.");

            if (job.State != AssetGenJobState.Failed)
                job.State = AssetGenJobState.Done;
            return job;
        }

        /// <summary>
        /// Walk the extracted directory for a model file, preferring FBX/OBJ (built-in importer)
        /// over glTF (needs glTFast). Returns a project-relative path or null when none found.
        /// </summary>
        private static string FindFirstModel(string folderAbs)
        {
            string[] all;
            try { all = Directory.GetFiles(folderAbs, "*", SearchOption.AllDirectories); }
            catch { return null; }

            string firstGltf = null;
            foreach (string abs in all)
            {
                string e = Path.GetExtension(abs).ToLowerInvariant();
                if (e == ".fbx" || e == ".obj")
                    return AssetGenPaths.ToProjectRelative(abs);
                if (firstGltf == null && (e == ".glb" || e == ".gltf"))
                    firstGltf = abs;
            }
            return firstGltf == null ? null : AssetGenPaths.ToProjectRelative(firstGltf);
        }

        private static void ApplyModelImporterSettings(string rel, AssetGenJob job)
        {
            if (!(AssetImporter.GetAtPath(rel) is ModelImporter importer)) return;
            importer.useFileScale = true;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            importer.animationType = ParseAnimationType(job?.AnimationType);

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

        /// <summary>
        /// Map the caller's rig mode to a <see cref="ModelImporterAnimationType"/>. Defaults to
        /// <c>None</c> (no rig) when unset — pass "generic"/"humanoid" to surface a rigged FBX/OBJ's
        /// AnimationClips, which the None default otherwise hides. "legacy" selects Unity's
        /// legacy Animation system — rarely needed.
        /// </summary>
        internal static ModelImporterAnimationType ParseAnimationType(string value)
        {
            switch ((value ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "generic": return ModelImporterAnimationType.Generic;
                case "humanoid":
                case "human": return ModelImporterAnimationType.Human;
                case "legacy": return ModelImporterAnimationType.Legacy;
                default: return ModelImporterAnimationType.None;
            }
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

        private static bool? _gltfastAvailable;

        /// <summary>
        /// True when the glTFast package is present. Cached after the first probe — the result only
        /// changes on a package install/uninstall, which triggers a domain reload that resets this
        /// static. Shared with the Asset Gen settings tab so the reflection scan runs at most once.
        /// </summary>
        internal static bool IsGltfastAvailable()
        {
            if (_gltfastAvailable.HasValue) return _gltfastAvailable.Value;
            bool found = Type.GetType("GLTFast.GltfImport, glTFast") != null;
            if (!found)
            {
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try { if (asm.GetType("GLTFast.GltfImport") != null) { found = true; break; } }
                    catch { /* dynamic/!resolvable assembly */ }
                }
            }
            _gltfastAvailable = found;
            return found;
        }

        private static AssetGenJob Fail(AssetGenJob job, string message)
        {
            job.State = AssetGenJobState.Failed;
            job.Error = message;
            return job;
        }
    }
}
