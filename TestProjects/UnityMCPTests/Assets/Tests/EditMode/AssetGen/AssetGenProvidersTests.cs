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
        public void Audio_Fal_ReturnsAdapter()
        {
            IAudioProviderAdapter adapter = AssetGenProviders.Audio("fal");
            Assert.IsNotNull(adapter);
            Assert.AreEqual("fal", adapter.Id);
        }

        [Test]
        public void Audio_Unimplemented_Throws()
        {
            Assert.Throws<NotSupportedException>(() => AssetGenProviders.Audio("elevenlabs"));
        }

        [Test]
        public void List_IncludesFalAudioRow()
        {
            ProviderInfo row = FindByIdKind("fal", "audio");
            Assert.IsNotNull(row, "List() should advertise a fal audio row");
            Assert.AreEqual("audio", row.Kind);
        }

        [Test]
        public void List_HasExactlyOneFalImage_AndOneFalAudio()
        {
            int image = 0, audio = 0;
            foreach (ProviderInfo p in AssetGenProviders.List())
            {
                if (p.Id != "fal") continue;
                if (p.Kind == "image") image++;
                else if (p.Kind == "audio") audio++;
            }
            Assert.AreEqual(1, image, "exactly one fal image row");
            Assert.AreEqual(1, audio, "exactly one fal audio row");
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

        private static ProviderInfo FindByIdKind(string id, string kind)
        {
            foreach (ProviderInfo p in AssetGenProviders.List())
            {
                if (p.Id == id && p.Kind == kind) return p;
            }
            return null;
        }
    }
}
