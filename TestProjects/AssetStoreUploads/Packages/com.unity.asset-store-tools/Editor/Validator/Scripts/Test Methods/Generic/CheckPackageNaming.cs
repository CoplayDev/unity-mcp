using AssetStoreTools.Utility;
using AssetStoreTools.Validator.Data;
using AssetStoreTools.Validator.Services.Validation;
using AssetStoreTools.Validator.TestDefinitions;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace AssetStoreTools.Validator.TestMethods
{
    internal class CheckPackageNaming : ITestScript
    {
        private const string ForbiddenCharacters = "~`!@#$%^&*()-_=+[{]}\\|;:'\",<>?/";
        private readonly string[] PathsToCheckForForbiddenCharacters = new string[]
        {
            "Assets/",
            "Assets/Editor/",
            "Assets/Plugins/",
            "Assets/Resources/",
            "Assets/StreamingAssets/",
            "Assets/WebGLTemplates/"
        };

        private class PathCheckResult
        {
            public Object[] InvalidMainPaths;
            public Object[] InvalidMainPathContentPaths;
            public Object[] InvalidMainPathLeadingUpPaths;
            public Object[] InvalidHybridPackages;
            public Object[] PotentiallyInvalidContent;

            public bool HasIssues => InvalidMainPaths.Length > 0
                || InvalidMainPathContentPaths.Length > 0
                || InvalidMainPathLeadingUpPaths.Length > 0
                || InvalidHybridPackages.Length > 0
                || PotentiallyInvalidContent.Length > 0;
        }

        private GenericTestConfig _config;
        private IAssetUtilityService _assetUtility;

        // Constructor also accepts dependency injection of registered IValidatorService types
        public CheckPackageNaming(GenericTestConfig config, IAssetUtilityService assetUtility)
        {
            _config = config;
            _assetUtility = assetUtility;
        }

        public TestResult Run()
        {
            var result = new TestResult() { Status = TestResultStatus.Undefined };

            var checkResult = GetInvalidPathsInAssets();

            if (checkResult.HasIssues)
            {
                result.Status = TestResultStatus.Warning;

                if (checkResult.InvalidMainPaths.Length > 0)
                {
                    result.Status = TestResultStatus.VariableSeverityIssue;
                    result.AddMessage("The following assets appear to be artificially bumped up in the project hierarchy within commonly used folders", null, checkResult.InvalidMainPaths);
                }

                if (checkResult.InvalidMainPathContentPaths.Length > 0)
                {
                    result.Status = TestResultStatus.VariableSeverityIssue;
                    result.AddMessage("The following assets appear to be artificially bumped up in the project hierarchy within commonly used folders", null, checkResult.InvalidMainPathContentPaths);
                }

                if (checkResult.InvalidMainPathLeadingUpPaths.Length > 0)
                {
                    result.Status = TestResultStatus.VariableSeverityIssue;
                    result.AddMessage("Despite not being directly validated, this path would be automatically created by the Unity Importer when importing your package", null, checkResult.InvalidMainPathLeadingUpPaths);
                }

                if (checkResult.InvalidHybridPackages.Length > 0)
                {
                    result.Status = TestResultStatus.VariableSeverityIssue;
                    result.AddMessage("The following packages appear to be artificially bumped up in the Package hierarchy with their 'Display Name' configuration", null, checkResult.InvalidHybridPackages);
                }

                if (checkResult.PotentiallyInvalidContent.Length > 0)
                {
                    // Do not override previously set severities
                    result.AddMessage("It is recommended that nested package content refrains from starting with a special character", null, checkResult.PotentiallyInvalidContent);
                }
            }
            else
            {
                result.Status = TestResultStatus.Pass;
                result.AddMessage("All package asset names are valid!");
            }

            return result;
        }

        private PathCheckResult GetInvalidPathsInAssets()
        {
            var allInvalidMainPaths = new List<string>();
            var allInvalidMainContentPaths = new List<string>();
            var allInvalidMainLeadingUpPaths = new List<string>();
            var allInvalidPackagePaths = new List<string>();
            var allInvalidOtherContentPaths = new List<string>();

            foreach (var validationPath in _config.ValidationPaths)
            {
                // Is path itself not forbidden e.g.: when validating Assets/_Package, the folder _Package would be invalid
                if (!IsDirectMainPathValid(validationPath))
                    allInvalidMainPaths.Add(validationPath);

                // Are path contents not forbidden e.g.: when validating Assets/, the folder _Package would be invalid
                if (!IsDirectMainPathContentValid(validationPath, out var invalidContentPaths))
                    allInvalidMainContentPaths.AddRange(invalidContentPaths);

                // Is the path leading up to this path not forbidden e.g: when validating Assets/_WorkDir/Package, the folder _Workdir would be invalid
                if (!IsPathLeadingUpToMainPathValid(validationPath, out var invalidLeadingUpPath))
                    allInvalidMainLeadingUpPaths.Add(invalidLeadingUpPath);

                // Is the path pointing to a package valid, e.g.: when validating Packages/com.company.product its display name _Product would be invalid
                if (!IsHybridPackageMainPathValid(validationPath, out string invalidPackagePath))
                    allInvalidPackagePaths.Add(invalidPackagePath);
            }

            var ignoredPaths = new List<string>();
            ignoredPaths.AddRange(allInvalidMainPaths);
            ignoredPaths.AddRange(allInvalidMainContentPaths);
            ignoredPaths.AddRange(allInvalidMainLeadingUpPaths);
            ignoredPaths.AddRange(allInvalidPackagePaths);

            // Mark any other paths that start with a forbidden character
            if (!ArePackageContentsValid(ignoredPaths, out var invalidContents))
                allInvalidOtherContentPaths.AddRange(invalidContents);

            return new PathCheckResult()
            {
                InvalidMainPaths = PathsToObjects(allInvalidMainPaths),
                InvalidMainPathContentPaths = PathsToObjects(allInvalidMainContentPaths),
                InvalidMainPathLeadingUpPaths = PathsToObjects(allInvalidMainLeadingUpPaths),
                InvalidHybridPackages = PathsToObjects(allInvalidPackagePaths),
                PotentiallyInvalidContent = PathsToObjects(allInvalidOtherContentPaths)
            };
        }

        private bool IsDirectMainPathValid(string validationPath)
        {
            foreach (var forbiddenPath in PathsToCheckForForbiddenCharacters)
            {
                var forbiddenPathWithSeparator = forbiddenPath.EndsWith("/") ? forbiddenPath : forbiddenPath + "/";
                if (!validationPath.StartsWith(forbiddenPathWithSeparator))
                    continue;

                var truncatedPath = validationPath.Remove(0, forbiddenPathWithSeparator.Length);
                var truncatedPathSplit = truncatedPath.Split('/');

                // It is not a direct main path if it has deeper paths
                if (truncatedPathSplit.Length != 1)
                    continue;

                if (ForbiddenCharacters.Any(x => truncatedPath.StartsWith(x.ToString())))
                    return false;
            }

            return true;
        }

        private bool IsDirectMainPathContentValid(string validationPath, out List<string> invalidContentPaths)
        {
            invalidContentPaths = new List<string>();

            var contents = Directory.EnumerateFileSystemEntries(validationPath, "*", SearchOption.AllDirectories)
                .Where(x => !x.EndsWith(".meta"))
                .Select(GetAdbPath);

            foreach (var contentPath in contents)
            {
                foreach (var forbiddenPath in PathsToCheckForForbiddenCharacters)
                {
                    var forbiddenPathWithSeparator = forbiddenPath.EndsWith("/") ? forbiddenPath : forbiddenPath + "/";
                    if (!contentPath.StartsWith(forbiddenPathWithSeparator))
                        continue;

                    var truncatedPath = contentPath.Remove(0, forbiddenPathWithSeparator.Length);
                    var truncatedPathSplit = truncatedPath.Split('/');

                    // Only check the first level of content relative to the forbidden path
                    if (truncatedPathSplit.Length > 1)
                        continue;

                    if (ForbiddenCharacters.Any(x => truncatedPathSplit[0].StartsWith(x.ToString())))
                        invalidContentPaths.Add(contentPath);
                }
            }

            return invalidContentPaths.Count == 0;
        }

        private bool IsPathLeadingUpToMainPathValid(string validationPath, out string invalidLeadingUpPath)
        {
            invalidLeadingUpPath = string.Empty;

            foreach (var forbiddenPath in PathsToCheckForForbiddenCharacters)
            {
                var forbiddenPathWithSeparator = forbiddenPath.EndsWith("/") ? forbiddenPath : forbiddenPath + "/";
                if (!validationPath.StartsWith(forbiddenPathWithSeparator))
                    continue;

                var truncatedPath = validationPath.Remove(0, forbiddenPathWithSeparator.Length);
                var truncatedPathSplit = truncatedPath.Split('/');

                // It is not a leading up path if it has no deeper path
                if (truncatedPathSplit.Length == 1)
                    continue;

                if (ForbiddenCharacters.Any(x => truncatedPathSplit[0].StartsWith(x.ToString())))
                {
                    invalidLeadingUpPath = forbiddenPathWithSeparator + truncatedPathSplit[0];
                    return false;
                }
            }

            return true;
        }

        private bool IsHybridPackageMainPathValid(string validationPath, out string invalidPackagePath)
        {
            invalidPackagePath = string.Empty;

            if (!PackageUtility.GetPackageByManifestPath($"{validationPath}/package.json", out var package))
                return true;

            var packageName = package.displayName;
            if (ForbiddenCharacters.Any(x => packageName.StartsWith(x.ToString())))
            {
                invalidPackagePath = validationPath;
                return false;
            }

            return true;
        }

        private bool ArePackageContentsValid(IEnumerable<string> ignoredPaths, out List<string> invalidContentPaths)
        {
            invalidContentPaths = new List<string>();

            foreach (var validationPath in _config.ValidationPaths)
            {
                var validationPathFolderName = validationPath.Split('/').Last();
                if (!ignoredPaths.Contains(validationPath) && ForbiddenCharacters.Any(x => validationPathFolderName.StartsWith(x.ToString())))
                    invalidContentPaths.Add(validationPath);

                var contents = Directory.EnumerateFileSystemEntries(validationPath, "*", SearchOption.AllDirectories)
                    .Where(x => !x.EndsWith(".meta"))
                    .Select(GetAdbPath);

                foreach (var contentEntry in contents)
                {
                    if (ignoredPaths.Contains(contentEntry))
                        continue;

                    var entryName = contentEntry.Split('/').Last();
                    if (ForbiddenCharacters.Any(x => entryName.StartsWith(x.ToString())))
                        invalidContentPaths.Add(contentEntry);
                }
            }

            return invalidContentPaths.Count == 0;
        }

        private string GetAdbPath(string path)
        {
            path = path.Replace("\\", "/");
            if (path.StartsWith(Constants.RootProjectPath))
                path = path.Remove(Constants.RootProjectPath.Length);

            return path;
        }

        private Object[] PathsToObjects(IEnumerable<string> paths)
        {
            var objects = new List<Object>();

            foreach (var path in paths)
            {
                var obj = _assetUtility.AssetPathToObject(path);
                if (obj != null)
                    objects.Add(obj);
            }

            return objects.ToArray();
        }
    }
}