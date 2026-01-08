using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace AssetStoreTools.Validator.Services.Validation
{
    internal interface IAssetUtilityService : IValidatorService
    {
        IEnumerable<string> GetAssetPathsFromAssets(string[] searchPaths, AssetType type);
        IEnumerable<T> GetObjectsFromAssets<T>(string[] searchPaths, AssetType type) where T : Object;
        IEnumerable<Object> GetObjectsFromAssets(string[] searchPaths, AssetType type);
        string ObjectToAssetPath(Object obj);
        T AssetPathToObject<T>(string assetPath) where T : Object;
        Object AssetPathToObject(string assetPath);
        AssetImporter GetAssetImporter(string assetPath);
        AssetImporter GetAssetImporter(Object asset);
    }
}