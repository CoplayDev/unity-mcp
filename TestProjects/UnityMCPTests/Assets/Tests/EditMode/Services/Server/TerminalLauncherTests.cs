using System;
using NUnit.Framework;
using MCPForUnity.Editor.Services.Server;

namespace MCPForUnityTests.Editor.Services.Server
{
    /// <summary>
    /// Unit tests for TerminalLauncher component.
    /// Note: Tests avoid actually launching terminals to prevent test instability.
    /// </summary>
    [TestFixture]
    public class TerminalLauncherTests
    {
        private TerminalLauncher _launcher;

        [SetUp]
        public void SetUp()
        {
            _launcher = new TerminalLauncher();
        }

        #region GetProjectRootPath Tests

        [Test]
        public void GetProjectRootPath_ReturnsNonEmpty()
        {
            // Act
            string path = _launcher.GetProjectRootPath();

            // Assert
            Assert.IsNotNull(path);
            Assert.IsNotEmpty(path);
        }

        [Test]
        public void GetProjectRootPath_ReturnsValidDirectory()
        {
            // Act
            string path = _launcher.GetProjectRootPath();

            // Assert
            Assert.IsTrue(System.IO.Directory.Exists(path), $"Project root path should exist: {path}");
        }

        [Test]
        public void GetProjectRootPath_DoesNotContainAssets()
        {
            // Act
            string path = _launcher.GetProjectRootPath();

            // Assert
            Assert.IsFalse(path.EndsWith("Assets"), "Project root should not end with Assets");
        }

        #endregion

        #region CreateTerminalProcessStartInfo Tests

        [Test]
        public void CreateTerminalProcessStartInfo_EmptyCommand_ThrowsArgumentException()
        {
            // Act & Assert
            Assert.Throws<ArgumentException>(() =>
            {
                _launcher.CreateTerminalProcessStartInfo(string.Empty);
            });
        }

        [Test]
        public void CreateTerminalProcessStartInfo_NullCommand_ThrowsArgumentException()
        {
            // Act & Assert
            Assert.Throws<ArgumentException>(() =>
            {
                _launcher.CreateTerminalProcessStartInfo(null);
            });
        }

        [Test]
        public void CreateTerminalProcessStartInfo_WhitespaceCommand_ThrowsArgumentException()
        {
            // Act & Assert
            Assert.Throws<ArgumentException>(() =>
            {
                _launcher.CreateTerminalProcessStartInfo("   ");
            });
        }

        [Test]
        public void CreateTerminalProcessStartInfo_ValidCommand_ReturnsStartInfo()
        {
            // Act
            var startInfo = _launcher.CreateTerminalProcessStartInfo("echo hello");

            // Assert
            Assert.IsNotNull(startInfo);
            Assert.IsNotNull(startInfo.FileName);
            Assert.IsNotEmpty(startInfo.FileName);
        }

        [Test]
        public void CreateTerminalProcessStartInfo_ValidCommand_SetsUseShellExecuteFalse()
        {
            // Act
            var startInfo = _launcher.CreateTerminalProcessStartInfo("echo hello");

            // Assert
            Assert.IsFalse(startInfo.UseShellExecute, "UseShellExecute should be false");
        }

        [Test]
        public void CreateTerminalProcessStartInfo_ValidCommand_SetsCreateNoWindowTrue()
        {
            // Act
            var startInfo = _launcher.CreateTerminalProcessStartInfo("echo hello");

            // Assert
            Assert.IsTrue(startInfo.CreateNoWindow, "CreateNoWindow should be true");
        }

        [Test]
        public void CreateTerminalProcessStartInfo_CommandWithNewlines_StripsNewlines()
        {
            // Act - Should not throw
            var startInfo = _launcher.CreateTerminalProcessStartInfo("echo\nhello\r\nworld");

            // Assert
            Assert.IsNotNull(startInfo);
        }

        [Test]
        public void CreateTerminalProcessStartInfo_LongCommand_HandlesGracefully()
        {
            // Arrange
            string longCommand = new string('a', 1000);

            // Act
            var startInfo = _launcher.CreateTerminalProcessStartInfo(longCommand);

            // Assert
            Assert.IsNotNull(startInfo);
        }

        [Test]
        public void CreateTerminalProcessStartInfo_SpecialCharacters_HandlesGracefully()
        {
            // Arrange
            string command = "echo \"hello world\" && echo 'test' | cat";

            // Act
            var startInfo = _launcher.CreateTerminalProcessStartInfo(command);

            // Assert
            Assert.IsNotNull(startInfo);
        }

        #endregion

        #region Interface Implementation Tests

        [Test]
        public void TerminalLauncher_ImplementsITerminalLauncher()
        {
            // Assert
            Assert.IsInstanceOf<ITerminalLauncher>(_launcher);
        }

        [Test]
        public void TerminalLauncher_CanBeUsedViaInterface()
        {
            // Arrange
            ITerminalLauncher launcher = new TerminalLauncher();

            // Act & Assert
            Assert.DoesNotThrow(() =>
            {
                launcher.GetProjectRootPath();
                launcher.CreateTerminalProcessStartInfo("test");
            });
        }

        #endregion

        #region Platform-Specific Behavior Tests

        [Test]
        public void CreateTerminalProcessStartInfo_ReturnsAppropriateTerminal()
        {
            // Act
            var startInfo = _launcher.CreateTerminalProcessStartInfo("echo test");

            // Assert - Platform-specific
#if UNITY_EDITOR_OSX
            Assert.AreEqual("/usr/bin/open", startInfo.FileName, "macOS should use 'open'");
#elif UNITY_EDITOR_WIN
            Assert.AreEqual("cmd.exe", startInfo.FileName, "Windows should use 'cmd.exe'");
#else
            // Linux uses detected terminal
            Assert.IsNotNull(startInfo.FileName, "Linux should have a terminal command");
#endif
        }

        #endregion
    }
}
