using AssetStoreTools.Validator.Data;
using AssetStoreTools.Validator.TestDefinitions;
using Newtonsoft.Json;
using System.Collections.Generic;

namespace AssetStoreTools.Validator.UI.Data.Serialization
{
    internal class ValidatorStateResults
    {
        // Primary data
        [JsonProperty("validation_status")]
        private ValidationStatus _status;
        [JsonProperty("test_results")]
        private SortedDictionary<int, TestResult> _results;

        // Secondary data
        [JsonProperty("project_path")]
        private string _projectPath;
        [JsonProperty("had_compilation_errors")]
        private bool _hadCompilationErrors;

        public ValidatorStateResults()
        {
            _projectPath = string.Empty;
            _status = ValidationStatus.NotRun;
            _hadCompilationErrors = false;
            _results = new SortedDictionary<int, TestResult>();
        }

        public ValidationStatus GetStatus()
        {
            return _status;
        }

        public void SetStatus(ValidationStatus status)
        {
            if (_status == status)
                return;

            _status = status;
        }

        public SortedDictionary<int, TestResult> GetResults()
        {
            return _results;
        }

        public void SetResults(IEnumerable<AutomatedTest> tests)
        {
            _results.Clear();
            foreach (var test in tests)
            {
                _results.Add(test.Id, test.Result);
            }
        }

        public string GetProjectPath()
        {
            return _projectPath;
        }

        public void SetProjectPath(string projectPath)
        {
            if (_projectPath == projectPath)
                return;

            _projectPath = projectPath;
        }

        public bool GetHadCompilationErrors()
        {
            return _hadCompilationErrors;
        }

        public void SetHadCompilationErrors(bool hadCompilationErrors)
        {
            if (_hadCompilationErrors == hadCompilationErrors)
                return;

            _hadCompilationErrors = hadCompilationErrors;
        }
    }
}