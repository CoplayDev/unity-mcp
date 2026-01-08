using System;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Services;
using Newtonsoft.Json.Linq;

namespace MCPForUnity.Editor.Tools
{
    /// <summary>
    /// Poll a previously started async test job by job_id.
    /// </summary>
    [McpForUnityTool("get_test_job", AutoRegister = false)]
    public static class GetTestJob
    {
        public static object HandleCommand(JObject @params)
        {
            string jobId = @params?["job_id"]?.ToString() ?? @params?["jobId"]?.ToString();
            if (string.IsNullOrWhiteSpace(jobId))
            {
                return new ErrorResponse("Missing required parameter 'job_id'.");
            }

            bool includeDetails = false;
            bool includeFailedTests = false;
            try
            {
                var includeDetailsToken = @params?["includeDetails"];
                if (includeDetailsToken != null && bool.TryParse(includeDetailsToken.ToString(), out var parsedIncludeDetails))
                {
                    includeDetails = parsedIncludeDetails;
                }
                var includeFailedTestsToken = @params?["includeFailedTests"];
                if (includeFailedTestsToken != null && bool.TryParse(includeFailedTestsToken.ToString(), out var parsedIncludeFailedTests))
                {
                    includeFailedTests = parsedIncludeFailedTests;
                }
            }
            catch
            {
                // ignore parse failures
            }

            var job = TestJobManager.GetJob(jobId);
            if (job == null)
            {
                return new ErrorResponse("Unknown job_id.");
            }

            var payload = TestJobManager.ToSerializable(job, includeDetails, includeFailedTests);
            return new SuccessResponse("Test job status retrieved.", payload);
        }
    }
}
