using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEditor.TestTools.TestRunner.Api;

namespace MCPForUnity.Editor.Services
{
    /// <summary>
    /// Provides access to Unity Test Runner data and execution.
    /// </summary>
    public interface ITestRunnerService
    {
        /// <summary>
        /// Retrieve the list of tests for the requested mode(s).
        /// When <paramref name="mode"/> is null, tests for both EditMode and PlayMode are returned.
        /// </summary>
        Task<IReadOnlyList<Dictionary<string, string>>> GetTestsAsync(TestMode? mode);

        /// <summary>
        /// Execute tests for the supplied mode.
        /// </summary>
        Task<TestRunResult> RunTestsAsync(TestMode mode);
    }
}
