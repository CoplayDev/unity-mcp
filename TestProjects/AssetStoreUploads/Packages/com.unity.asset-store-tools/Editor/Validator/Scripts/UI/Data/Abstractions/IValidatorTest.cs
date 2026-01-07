using AssetStoreTools.Validator.Data;

namespace AssetStoreTools.Validator.UI.Data
{
    internal interface IValidatorTest
    {
        int Id { get; }
        string Name { get; }
        string Description { get; }
        ValidationType ValidationType { get; }
        TestResult Result { get; }

        void SetResult(TestResult result);
    }
}