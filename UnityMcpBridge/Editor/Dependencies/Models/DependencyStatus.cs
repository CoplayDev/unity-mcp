using System;

namespace MCPForUnity.Editor.Dependencies.Models
{
    /// <summary>
    /// Represents the status of a dependency check
    /// </summary>
    [Serializable]
    public class DependencyStatus
    {
        /// <summary>
        /// Name of the dependency being checked
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Whether the dependency is available and functional
        /// </summary>
        public bool IsAvailable { get; set; }

        /// <summary>
        /// Version information if available
        /// </summary>
        public string Version { get; set; }

        /// <summary>
        /// Path to the dependency executable/installation
        /// </summary>
        public string Path { get; set; }

        /// <summary>
        /// Additional details about the dependency status
        /// </summary>
        public string Details { get; set; }

        /// <summary>
        /// Error message if dependency check failed
        /// </summary>
        public string ErrorMessage { get; set; }

        /// <summary>
        /// Whether this dependency is required for basic functionality
        /// </summary>
        public bool IsRequired { get; set; }

        /// <summary>
        /// Suggested installation method or URL
        /// </summary>
        public string InstallationHint { get; set; }

        public DependencyStatus(string name, bool isRequired = true)
        {
            Name = name;
            IsRequired = isRequired;
            IsAvailable = false;
        }

        public override string ToString()
        {
            var status = IsAvailable ? "✓" : "✗";
            var version = !string.IsNullOrEmpty(Version) ? $" ({Version})" : "";
            return $"{status} {Name}{version}";
        }
    }
}
