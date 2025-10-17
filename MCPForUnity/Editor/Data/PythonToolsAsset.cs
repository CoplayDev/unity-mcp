using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MCPForUnity.Editor.Data
{
    /// <summary>
    /// Registry of Python tool files to sync to the MCP server.
    /// Add your Python files here - they can be stored anywhere in your project.
    /// </summary>
    [CreateAssetMenu(fileName = "PythonTools", menuName = "MCP For Unity/Python Tools")]
    public class PythonToolsAsset : ScriptableObject
    {
        [Header("Python Tool Files")]
        [Tooltip("Add Python files (.py) to sync to the MCP server. Files can be located anywhere in your project.")]
        public List<TextAsset> pythonFiles = new List<TextAsset>();

        [Header("Sync Options")]
        [Tooltip("Use content hashing to detect changes (recommended). If false, always copies on startup.")]
        public bool useContentHashing = true;

        [Header("Sync State (Read-only)")]
        [Tooltip("Internal tracking - do not modify")]
        public List<PythonFileState> fileStates = new List<PythonFileState>();

        /// <summary>
        /// Gets all valid Python files (filters out null/missing references)
        /// </summary>
        public IEnumerable<TextAsset> GetValidFiles()
        {
            return pythonFiles.Where(f => f != null);
        }

        /// <summary>
        /// Checks if a file needs syncing
        /// </summary>
        public bool NeedsSync(TextAsset file, string currentHash)
        {
            if (!useContentHashing) return true; // Always sync if hashing disabled

            var state = fileStates.FirstOrDefault(s => s.assetGuid == GetAssetGuid(file));
            return state == null || state.contentHash != currentHash;
        }

        /// <summary>
        /// Records that a file was synced
        /// </summary>
        public void RecordSync(TextAsset file, string hash)
        {
            string guid = GetAssetGuid(file);
            var state = fileStates.FirstOrDefault(s => s.assetGuid == guid);

            if (state == null)
            {
                state = new PythonFileState { assetGuid = guid };
                fileStates.Add(state);
            }

            state.contentHash = hash;
            state.lastSyncTime = DateTime.UtcNow;
            state.fileName = file.name;
        }

        /// <summary>
        /// Removes state entries for files no longer in the list
        /// </summary>
        public void CleanupStaleStates()
        {
            var validGuids = new HashSet<string>(GetValidFiles().Select(GetAssetGuid));
            fileStates.RemoveAll(s => !validGuids.Contains(s.assetGuid));
        }

        private string GetAssetGuid(TextAsset asset)
        {
            return UnityEditor.AssetDatabase.AssetPathToGUID(UnityEditor.AssetDatabase.GetAssetPath(asset));
        }
    }

    [Serializable]
    public class PythonFileState
    {
        public string assetGuid;
        public string fileName;
        public string contentHash;
        public DateTime lastSyncTime;
    }
}