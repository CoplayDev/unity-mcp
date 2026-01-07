using System.Collections.Generic;
using UnityEngine;

namespace AssetStoreTools.Validator.Services.Validation
{
    internal interface IMeshUtilityService : IValidatorService
    {
        IEnumerable<Mesh> GetCustomMeshesInObject(GameObject obj);
    }
}