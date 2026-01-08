namespace AssetStoreTools.Validator.Services.Validation
{
    internal interface IFileSignatureUtilityService : IValidatorService
    {
        ArchiveType GetArchiveType(string filePath);
    }
}