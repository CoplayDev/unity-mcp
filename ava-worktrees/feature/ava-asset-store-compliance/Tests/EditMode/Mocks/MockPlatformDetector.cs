using MCPForUnity.Editor.Dependencies.Models;
using MCPForUnity.Editor.Dependencies.PlatformDetectors;

namespace MCPForUnity.Tests.Mocks
{
    /// <summary>
    /// Mock platform detector for testing purposes
    /// </summary>
    public class MockPlatformDetector : IPlatformDetector
    {
        private bool _pythonAvailable = false;
        private string _pythonVersion = "";
        private string _pythonPath = "";
        private string _pythonError = "";

        private bool _uvAvailable = false;
        private string _uvVersion = "";
        private string _uvPath = "";
        private string _uvError = "";

        private bool _mcpServerAvailable = false;
        private string _mcpServerPath = "";
        private string _mcpServerError = "";

        public string PlatformName => "Mock Platform";
        public bool CanDetect => true;

        public void SetPythonAvailable(bool available, string version = "", string path = "", string error = "")
        {
            _pythonAvailable = available;
            _pythonVersion = version;
            _pythonPath = path;
            _pythonError = error;
        }

        public void SetUVAvailable(bool available, string version = "", string path = "", string error = "")
        {
            _uvAvailable = available;
            _uvVersion = version;
            _uvPath = path;
            _uvError = error;
        }

        public void SetMCPServerAvailable(bool available, string path = "", string error = "")
        {
            _mcpServerAvailable = available;
            _mcpServerPath = path;
            _mcpServerError = error;
        }

        public DependencyStatus DetectPython()
        {
            return new DependencyStatus
            {
                Name = "Python",
                IsAvailable = _pythonAvailable,
                IsRequired = true,
                Version = _pythonVersion,
                Path = _pythonPath,
                ErrorMessage = _pythonError,
                Details = _pythonAvailable ? "Mock Python detected" : "Mock Python not found"
            };
        }

        public DependencyStatus DetectUV()
        {
            return new DependencyStatus
            {
                Name = "UV Package Manager",
                IsAvailable = _uvAvailable,
                IsRequired = true,
                Version = _uvVersion,
                Path = _uvPath,
                ErrorMessage = _uvError,
                Details = _uvAvailable ? "Mock UV detected" : "Mock UV not found"
            };
        }

        public DependencyStatus DetectMCPServer()
        {
            return new DependencyStatus
            {
                Name = "MCP Server",
                IsAvailable = _mcpServerAvailable,
                IsRequired = false,
                Path = _mcpServerPath,
                ErrorMessage = _mcpServerError,
                Details = _mcpServerAvailable ? "Mock MCP Server detected" : "Mock MCP Server not found"
            };
        }

        public string GetInstallationRecommendations()
        {
            return "Mock installation recommendations for testing";
        }

        public string GetPythonInstallUrl()
        {
            return "https://mock-python-install.com";
        }

        public string GetUVInstallUrl()
        {
            return "https://mock-uv-install.com";
        }
    }
}