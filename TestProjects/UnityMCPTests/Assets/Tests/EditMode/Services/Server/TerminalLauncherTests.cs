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

        #region CreateHeadlessProcessStartInfo Tests

        [Test]
        public void CreateHeadlessProcessStartInfo_EmptyCommand_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() =>
                _launcher.CreateHeadlessProcessStartInfo(string.Empty, "/tmp/log.txt"));
        }

        [Test]
        public void CreateHeadlessProcessStartInfo_EmptyLogPath_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() =>
                _launcher.CreateHeadlessProcessStartInfo("echo hello", string.Empty));
        }

        [Test]
        public void CreateHeadlessProcessStartInfo_IsHiddenNoWindow()
        {
            var startInfo = _launcher.CreateHeadlessProcessStartInfo("echo hello", LogPath());

            Assert.IsFalse(startInfo.UseShellExecute, "UseShellExecute should be false for headless launch");
            Assert.IsTrue(startInfo.CreateNoWindow, "CreateNoWindow should be true for headless launch");
            Assert.AreEqual(System.Diagnostics.ProcessWindowStyle.Hidden, startInfo.WindowStyle,
                "WindowStyle should be Hidden for headless launch");
        }

        [Test]
        public void CreateHeadlessProcessStartInfo_DoesNotOpenTerminal()
        {
            var startInfo = _launcher.CreateHeadlessProcessStartInfo("echo hello", LogPath());

            // Must NOT route through Terminal.app / start / a terminal emulator.
#if UNITY_EDITOR_WIN
            Assert.AreEqual("cmd.exe", startInfo.FileName, "Windows headless should run via cmd.exe");
            StringAssert.DoesNotContain("start ", startInfo.Arguments, "Windows headless must not use 'start' (new window)");
#else
            Assert.AreEqual("/bin/bash", startInfo.FileName, "macOS/Linux headless should run via /bin/bash");
            StringAssert.DoesNotContain("open", startInfo.Arguments, "macOS headless must not use 'open -a Terminal'");
            StringAssert.DoesNotContain("Terminal", startInfo.Arguments, "macOS headless must not open Terminal.app");
#endif
        }

        [Test]
        public void CreateHeadlessProcessStartInfo_RedirectsOutputToLogFile()
        {
            string logPath = LogPath();

            var startInfo = _launcher.CreateHeadlessProcessStartInfo("echo hello", logPath);

            // The redirect to the log file is part of the shell payload.
            StringAssert.Contains(logPath, startInfo.Arguments, "Arguments should reference the log file path");
            StringAssert.Contains("2>&1", startInfo.Arguments, "stderr should be redirected to stdout (and the log)");
            StringAssert.Contains(">>", startInfo.Arguments, "output should be appended to the log via >>");
        }

        [Test]
        public void CreateHeadlessProcessStartInfo_LogPathWithSpaces_IsQuoted()
        {
            // A log path containing spaces must remain a single token.
            string logPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Mcp Logs", "server launch.log");

            var startInfo = _launcher.CreateHeadlessProcessStartInfo("uvx run-server", logPath);

            StringAssert.Contains(logPath, startInfo.Arguments, "Arguments should contain the full spaced log path");
#if UNITY_EDITOR_WIN
            StringAssert.Contains($"\"{logPath}\"", startInfo.Arguments, "Windows should double-quote a spaced log path");
#else
            StringAssert.Contains($"'{logPath}'", startInfo.Arguments, "macOS/Linux should single-quote a spaced log path");
#endif
        }

        [Test]
        public void CreateHeadlessProcessStartInfo_CommandWithSpaces_Preserved()
        {
            string command = "/path with spaces/uvx --no-cache run mcp-for-unity";

            var startInfo = _launcher.CreateHeadlessProcessStartInfo(command, LogPath());

            StringAssert.Contains(command, startInfo.Arguments, "The command (incl. spaces) should be preserved in Arguments");
        }

        [Test]
        public void CreateHeadlessProcessStartInfo_StripsNewlines()
        {
            var startInfo = _launcher.CreateHeadlessProcessStartInfo("echo\nhello\r\nworld", LogPath());

            Assert.IsNotNull(startInfo);
            StringAssert.DoesNotContain("\n", startInfo.Arguments);
            StringAssert.DoesNotContain("\r", startInfo.Arguments);
        }

        private static string LogPath()
        {
            return System.IO.Path.Combine(System.IO.Path.GetTempPath(), "mcp-headless-test.log");
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
