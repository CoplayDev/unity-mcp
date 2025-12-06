using System;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Services.Transport;
using MCPForUnity.Editor.Services.Transport.Transports;

namespace MCPForUnity.Editor.Services
{
    /// <summary>
    /// Service locator for accessing MCP services without dependency injection
    /// </summary>
    public static class MCPServiceLocator
    {
        private static IBridgeControlService _bridgeService;
        private static IPathResolverService _pathService;
        private static ITestRunnerService _testRunnerService;
        private static IPackageUpdateService _packageUpdateService;
        private static IPlatformService _platformService;
        private static IToolDiscoveryService _toolDiscoveryService;
        private static TransportManager _transportManager;

        public static IBridgeControlService Bridge => _bridgeService ??= new BridgeControlService();
        public static IPathResolverService Paths => _pathService ??= new PathResolverService();
        public static ITestRunnerService Tests => _testRunnerService ??= new TestRunnerService();
        public static IPackageUpdateService Updates => _packageUpdateService ??= new PackageUpdateService();
        public static IPlatformService Platform => _platformService ??= new PlatformService();
        public static IToolDiscoveryService ToolDiscovery => _toolDiscoveryService ??= new ToolDiscoveryService();
        public static TransportManager TransportManager => _transportManager ??= new TransportManager();

        /// <summary>
        /// Registers a custom implementation for a service (useful for testing)
        /// </summary>
        /// <typeparam name="T">The service interface type</typeparam>
        /// <param name="implementation">The implementation to register</param>
        public static void Register<T>(T implementation) where T : class
        {
            if (implementation is IBridgeControlService b)
                _bridgeService = b;
            else if (implementation is IPathResolverService p)
                _pathService = p;
            else if (implementation is ITestRunnerService t)
                _testRunnerService = t;
            else if (implementation is IPackageUpdateService pu)
                _packageUpdateService = pu;
            else if (implementation is IPlatformService ps)
                _platformService = ps;
            else if (implementation is IToolDiscoveryService td)
                _toolDiscoveryService = td;
            else if (implementation is TransportManager tm)
                _transportManager = tm;
        }

        /// <summary>
        /// Resets all services to their default implementations (useful for testing)
        /// </summary>
        public static void Reset()
        {
            (_bridgeService as IDisposable)?.Dispose();
            (_pathService as IDisposable)?.Dispose();
            (_testRunnerService as IDisposable)?.Dispose();
            (_packageUpdateService as IDisposable)?.Dispose();
            (_platformService as IDisposable)?.Dispose();
            (_toolDiscoveryService as IDisposable)?.Dispose();
            (_transportManager as IDisposable)?.Dispose();

            _bridgeService = null;
            _pathService = null;
            _testRunnerService = null;
            _packageUpdateService = null;
            _platformService = null;
            _toolDiscoveryService = null;
            _transportManager = null;
        }
    }
}
