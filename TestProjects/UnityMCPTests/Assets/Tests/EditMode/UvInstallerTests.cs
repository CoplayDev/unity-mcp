using System.Runtime.InteropServices;
using MCPForUnity.Editor.Dependencies;
using NUnit.Framework;

namespace MCPForUnityTests.Editor
{
    /// <summary>
    /// Verifies the one-click uv installer builds the correct command per platform. The command
    /// is built by a pure method so it can be asserted without actually running the installer.
    /// </summary>
    public class UvInstallerTests
    {
        [Test]
        public void BuildInstallCommand_MatchesPlatform()
        {
            var (file, args) = UvInstaller.BuildInstallCommand();
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Assert.AreEqual("powershell", file);
                StringAssert.Contains("astral.sh/uv/install.ps1", args);
                StringAssert.Contains("iex", args);
            }
            else
            {
                Assert.AreEqual("/bin/sh", file);
                StringAssert.Contains("-c", args);
                StringAssert.Contains("astral.sh/uv/install.sh", args);
            }
        }

        [Test]
        public void DescribeCommand_IncludesFileAndInstallerUrl()
        {
            var (file, _) = UvInstaller.BuildInstallCommand();
            string desc = UvInstaller.DescribeCommand();
            StringAssert.Contains(file, desc);
            StringAssert.Contains("astral.sh/uv", desc);
        }

        [Test]
        public void IsSupported_IsTrueOnDesktopEditorPlatforms()
        {
            // EditMode tests only run on Windows/macOS/Linux editors, all of which are supported.
            Assert.IsTrue(UvInstaller.IsSupported);
        }
    }
}
