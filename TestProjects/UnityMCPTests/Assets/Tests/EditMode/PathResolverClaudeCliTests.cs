using System;
using System.IO;
using MCPForUnity.Editor.Constants;
using MCPForUnity.Editor.Services;
using NUnit.Framework;
using UnityEditor;

namespace MCPForUnityTests.Editor
{
    /// <summary>
    /// Locks the strict override contract of Claude CLI resolution after discovery was
    /// consolidated into ExecPath.ResolveClaude(): a valid override is honored, and an
    /// override that points at a missing file returns null rather than falling back to
    /// auto-discovery.
    /// </summary>
    public class PathResolverClaudeCliTests
    {
        private const string Key = EditorPrefKeys.ClaudeCliPathOverride;
        private bool _hadOverride;
        private string _savedOverride;

        [SetUp]
        public void SetUp()
        {
            _savedOverride = EditorPrefs.GetString(Key, string.Empty);
            _hadOverride = !string.IsNullOrEmpty(_savedOverride);
        }

        [TearDown]
        public void TearDown()
        {
            if (_hadOverride) EditorPrefs.SetString(Key, _savedOverride);
            else EditorPrefs.DeleteKey(Key);
        }

        [Test]
        public void GetClaudeCliPath_ValidOverride_ReturnsOverride()
        {
            string stub = Path.Combine(Path.GetTempPath(), "mcp_claude_stub_" + Guid.NewGuid().ToString("N"));
            File.WriteAllText(stub, "stub");
            try
            {
                EditorPrefs.SetString(Key, stub);
                var resolver = new PathResolverService();
                Assert.AreEqual(stub, resolver.GetClaudeCliPath());
                Assert.IsTrue(resolver.IsClaudeCliDetected());
            }
            finally
            {
                File.Delete(stub);
            }
        }

        [Test]
        public void GetClaudeCliPath_InvalidOverride_ReturnsNull_WithoutDiscoveryFallback()
        {
            string missing = Path.Combine(Path.GetTempPath(), "mcp_claude_missing_" + Guid.NewGuid().ToString("N"));
            EditorPrefs.SetString(Key, missing);

            var resolver = new PathResolverService();
            Assert.IsNull(resolver.GetClaudeCliPath());
        }
    }
}
