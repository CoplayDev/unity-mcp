using System;
using System.Collections.Generic;
using UnityEditor;
using Object = UnityEngine.Object;

namespace AssetStoreTools.Validator.Services.Validation
{
    internal interface IScriptUtilityService : IValidatorService
    {
        IReadOnlyDictionary<MonoScript, IList<(string Name, string Namespace)>> GetTypeNamespacesFromScriptAssets(IList<MonoScript> monoScripts);
        IReadOnlyDictionary<Object, IList<Type>> GetTypesFromAssemblies(IList<Object> assemblies);
        IReadOnlyDictionary<MonoScript, IList<Type>> GetTypesFromScriptAssets(IList<MonoScript> monoScripts);
    }
}