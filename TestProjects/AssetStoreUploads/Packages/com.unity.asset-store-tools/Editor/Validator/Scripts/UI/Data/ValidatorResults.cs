using AssetStoreTools.Validator.Data;
using AssetStoreTools.Validator.TestDefinitions;
using AssetStoreTools.Validator.UI.Data.Serialization;
using AssetStoreTools.Validator.Utility;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AssetStoreTools.Validator.UI.Data
{
    internal class ValidatorResults : IValidatorResults
    {
        private ValidatorStateResults _stateData;

        private IValidatorSettings _settings;
        private IEnumerable<IValidatorTest> _tests;

        private readonly TestResultStatus[] _priorityGroups = new TestResultStatus[]
        {
            TestResultStatus.Undefined,
            TestResultStatus.Fail,
            TestResultStatus.Warning
        };

        public event Action OnResultsChanged;
        public event Action OnRequireSerialize;

        public ValidatorResults(IValidatorSettings settings, ValidatorStateResults stateData)
        {
            _settings = settings;
            _stateData = stateData;

            _tests = GetAllTests();

            Deserialize();
        }

        private IEnumerable<IValidatorTest> GetAllTests()
        {
            var tests = new List<IValidatorTest>();
            var testObjects = ValidatorUtility.GetAutomatedTestCases(ValidatorUtility.SortType.Alphabetical);

            foreach (var testObject in testObjects)
            {
                var testSource = new AutomatedTest(testObject);
                var test = new ValidatorTest(testSource);
                tests.Add(test);
            }

            return tests;
        }

        public void LoadResult(ValidationResult result)
        {
            if (result == null)
                return;

            foreach (var test in _tests)
            {
                if (!result.Tests.Any(x => x.Id == test.Id))
                    continue;

                var matchingResult = result.Tests.First(x => x.Id == test.Id);
                test.SetResult(matchingResult.Result);
            }

            OnResultsChanged?.Invoke();

            Serialize(result);
        }

        public IEnumerable<IValidatorTestGroup> GetSortedTestGroups()
        {
            var groups = new List<IValidatorTestGroup>();
            var testsByStatus = _tests
                .Where(x => x.ValidationType == ValidationType.Generic || x.ValidationType == _settings.GetValidationType())
                .GroupBy(x => x.Result.Status).ToDictionary(x => x.Key, x => x.ToList());

            foreach (var kvp in testsByStatus)
            {
                var group = new ValidatorTestGroup(kvp.Key, kvp.Value);
                groups.Add(group);
            }

            return SortGroups(groups);
        }

        private IEnumerable<IValidatorTestGroup> SortGroups(IEnumerable<IValidatorTestGroup> unsortedGroups)
        {
            var sortedGroups = new List<IValidatorTestGroup>();
            var groups = unsortedGroups.OrderBy(x => x.Status).ToList();

            // Select priority groups first
            foreach (var priority in _priorityGroups)
            {
                var priorityGroup = groups.FirstOrDefault(x => x.Status == priority);
                if (priorityGroup == null)
                    continue;

                sortedGroups.Add(priorityGroup);
                groups.Remove(priorityGroup);
            }

            // Add the rest
            sortedGroups.AddRange(groups);

            return sortedGroups;
        }

        private void Serialize(ValidationResult result)
        {
            _stateData.SetStatus(result.Status);
            _stateData.SetResults(result.Tests);
            _stateData.SetProjectPath(result.ProjectPath);
            _stateData.SetHadCompilationErrors(result.HadCompilationErrors);
            OnRequireSerialize?.Invoke();
        }

        private void Deserialize()
        {
            if (_stateData == null)
                return;

            var serializedResults = _stateData.GetResults();
            foreach (var test in _tests)
            {
                if (!serializedResults.Any(x => x.Key == test.Id))
                    continue;

                var matchingResult = serializedResults.First(x => x.Key == test.Id);
                test.SetResult(matchingResult.Value);
            }

            OnResultsChanged?.Invoke();
        }
    }
}