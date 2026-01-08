using AssetStoreTools.Validator.Data;
using AssetStoreTools.Validator.TestDefinitions;
using UnityEditor;

namespace AssetStoreTools.Validator.TestMethods
{
    internal class CheckScriptCompilation : ITestScript
    {
        public TestResult Run()
        {
            var result = new TestResult() { Status = TestResultStatus.Undefined };

            var hasCompilationErrors = EditorUtility.scriptCompilationFailed;

            if (hasCompilationErrors)
            {
                result.Status = TestResultStatus.VariableSeverityIssue;
                result.AddMessage("One or more scripts in the project failed to compile.\n" +
                    "Please check the Console window to see the list of errors");
            }
            else
            {
                result.Status = TestResultStatus.Pass;
                result.AddMessage("All scripts in the project compiled successfully!");
            }

            return result;
        }
    }
}
