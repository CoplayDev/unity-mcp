using System.Collections.Generic;
using UnityEngine;

namespace AssetStoreTools.Validator.Services.Validation
{
    internal interface IModelUtilityService : IValidatorService
    {
        Dictionary<Object, List<LogEntry>> GetImportLogs(params Object[] models);
    }
}