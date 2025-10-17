using System;

namespace MCPForUnity.Editor.Services
{
    /// <summary>
    /// Service locator for accessing MCP services without dependency injection
    /// </summary>
    public static class MCPServiceLocator
    {
        private static IBridgeControlService _bridgeService;
        private static IClientConfigurationService _clientService;
        private static IPathResolverService _pathService;
        private static ITestRunnerService _testRunnerService;

        /// <summary>
        /// Gets the bridge control service
        /// </summary>
        public static IBridgeControlService Bridge => _bridgeService ??= new BridgeControlService();

        /// <summary>
        /// Gets the client configuration service
        /// </summary>
        public static IClientConfigurationService Client => _clientService ??= new ClientConfigurationService();

        /// <summary>
        /// Gets the path resolver service
        /// </summary>
        public static IPathResolverService Paths => _pathService ??= new PathResolverService();

        /// <summary>
        /// Gets the Unity test runner service
        /// </summary>
        public static ITestRunnerService Tests => _testRunnerService ??= new TestRunnerService();

        /// <summary>
        /// Registers a custom implementation for a service (useful for testing)
        /// </summary>
        /// <typeparam name="T">The service interface type</typeparam>
        /// <param name="implementation">The implementation to register</param>
        public static void Register<T>(T implementation) where T : class
        {
            if (implementation is IBridgeControlService b)
                _bridgeService = b;
            else if (implementation is IClientConfigurationService c)
                _clientService = c;
            else if (implementation is IPathResolverService p)
                _pathService = p;
            else if (implementation is ITestRunnerService t)
                _testRunnerService = t;
        }

        /// <summary>
        /// Resets all services to their default implementations (useful for testing)
        /// </summary>
        public static void Reset()
        {
            (_bridgeService as IDisposable)?.Dispose();
            (_clientService as IDisposable)?.Dispose();
            (_pathService as IDisposable)?.Dispose();
            (_testRunnerService as IDisposable)?.Dispose();

            _bridgeService = null;
            _clientService = null;
            _pathService = null;
            _testRunnerService = null;
        }
    }
}
