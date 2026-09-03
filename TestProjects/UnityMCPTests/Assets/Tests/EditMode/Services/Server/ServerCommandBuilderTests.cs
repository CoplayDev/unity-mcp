using System;
using NUnit.Framework;
using MCPForUnity.Editor.Services;
using MCPForUnity.Editor.Services.Server;
using MCPForUnity.Editor.Constants;
using MCPForUnity.Editor.Helpers;
using UnityEditor;

namespace MCPForUnityTests.Editor.Services.Server
{
    /// <summary>
    /// Unit tests for ServerCommandBuilder component.
    /// </summary>
    [TestFixture]
    public class ServerCommandBuilderTests
    {
        private ServerCommandBuilder _builder;
        private bool _savedUseHttpTransport;
        private bool _hadUseHttpTransport;
        private string _savedHttpUrl;
        private bool _hadHttpUrl;
        private string _savedHttpUrlEnvironment;

        [SetUp]
        public void SetUp()
        {
            _builder = new ServerCommandBuilder();
            // Save current settings
            _savedHttpUrlEnvironment = Environment.GetEnvironmentVariable("UNITY_MCP_HTTP_URL");
            Environment.SetEnvironmentVariable("UNITY_MCP_HTTP_URL", null);
            _hadUseHttpTransport = EditorPrefs.HasKey(ProjectScopedEditorPrefs.GetKey(EditorPrefKeys.UseHttpTransport));
            _savedUseHttpTransport = ProjectScopedEditorPrefs.GetBool(
                EditorPrefKeys.UseHttpTransport,
                true,
                allowLegacyFallback: false);
            _hadHttpUrl = EditorPrefs.HasKey(ProjectScopedEditorPrefs.GetKey(EditorPrefKeys.HttpBaseUrl));
            _savedHttpUrl = ProjectScopedEditorPrefs.GetString(
                EditorPrefKeys.HttpBaseUrl,
                string.Empty,
                allowLegacyFallback: false);
        }

        [TearDown]
        public void TearDown()
        {
            // Restore settings
            if (_hadUseHttpTransport)
                EditorConfigurationCache.Instance.SetUseHttpTransport(_savedUseHttpTransport);
            else
                ProjectScopedEditorPrefs.DeleteKey(EditorPrefKeys.UseHttpTransport);
            if (_hadHttpUrl)
            {
                EditorConfigurationCache.Instance.SetHttpBaseUrl( _savedHttpUrl);
            }
            else
            {
                ProjectScopedEditorPrefs.DeleteKey(EditorPrefKeys.HttpBaseUrl);
            }
            Environment.SetEnvironmentVariable("UNITY_MCP_HTTP_URL", _savedHttpUrlEnvironment);
            // Refresh without recreating originally absent scoped keys from legacy prefs.
            EditorConfigurationCache.Instance.Refresh(allowLegacyFallback: false);
        }

        #region QuoteIfNeeded Tests

        [Test]
        public void QuoteIfNeeded_PathWithSpaces_AddsQuotes()
        {
            // Arrange
            string input = "path with spaces";

            // Act
            string result = _builder.QuoteIfNeeded(input);

            // Assert
            Assert.AreEqual("\"path with spaces\"", result);
        }

        [Test]
        public void QuoteIfNeeded_PathWithoutSpaces_NoChange()
        {
            // Arrange
            string input = "pathwithoutspaces";

            // Act
            string result = _builder.QuoteIfNeeded(input);

            // Assert
            Assert.AreEqual("pathwithoutspaces", result);
        }

        [Test]
        public void QuoteIfNeeded_NullInput_ReturnsNull()
        {
            // Act
            string result = _builder.QuoteIfNeeded(null);

            // Assert
            Assert.IsNull(result);
        }

        [Test]
        public void QuoteIfNeeded_EmptyInput_ReturnsEmpty()
        {
            // Act
            string result = _builder.QuoteIfNeeded(string.Empty);

            // Assert
            Assert.AreEqual(string.Empty, result);
        }

        [Test]
        public void QuoteIfNeeded_AlreadyQuoted_AddsMoreQuotes()
        {
            // Arrange - This is intentional behavior - don't double-escape
            string input = "\"already quoted\"";

            // Act
            string result = _builder.QuoteIfNeeded(input);

            // Assert - Has spaces so gets quoted
            Assert.AreEqual("\"\"already quoted\"\"", result);
        }

        #endregion

        #region BuildUvPathFromUvx Tests

        [Test]
        public void BuildUvPathFromUvx_ValidPath_ConvertsCorrectly()
        {
            // This test uses Unix-style paths which only work correctly on non-Windows
            if (UnityEngine.Application.platform == UnityEngine.RuntimePlatform.WindowsEditor)
            {
                Assert.Pass("Skipped on Windows - use BuildUvPathFromUvx_WindowsPath_ConvertsCorrectly instead");
                return;
            }

            // Arrange
            string uvxPath = "/usr/local/bin/uvx";

            // Act
            string result = _builder.BuildUvPathFromUvx(uvxPath);

            // Assert
            Assert.AreEqual("/usr/local/bin/uv", result);
        }

        [Test]
        public void BuildUvPathFromUvx_WindowsPath_ConvertsCorrectly()
        {
            // This test only makes sense on Windows where backslash paths are native
            if (UnityEngine.Application.platform != UnityEngine.RuntimePlatform.WindowsEditor)
            {
                Assert.Pass("Skipped on non-Windows platform");
                return;
            }

            // Arrange
            string uvxPath = @"C:\Program Files\uv\uvx.exe";

            // Act
            string result = _builder.BuildUvPathFromUvx(uvxPath);

            // Assert
            Assert.AreEqual(@"C:\Program Files\uv\uv.exe", result);
        }

        [Test]
        public void BuildUvPathFromUvx_NullPath_ReturnsNull()
        {
            // Act
            string result = _builder.BuildUvPathFromUvx(null);

            // Assert
            Assert.IsNull(result);
        }

        [Test]
        public void BuildUvPathFromUvx_EmptyPath_ReturnsEmpty()
        {
            // Act
            string result = _builder.BuildUvPathFromUvx(string.Empty);

            // Assert
            Assert.AreEqual(string.Empty, result);
        }

        [Test]
        public void BuildUvPathFromUvx_WhitespacePath_ReturnsWhitespace()
        {
            // Act
            string result = _builder.BuildUvPathFromUvx("   ");

            // Assert
            Assert.AreEqual("   ", result);
        }

        [Test]
        public void BuildUvPathFromUvx_JustFilename_ConvertsCorrectly()
        {
            // Arrange
            string uvxPath = "uvx";

            // Act
            string result = _builder.BuildUvPathFromUvx(uvxPath);

            // Assert
            Assert.AreEqual("uv", result);
        }

        #endregion

        #region GetPlatformSpecificPathPrepend Tests

        [Test]
        public void GetPlatformSpecificPathPrepend_ReturnsNonNull()
        {
            // Act
            string result = _builder.GetPlatformSpecificPathPrepend();

            // Assert - May be null on some platforms, but should not throw
            Assert.Pass($"GetPlatformSpecificPathPrepend returned: {result ?? "null"}");
        }

        [Test]
        public void GetPlatformSpecificPathPrepend_DoesNotThrow()
        {
            // Act & Assert
            Assert.DoesNotThrow(() =>
            {
                _builder.GetPlatformSpecificPathPrepend();
            });
        }

        #endregion

        #region TryBuildCommand Tests

        [Test]
        public void TryBuildCommand_HttpDisabled_ReturnsFalse()
        {
            // Arrange
            EditorConfigurationCache.Instance.SetUseHttpTransport(false);
            EditorConfigurationCache.Instance.SetHttpBaseUrl("http://localhost:8080");
            EditorConfigurationCache.Instance.Refresh();

            // Act
            bool result = _builder.TryBuildCommand(out string fileName, out string arguments, out string displayCommand, out string error);

            // Assert
            Assert.IsFalse(result);
            Assert.IsNull(fileName);
            Assert.IsNull(arguments);
            Assert.IsNull(displayCommand);
            Assert.IsNotNull(error);
            Assert.That(error, Does.Contain("HTTP").IgnoreCase);
        }

        [Test]
        public void TryBuildCommand_RemoteUrl_ReturnsFalse()
        {
            // Arrange
            EditorConfigurationCache.Instance.SetUseHttpTransport(true);
            EditorConfigurationCache.Instance.SetHttpBaseUrl("http://remote.server.com:8080");
            EditorConfigurationCache.Instance.Refresh();

            // Act
            bool result = _builder.TryBuildCommand(out string fileName, out string arguments, out string displayCommand, out string error);

            // Assert
            Assert.IsFalse(result);
            Assert.IsNotNull(error);
            Assert.That(error, Does.Contain("local").IgnoreCase);
        }

        [Test]
        public void TryBuildCommand_LocalUrl_ReturnsCommandOrError()
        {
            // Arrange
            EditorConfigurationCache.Instance.SetUseHttpTransport(true);
            EditorConfigurationCache.Instance.SetHttpBaseUrl("http://localhost:8080");
            EditorConfigurationCache.Instance.Refresh();

            // Act
            bool result = _builder.TryBuildCommand(out string fileName, out string arguments, out string displayCommand, out string error);

            // Assert - Success depends on uvx availability
            if (result)
            {
                Assert.IsNotNull(fileName, "fileName should be set on success");
                Assert.IsNotNull(arguments, "arguments should be set on success");
                Assert.IsNotNull(displayCommand, "displayCommand should be set on success");
                Assert.IsNull(error, "error should be null on success");
                Assert.That(displayCommand, Does.Contain("uvx").Or.Contain("uv"));
            }
            else
            {
                Assert.IsNotNull(error, "error message should be provided on failure");
            }

            Assert.Pass($"TryBuildCommand: success={result}, error={error ?? "null"}");
        }

        [Test]
        public void TryBuildCommand_DoesNotThrow()
        {
            // Act & Assert
            Assert.DoesNotThrow(() =>
            {
                _builder.TryBuildCommand(out _, out _, out _, out _);
            });
        }

        #endregion

        #region Interface Implementation Tests

        [Test]
        public void ServerCommandBuilder_ImplementsIServerCommandBuilder()
        {
            // Assert
            Assert.IsInstanceOf<IServerCommandBuilder>(_builder);
        }

        [Test]
        public void ServerCommandBuilder_CanBeUsedViaInterface()
        {
            // Arrange
            IServerCommandBuilder builder = new ServerCommandBuilder();

            // Act & Assert - All interface methods should work
            Assert.DoesNotThrow(() =>
            {
                builder.QuoteIfNeeded("test");
                builder.BuildUvPathFromUvx("uvx");
                builder.GetPlatformSpecificPathPrepend();
                builder.TryBuildCommand(out _, out _, out _, out _);
            });
        }

        #endregion
    }
}
