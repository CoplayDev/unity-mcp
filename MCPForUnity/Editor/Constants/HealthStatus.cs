namespace MCPForUnity.Editor.Constants
{
    /// <summary>
    /// Constants for health check status values.
    /// Used for coordinating health state between Connection and Advanced sections.
    /// </summary>
    public static class HealthStatus
    {
        public const string Unknown = "Unknown";
        public const string Healthy = "Healthy";
        public const string PingFailed = "Ping Failed";
        public const string Unhealthy = "Unhealthy";
    }
}
