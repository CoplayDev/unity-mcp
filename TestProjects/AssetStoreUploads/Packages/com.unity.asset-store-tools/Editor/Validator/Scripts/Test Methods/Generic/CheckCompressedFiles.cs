using AssetStoreTools.Validator.Data;
using AssetStoreTools.Validator.Services.Validation;
using AssetStoreTools.Validator.TestDefinitions;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityObject = UnityEngine.Object;

namespace AssetStoreTools.Validator.TestMethods
{
    internal class CheckCompressedFiles : ITestScript
    {
        private enum ArchiveResult
        {
            Allowed,
            NotAllowed,
            TarGzWithIssues,
            ZipWithIssues
        }

        private GenericTestConfig _config;
        private IAssetUtilityService _assetUtility;
        private IFileSignatureUtilityService _fileSignatureUtility;

        public CheckCompressedFiles(GenericTestConfig config, IAssetUtilityService assetUtility, IFileSignatureUtilityService fileSignatureUtility)
        {
            _config = config;
            _assetUtility = assetUtility;
            _fileSignatureUtility = fileSignatureUtility;
        }

        public TestResult Run()
        {
            var result = new TestResult() { Status = TestResultStatus.Undefined };

            var checkedArchives = new Dictionary<UnityObject, ArchiveResult>();

            // Retrieving assets via GetObjectsFromAssets() is insufficient because
            // archives might be renamed and not use the expected extension
            var allAssetPaths = _assetUtility.GetAssetPathsFromAssets(_config.ValidationPaths, AssetType.All);

            foreach (var assetPath in allAssetPaths)
            {
                ArchiveType archiveType;
                if ((archiveType = _fileSignatureUtility.GetArchiveType(assetPath)) == ArchiveType.None)
                    continue;

                var archiveObj = _assetUtility.AssetPathToObject(assetPath);

                switch (archiveType)
                {
                    case ArchiveType.TarGz:
                        if (assetPath.ToLower().EndsWith(".unitypackage"))
                            checkedArchives.Add(archiveObj, ArchiveResult.Allowed);
                        else
                            checkedArchives.Add(archiveObj, ArchiveResult.TarGzWithIssues);
                        break;
                    case ArchiveType.Zip:
                        if (FileNameContainsKeyword(assetPath, "source") && assetPath.ToLower().EndsWith(".zip"))
                            checkedArchives.Add(archiveObj, ArchiveResult.Allowed);
                        else
                            checkedArchives.Add(archiveObj, ArchiveResult.ZipWithIssues);
                        break;
                    default:
                        checkedArchives.Add(archiveObj, ArchiveResult.NotAllowed);
                        break;
                }
            }

            if (checkedArchives.Count == 0)
            {
                result.Status = TestResultStatus.Pass;
                result.AddMessage("No archives were found in the package content!");
                return result;
            }

            if (checkedArchives.Any(x => x.Value == ArchiveResult.Allowed))
            {
                result.Status = TestResultStatus.Warning;
                result.AddMessage("The following archives of allowed format were found in the package content.\n" +
                    "Please make sure they adhere to the nested archive guidelines:", null,
                    checkedArchives.Where(x => x.Value == ArchiveResult.Allowed).Select(x => x.Key).ToArray());
            }

            if (checkedArchives.Any(x => x.Value == ArchiveResult.TarGzWithIssues))
            {
                result.Status = TestResultStatus.VariableSeverityIssue;
                result.AddMessage("The following .gz archives were found in the package content.\n" +
                    "• Gz archives are only allowed in form of '.unitypackage' files", null,
                    checkedArchives.Where(x => x.Value == ArchiveResult.TarGzWithIssues).Select(x => x.Key).ToArray());
            }

            if (checkedArchives.Any(x => x.Value == ArchiveResult.ZipWithIssues))
            {
                result.Status = TestResultStatus.VariableSeverityIssue;
                result.AddMessage("The following .zip archives were found in the package content.\n" +
                    "• Zip archives should contain the keyword 'source' in the file name", null,
                    checkedArchives.Where(x => x.Value == ArchiveResult.ZipWithIssues).Select(x => x.Key).ToArray());
            }

            if (checkedArchives.Any(x => x.Value == ArchiveResult.NotAllowed))
            {
                result.Status = TestResultStatus.VariableSeverityIssue;
                result.AddMessage("The following archives are using formats that are not allowed:", null,
                    checkedArchives.Where(x => x.Value == ArchiveResult.NotAllowed).Select(x => x.Key).ToArray());
            }

            return result;
        }

        private bool FileNameContainsKeyword(string filePath, string keyword)
        {
            var fileInfo = new FileInfo(filePath);

            if (!fileInfo.Exists)
                return false;

            return fileInfo.Name.Remove(fileInfo.Name.Length - fileInfo.Extension.Length).ToLower().Contains(keyword.ToLower());
        }
    }
}
