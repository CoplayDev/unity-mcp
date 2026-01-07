using AssetStoreTools.Validator.Data;

namespace AssetStoreTools.Validator
{
    internal interface IValidator
    {
        ValidationSettings Settings { get; }

        ValidationResult Validate();
    }
}