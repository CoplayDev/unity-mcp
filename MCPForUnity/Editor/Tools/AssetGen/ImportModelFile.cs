using System;
using System.IO;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Security;
using MCPForUnity.Editor.Services.AssetGen;
using MCPForUnity.Editor.Services.AssetGen.Import;
using Newtonsoft.Json.Linq;
using UnityEditor;

namespace MCPForUnity.Editor.Tools.AssetGen
{
    /// <summary>
    /// Import a local 3D model file (already on disk — e.g. exported from Blender/Maya) into the
    /// Unity project. DCC-agnostic and key-free: the file is copied under Assets/ and run through
    /// the shared ModelImportPipeline (glTFast/FBX/OBJ/zip handling, scale-normalize, material
    /// settings). Placement into the scene is the caller's job (kept single-purpose).
    /// </summary>
    [McpForUnityTool("import_model_file", AutoRegister = false, Group = "asset_gen")]
    public static class ImportModelFile
    {
        private static readonly string[] SupportedExt = { ".fbx", ".obj", ".glb", ".gltf", ".zip" };

        public static object HandleCommand(JObject @params)
        {
            if (@params == null) return new ErrorResponse("Parameters cannot be null.");
            var p = new ToolParams(@params);
            try
            {
                string source = p.Get("sourcePath");
                if (string.IsNullOrWhiteSpace(source))
                    return new ErrorResponse("'source_path' is required.");

                string srcAbs = ResolveSource(source);
                if (!File.Exists(srcAbs))
                    return new ErrorResponse($"Source file not found: {source}");

                string ext = Path.GetExtension(srcAbs).ToLowerInvariant();
                if (Array.IndexOf(SupportedExt, ext) < 0)
                    return new ErrorResponse(
                        $"Unsupported model extension '{ext}'. Supported: .fbx, .obj, .glb, .gltf, .zip.");

                string baseName = p.Get("name");
                if (string.IsNullOrWhiteSpace(baseName))
                    baseName = Path.GetFileNameWithoutExtension(srcAbs);

                string destRel = StageUnderAssets(srcAbs, baseName, ext, p.Get("outputFolder"));
                AssetDatabase.Refresh();

                var job = new AssetGenJob { TargetSize = p.GetFloat("targetSize", 1f) ?? 1f };
                AssetGenJob result = ModelImportPipeline.ImportInto(job, destRel);

                if (result == null || result.State == AssetGenJobState.Failed)
                    return new ErrorResponse(result?.Error ?? "Import failed.");

                return new SuccessResponse(
                    $"Imported model: {result.AssetPath}",
                    new { asset_path = result.AssetPath, asset_guid = result.AssetGuid });
            }
            catch (Exception e)
            {
                return new ErrorResponse(SecretRedactor.Scrub(e.Message));
            }
        }

        private static string ResolveSource(string source)
        {
            string s = source.Replace('\\', '/');
            if (s == "Assets" || s.StartsWith("Assets/")) return AssetGenPaths.ToAbsolute(s);
            return s; // absolute path on disk
        }

        private static string StageUnderAssets(string srcAbs, string baseName, string ext, string outputFolder)
        {
            string root = !string.IsNullOrWhiteSpace(outputFolder)
                ? outputFolder
                : AssetGenPrefs.OutputRoot + "/Imported";
            if (!AssetGenPaths.TryGetAssetsFolder(root, out root))
            {
                if (!string.IsNullOrWhiteSpace(outputFolder))
                    throw new ArgumentException("'output_folder' must resolve under the project's Assets folder.");
                root = AssetGenPrefs.DefaultOutputRoot + "/Imported";
            }

            string absRoot = AssetGenPaths.ToAbsolute(root);
            Directory.CreateDirectory(absRoot);

            string safe = SanitizeName(baseName);
            string fileName = safe + ext;
            string abs = Path.Combine(absRoot, fileName);
            int n = 1;
            while (File.Exists(abs)) { fileName = safe + "_" + n++ + ext; abs = Path.Combine(absRoot, fileName); }

            File.Copy(srcAbs, abs);
            return (root.TrimEnd('/') + "/" + fileName).Replace('\\', '/');
        }

        private static string SanitizeName(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "model";
            foreach (char c in Path.GetInvalidFileNameChars()) raw = raw.Replace(c, '_');
            return raw.Trim();
        }
    }
}
