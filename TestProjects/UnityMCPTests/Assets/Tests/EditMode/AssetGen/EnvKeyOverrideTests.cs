using System;
using MCPForUnity.Editor.Security;
using NUnit.Framework;

namespace MCPForUnityTests.Editor.AssetGen
{
    public class EnvKeyOverrideTests
    {
        private const string Var = "MCPFORUNITY_TRIPO_API_KEY";

        [TearDown]
        public void TearDown() => Environment.SetEnvironmentVariable(Var, null);

        [Test]
        public void EnvVarName_UpperWithApiKeySuffix()
        {
            Assert.AreEqual("MCPFORUNITY_TRIPO_API_KEY", EnvKeyOverride.EnvVarName("tripo"));
            Assert.AreEqual("MCPFORUNITY_OPENROUTER_API_KEY", EnvKeyOverride.EnvVarName("openrouter"));
        }

        [Test]
        public void TryGet_ReturnsValue_WhenEnvSet()
        {
            Environment.SetEnvironmentVariable(Var, "tsk_env_value_123");
            Assert.IsTrue(EnvKeyOverride.TryGet("tripo", out string v));
            Assert.AreEqual("tsk_env_value_123", v);
        }

        [Test]
        public void TryGet_ReturnsFalse_WhenEnvMissing()
        {
            Environment.SetEnvironmentVariable(Var, null);
            Assert.IsFalse(EnvKeyOverride.TryGet("tripo", out string v));
            Assert.IsNull(v);
        }
    }
}
