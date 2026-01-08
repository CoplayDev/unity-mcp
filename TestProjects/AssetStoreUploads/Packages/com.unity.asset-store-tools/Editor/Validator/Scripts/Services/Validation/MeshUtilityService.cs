using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace AssetStoreTools.Validator.Services.Validation
{
    internal class MeshUtilityService : IMeshUtilityService
    {
        public IEnumerable<Mesh> GetCustomMeshesInObject(GameObject obj)
        {
            var meshes = new List<Mesh>();

            var meshFilters = obj.GetComponentsInChildren<MeshFilter>(true);
            var skinnedMeshes = obj.GetComponentsInChildren<SkinnedMeshRenderer>(true);

            meshes.AddRange(meshFilters.Select(m => m.sharedMesh));
            meshes.AddRange(skinnedMeshes.Select(m => m.sharedMesh));

            meshes = meshes.Where(m => AssetDatabase.GetAssetPath(m).StartsWith("Assets/") ||
            AssetDatabase.GetAssetPath(m).StartsWith("Packages/")).ToList();

            return meshes;
        }
    }
}