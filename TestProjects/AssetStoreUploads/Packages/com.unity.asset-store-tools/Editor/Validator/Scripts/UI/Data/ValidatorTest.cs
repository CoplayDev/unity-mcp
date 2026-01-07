using AssetStoreTools.Validator.Data;
using AssetStoreTools.Validator.TestDefinitions;

namespace AssetStoreTools.Validator.UI.Data
{
    internal class ValidatorTest : IValidatorTest
    {
        public int Id { get; private set; }
        public string Name { get; private set; }
        public string Description { get; private set; }
        public ValidationType ValidationType { get; private set; }
        public TestResult Result { get; private set; }

        public ValidatorTest(AutomatedTest source)
        {
            Id = source.Id;
            Name = source.Title;
            Description = source.Description;
            ValidationType = source.ValidationType;
            Result = source.Result;
        }

        public void SetResult(TestResult result)
        {
            Result = result;
        }
    }
}