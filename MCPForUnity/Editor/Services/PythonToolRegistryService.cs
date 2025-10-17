using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;
using MCPForUnity.Editor.Data;

namespace MCPForUnity.Editor.Services
{
    public class PythonToolRegistryService : IPythonToolRegistryService
    {
        public IEnumerable<PythonToolsAsset> GetAllRegistries()
        {
            // Find all PythonToolsAsset instances in the project
            string[] guids = AssetDatabase.FindAssets("t:PythonToolsAsset");
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<PythonToolsAsset>(path);
                if (asset != null)
                    yield return asset;
            }
        }

        public bool NeedsSync(PythonToolsAsset registry, TextAsset file)
        {
            if (!registry.useContentHashing) return true;

            string currentHash = ComputeHash(file);
            return registry.NeedsSync(file, currentHash);
        }

        public void RecordSync(PythonToolsAsset registry, TextAsset file)
        {
            string hash = ComputeHash(file);
            registry.RecordSync(file, hash);
            EditorUtility.SetDirty(registry);
        }

        public string ComputeHash(TextAsset file)
        {
            if (file == null || string.IsNullOrEmpty(file.text))
                return string.Empty;

            using (var sha256 = SHA256.Create())
            {
                byte[] bytes = System.Text.Encoding.UTF8.GetBytes(file.text);
                byte[] hash = sha256.ComputeHash(bytes);
                return BitConverter.ToString(hash).Replace("-", "").ToLower();
            }
        }
    }
}
