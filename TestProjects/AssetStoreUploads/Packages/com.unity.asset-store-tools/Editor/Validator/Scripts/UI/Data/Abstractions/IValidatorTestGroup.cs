using AssetStoreTools.Validator.Data;
using System.Collections.Generic;

namespace AssetStoreTools.Validator.UI.Data
{
    internal interface IValidatorTestGroup
    {
        string Name { get; }
        TestResultStatus Status { get; }
        IEnumerable<IValidatorTest> Tests { get; }
    }
}