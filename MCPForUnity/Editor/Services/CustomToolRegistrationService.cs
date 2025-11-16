using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace MCPForUnity.Editor.Services
{
    public class CustomToolRegistrationService : ICustomToolRegistrationService
    {
        private static readonly HttpClient HttpClient = new HttpClient();
        private readonly IToolDiscoveryService _discoveryService;

        public CustomToolRegistrationService(IToolDiscoveryService discoveryService = null)
        {
            _discoveryService = discoveryService ?? new ToolDiscoveryService();
        }

        public async Task<bool> RegisterAllToolsAsync(string projectId = null)
        {
            try
            {
                projectId ??= GetProjectId();

                var tools = _discoveryService.DiscoverAllTools();
                if (tools.Count == 0)
                {
                    McpLog.Info("No tools found, skipping registration");
                    return true;
                }

                var candidates = tools.Where(t => t.AutoRegister).ToList();
                if (candidates.Count == 0)
                {
                    McpLog.Info("No tools marked for auto-registration, skipping");
                    return true;
                }

                var request = BuildRegisterRequest(projectId, candidates);
                string endpoint = HttpEndpointUtility.GetRegisterToolsUrl();
                var response = await SendRegistrationAsync(endpoint, request);

                if (response.success)
                {
                    McpLog.Info($"Successfully registered {response.registered?.Count ?? 0} tools with MCP server");
                    return true;
                }

                McpLog.Error($"Failed to register tools: {response.error ?? "Unknown error"}");
                return false;
            }
            catch (Exception ex)
            {
                McpLog.Error($"Error registering tools: {ex.Message}");
                return false;
            }
        }

        private RegisterToolsRequest BuildRegisterRequest(string projectId, List<ToolMetadata> tools)
        {
            return new RegisterToolsRequest
            {
                project_id = projectId,
                tools = tools.Select(t => new ToolDefinition
                {
                    name = t.Name,
                    description = t.Description,
                    structured_output = t.StructuredOutput,
                    parameters = (t.Parameters ?? new List<ParameterMetadata>()).Select(p => new ParameterDefinition
                    {
                        name = p.Name,
                        description = p.Description,
                        type = p.Type,
                        required = p.Required,
                        default_value = p.DefaultValue
                    }).ToList()
                }).ToList()
            };
        }

        private async Task<RegisterToolsResponse> SendRegistrationAsync(string endpoint, RegisterToolsRequest request)
        {
            try
            {
                string payload = JsonConvert.SerializeObject(request);
                using var content = new StringContent(payload, Encoding.UTF8, "application/json");
                var response = await HttpClient.PostAsync(endpoint, content);
                string responseText = await response.Content.ReadAsStringAsync();

                RegisterToolsResponse parsedResponse = null;
                try
                {
                    parsedResponse = JsonConvert.DeserializeObject<RegisterToolsResponse>(responseText);
                }
                catch (Exception ex)
                {
                    McpLog.Error($"Failed to parse tool registration response: {ex.Message}");
                }

                if (response.IsSuccessStatusCode)
                {
                    return parsedResponse ?? new RegisterToolsResponse { success = false, error = "Empty response from server" };
                }

                if (response.StatusCode == HttpStatusCode.Conflict)
                {
                    var duplicates = parsedResponse?.duplicates ?? new List<string>();
                    string duplicateList = duplicates.Count > 0 ? string.Join(", ", duplicates) : "existing tools";
                    McpLog.Info($"Tool registration skipped - already registered ({duplicateList})");
                    return new RegisterToolsResponse
                    {
                        success = true,
                        registered = duplicates
                    };
                }

                string errorText = parsedResponse?.error ?? responseText;
                McpLog.Error($"Tool registration failed: HTTP {(int)response.StatusCode} {response.ReasonPhrase} - {responseText}");
                return new RegisterToolsResponse
                {
                    success = false,
                    error = errorText
                };
            }
            catch (HttpRequestException ex)
            {
                McpLog.Error($"Tool registration HTTP request failed: {ex.Message}");
                return new RegisterToolsResponse { success = false, error = ex.Message };
            }
        }

        private string GetProjectId()
        {
            string projectName = Application.productName;
            string projectPath = Application.dataPath;
            string combined = $"{projectName}:{projectPath}";

            using (var sha256 = System.Security.Cryptography.SHA256.Create())
            {
                byte[] hash = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(combined));
                return BitConverter.ToString(hash).Replace("-", "").Substring(0, 16);
            }
        }

        [Serializable]
        private class RegisterToolsRequest
        {
            public string project_id;
            public List<ToolDefinition> tools;
        }

        [Serializable]
        private class ToolDefinition
        {
            public string name;
            public string description;
            public bool structured_output;
            public List<ParameterDefinition> parameters;
        }

        [Serializable]
        private class ParameterDefinition
        {
            public string name;
            public string description;
            public string type;
            public bool required;
            public string default_value;
        }

        private class RegisterToolsResponse
        {
            public bool success;
            public string error;
            public List<string> registered;
            public List<string> duplicates;
        }
    }
}
