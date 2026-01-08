using System;
using System.Collections;
using System.IO;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace MCPForUnityTests.Editor
{
    /// <summary>
    /// Shared test utilities for EditMode tests across the MCP for Unity test suite.
    /// Consolidates common patterns to avoid duplication across test files.
    /// </summary>
    public static class TestUtilities
    {
        /// <summary>
        /// Safely converts a command result to JObject, handling both JSON objects and other types.
        /// Returns an empty JObject if result is null.
        /// </summary>
        public static JObject ToJObject(object result)
        {
            if (result == null) return new JObject();
            return result as JObject ?? JObject.FromObject(result);
        }

        /// <summary>
        /// Creates all parent directories for the given asset path if they don't exist.
        /// Handles normalization and validates against dangerous patterns.
        /// </summary>
        /// <param name="folderPath">An Assets-relative folder path (e.g., "Assets/Temp/MyFolder")</param>
        public static void EnsureFolder(string folderPath)
        {
            if (AssetDatabase.IsValidFolder(folderPath))
                return;

            var sanitized = MCPForUnity.Editor.Helpers.AssetPathUtility.SanitizeAssetPath(folderPath);
            if (string.Equals(sanitized, "Assets", StringComparison.OrdinalIgnoreCase))
                return;

            var parts = sanitized.Split('/');
            string current = "Assets";
            for (int i = 1; i < parts.Length; i++)
            {
                var next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }
                current = next;
            }
        }

        /// <summary>
        /// Waits for Unity to finish compiling and updating, with a configurable timeout.
        /// Some EditMode tests trigger script compilation/domain reload. 
        /// Tools intentionally return "compiling_or_reloading" during these windows.
        /// </summary>
        /// <param name="timeoutSeconds">Maximum time to wait before failing the test.</param>
        public static IEnumerator WaitForUnityReady(double timeoutSeconds = 30.0)
        {
            double start = EditorApplication.timeSinceStartup;
            while (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                if (EditorApplication.timeSinceStartup - start > timeoutSeconds)
                {
                    Assert.Fail($"Timed out waiting for Unity to finish compiling/updating (>{timeoutSeconds:0.0}s).");
                }
                yield return null;
            }
        }

        /// <summary>
        /// Finds a fallback shader for creating materials in tests.
        /// Tries modern pipelines first, then falls back to Standard/Unlit.
        /// </summary>
        /// <returns>A shader suitable for test materials, or null if none found.</returns>
        public static Shader FindFallbackShader()
        {
            return Shader.Find("Universal Render Pipeline/Lit")
                ?? Shader.Find("HDRP/Lit")
                ?? Shader.Find("Standard")
                ?? Shader.Find("Unlit/Color");
        }

        /// <summary>
        /// Safely deletes an asset if it exists.
        /// </summary>
        /// <param name="path">The asset path to delete.</param>
        public static void SafeDeleteAsset(string path)
        {
            if (!string.IsNullOrEmpty(path) && AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path) != null)
            {
                AssetDatabase.DeleteAsset(path);
            }
        }

        /// <summary>
        /// Cleans up empty parent folders recursively up to but not including "Assets".
        /// Useful in TearDown to avoid leaving folder debris.
        /// </summary>
        /// <param name="folderPath">The starting folder path to check.</param>
        public static void CleanupEmptyParentFolders(string folderPath)
        {
            if (string.IsNullOrEmpty(folderPath))
                return;

            var parent = Path.GetDirectoryName(folderPath)?.Replace('\\', '/');
            while (!string.IsNullOrEmpty(parent) && parent != "Assets")
            {
                if (AssetDatabase.IsValidFolder(parent))
                {
                    try
                    {
                        var dirs = Directory.GetDirectories(parent);
                        var files = Directory.GetFiles(parent);
                        if (dirs.Length == 0 && files.Length == 0)
                        {
                            AssetDatabase.DeleteAsset(parent);
                            parent = Path.GetDirectoryName(parent)?.Replace('\\', '/');
                        }
                        else
                        {
                            break;
                        }
                    }
                    catch
                    {
                        break;
                    }
                }
                else
                {
                    break;
                }
            }
        }
    }
}

