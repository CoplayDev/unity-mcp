using AssetStoreTools.Validator.Categories;
using AssetStoreTools.Validator.Data;
using AssetStoreTools.Validator.Services;
using AssetStoreTools.Validator.TestDefinitions;
using AssetStoreTools.Validator.Utility;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace AssetStoreTools.Validator
{
    internal abstract class ValidatorBase : IValidator
    {
        public ValidationSettings Settings { get; private set; }

        private CategoryEvaluator _categoryEvaluator;
        private List<AutomatedTest> _automatedTests;

        protected ICachingService CachingService;

        public ValidatorBase(ValidationSettings settings)
        {
            Settings = settings;
            _categoryEvaluator = new CategoryEvaluator(settings?.Category);

            CachingService = ValidatorServiceProvider.Instance.GetService<ICachingService>();

            CreateAutomatedTestCases();
        }

        private void CreateAutomatedTestCases()
        {
            var testData = ValidatorUtility.GetAutomatedTestCases(ValidatorUtility.SortType.Alphabetical);
            _automatedTests = new List<AutomatedTest>();

            foreach (var t in testData)
            {
                var test = new AutomatedTest(t);
                _automatedTests.Add(test);
            }
        }

        protected abstract void ValidateSettings();
        protected abstract ValidationResult GenerateValidationResult();

        public ValidationResult Validate()
        {
            try
            {
                ValidateSettings();
            }
            catch (Exception e)
            {
                return new ValidationResult() { Status = ValidationStatus.Failed, Exception = e };
            }

            var result = GenerateValidationResult();
            return result;
        }

        protected List<AutomatedTest> GetApplicableTests(params ValidationType[] validationTypes)
        {
            return _automatedTests.Where(x => validationTypes.Any(y => y == x.ValidationType)).ToList();
        }

        protected ValidationResult RunTests(List<AutomatedTest> tests, ITestConfig config)
        {
            var completedTests = new List<AutomatedTest>();

            for (int i = 0; i < tests.Count; i++)
            {
                var test = tests[i];

                EditorUtility.DisplayProgressBar("Validating", $"Running validation: {i + 1} - {test.Title}", (float)i / _automatedTests.Count);

                test.Run(config);

                // Adjust result based on categories
                var updatedStatus = _categoryEvaluator.Evaluate(test);
                test.Result.Status = updatedStatus;

                // Add the result
                completedTests.Add(test);

#if AB_BUILDER
                EditorUtility.UnloadUnusedAssetsImmediate();
#endif
            }

            EditorUtility.UnloadUnusedAssetsImmediate();
            EditorUtility.ClearProgressBar();

            var projectPath = Application.dataPath.Substring(0, Application.dataPath.Length - "/Assets".Length);
            var hasCompilationErrors = EditorUtility.scriptCompilationFailed;
            var result = new ValidationResult()
            {
                Status = ValidationStatus.RanToCompletion,
                Tests = completedTests,
                ProjectPath = projectPath,
                HadCompilationErrors = hasCompilationErrors
            };

            return result;
        }
    }
}