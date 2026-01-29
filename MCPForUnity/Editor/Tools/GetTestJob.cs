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

            bool includeDetails = ParamCoercion.CoerceBool(@params?["includeDetails"], false);
            bool includeFailedTests = ParamCoercion.CoerceBool(@params?["includeFailedTests"], false);

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
