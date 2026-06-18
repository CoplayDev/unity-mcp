using System.Reflection;
using NUnit.Framework;
using UnityEditor;

namespace MCPForUnityTests.Editor.Services
{
    [TestFixture]
    public class StartupConfigRewriteTests
    {
        [Test]
        public void StartupConfigRewrite_TypeExists()
        {
            var t = System.Type.GetType("MCPForUnity.Editor.Services.StartupConfigRewrite, MCPForUnity.Editor");
            Assert.IsNotNull(t, "StartupConfigRewrite type must exist");
            Assert.IsTrue(t.IsPublic, "StartupConfigRewrite must be public so the [InitializeOnLoad] attribute fires");
        }

        [Test]
        public void StartupConfigRewrite_HasInitializeOnLoad()
        {
            var t = System.Type.GetType("MCPForUnity.Editor.Services.StartupConfigRewrite, MCPForUnity.Editor");
            Assert.IsNotNull(t);
            object[] attrs = t.GetCustomAttributes(typeof(InitializeOnLoadAttribute), inherit: false);
            Assert.AreEqual(1, attrs.Length, "Class must be decorated with [InitializeOnLoad]");
        }

        [Test]
        public void StartupConfigRewrite_RunOncePerSession_GuardKey()
        {
            var t = System.Type.GetType("MCPForUnity.Editor.Services.StartupConfigRewrite, MCPForUnity.Editor");
            var keyField = t?.GetField("SESSION_GUARD_KEY",
                BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Public);
            Assert.IsNotNull(keyField);
            string val = (string)keyField.GetValue(null);
            StringAssert.StartsWith("MCPForUnity.", val);
        }
    }
}
