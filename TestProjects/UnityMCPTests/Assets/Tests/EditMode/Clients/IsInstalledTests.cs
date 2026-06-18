using System.IO;
using MCPForUnity.Editor.Clients;
using MCPForUnity.Editor.Clients.Configurators;
using NUnit.Framework;

namespace MCPForUnityTests.Editor.Clients
{
    [TestFixture]
    public class IsInstalledTests
    {
        [Test]
        public void IMcpClientConfigurator_ExposesIsInstalled()
        {
            var prop = typeof(IMcpClientConfigurator).GetProperty("IsInstalled");
            Assert.IsNotNull(prop, "IMcpClientConfigurator must expose an IsInstalled property");
            Assert.AreEqual(typeof(bool), prop.PropertyType);
        }

        [Test]
        public void JsonClient_NotInstalled_WhenParentDirMissing()
        {
            var cursor = new CursorConfigurator();
            string parent = Path.GetDirectoryName(cursor.GetConfigPath());
            if (parent == null || !Directory.Exists(parent))
            {
                Assert.IsFalse(cursor.IsInstalled,
                    "Cursor parent dir does not exist on this machine, IsInstalled must be false");
            }
            else
            {
                Assert.IsTrue(cursor.IsInstalled,
                    "Cursor parent dir exists, IsInstalled must be true");
            }
        }

        [Test]
        public void JsonClient_Installed_WhenParentDirExists()
        {
            var claude = new ClaudeDesktopConfigurator();
            string parent = Path.GetDirectoryName(claude.GetConfigPath());
            bool expected = parent != null && Directory.Exists(parent);
            Assert.AreEqual(expected, claude.IsInstalled);
        }
    }
}
