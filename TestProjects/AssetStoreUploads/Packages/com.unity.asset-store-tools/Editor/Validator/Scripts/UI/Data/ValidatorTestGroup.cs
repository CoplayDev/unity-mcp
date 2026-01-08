using AssetStoreTools.Validator.Data;
using System.Collections.Generic;

namespace AssetStoreTools.Validator.UI.Data
{
    internal class ValidatorTestGroup : IValidatorTestGroup
    {
        public string Name => Status.ToString();
        public TestResultStatus Status { get; private set; }
        public IEnumerable<IValidatorTest> Tests { get; private set; }

        public ValidatorTestGroup(TestResultStatus status, IEnumerable<IValidatorTest> tests)
        {
            Status = status;
            Tests = tests;
        }
    }
}