using AssetStoreTools.Utility;
using AssetStoreTools.Validator.Data;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using UnityEditor;
using UnityEngine;

namespace AssetStoreTools.Validator
{
    internal class ExternalProjectValidator : ValidatorBase
    {
        private ExternalProjectValidationSettings _settings;

        public ExternalProjectValidator(ExternalProjectValidationSettings settings) : base(settings)
        {
            _settings = settings;
        }

        protected override void ValidateSettings()
        {
            if (_settings == null)
                throw new Exception("Validation Settings is null");

            if (string.IsNullOrEmpty(_settings.PackagePath)
                || !File.Exists(_settings.PackagePath))
                throw new Exception("Package was not found");
        }

        protected override ValidationResult GenerateValidationResult()
        {
            bool interactiveMode = false;
            try
            {
                // Step 1 - prepare a temporary project
                var result = PrepareTemporaryValidationProject(interactiveMode);

                // If preparation was cancelled or setting up project failed - return immediately
                if (result.Status == ValidationStatus.Cancelled || result.Status == ValidationStatus.Failed)
                    return result;

                // Step 2 - load the temporary project and validate the package
                result = ValidateTemporaryValidationProject(result, interactiveMode);

                // Step 3 - copy validation results
                result = ParseValidationResult(result.ProjectPath);

                return result;
            }
            catch (Exception e)
            {
                return new ValidationResult() { Status = ValidationStatus.Failed, Exception = e };
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        private ValidationResult PrepareTemporaryValidationProject(bool interactiveMode)
        {
            EditorUtility.DisplayProgressBar("Validating...", "Preparing the validation project. This may take a while.", 0.3f);

            var result = new ValidationResult();
            var tempProjectPath = Path.Combine(Constants.RootProjectPath, "Temp", GUID.Generate().ToString()).Replace("\\", "/");
            result.ProjectPath = tempProjectPath;

            if (!Directory.Exists(tempProjectPath))
                Directory.CreateDirectory(tempProjectPath);

            // Cannot edit a package.json file that does not yet exist - copy over AST instead
            var tempPackagesPath = $"{tempProjectPath}/Packages";
            if (!Directory.Exists(tempPackagesPath))
                Directory.CreateDirectory(tempPackagesPath);
            var assetStoreToolsPath = PackageUtility.GetAllPackages().FirstOrDefault(x => x.name == "com.unity.asset-store-tools").resolvedPath.Replace("\\", "/");
            FileUtility.CopyDirectory(assetStoreToolsPath, $"{tempPackagesPath}/com.unity.asset-store-tools", true);

            var logFilePath = $"{tempProjectPath}/preparation.log";

            // Create the temporary project
            var processInfo = new System.Diagnostics.ProcessStartInfo()
            {
                FileName = Constants.UnityPath,
                Arguments = $"-createProject \"{tempProjectPath}\" -logFile \"{logFilePath}\" -importpackage \"{Path.GetFullPath(_settings.PackagePath)}\" -quit"
            };

            if (!interactiveMode)
                processInfo.Arguments += " -batchmode";

            var exitCode = 0;

            using (var process = System.Diagnostics.Process.Start(processInfo))
            {
                while (!process.HasExited)
                {
                    if (EditorUtility.DisplayCancelableProgressBar("Validating...", "Preparing the validation project. This may take a while.", 0.3f))
                        process.Kill();

                    Thread.Sleep(10);
                }

                exitCode = process.ExitCode;

                // Windows and MacOS exit codes
                if (exitCode == -1 || exitCode == 137)
                {
                    result.Status = ValidationStatus.Cancelled;
                    return result;
                }
            }

            if (exitCode != 0)
            {
                result.Status = ValidationStatus.Failed;
                result.Exception = new Exception($"Setting up the temporary project failed (exit code {exitCode})\n\nMore information can be found in the log file: {logFilePath}");
            }
            else
            {
                result.Status = ValidationStatus.RanToCompletion;
            }

            return result;
        }

        private ValidationResult ValidateTemporaryValidationProject(ValidationResult result, bool interactiveMode)
        {
            EditorUtility.DisplayProgressBar("Validating...", "Performing validation...", 0.6f);

            var logFilePath = $"{result.ProjectPath}/validation.log";
            var processInfo = new System.Diagnostics.ProcessStartInfo()
            {
                FileName = Constants.UnityPath,
                Arguments = $"-projectPath \"{result.ProjectPath}\" -logFile \"{logFilePath}\" -executeMethod AssetStoreTools.Validator.ExternalProjectValidator.ValidateProject -category \"{_settings.Category}\""
            };

            if (!interactiveMode)
                processInfo.Arguments += " -batchmode -ignorecompilererrors";

            var exitCode = 0;

            using (var process = System.Diagnostics.Process.Start(processInfo))
            {
                process.WaitForExit();
                exitCode = process.ExitCode;
            }

            if (exitCode != 0)
            {
                result.Status = ValidationStatus.Failed;
                result.Exception = new Exception($"Validating the temporary project failed (exit code {exitCode})\n\nMore information can be found in the log file: {logFilePath}");
            }
            else
            {
                result.Status = ValidationStatus.RanToCompletion;
            }

            return result;
        }

        private ValidationResult ParseValidationResult(string externalProjectPath)
        {
            if (!CachingService.GetCachedValidatorStateData(externalProjectPath, out var validationStateData))
                throw new Exception("Could not find external project's validation results");

            var cachedResult = validationStateData.GetResults();
            var cachedTestResults = cachedResult.GetResults();
            var tests = GetApplicableTests(ValidationType.Generic, ValidationType.UnityPackage);

            foreach (var test in tests)
            {
                if (!cachedTestResults.Any(x => x.Key == test.Id))
                    continue;

                var matchingTest = cachedTestResults.First(x => x.Key == test.Id);
                test.Result = matchingTest.Value;
            }

            var result = new ValidationResult()
            {
                Status = cachedResult.GetStatus(),
                HadCompilationErrors = cachedResult.GetHadCompilationErrors(),
                ProjectPath = cachedResult.GetProjectPath(),
                Tests = tests
            };

            return result;
        }

        public static void OpenExternalValidationProject(string projectPath)
        {
            var unityPath = Constants.UnityPath;
            var logFilePath = $"{projectPath}/editor.log";

            var processInfo = new System.Diagnostics.ProcessStartInfo()
            {
                FileName = unityPath,
                Arguments = $"-projectPath \"{projectPath}\" -logFile \"{logFilePath}\" -executeMethod AssetStoreTools.AssetStoreTools.ShowAssetStoreToolsValidator"
            };

            using (var process = System.Diagnostics.Process.Start(processInfo))
            {
                process.WaitForExit();
            }
        }

        // Invoked via Command Line Arguments
        private static void ValidateProject()
        {
            var exitCode = 0;
            try
            {
                // Determine whether to validate Assets folder or Packages folders
                var validationPaths = new List<string>();
                var packageDirectories = Directory.GetDirectories("Packages", "*", SearchOption.TopDirectoryOnly)
                    .Select(x => x.Replace("\\", "/"))
                    .Where(x => x != "Packages/com.unity.asset-store-tools").ToArray();

                if (packageDirectories.Length > 0)
                    validationPaths.AddRange(packageDirectories);
                else
                    validationPaths.Add("Assets");

                // Parse category
                var category = string.Empty;
                var args = Environment.GetCommandLineArgs().ToList();
                var categoryIndex = args.IndexOf("-category");
                if (categoryIndex != -1 && categoryIndex + 1 < args.Count)
                    category = args[categoryIndex + 1];

                // Run validation
                var validationSettings = new CurrentProjectValidationSettings()
                {
                    Category = category,
                    ValidationPaths = validationPaths,
                    ValidationType = ValidationType.UnityPackage
                };

                var validator = new CurrentProjectValidator(validationSettings);
                var result = validator.Validate();

                // Display results
                AssetStoreTools.ShowAssetStoreToolsValidator(validationSettings, result);
                EditorUtility.DisplayDialog("Validation complete", "Package validation complete.\n\nTo resume work in the original project, close this Editor instance", "OK");
            }
            catch
            {
                exitCode = 1;
                throw;
            }
            finally
            {
                if (Application.isBatchMode)
                    EditorApplication.Exit(exitCode);
            }
        }
    }
}
