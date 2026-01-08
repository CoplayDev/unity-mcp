using AssetStoreTools.Validator.TestDefinitions;
using System;
using System.Collections.Generic;

namespace AssetStoreTools.Validator.Data
{
    internal class ValidationResult
    {
        public ValidationStatus Status;
        public bool HadCompilationErrors;
        public string ProjectPath;
        public List<AutomatedTest> Tests;
        public Exception Exception;

        public ValidationResult()
        {
            Status = ValidationStatus.NotRun;
            HadCompilationErrors = false;
            ProjectPath = string.Empty;
            Tests = new List<AutomatedTest>();
            Exception = null;
        }
    }
}