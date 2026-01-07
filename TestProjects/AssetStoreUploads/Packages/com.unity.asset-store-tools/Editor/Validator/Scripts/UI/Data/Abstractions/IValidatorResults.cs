using AssetStoreTools.Validator.Data;
using System;
using System.Collections.Generic;

namespace AssetStoreTools.Validator.UI.Data
{
    internal interface IValidatorResults
    {
        event Action OnResultsChanged;
        event Action OnRequireSerialize;

        void LoadResult(ValidationResult result);
        IEnumerable<IValidatorTestGroup> GetSortedTestGroups();
    }
}