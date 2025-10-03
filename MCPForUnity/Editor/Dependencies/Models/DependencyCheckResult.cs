using System;
using System.Collections.Generic;
using System.Linq;

namespace MCPForUnity.Editor.Dependencies.Models
{
    /// <summary>
    /// Result of a comprehensive dependency check
    /// </summary>
    [Serializable]
    public class DependencyCheckResult
    {
        /// <summary>
        /// List of all dependency statuses checked
        /// </summary>
        public List<DependencyStatus> Dependencies { get; set; }

        /// <summary>
        /// Overall system readiness for MCP operations
        /// </summary>
        public bool IsSystemReady { get; set; }

        /// <summary>
        /// Whether all required dependencies are available
        /// </summary>
        public bool AllRequiredAvailable => Dependencies?.Where(d => d.IsRequired).All(d => d.IsAvailable) ?? false;

        /// <summary>
        /// Whether any optional dependencies are missing
        /// </summary>
        public bool HasMissingOptional => Dependencies?.Where(d => !d.IsRequired).Any(d => !d.IsAvailable) ?? false;

        /// <summary>
        /// Summary message about the dependency state
        /// </summary>
        public string Summary { get; set; }

        /// <summary>
        /// Recommended next steps for the user
        /// </summary>
        public List<string> RecommendedActions { get; set; }

        /// <summary>
        /// Timestamp when this check was performed
        /// </summary>
        public DateTime CheckedAt { get; set; }

        public DependencyCheckResult()
        {
            Dependencies = new List<DependencyStatus>();
            RecommendedActions = new List<string>();
            CheckedAt = DateTime.UtcNow;
        }

        /// <summary>
        /// Get dependencies by availability status
        /// </summary>
        public List<DependencyStatus> GetMissingDependencies()
        {
            return Dependencies?.Where(d => !d.IsAvailable).ToList() ?? new List<DependencyStatus>();
        }

        /// <summary>
        /// Get missing required dependencies
        /// </summary>
        public List<DependencyStatus> GetMissingRequired()
        {
            return Dependencies?.Where(d => d.IsRequired && !d.IsAvailable).ToList() ?? new List<DependencyStatus>();
        }

        /// <summary>
        /// Generate a user-friendly summary of the dependency state
        /// </summary>
        public void GenerateSummary()
        {
            var missing = GetMissingDependencies();
            var missingRequired = GetMissingRequired();

            if (missing.Count == 0)
            {
                Summary = "All dependencies are available and ready.";
                IsSystemReady = true;
            }
            else if (missingRequired.Count == 0)
            {
                Summary = $"System is ready. {missing.Count} optional dependencies are missing.";
                IsSystemReady = true;
            }
            else
            {
                Summary = $"System is not ready. {missingRequired.Count} required dependencies are missing.";
                IsSystemReady = false;
            }
        }
    }
}
