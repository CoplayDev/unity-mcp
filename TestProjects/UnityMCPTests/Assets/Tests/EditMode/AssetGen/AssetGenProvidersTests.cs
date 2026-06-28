using System;
using System.IO;
using MCPForUnity.Editor.Security;
using MCPForUnity.Editor.Services.AssetGen.Providers;
using NUnit.Framework;

namespace MCPForUnityTests.Editor.AssetGen
{
    /// <summary>
    /// Covers the provider factory/registry: Tripo resolves to a real adapter, unimplemented
    /// providers throw, and <c>List()</c> reports a key-free <c>Configured</c> bool that honors
    /// the env override (<c>MCPFORUNITY_TRIPO_API_KEY</c>) without ever exposing a key value.
    /// </summary>
    public class AssetGenProvidersTests
    {
        [Test]
        public void Model_Tripo_ReturnsAdapter()
        {
            IModelProviderAdapter adapter = AssetGenProviders.Model("tripo");
            Assert.IsNotNull(adapter);
            Assert.AreEqual("tripo", adapter.Id);
        }

        [Test]
        public void Model_Unimplemented_Throws()
        {
            Assert.Throws<NotSupportedException>(() => AssetGenProviders.Model("hunyuan"));
        }

        [Test]
        public void List_IncludesTripo_ConfiguredIsBool()
        {
            const string envName = "MCPFORUNITY_TRIPO_API_KEY";
            string original = Environment.GetEnvironmentVariable(envName);
            string tempDir = Path.Combine(Path.GetTempPath(), "mcp_assetgen_providers_" + Guid.NewGuid().ToString("N"));
            try
            {
                // Deterministic empty baseline that still consults the env-override layer,
                // so this test never depends on the dev machine's real key store.
                SecureKeyStore.OverrideForTests(new EnvOverlayKeyStore(new EncryptedFileKeyStore(tempDir)));

                Environment.SetEnvironmentVariable(envName, null);
                ProviderInfo tripo = FindTripo();
                Assert.IsNotNull(tripo);
                Assert.AreEqual("model", tripo.Kind);
                Assert.IsFalse(tripo.Configured, "no key/env should report not configured");

                Environment.SetEnvironmentVariable(envName, "tsk_env_override_value");
                tripo = FindTripo();
                Assert.IsTrue(tripo.Configured, "env present should flip Configured to true");
            }
            finally
            {
                Environment.SetEnvironmentVariable(envName, original);
                SecureKeyStore.ResetForTests();
                try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { /* ignore */ }
            }
        }

        private static ProviderInfo FindTripo()
        {
            foreach (ProviderInfo p in AssetGenProviders.List())
            {
                if (p.Id == "tripo") return p;
            }
            return null;
        }
    }
}
