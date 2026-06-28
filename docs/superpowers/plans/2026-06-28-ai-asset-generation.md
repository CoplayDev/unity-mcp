# AI Asset Generation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add bring-your-own-key AI asset generation (3D model gen + 3D marketplace import + 2D image gen) to MCP for Unity, triggered only via MCP tools/CLI, executed C#-side, imported into the Unity project.

**Architecture:** Hybrid — the Unity "Asset Generation" GUI tab only configures provider keys (in the OS secure store) and toggles; generation is triggered by Python MCP tools / Click CLI (thin pass-throughs carrying no key and no bytes); the C# editor side reads the key from the secure store, calls the provider via UnityWebRequest, downloads into Assets/, and imports via ModelImporter/TextureImporter, using a SessionState-backed async job manager that survives the import domain reload.

**Tech Stack:** Unity Editor C# (UIToolkit, AssetDatabase, ModelImporter, UnityWebRequest, SessionState, optional glTFast), Python FastMCP + Click CLI, pytest, Unity EditMode tests. Providers: Tripo/Meshy/Hunyuan (3D gen), Sketchfab (3D import), fal.ai/OpenRouter (2D image).

## Global Constraints

- Branch/worktree: `feat/3d-asset-generation` at `.worktrees/3d-asset-generation`. Commit per task; do NOT push or open a PR.
- Tool group `asset_gen` is OFF by default (parity with vfx/animation); enabled via `manage_tools`.
- Keys live ONLY in the OS secure store (Keychain/Credential Manager/libsecret + AES-256-GCM fallback). Keys: never in EditorPrefs, never over the bridge, never in tool output / job records / logs / git, never returned by any tool. Non-secret config uses EditorPrefs `MCPForUnity.AssetGen.*`.
- Heavy bytes never cross the bridge — C# downloads to `Assets/Generated/` directly.
- Async long ops use `RequiresPolling=true, PollAction="status"`; jobs persist to SessionState and survive domain reloads (DomainReloadTimeoutMs=120000).
- GLB import requires glTFast (`com.unity.cloud.gltfast`), an OPTIONAL Deps-tab dependency; prefer FBX; fail GLB jobs with an actionable message when absent.
- Domain symmetry: each C# `[McpForUnityTool]` mirrored by a Python `@mcp_for_unity_tool` + a Click CLI command, with tests both sides. camelCase params via ToolParams; strip None in Python.
- Route version-fragile APIs (ModelImporter/UnityWebRequest/UIToolkit) through `MCPForUnity/Runtime/Helpers/Unity*Compat.cs` shims (CLAUDE.md policy); run `tools/check-unity-versions.sh` when touching them.
- Conventional commits, scope `asset-gen`; end each commit body with `Claude-Session: https://claude.ai/code/session_01Tjpb5gYgUe2AUJuRdXr7Lv`.
- "Test ready" = Python pytest green; C# compiles across the CI matrix; EditMode tests authored; provider HTTP exercised against fakes. Live provider + live Unity import are user-verified.

---

I have everything I need. Here is the Phase 1 plan.

---

## Phase 1: SecureKeyStore (security-first)

This phase builds the entire at-rest key-storage subsystem **before** any provider or tool can read a key. It delivers the `ISecureKeyStore` contract, all four platform implementations, the env-var override, the platform-selecting factory, and the `SecretRedactor`. Tests target the deterministic `EncryptedFileKeyStore` fallback (CI-safe) plus a guard that proves a serialized job record can never carry a key.

**Prerequisites & ground truth (verified against the repo):**
- All new code lives in the existing `MCPForUnity.Editor` assembly. New files under `MCPForUnity/Editor/Security/SecureKeyStore/` are auto-included by `MCPForUnity/Editor/MCPForUnity.Editor.asmdef` (folder-based, `autoReferenced: true`). **No new asmdef.**
- Test files go under `TestProjects/UnityMCPTests/Assets/Tests/EditMode/AssetGen/`. The existing `MCPForUnityTests.Editor.asmdef` at `.../EditMode/` covers all subfolders, so **no new test asmdef** is needed.
- Internal test seams are reachable: `MCPForUnity/Editor/AssemblyInfo.cs` already declares `[assembly: InternalsVisibleTo("MCPForUnityTests.EditMode")]` (verified).
- Namespace for production code: `MCPForUnity.Editor.Security`. Namespace for tests: `MCPForUnity.Editor.Tests.EditMode.AssetGen` (mirrors the existing `...Tests.EditMode.Services`).
- `AesGcm`, `Rfc2898DeriveBytes(…, HashAlgorithmName)`, and `ProcessStartInfo.ArgumentList` require the project's **.NET Standard 2.1** API level (Unity 2021.2+ default). The encrypted store translates a `PlatformNotSupportedException` into an actionable error.

**How to run the C# tests in this phase (no headless run is possible here without a license):**
- **Interactive:** open `TestProjects/UnityMCPTests` in Unity → `Window > General > Test Runner` → `EditMode` tab → run the `AssetGen` fixtures.
- **Headless (needs a Hub-licensed editor):** `python tools/local_harness.py --legs editmode`
- **Compile across the CI matrix:** `tools/check-unity-versions.sh`
- **"Expected FAIL" before impl** means: the test references a type that does not yet exist, so the entire EditMode assembly fails to compile and the Test Runner shows red compile errors. **"Expected PASS"** means the named fixture is green.
- When committing, add the `.meta` files Unity generates for each new `.cs` file and for each new folder.

---

### Task 1.1: Key-store contract, constants, and env-var override

**Files:**
- Create: `MCPForUnity/Editor/Security/SecureKeyStore/ISecureKeyStore.cs`
- Create: `MCPForUnity/Editor/Security/SecureKeyStore/SecureKeyStoreConstants.cs`
- Create: `MCPForUnity/Editor/Security/SecureKeyStore/EnvKeyOverride.cs`
- Test: `TestProjects/UnityMCPTests/Assets/Tests/EditMode/AssetGen/EnvKeyOverrideTests.cs`

**Interfaces:**
- Produces: `public interface ISecureKeyStore { bool TryGet(string,out string); void Set(string,string); void Delete(string); bool Has(string); }`
- Produces: `internal static class SecureKeyStoreConstants { const string ServiceName="MCPForUnity.AssetGen"; static readonly string[] ProviderIds; }`
- Produces: `internal static class EnvKeyOverride { static string EnvVarName(string); static bool TryGet(string,out string); }` — env var `MCPFORUNITY_<PROVIDER>_API_KEY`.

**Steps:**

- [ ] **Step 1: Write the failing env-override test.** Create `EnvKeyOverrideTests.cs`:
  ```csharp
  using NUnit.Framework;
  using MCPForUnity.Editor.Security;

  namespace MCPForUnity.Editor.Tests.EditMode.AssetGen
  {
      [TestFixture]
      public class EnvKeyOverrideTests
      {
          private const string Var = "MCPFORUNITY_TRIPO_API_KEY";

          [TearDown]
          public void TearDown() => System.Environment.SetEnvironmentVariable(Var, null);

          [Test]
          public void EnvVarName_MapsProviderId_UpperWithApiKeySuffix()
          {
              Assert.AreEqual("MCPFORUNITY_TRIPO_API_KEY", EnvKeyOverride.EnvVarName("tripo"));
              Assert.AreEqual("MCPFORUNITY_OPENROUTER_API_KEY", EnvKeyOverride.EnvVarName("openrouter"));
          }

          [Test]
          public void TryGet_ReturnsValue_WhenEnvSet()
          {
              System.Environment.SetEnvironmentVariable(Var, "tsk_env_value_123");
              Assert.IsTrue(EnvKeyOverride.TryGet("tripo", out var v));
              Assert.AreEqual("tsk_env_value_123", v);
          }

          [Test]
          public void TryGet_ReturnsFalse_WhenEnvMissing()
          {
              System.Environment.SetEnvironmentVariable(Var, null);
              Assert.IsFalse(EnvKeyOverride.TryGet("tripo", out var v));
              Assert.IsNull(v);
          }
      }
  }
  ```

- [ ] **Step 2: Run it — expect FAIL.** Test Runner ▸ EditMode ▸ `EnvKeyOverrideTests`. Expected: red — `EnvKeyOverride` does not exist, EditMode assembly will not compile.

- [ ] **Step 3: Add the interface.** Create `ISecureKeyStore.cs`:
  ```csharp
  namespace MCPForUnity.Editor.Security
  {
      /// <summary>
      /// At-rest provider-key storage. Implementations persist into the OS secure store
      /// (Keychain / Credential Manager / libsecret) or the AES-256-GCM file fallback.
      /// Keys are read into a local only at the moment of an HTTP call and never serialized
      /// into job records, logs, or anything that crosses the MCP bridge.
      /// </summary>
      public interface ISecureKeyStore
      {
          bool TryGet(string providerId, out string apiKey);
          void Set(string providerId, string apiKey);
          void Delete(string providerId);
          bool Has(string providerId); // existence only; never returns the value
      }
  }
  ```

- [ ] **Step 4: Add the shared constants.** Create `SecureKeyStoreConstants.cs`:
  ```csharp
  namespace MCPForUnity.Editor.Security
  {
      internal static class SecureKeyStoreConstants
      {
          /// <summary>Service/account namespace under which every provider key is stored.</summary>
          public const string ServiceName = "MCPForUnity.AssetGen";

          /// <summary>Canonical provider ids (lowercase). Used by the redactor and the provider registry.</summary>
          public static readonly string[] ProviderIds =
              { "tripo", "meshy", "hunyuan", "sketchfab", "fal", "openrouter" };
      }
  }
  ```

- [ ] **Step 5: Add the env override.** Create `EnvKeyOverride.cs`:
  ```csharp
  using System;
  using System.Text;

  namespace MCPForUnity.Editor.Security
  {
      /// <summary>
      /// Read-only environment-variable override for provider keys. Resolution order across
      /// every key store is always: env -> persisted store. Variable name is
      /// MCPFORUNITY_&lt;PROVIDER&gt;_API_KEY (provider upper-cased, each non-alphanumeric
      /// char folded to '_'). Never persisted, never written back.
      /// </summary>
      internal static class EnvKeyOverride
      {
          public static string EnvVarName(string providerId)
          {
              var sb = new StringBuilder("MCPFORUNITY_");
              foreach (char c in (providerId ?? string.Empty).ToUpperInvariant())
                  sb.Append(char.IsLetterOrDigit(c) ? c : '_');
              sb.Append("_API_KEY");
              return sb.ToString();
          }

          public static bool TryGet(string providerId, out string apiKey)
          {
              apiKey = null;
              if (string.IsNullOrWhiteSpace(providerId)) return false;
              string val = Environment.GetEnvironmentVariable(EnvVarName(providerId));
              if (string.IsNullOrEmpty(val)) return false;
              apiKey = val;
              return true;
          }
      }
  }
  ```

- [ ] **Step 6: Run it — expect PASS.** Test Runner ▸ EditMode ▸ `EnvKeyOverrideTests` → all 3 green.

- [ ] **Step 7: Compile across the matrix.** Run `tools/check-unity-versions.sh` (no `#if` here, but confirms the new files compile on every editor). Expected: green.

- [ ] **Step 8: Commit.**
  ```bash
  git add MCPForUnity/Editor/Security \
          TestProjects/UnityMCPTests/Assets/Tests/EditMode/AssetGen
  git commit -m "feat(asset-gen): add ISecureKeyStore contract, constants, and env-var override

Adds the ISecureKeyStore interface, the shared service-name/provider-id constants,
and the read-only MCPFORUNITY_<PROVIDER>_API_KEY env override (env -> store).

Claude-Session: https://claude.ai/code/session_01Tjpb5gYgUe2AUJuRdXr7Lv"
  ```

---

### Task 1.2: EncryptedFileKeyStore (AES-256-GCM fallback)

**Files:**
- Create: `MCPForUnity/Editor/Security/SecureKeyStore/EncryptedFileKeyStore.cs`
- Test: `TestProjects/UnityMCPTests/Assets/Tests/EditMode/AssetGen/EncryptedFileKeyStoreTests.cs`

**Interfaces:**
- Consumes: `EnvKeyOverride.TryGet`, `SecureKeyStoreConstants` (none directly here, but same namespace).
- Produces: `public sealed class EncryptedFileKeyStore : ISecureKeyStore` with `public EncryptedFileKeyStore()` and a deterministic test ctor `public EncryptedFileKeyStore(string baseDir, string machineId)`.

**Steps:**

- [ ] **Step 1: Write the failing round-trip test.** Create `EncryptedFileKeyStoreTests.cs`:
  ```csharp
  using System.IO;
  using NUnit.Framework;
  using MCPForUnity.Editor.Security;

  namespace MCPForUnity.Editor.Tests.EditMode.AssetGen
  {
      [TestFixture]
      public class EncryptedFileKeyStoreTests
      {
          private string _dir;
          private EncryptedFileKeyStore _store;

          [SetUp]
          public void SetUp()
          {
              _dir = Path.Combine(Path.GetTempPath(), "mcp_keystore_" + System.Guid.NewGuid().ToString("N"));
              _store = new EncryptedFileKeyStore(_dir, "test-machine-id");
          }

          [TearDown]
          public void TearDown()
          {
              System.Environment.SetEnvironmentVariable("MCPFORUNITY_TRIPO_API_KEY", null);
              if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
          }

          [Test]
          public void SetGetDeleteHas_RoundTrips()
          {
              Assert.IsFalse(_store.Has("tripo"));
              Assert.IsFalse(_store.TryGet("tripo", out _));

              _store.Set("tripo", "tsk_secret_value_abc123");
              Assert.IsTrue(_store.Has("tripo"));
              Assert.IsTrue(_store.TryGet("tripo", out var got));
              Assert.AreEqual("tsk_secret_value_abc123", got);

              _store.Delete("tripo");
              Assert.IsFalse(_store.Has("tripo"));
              Assert.IsFalse(_store.TryGet("tripo", out _));
          }

          [Test]
          public void Ciphertext_OnDisk_DoesNotContainPlaintext()
          {
              _store.Set("tripo", "tsk_secret_value_abc123");
              string raw = File.ReadAllText(Path.Combine(_dir, "keystore.json"));
              StringAssert.DoesNotContain("tsk_secret_value_abc123", raw);
          }

          [Test]
          public void EnvOverride_TakesPrecedence_OverStoredValue()
          {
              _store.Set("tripo", "stored_value");
              System.Environment.SetEnvironmentVariable("MCPFORUNITY_TRIPO_API_KEY", "env_value");
              Assert.IsTrue(_store.TryGet("tripo", out var got));
              Assert.AreEqual("env_value", got);
              Assert.IsTrue(_store.Has("tripo"));
          }

          [Test]
          public void MultiSecretJsonBlob_RoundTrips_ForHunyuan()
          {
              string blob = "{\"secretId\":\"AKIDxxxxxxxx\",\"secretKey\":\"yyyyyyyyzzzz\"}";
              _store.Set("hunyuan", blob);
              Assert.IsTrue(_store.TryGet("hunyuan", out var got));
              Assert.AreEqual(blob, got);
          }

          [Test]
          public void NewInstance_SameDirAndMachineId_DecryptsExistingEntry()
          {
              _store.Set("meshy", "msy_persisted_key_value");
              var reopened = new EncryptedFileKeyStore(_dir, "test-machine-id");
              Assert.IsTrue(reopened.TryGet("meshy", out var got));
              Assert.AreEqual("msy_persisted_key_value", got);
          }

          [Test]
          public void Set_NullKey_Throws()
          {
              Assert.Throws<System.ArgumentNullException>(() => _store.Set("tripo", null));
          }
      }
  }
  ```

- [ ] **Step 2: Run it — expect FAIL.** Test Runner ▸ EditMode ▸ `EncryptedFileKeyStoreTests`. Expected: red — `EncryptedFileKeyStore` does not exist.

- [ ] **Step 3: Implement the store.** Create `EncryptedFileKeyStore.cs` (complete; AES-256-GCM, PBKDF2-derived key bound to a per-user random salt + machine id, ciphertext under user app-data, `chmod 600`):
  ```csharp
  using System;
  using System.Collections.Generic;
  using System.IO;
  using System.Runtime.InteropServices;
  using System.Security.Cryptography;
  using System.Text;
  using Newtonsoft.Json;

  namespace MCPForUnity.Editor.Security
  {
      /// <summary>
      /// AES-256-GCM file fallback used when no OS secure store is available. The data key is
      /// PBKDF2(SHA-256, "&lt;machineId&gt;:&lt;user&gt;", perUserRandomSalt). The 32-byte salt is
      /// generated once and stored next to the ciphertext under the user app-data dir
      /// (never under Assets/ or the repo), with files hardened to 0600 on Unix. Documented
      /// as weaker than an OS store. The env override still wins at get-time.
      /// </summary>
      public sealed class EncryptedFileKeyStore : ISecureKeyStore
      {
          private const int SaltBytes = 32;
          private const int NonceBytes = 12;
          private const int TagBytes = 16;
          private const int Pbkdf2Iterations = 100_000;

          private readonly object _gate = new();
          private readonly string _baseDir;
          private readonly string _saltPath;
          private readonly string _storePath;
          private readonly string _machineId;
          private byte[] _cachedKey;

          public EncryptedFileKeyStore() : this(DefaultBaseDir(), DefaultMachineId()) { }

          // Deterministic ctor for tests (explicit dir + machine id => CI-safe).
          public EncryptedFileKeyStore(string baseDir, string machineId)
          {
              _baseDir = baseDir;
              _machineId = string.IsNullOrEmpty(machineId) ? "unknown-machine" : machineId;
              _saltPath = Path.Combine(_baseDir, "keystore.salt");
              _storePath = Path.Combine(_baseDir, "keystore.json");
          }

          private static string DefaultBaseDir()
          {
              string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
              if (string.IsNullOrEmpty(appData))
                  appData = Path.Combine(
                      Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
              return Path.Combine(appData, "MCPForUnity", "AssetGen", "keystore");
          }

          private static string DefaultMachineId() =>
              Environment.MachineName + ":" + Environment.OSVersion.Platform;

          public bool TryGet(string providerId, out string apiKey)
          {
              // env override always wins, read-only.
              if (EnvKeyOverride.TryGet(providerId, out apiKey)) return true;
              apiKey = null;
              if (string.IsNullOrWhiteSpace(providerId)) return false;
              lock (_gate)
              {
                  var store = ReadStore();
                  if (!store.TryGetValue(providerId, out var b64) || string.IsNullOrEmpty(b64))
                      return false;
                  try
                  {
                      apiKey = Decrypt(Convert.FromBase64String(b64));
                      return true;
                  }
                  catch
                  {
                      return false; // corrupt / undecryptable entry (e.g. machine changed)
                  }
              }
          }

          public void Set(string providerId, string apiKey)
          {
              if (string.IsNullOrWhiteSpace(providerId))
                  throw new ArgumentException("providerId is required", nameof(providerId));
              if (apiKey == null)
                  throw new ArgumentNullException(nameof(apiKey));
              lock (_gate)
              {
                  var store = ReadStore();
                  store[providerId] = Convert.ToBase64String(Encrypt(apiKey));
                  WriteStore(store);
              }
          }

          public void Delete(string providerId)
          {
              if (string.IsNullOrWhiteSpace(providerId)) return;
              lock (_gate)
              {
                  var store = ReadStore();
                  if (store.Remove(providerId))
                      WriteStore(store);
              }
          }

          public bool Has(string providerId)
          {
              if (EnvKeyOverride.TryGet(providerId, out _)) return true;
              if (string.IsNullOrWhiteSpace(providerId)) return false;
              lock (_gate)
              {
                  return ReadStore().ContainsKey(providerId);
              }
          }

          // ---- crypto ----
          private byte[] Encrypt(string plaintext)
          {
              byte[] key = GetKey();
              byte[] pt = Encoding.UTF8.GetBytes(plaintext);
              byte[] nonce = new byte[NonceBytes];
              using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(nonce);
              byte[] ct = new byte[pt.Length];
              byte[] tag = new byte[TagBytes];
              try
              {
                  using var gcm = new AesGcm(key);
                  gcm.Encrypt(nonce, pt, ct, tag);
              }
              catch (PlatformNotSupportedException ex)
              {
                  throw new InvalidOperationException(
                      "AES-GCM is unavailable in this runtime. Use an OS secure store, or set the " +
                      "MCPFORUNITY_<PROVIDER>_API_KEY environment variable.", ex);
              }
              // layout: nonce(12) | tag(16) | ciphertext
              byte[] blob = new byte[NonceBytes + TagBytes + ct.Length];
              Buffer.BlockCopy(nonce, 0, blob, 0, NonceBytes);
              Buffer.BlockCopy(tag, 0, blob, NonceBytes, TagBytes);
              Buffer.BlockCopy(ct, 0, blob, NonceBytes + TagBytes, ct.Length);
              return blob;
          }

          private string Decrypt(byte[] blob)
          {
              if (blob == null || blob.Length < NonceBytes + TagBytes)
                  throw new CryptographicException("Ciphertext too short.");
              byte[] key = GetKey();
              byte[] nonce = new byte[NonceBytes];
              byte[] tag = new byte[TagBytes];
              byte[] ct = new byte[blob.Length - NonceBytes - TagBytes];
              Buffer.BlockCopy(blob, 0, nonce, 0, NonceBytes);
              Buffer.BlockCopy(blob, NonceBytes, tag, 0, TagBytes);
              Buffer.BlockCopy(blob, NonceBytes + TagBytes, ct, 0, ct.Length);
              byte[] pt = new byte[ct.Length];
              using var gcm = new AesGcm(key);
              gcm.Decrypt(nonce, ct, tag, pt);
              return Encoding.UTF8.GetString(pt);
          }

          private byte[] GetKey()
          {
              if (_cachedKey != null) return _cachedKey;
              byte[] salt = LoadOrCreateSalt();
              byte[] pw = Encoding.UTF8.GetBytes(_machineId + ":" + Environment.UserName);
              using var kdf = new Rfc2898DeriveBytes(pw, salt, Pbkdf2Iterations, HashAlgorithmName.SHA256);
              _cachedKey = kdf.GetBytes(32);
              return _cachedKey;
          }

          private byte[] LoadOrCreateSalt()
          {
              Directory.CreateDirectory(_baseDir);
              HardenDir(_baseDir);
              if (File.Exists(_saltPath))
              {
                  byte[] existing = File.ReadAllBytes(_saltPath);
                  if (existing.Length == SaltBytes) return existing;
              }
              byte[] salt = new byte[SaltBytes];
              using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(salt);
              File.WriteAllBytes(_saltPath, salt);
              HardenFile(_saltPath);
              return salt;
          }

          private Dictionary<string, string> ReadStore()
          {
              if (!File.Exists(_storePath)) return new Dictionary<string, string>();
              try
              {
                  string json = File.ReadAllText(_storePath);
                  return JsonConvert.DeserializeObject<Dictionary<string, string>>(json)
                         ?? new Dictionary<string, string>();
              }
              catch
              {
                  return new Dictionary<string, string>();
              }
          }

          private void WriteStore(Dictionary<string, string> store)
          {
              Directory.CreateDirectory(_baseDir);
              HardenDir(_baseDir);
              File.WriteAllText(_storePath, JsonConvert.SerializeObject(store));
              HardenFile(_storePath);
          }

          // ---- unix perms (no-op on Windows; NTFS user-profile ACL applies) ----
          private static void HardenFile(string path) => Chmod(path, "600");
          private static void HardenDir(string path) => Chmod(path, "700");

          private static void Chmod(string path, string mode)
          {
              if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;
              try
              {
                  var psi = new System.Diagnostics.ProcessStartInfo("/bin/chmod")
                  {
                      UseShellExecute = false,
                      CreateNoWindow = true,
                      RedirectStandardError = true
                  };
                  psi.ArgumentList.Add(mode);
                  psi.ArgumentList.Add(path);
                  using var p = System.Diagnostics.Process.Start(psi);
                  p?.WaitForExit(2000);
              }
              catch { /* best-effort hardening */ }
          }
      }
  }
  ```

- [ ] **Step 4: Run it — expect PASS.** Test Runner ▸ EditMode ▸ `EncryptedFileKeyStoreTests` → all 6 green. (`Ciphertext_OnDisk_DoesNotContainPlaintext` proves at-rest encryption; `EnvOverride_TakesPrecedence` proves env→store ordering; `MultiSecretJsonBlob_RoundTrips_ForHunyuan` proves the Hunyuan JSON blob survives intact.)

- [ ] **Step 5: Compile across the matrix.** `tools/check-unity-versions.sh`. Expected: green on every editor (confirms `AesGcm`/`Rfc2898DeriveBytes`/`ArgumentList` resolve at the project's NS2.1 API level).

- [ ] **Step 6: Commit.**
  ```bash
  git add MCPForUnity/Editor/Security/SecureKeyStore/EncryptedFileKeyStore.cs* \
          TestProjects/UnityMCPTests/Assets/Tests/EditMode/AssetGen/EncryptedFileKeyStoreTests.cs*
  git commit -m "feat(asset-gen): add AES-256-GCM EncryptedFileKeyStore fallback

PBKDF2(SHA-256)-derived key bound to a per-user random salt + machine id; ciphertext
under the user app-data dir (0600), never under Assets/. Env override wins at get-time.
Round-trip, at-rest-encryption, env-precedence, and Hunyuan-JSON-blob tests pass.

Claude-Session: https://claude.ai/code/session_01Tjpb5gYgUe2AUJuRdXr7Lv"
  ```

---

### Task 1.3: macOS Keychain key store

**Files:**
- Create: `MCPForUnity/Editor/Security/SecureKeyStore/MacKeychainKeyStore.cs`
- Test: `TestProjects/UnityMCPTests/Assets/Tests/EditMode/AssetGen/MacKeychainKeyStoreTests.cs`

**Interfaces:**
- Consumes: `SecureKeyStoreConstants.ServiceName`, `EnvKeyOverride.TryGet`.
- Produces: `public sealed class MacKeychainKeyStore : ISecureKeyStore` + `static bool IsAvailable()`.

**Steps:**

- [ ] **Step 1: Write the explicit OS round-trip test.** Create `MacKeychainKeyStoreTests.cs`. It touches the real Keychain so it is `[Explicit]` (skipped in CI / on other platforms; run manually on a macOS editor):
  ```csharp
  using NUnit.Framework;
  using MCPForUnity.Editor.Security;

  namespace MCPForUnity.Editor.Tests.EditMode.AssetGen
  {
      // Writes to the real macOS Keychain. [Explicit] => not run in CI; run manually on macOS.
      [TestFixture]
      [Explicit("Writes to the real macOS Keychain; run manually on macOS.")]
      public class MacKeychainKeyStoreTests
      {
          private const string Provider = "mcp_test_provider";
          private MacKeychainKeyStore _store;

          [SetUp]
          public void SetUp()
          {
              if (!MacKeychainKeyStore.IsAvailable()) Assert.Ignore("/usr/bin/security not present");
              _store = new MacKeychainKeyStore();
              _store.Delete(Provider);
          }

          [TearDown]
          public void TearDown() => _store?.Delete(Provider);

          [Test]
          public void SetGetDeleteHas_RoundTrips()
          {
              Assert.IsFalse(_store.Has(Provider));
              _store.Set(Provider, "tsk_keychain_roundtrip_1");
              Assert.IsTrue(_store.Has(Provider));
              Assert.IsTrue(_store.TryGet(Provider, out var got));
              Assert.AreEqual("tsk_keychain_roundtrip_1", got);
              _store.Delete(Provider);
              Assert.IsFalse(_store.Has(Provider));
          }
      }
  }
  ```

- [ ] **Step 2: Run it — expect FAIL.** Test Runner ▸ EditMode. Expected: red — `MacKeychainKeyStore` does not exist, assembly will not compile.

- [ ] **Step 3: Implement the store.** Create `MacKeychainKeyStore.cs` (complete; `/usr/bin/security` generic passwords via `System.Diagnostics.Process`, args via `ArgumentList` to avoid quoting/injection):
  ```csharp
  using System;
  using System.Diagnostics;
  using System.IO;

  namespace MCPForUnity.Editor.Security
  {
      /// <summary>
      /// macOS Keychain generic-password store via /usr/bin/security. Service =
      /// MCPForUnity.AssetGen, account = providerId. Note: Set passes the key as a -w
      /// argument, briefly visible to a same-user `ps`; this is inside the documented
      /// same-OS-user threat boundary and still far stronger than plaintext EditorPrefs.
      /// </summary>
      public sealed class MacKeychainKeyStore : ISecureKeyStore
      {
          private const string SecurityBin = "/usr/bin/security";

          public static bool IsAvailable() => File.Exists(SecurityBin);

          public bool TryGet(string providerId, out string apiKey)
          {
              if (EnvKeyOverride.TryGet(providerId, out apiKey)) return true;
              apiKey = null;
              if (string.IsNullOrWhiteSpace(providerId)) return false;
              int code = Run(out string stdout, out _,
                  "find-generic-password", "-s", SecureKeyStoreConstants.ServiceName,
                  "-a", providerId, "-w");
              if (code != 0) return false; // 44 = errSecItemNotFound
              apiKey = stdout.TrimEnd('\n', '\r');
              return true;
          }

          public void Set(string providerId, string apiKey)
          {
              if (string.IsNullOrWhiteSpace(providerId))
                  throw new ArgumentException("providerId is required", nameof(providerId));
              if (apiKey == null) throw new ArgumentNullException(nameof(apiKey));
              // -U updates the item in place if it already exists.
              int code = Run(out _, out string stderr,
                  "add-generic-password", "-U", "-s", SecureKeyStoreConstants.ServiceName,
                  "-a", providerId, "-w", apiKey);
              if (code != 0)
                  throw new InvalidOperationException(
                      $"Keychain add-generic-password failed (exit {code}): {stderr}");
          }

          public void Delete(string providerId)
          {
              if (string.IsNullOrWhiteSpace(providerId)) return;
              Run(out _, out _, "delete-generic-password",
                  "-s", SecureKeyStoreConstants.ServiceName, "-a", providerId);
          }

          public bool Has(string providerId)
          {
              if (EnvKeyOverride.TryGet(providerId, out _)) return true;
              if (string.IsNullOrWhiteSpace(providerId)) return false;
              // No -w => password is not emitted; exit 0 means the item exists.
              return Run(out _, out _, "find-generic-password",
                  "-s", SecureKeyStoreConstants.ServiceName, "-a", providerId) == 0;
          }

          private static int Run(out string stdout, out string stderr, params string[] args)
          {
              var psi = new ProcessStartInfo(SecurityBin)
              {
                  UseShellExecute = false,
                  CreateNoWindow = true,
                  RedirectStandardOutput = true,
                  RedirectStandardError = true
              };
              foreach (var a in args) psi.ArgumentList.Add(a);
              using var p = Process.Start(psi);
              stdout = p.StandardOutput.ReadToEnd();
              stderr = p.StandardError.ReadToEnd();
              p.WaitForExit(5000);
              return p.HasExited ? p.ExitCode : -1;
          }
      }
  }
  ```

- [ ] **Step 4: Verify.** Compile-only here (CI cannot exercise the Keychain). On a macOS editor, optionally run Test Runner ▸ EditMode ▸ `MacKeychainKeyStoreTests` with "Run Explicit" enabled → green. Then `tools/check-unity-versions.sh` → green (class compiles on all platforms; `Process` calls are inert off-macOS).

- [ ] **Step 5: Commit.**
  ```bash
  git add MCPForUnity/Editor/Security/SecureKeyStore/MacKeychainKeyStore.cs* \
          TestProjects/UnityMCPTests/Assets/Tests/EditMode/AssetGen/MacKeychainKeyStoreTests.cs*
  git commit -m "feat(asset-gen): add macOS Keychain key store

Generic-password store over /usr/bin/security (service MCPForUnity.AssetGen, account
providerId) with env override at get-time. Explicit round-trip test runs manually on macOS.

Claude-Session: https://claude.ai/code/session_01Tjpb5gYgUe2AUJuRdXr7Lv"
  ```

---

### Task 1.4: Windows Credential Manager key store

**Files:**
- Create: `MCPForUnity/Editor/Security/SecureKeyStore/WindowsCredentialKeyStore.cs`
- Test: `TestProjects/UnityMCPTests/Assets/Tests/EditMode/AssetGen/WindowsCredentialKeyStoreTests.cs`

**Interfaces:**
- Consumes: `SecureKeyStoreConstants.ServiceName`, `EnvKeyOverride.TryGet`.
- Produces: `public sealed class WindowsCredentialKeyStore : ISecureKeyStore` (advapi32 P/Invoke `CredWrite`/`CredRead`/`CredDelete`/`CredFree`, `CRED_TYPE_GENERIC`).

**Steps:**

- [ ] **Step 1: Write the explicit OS round-trip test.** Create `WindowsCredentialKeyStoreTests.cs`:
  ```csharp
  using NUnit.Framework;
  using MCPForUnity.Editor.Security;

  namespace MCPForUnity.Editor.Tests.EditMode.AssetGen
  {
      // Writes to the real Windows Credential Manager. [Explicit] => not run in CI; run manually on Windows.
      [TestFixture]
      [Explicit("Writes to the real Windows Credential Manager; run manually on Windows.")]
      public class WindowsCredentialKeyStoreTests
      {
          private const string Provider = "mcp_test_provider";
          private WindowsCredentialKeyStore _store;

          [SetUp]
          public void SetUp()
          {
              if (System.Environment.OSVersion.Platform != System.PlatformID.Win32NT)
                  Assert.Ignore("Not running on Windows");
              _store = new WindowsCredentialKeyStore();
              _store.Delete(Provider);
          }

          [TearDown]
          public void TearDown() => _store?.Delete(Provider);

          [Test]
          public void SetGetDeleteHas_RoundTrips()
          {
              Assert.IsFalse(_store.Has(Provider));
              _store.Set(Provider, "tsk_credman_roundtrip_1");
              Assert.IsTrue(_store.Has(Provider));
              Assert.IsTrue(_store.TryGet(Provider, out var got));
              Assert.AreEqual("tsk_credman_roundtrip_1", got);
              _store.Delete(Provider);
              Assert.IsFalse(_store.Has(Provider));
          }
      }
  }
  ```

- [ ] **Step 2: Run it — expect FAIL.** Test Runner ▸ EditMode. Expected: red — `WindowsCredentialKeyStore` does not exist.

- [ ] **Step 3: Implement the store.** Create `WindowsCredentialKeyStore.cs` (complete P/Invoke; compiles on all platforms because DllImport binds lazily — only instantiated on Windows by the factory):
  ```csharp
  using System;
  using System.Runtime.InteropServices;
  using System.Text;

  namespace MCPForUnity.Editor.Security
  {
      /// <summary>
      /// Windows Credential Manager generic-credential store via advapi32. Target =
      /// "MCPForUnity.AssetGen:&lt;provider&gt;", DPAPI-protected by the OS for the current user.
      /// Env override wins at get-time.
      /// </summary>
      public sealed class WindowsCredentialKeyStore : ISecureKeyStore
      {
          private const uint CRED_TYPE_GENERIC = 1;
          private const uint CRED_PERSIST_LOCAL_MACHINE = 2;

          private static string Target(string providerId) =>
              SecureKeyStoreConstants.ServiceName + ":" + providerId;

          public bool TryGet(string providerId, out string apiKey)
          {
              if (EnvKeyOverride.TryGet(providerId, out apiKey)) return true;
              apiKey = null;
              if (string.IsNullOrWhiteSpace(providerId)) return false;
              if (!CredRead(Target(providerId), CRED_TYPE_GENERIC, 0, out IntPtr credPtr))
                  return false;
              try
              {
                  var cred = Marshal.PtrToStructure<CREDENTIAL>(credPtr);
                  if (cred.CredentialBlob == IntPtr.Zero || cred.CredentialBlobSize == 0)
                  {
                      apiKey = string.Empty;
                      return true;
                  }
                  byte[] data = new byte[cred.CredentialBlobSize];
                  Marshal.Copy(cred.CredentialBlob, data, 0, (int)cred.CredentialBlobSize);
                  apiKey = Encoding.UTF8.GetString(data);
                  return true;
              }
              finally
              {
                  CredFree(credPtr);
              }
          }

          public void Set(string providerId, string apiKey)
          {
              if (string.IsNullOrWhiteSpace(providerId))
                  throw new ArgumentException("providerId is required", nameof(providerId));
              if (apiKey == null) throw new ArgumentNullException(nameof(apiKey));
              byte[] blob = Encoding.UTF8.GetBytes(apiKey);
              IntPtr blobPtr = Marshal.AllocHGlobal(blob.Length == 0 ? 1 : blob.Length);
              try
              {
                  if (blob.Length > 0) Marshal.Copy(blob, 0, blobPtr, blob.Length);
                  var cred = new CREDENTIAL
                  {
                      Type = CRED_TYPE_GENERIC,
                      TargetName = Target(providerId),
                      CredentialBlobSize = (uint)blob.Length,
                      CredentialBlob = blobPtr,
                      Persist = CRED_PERSIST_LOCAL_MACHINE,
                      UserName = providerId
                  };
                  if (!CredWrite(ref cred, 0))
                      throw new InvalidOperationException(
                          $"CredWrite failed: Win32 error {Marshal.GetLastWin32Error()}");
              }
              finally
              {
                  Marshal.FreeHGlobal(blobPtr);
              }
          }

          public void Delete(string providerId)
          {
              if (string.IsNullOrWhiteSpace(providerId)) return;
              CredDelete(Target(providerId), CRED_TYPE_GENERIC, 0);
          }

          public bool Has(string providerId)
          {
              if (EnvKeyOverride.TryGet(providerId, out _)) return true;
              if (string.IsNullOrWhiteSpace(providerId)) return false;
              if (!CredRead(Target(providerId), CRED_TYPE_GENERIC, 0, out IntPtr credPtr))
                  return false;
              CredFree(credPtr);
              return true;
          }

          [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
          private struct CREDENTIAL
          {
              public uint Flags;
              public uint Type;
              [MarshalAs(UnmanagedType.LPWStr)] public string TargetName;
              [MarshalAs(UnmanagedType.LPWStr)] public string Comment;
              public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
              public uint CredentialBlobSize;
              public IntPtr CredentialBlob;
              public uint Persist;
              public uint AttributeCount;
              public IntPtr Attributes;
              [MarshalAs(UnmanagedType.LPWStr)] public string TargetAlias;
              [MarshalAs(UnmanagedType.LPWStr)] public string UserName;
          }

          [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
          private static extern bool CredWrite(ref CREDENTIAL credential, uint flags);

          [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
          private static extern bool CredRead(string target, uint type, uint reservedFlag, out IntPtr credentialPtr);

          [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
          private static extern bool CredDelete(string target, uint type, uint flags);

          [DllImport("advapi32.dll", EntryPoint = "CredFree")]
          private static extern void CredFree(IntPtr buffer);
      }
  }
  ```

- [ ] **Step 4: Verify.** `tools/check-unity-versions.sh` → green (the file compiles on macOS/Linux editors too; advapi32 only resolves when called on Windows). On a Windows editor, optionally run `WindowsCredentialKeyStoreTests` with Explicit enabled → green.

- [ ] **Step 5: Commit.**
  ```bash
  git add MCPForUnity/Editor/Security/SecureKeyStore/WindowsCredentialKeyStore.cs* \
          TestProjects/UnityMCPTests/Assets/Tests/EditMode/AssetGen/WindowsCredentialKeyStoreTests.cs*
  git commit -m "feat(asset-gen): add Windows Credential Manager key store

advapi32 CredWrite/CredRead/CredDelete generic credentials (target MCPForUnity.AssetGen:<provider>),
DPAPI-backed, with env override at get-time. Explicit round-trip test runs manually on Windows.

Claude-Session: https://claude.ai/code/session_01Tjpb5gYgUe2AUJuRdXr7Lv"
  ```

---

### Task 1.5: Linux secret-tool key store

**Files:**
- Create: `MCPForUnity/Editor/Security/SecureKeyStore/LinuxSecretToolKeyStore.cs`
- Test: `TestProjects/UnityMCPTests/Assets/Tests/EditMode/AssetGen/LinuxSecretToolKeyStoreTests.cs`

**Interfaces:**
- Consumes: `SecureKeyStoreConstants.ServiceName`, `EnvKeyOverride.TryGet`.
- Produces: `public sealed class LinuxSecretToolKeyStore : ISecureKeyStore` + `static bool IsAvailable()`.

**Steps:**

- [ ] **Step 1: Write the explicit OS round-trip test.** Create `LinuxSecretToolKeyStoreTests.cs`:
  ```csharp
  using NUnit.Framework;
  using MCPForUnity.Editor.Security;

  namespace MCPForUnity.Editor.Tests.EditMode.AssetGen
  {
      // Writes to the real libsecret store via secret-tool. [Explicit] => not run in CI; run manually on Linux.
      [TestFixture]
      [Explicit("Writes to the real libsecret store; run manually on Linux with secret-tool + an unlocked keyring.")]
      public class LinuxSecretToolKeyStoreTests
      {
          private const string Provider = "mcp_test_provider";
          private LinuxSecretToolKeyStore _store;

          [SetUp]
          public void SetUp()
          {
              if (!LinuxSecretToolKeyStore.IsAvailable()) Assert.Ignore("secret-tool not present");
              _store = new LinuxSecretToolKeyStore();
              _store.Delete(Provider);
          }

          [TearDown]
          public void TearDown() => _store?.Delete(Provider);

          [Test]
          public void SetGetDeleteHas_RoundTrips()
          {
              Assert.IsFalse(_store.Has(Provider));
              _store.Set(Provider, "tsk_secrettool_roundtrip_1");
              Assert.IsTrue(_store.Has(Provider));
              Assert.IsTrue(_store.TryGet(Provider, out var got));
              Assert.AreEqual("tsk_secrettool_roundtrip_1", got);
              _store.Delete(Provider);
              Assert.IsFalse(_store.Has(Provider));
          }
      }
  }
  ```

- [ ] **Step 2: Run it — expect FAIL.** Test Runner ▸ EditMode. Expected: red — `LinuxSecretToolKeyStore` does not exist.

- [ ] **Step 3: Implement the store.** Create `LinuxSecretToolKeyStore.cs` (complete; the secret is passed on **stdin**, never in process args):
  ```csharp
  using System;
  using System.Diagnostics;

  namespace MCPForUnity.Editor.Security
  {
      /// <summary>
      /// Linux libsecret store via the `secret-tool` CLI. Attributes:
      /// service=MCPForUnity.AssetGen, account=providerId. The secret is written to stdin
      /// (never visible in process args). Env override wins at get-time.
      /// </summary>
      public sealed class LinuxSecretToolKeyStore : ISecureKeyStore
      {
          public static bool IsAvailable()
          {
              try
              {
                  return Run(null, out _, out _, "--version") == 0;
              }
              catch
              {
                  return false;
              }
          }

          public bool TryGet(string providerId, out string apiKey)
          {
              if (EnvKeyOverride.TryGet(providerId, out apiKey)) return true;
              apiKey = null;
              if (string.IsNullOrWhiteSpace(providerId)) return false;
              int code = Run(null, out string stdout, out _,
                  "lookup", "service", SecureKeyStoreConstants.ServiceName, "account", providerId);
              if (code != 0) return false; // 1 = not found
              apiKey = stdout.TrimEnd('\n', '\r');
              return true;
          }

          public void Set(string providerId, string apiKey)
          {
              if (string.IsNullOrWhiteSpace(providerId))
                  throw new ArgumentException("providerId is required", nameof(providerId));
              if (apiKey == null) throw new ArgumentNullException(nameof(apiKey));
              int code = Run(apiKey, out _, out string stderr,
                  "store", "--label", SecureKeyStoreConstants.ServiceName + " " + providerId,
                  "service", SecureKeyStoreConstants.ServiceName, "account", providerId);
              if (code != 0)
                  throw new InvalidOperationException(
                      $"secret-tool store failed (exit {code}): {stderr}");
          }

          public void Delete(string providerId)
          {
              if (string.IsNullOrWhiteSpace(providerId)) return;
              Run(null, out _, out _,
                  "clear", "service", SecureKeyStoreConstants.ServiceName, "account", providerId);
          }

          public bool Has(string providerId)
          {
              if (EnvKeyOverride.TryGet(providerId, out _)) return true;
              if (string.IsNullOrWhiteSpace(providerId)) return false;
              return Run(null, out _, out _,
                  "lookup", "service", SecureKeyStoreConstants.ServiceName, "account", providerId) == 0;
          }

          private static int Run(string stdin, out string stdout, out string stderr, params string[] args)
          {
              var psi = new ProcessStartInfo("secret-tool")
              {
                  UseShellExecute = false,
                  CreateNoWindow = true,
                  RedirectStandardInput = stdin != null,
                  RedirectStandardOutput = true,
                  RedirectStandardError = true
              };
              foreach (var a in args) psi.ArgumentList.Add(a);
              using var p = Process.Start(psi);
              if (stdin != null)
              {
                  p.StandardInput.Write(stdin);
                  p.StandardInput.Close();
              }
              stdout = p.StandardOutput.ReadToEnd();
              stderr = p.StandardError.ReadToEnd();
              p.WaitForExit(5000);
              return p.HasExited ? p.ExitCode : -1;
          }
      }
  }
  ```

- [ ] **Step 4: Verify.** `tools/check-unity-versions.sh` → green. On a Linux editor with `secret-tool` + an unlocked keyring, optionally run `LinuxSecretToolKeyStoreTests` with Explicit enabled → green.

- [ ] **Step 5: Commit.**
  ```bash
  git add MCPForUnity/Editor/Security/SecureKeyStore/LinuxSecretToolKeyStore.cs* \
          TestProjects/UnityMCPTests/Assets/Tests/EditMode/AssetGen/LinuxSecretToolKeyStoreTests.cs*
  git commit -m "feat(asset-gen): add Linux secret-tool key store

libsecret store via secret-tool (service MCPForUnity.AssetGen, account providerId); the
secret is passed on stdin, never in argv. Env override at get-time. Explicit test on Linux.

Claude-Session: https://claude.ai/code/session_01Tjpb5gYgUe2AUJuRdXr7Lv"
  ```

---

### Task 1.6: Platform-selecting SecureKeyStore factory

**Files:**
- Create: `MCPForUnity/Editor/Security/SecureKeyStore/SecureKeyStore.cs`
- Test: `TestProjects/UnityMCPTests/Assets/Tests/EditMode/AssetGen/SecureKeyStoreFactoryTests.cs`

**Interfaces:**
- Consumes: `MacKeychainKeyStore`, `WindowsCredentialKeyStore`, `LinuxSecretToolKeyStore`, `EncryptedFileKeyStore`, `McpLog.Warn`.
- Produces: `public static class SecureKeyStore { public static ISecureKeyStore Current { get; } }` + internal `SetForTesting`/`ResetForTesting` seams.

**Steps:**

- [ ] **Step 1: Write the failing factory test.** Create `SecureKeyStoreFactoryTests.cs`:
  ```csharp
  using NUnit.Framework;
  using MCPForUnity.Editor.Security;

  namespace MCPForUnity.Editor.Tests.EditMode.AssetGen
  {
      [TestFixture]
      public class SecureKeyStoreFactoryTests
      {
          [TearDown]
          public void TearDown() => SecureKeyStore.ResetForTesting();

          [Test]
          public void Current_IsNeverNull()
          {
              Assert.IsNotNull(SecureKeyStore.Current);
          }

          [Test]
          public void SetForTesting_OverridesCurrent_AndResetRestores()
          {
              var dir = System.IO.Path.Combine(
                  System.IO.Path.GetTempPath(), "mcp_factory_" + System.Guid.NewGuid().ToString("N"));
              var fake = new EncryptedFileKeyStore(dir, "test-machine-id");
              SecureKeyStore.SetForTesting(fake);
              Assert.AreSame(fake, SecureKeyStore.Current);
              SecureKeyStore.ResetForTesting();
              Assert.AreNotSame(fake, SecureKeyStore.Current);
          }
      }
  }
  ```

- [ ] **Step 2: Run it — expect FAIL.** Test Runner ▸ EditMode ▸ `SecureKeyStoreFactoryTests`. Expected: red — `SecureKeyStore` does not exist.

- [ ] **Step 3: Implement the factory.** Create `SecureKeyStore.cs`. All four store types now exist (Tasks 1.2–1.5), so the `#if`-guarded branches compile on every platform:
  ```csharp
  using System;
  using MCPForUnity.Editor.Helpers;

  namespace MCPForUnity.Editor.Security
  {
      /// <summary>
      /// Platform-selecting factory for <see cref="ISecureKeyStore"/>:
      /// macOS -> Keychain, Windows -> Credential Manager, Linux -> secret-tool, otherwise the
      /// AES-256-GCM <see cref="EncryptedFileKeyStore"/> fallback. The selected store is cached
      /// for the editor session. There is intentionally no "read key" tool/action anywhere.
      /// </summary>
      public static class SecureKeyStore
      {
          private static readonly object Gate = new();
          private static ISecureKeyStore _current;
          private static ISecureKeyStore _testOverride;

          public static ISecureKeyStore Current
          {
              get
              {
                  if (_testOverride != null) return _testOverride;
                  if (_current != null) return _current;
                  lock (Gate)
                  {
                      _current ??= Create();
                      return _current;
                  }
              }
          }

          private static ISecureKeyStore Create()
          {
              try
              {
  #if UNITY_EDITOR_OSX
                  if (MacKeychainKeyStore.IsAvailable()) return new MacKeychainKeyStore();
  #elif UNITY_EDITOR_WIN
                  return new WindowsCredentialKeyStore();
  #elif UNITY_EDITOR_LINUX
                  if (LinuxSecretToolKeyStore.IsAvailable()) return new LinuxSecretToolKeyStore();
  #endif
              }
              catch (Exception ex)
              {
                  McpLog.Warn($"[SecureKeyStore] OS store unavailable, using encrypted file fallback: {ex.Message}");
              }
              return new EncryptedFileKeyStore();
          }

          // ---- Test seams (MCPForUnityTests.EditMode has InternalsVisibleTo access) ----
          internal static void SetForTesting(ISecureKeyStore store) => _testOverride = store;
          internal static void ResetForTesting() => _testOverride = null;
      }
  }
  ```

- [ ] **Step 4: Run it — expect PASS.** Test Runner ▸ EditMode ▸ `SecureKeyStoreFactoryTests` → both green.

- [ ] **Step 5: Compile across the matrix.** `tools/check-unity-versions.sh` — this is the first file with `#if UNITY_EDITOR_*` branches, so verify all branches compile on every editor in `tools/unity-versions.json`. Expected: green.

- [ ] **Step 6: Commit.**
  ```bash
  git add MCPForUnity/Editor/Security/SecureKeyStore/SecureKeyStore.cs* \
          TestProjects/UnityMCPTests/Assets/Tests/EditMode/AssetGen/SecureKeyStoreFactoryTests.cs*
  git commit -m "feat(asset-gen): add platform-selecting SecureKeyStore factory

SecureKeyStore.Current picks Keychain/CredMan/secret-tool by platform and falls back to the
AES-256-GCM file store; selection is cached. Adds internal SetForTesting/ResetForTesting seams.

Claude-Session: https://claude.ai/code/session_01Tjpb5gYgUe2AUJuRdXr7Lv"
  ```

---

### Task 1.7: SecretRedactor (log/error scrubbing)

**Files:**
- Create: `MCPForUnity/Editor/Security/SecureKeyStore/SecretRedactor.cs`
- Test: `TestProjects/UnityMCPTests/Assets/Tests/EditMode/AssetGen/SecretRedactorTests.cs`

**Interfaces:**
- Consumes: `SecureKeyStore.Current.TryGet`, `SecureKeyStoreConstants.ProviderIds`.
- Produces: `public static class SecretRedactor { public static string Scrub(string text); }` — strips known stored key values (incl. Hunyuan inner JSON secrets) plus bearer/token/key auth headers and key-shaped literals.

**Steps:**

- [ ] **Step 1: Write the failing redactor test.** Create `SecretRedactorTests.cs`:
  ```csharp
  using System.IO;
  using NUnit.Framework;
  using MCPForUnity.Editor.Security;

  namespace MCPForUnity.Editor.Tests.EditMode.AssetGen
  {
      [TestFixture]
      public class SecretRedactorTests
      {
          private string _dir;

          [SetUp]
          public void SetUp()
          {
              _dir = Path.Combine(Path.GetTempPath(), "mcp_redactor_" + System.Guid.NewGuid().ToString("N"));
              SecureKeyStore.SetForTesting(new EncryptedFileKeyStore(_dir, "test-machine-id"));
          }

          [TearDown]
          public void TearDown()
          {
              SecureKeyStore.ResetForTesting();
              if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
          }

          [Test]
          public void Scrub_StripsStoredKeyValue()
          {
              SecureKeyStore.Current.Set("tripo", "tsk_super_secret_value_42");
              string redacted = SecretRedactor.Scrub("request failed with key tsk_super_secret_value_42 attached");
              StringAssert.DoesNotContain("tsk_super_secret_value_42", redacted);
              StringAssert.Contains("***REDACTED***", redacted);
          }

          [Test]
          public void Scrub_StripsHunyuanInnerSecrets()
          {
              SecureKeyStore.Current.Set("hunyuan",
                  "{\"secretId\":\"AKIDexample01\",\"secretKey\":\"superSecretKey99\"}");
              string redacted = SecretRedactor.Scrub("signing with AKIDexample01 and superSecretKey99 now");
              StringAssert.DoesNotContain("AKIDexample01", redacted);
              StringAssert.DoesNotContain("superSecretKey99", redacted);
          }

          [Test]
          public void Scrub_StripsBearerToken_EvenWhenNotStored()
          {
              string redacted = SecretRedactor.Scrub("Authorization: Bearer abcd1234efgh5678ijkl");
              StringAssert.DoesNotContain("abcd1234efgh5678ijkl", redacted);
              StringAssert.Contains("***REDACTED***", redacted);
          }

          [Test]
          public void Scrub_NullOrEmpty_Passthrough()
          {
              Assert.IsNull(SecretRedactor.Scrub(null));
              Assert.AreEqual("", SecretRedactor.Scrub(""));
          }
      }
  }
  ```

- [ ] **Step 2: Run it — expect FAIL.** Test Runner ▸ EditMode ▸ `SecretRedactorTests`. Expected: red — `SecretRedactor` does not exist.

- [ ] **Step 3: Implement the redactor.** Create `SecretRedactor.cs`:
  ```csharp
  using System.Collections.Generic;
  using System.Text.RegularExpressions;
  using Newtonsoft.Json.Linq;

  namespace MCPForUnity.Editor.Security
  {
      /// <summary>
      /// Scrubs known provider key values and auth/bearer/token literals out of any text before
      /// it is logged, surfaced in an error, or returned to an agent. Never throws.
      /// </summary>
      public static class SecretRedactor
      {
          private const string Mask = "***REDACTED***";

          // Authorization: Bearer xxx  /  Authorization: Token xxx  /  Authorization: Key xxx
          private static readonly Regex AuthHeaderRegex = new Regex(
              @"(?i)(authorization\s*[:=]\s*)(bearer|token|key)\s+[A-Za-z0-9._\-]+",
              RegexOptions.Compiled);

          // Common provider key shapes: tsk_ (Tripo), sk-/sk_ (OpenRouter etc.), msy_ (Meshy), key- (fal).
          private static readonly Regex TokenLiteralRegex = new Regex(
              @"(?i)\b(tsk_|sk-|sk_|msy_|key-)[A-Za-z0-9._\-]{8,}",
              RegexOptions.Compiled);

          public static string Scrub(string text)
          {
              if (string.IsNullOrEmpty(text)) return text;
              string result = text;

              // 1) Strip any concrete stored key values we know about.
              foreach (var id in SecureKeyStoreConstants.ProviderIds)
              {
                  try
                  {
                      if (!SecureKeyStore.Current.TryGet(id, out var key)) continue;
                      if (string.IsNullOrEmpty(key) || key.Length < 6) continue;
                      result = result.Replace(key, Mask);
                      // Hunyuan stores a JSON blob; redact its inner secret values too.
                      foreach (var inner in ExtractJsonStringValues(key))
                          if (inner != null && inner.Length >= 6) result = result.Replace(inner, Mask);
                  }
                  catch
                  {
                      // redaction must never throw — fall through to pattern scrubbing
                  }
              }

              // 2) Strip auth headers + common key-shaped literals.
              result = AuthHeaderRegex.Replace(result, "$1$2 " + Mask);
              result = TokenLiteralRegex.Replace(result, Mask);
              return result;
          }

          private static IEnumerable<string> ExtractJsonStringValues(string maybeJson)
          {
              var values = new List<string>();
              if (string.IsNullOrEmpty(maybeJson) || maybeJson[0] != '{') return values;
              try
              {
                  var obj = JObject.Parse(maybeJson);
                  foreach (var prop in obj.Properties())
                      if (prop.Value.Type == JTokenType.String)
                          values.Add(prop.Value.Value<string>());
              }
              catch
              {
                  // not JSON — nothing extra to extract
              }
              return values;
          }
      }
  }
  ```

- [ ] **Step 4: Run it — expect PASS.** Test Runner ▸ EditMode ▸ `SecretRedactorTests` → all 4 green.

- [ ] **Step 5: Compile across the matrix.** `tools/check-unity-versions.sh` → green.

- [ ] **Step 6: Commit.**
  ```bash
  git add MCPForUnity/Editor/Security/SecureKeyStore/SecretRedactor.cs* \
          TestProjects/UnityMCPTests/Assets/Tests/EditMode/AssetGen/SecretRedactorTests.cs*
  git commit -m "feat(asset-gen): add SecretRedactor for log/error scrubbing

Scrubs stored provider keys (incl. Hunyuan inner JSON secrets) and bearer/token/key auth
literals from any text before it is logged or surfaced. Never throws.

Claude-Session: https://claude.ai/code/session_01Tjpb5gYgUe2AUJuRdXr7Lv"
  ```

---

### Task 1.8: Guard — a serialized job record can never contain a key

**Files:**
- Test: `TestProjects/UnityMCPTests/Assets/Tests/EditMode/AssetGen/AssetGenJobKeyLeakGuardTests.cs`

**Interfaces:**
- Consumes: `SecureKeyStore.SetForTesting`/`Current.Set`, `SecretRedactor.Scrub`, `Newtonsoft.Json.JsonConvert`.
- Produces: a regression guard locking in the security invariant that the job-record shape carries no secret field, and that any accidental embedding in an error string is redacted.

This task is test-only — it codifies the contract that `AssetGenJob` (built in the job-manager phase) has exactly the fields `{ JobId, Kind, Provider, Action, State, Progress, AssetPath, AssetGuid, Error }`, **none** of which is a key. The inline anonymous object below mirrors that field set so the guard runs now; when the real `AssetGenJob` type lands, swap the anonymous object for it and the assertions are unchanged.

**Steps:**

- [ ] **Step 1: Write the guard test.** Create `AssetGenJobKeyLeakGuardTests.cs`:
  ```csharp
  using System.IO;
  using NUnit.Framework;
  using Newtonsoft.Json;
  using MCPForUnity.Editor.Security;

  namespace MCPForUnity.Editor.Tests.EditMode.AssetGen
  {
      /// <summary>
      /// Locks in the security invariant that a serialized job record can never contain a
      /// provider key. The inline object mirrors the AssetGenJob contract field set
      /// (JobId, Kind, Provider, Action, State, Progress, AssetPath, AssetGuid, Error) — none of
      /// which is a secret field. When AssetGenJob lands it replaces the inline object; the
      /// assertions stay the same.
      /// </summary>
      [TestFixture]
      public class AssetGenJobKeyLeakGuardTests
      {
          private string _dir;

          [SetUp]
          public void SetUp()
          {
              _dir = Path.Combine(Path.GetTempPath(), "mcp_jobguard_" + System.Guid.NewGuid().ToString("N"));
              SecureKeyStore.SetForTesting(new EncryptedFileKeyStore(_dir, "test-machine-id"));
          }

          [TearDown]
          public void TearDown()
          {
              SecureKeyStore.ResetForTesting();
              if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
          }

          [Test]
          public void SerializedJob_ContainsNoKeyValue()
          {
              const string key = "tsk_this_must_never_serialize_123";
              SecureKeyStore.Current.Set("tripo", key);

              var job = new
              {
                  JobId = System.Guid.NewGuid().ToString("N"),
                  Kind = "model",
                  Provider = "tripo",
                  Action = "generate",
                  State = "running",
                  Progress = 0.5f,
                  AssetPath = (string)null,
                  AssetGuid = (string)null,
                  Error = (string)null
              };

              string serialized = JsonConvert.SerializeObject(job);
              StringAssert.DoesNotContain(key, serialized);

              // And even if an error path tried to embed the key, the redactor strips it.
              string viaError = SecretRedactor.Scrub($"job {job.JobId} failed: provider rejected {key}");
              StringAssert.DoesNotContain(key, viaError);
              StringAssert.Contains("***REDACTED***", viaError);
          }
      }
  }
  ```

- [ ] **Step 2: Run it — expect PASS immediately.** Test Runner ▸ EditMode ▸ `AssetGenJobKeyLeakGuardTests`. This is a guard, not red-green: it must be green on first run. If it ever goes red (someone adds a key-bearing field or an unredacted error path), the invariant has been broken. Expected: green.

- [ ] **Step 3: Run the full AssetGen suite once.** Test Runner ▸ EditMode → run the whole `AssetGen` folder. Expected green: `EnvKeyOverrideTests`, `EncryptedFileKeyStoreTests`, `SecureKeyStoreFactoryTests`, `SecretRedactorTests`, `AssetGenJobKeyLeakGuardTests`. The three platform fixtures (`Mac*`/`Windows*`/`Linux*`) show as not-run (Explicit) unless run manually on the matching OS. Then `tools/check-unity-versions.sh` → green across the matrix.

- [ ] **Step 4: Commit.**
  ```bash
  git add TestProjects/UnityMCPTests/Assets/Tests/EditMode/AssetGen/AssetGenJobKeyLeakGuardTests.cs*
  git commit -m "test(asset-gen): guard that serialized jobs never contain a key

Locks in the invariant that the job-record shape has no secret field and that any accidental
embedding in an error string is redacted by SecretRedactor.

Claude-Session: https://claude.ai/code/session_01Tjpb5gYgUe2AUJuRdXr7Lv"
  ```

---

**Phase 1 exit criteria:** the EditMode `AssetGen` suite is green (deterministic fixtures), all `#if UNITY_EDITOR_*` branches compile across `tools/check-unity-versions.sh`, and the security guarantees hold: keys are encrypted/OS-stored at rest, an env override resolves env→store, no key is ever serialized into a job-shaped record, and every log/error path can be scrubbed via `SecretRedactor.Scrub`. Phase 2 (provider abstraction + Tripo) consumes `SecureKeyStore.Current.TryGet` at the moment of the HTTP call and `SecretRedactor.Scrub` on every error body.

---

I have everything I need. Here is the Phase 8 plan.

---

## Phase 8: Deps glTFast row + docs + version-compat sweep + final test pass

This phase **wires together** everything built in Phases 0–7 and produces the "test ready" exit state. It has four jobs: (8.1) surface **glTFast** as an installable optional dependency in the existing Dependencies tab so GLB import works; (8.2) sweep the version-fragile Unity APIs the asset-gen code touches (`ModelImporter`, `UnityWebRequest`, UIToolkit) and add the one shim that is actually needed, then run the CI-matrix compile check; (8.3) document the feature in the README + a guide; (8.4) run the full Python test suite, inventory the C# EditMode tests authored across all phases, and hand the user a manual-verification checklist plus the definition-of-done exit checklist.

No new tool, provider, or job code is added here — this is integration, docs, and verification. Everything lives in the worktree `/Users/scriptwonder/Documents/GitHub/unity-mcp/.worktrees/3d-asset-generation`.

> **Ground truth confirmed by reading the repo:**
> - `BuildDependenciesSection` is at `MCPForUnityEditorWindow.cs:758`; the bulk array `upmPackages` is at line 780; helpers `IsUpmPackageInstalled` (line 1030, reads `Packages/manifest.json`), `InstallUpmPackage` (975), `RemoveUpmPackage` (980), and `AddDependencyRow` (878) are all private statics in `namespace MCPForUnity.Editor.Windows`.
> - The Install-All / Uninstall-All dialog strings (lines 786, 806) enumerate the packages and must be updated.
> - Python tool groups live in `Server/src/services/registry/tool_registry.py` (`TOOL_GROUPS` dict, line 18); the `asset_gen` key is added in **Phase 0**, not here.
> - The shim catalog source-of-truth is `MCPForUnity/Runtime/Helpers/UnityCompatShims.cs` (empty marker class, XML-doc catalog).
> - EditMode tests share a single assembly: `TestProjects/UnityMCPTests/Assets/Tests/EditMode/MCPForUnityTests.Editor.asmdef` (already references the editor assembly + UnityEditor). New AssetGen test files drop into `…/EditMode/AssetGen/` under that same asmdef — no new asmdef required.

---

### Task 8.1: Add a glTFast optional-dependency row to the Dependencies tab

**Files:**
- Modify: `MCPForUnity/Editor/Windows/MCPForUnityEditorWindow.cs` (`BuildDependenciesSection`)
- Create (test): `TestProjects/UnityMCPTests/Assets/Tests/EditMode/AssetGen/DependenciesSectionGltfastTests.cs`

**Interfaces:**
- Consumes (existing private statics): `IsUpmPackageInstalled(string packageId) -> bool`; `InstallUpmPackage(string packageId, Action onComplete)`; `RemoveUpmPackage(string packageId, Action onComplete)`; `AddDependencyRow(VisualElement parent, string name, string description, bool isInstalled, string installedText, string missingText, Action<Action> installAction, Action<Action> uninstallAction)`.
- Produces: a new dependency row whose detection is `IsUpmPackageInstalled("com.unity.cloud.gltfast") || Type.GetType("GLTFast.GltfImport, glTFast") != null`, and `"com.unity.cloud.gltfast"` added to the `upmPackages` bulk array.

Steps:

- [ ] **Step 1: Write the failing EditMode test.** Create `TestProjects/UnityMCPTests/Assets/Tests/EditMode/AssetGen/DependenciesSectionGltfastTests.cs`. It reflectively invokes the private `BuildDependenciesSection(VisualElement)` against a throwaway container and asserts a `glTFast` row and an `asset_gen` reference exist. (Reflection is used because the method is `private static`; invoking it only builds UI elements — no UPM calls happen until a button is clicked, so it is side-effect-free and CI-safe.)

```csharp
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using MCPForUnity.Editor.Windows;
using UnityEngine.UIElements;

namespace MCPForUnity.Editor.Tests.AssetGen
{
    public class DependenciesSectionGltfastTests
    {
        private static VisualElement BuildSection()
        {
            MethodInfo method = typeof(MCPForUnityEditorWindow).GetMethod(
                "BuildDependenciesSection",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(method, "BuildDependenciesSection(VisualElement) must exist.");

            var container = new VisualElement();
            method.Invoke(null, new object[] { container });
            return container;
        }

        [Test]
        public void DependenciesSection_ContainsGltfastRow()
        {
            VisualElement container = BuildSection();
            bool found = container.Query<Label>().ToList()
                .Any(l => l.text != null && l.text.Contains("glTFast"));
            Assert.IsTrue(found, "Dependencies tab must contain a glTFast row.");
        }

        [Test]
        public void DependenciesSection_GltfastRowReferencesAssetGenGroup()
        {
            VisualElement container = BuildSection();
            bool mentionsGroup = container.Query<Label>().ToList()
                .Any(l => l.text != null && l.text.Contains("asset_gen"));
            Assert.IsTrue(mentionsGroup,
                "The glTFast row description must reference the asset_gen group.");
        }
    }
}
```

- [ ] **Step 2: Run the test — expect FAIL.** EditMode tests can't run headless here without a license; run them in Unity (Test Runner → EditMode) or via the harness:

```bash
python tools/local_harness.py --legs editmode
```

Expected: both `DependenciesSection_*` assertions FAIL (no row matches `glTFast` / `asset_gen`). If the harness can't acquire a license locally, open `TestProjects/UnityMCPTests` in the Editor and run the `MCPForUnity.Editor.Tests.AssetGen` group — both tests show red.

- [ ] **Step 3: Add `com.unity.cloud.gltfast` to the bulk array and update the dialog strings.** In `MCPForUnityEditorWindow.cs`, change the `upmPackages` array (line 780):

```csharp
var upmPackages = new[] { "com.unity.probuilder", "com.unity.cinemachine", "com.unity.visualeffectgraph", "com.unity.cloud.gltfast" };
```

Update the Install-All dialog body (line 786):

```csharp
                if (!EditorUtility.DisplayDialog("Install All Dependencies",
                    "This will install Roslyn DLLs, ProBuilder, Cinemachine, VFX Graph, and glTFast. Continue?",
                    "Install All", "Cancel")) return;
```

Update the Uninstall-All dialog body (line 806):

```csharp
                if (!EditorUtility.DisplayDialog("Uninstall All Dependencies",
                    "This will remove Roslyn DLLs, ProBuilder, Cinemachine, VFX Graph, and glTFast. Continue?",
                    "Uninstall All", "Cancel")) return;
```

- [ ] **Step 4: Add the glTFast row.** Immediately after the VFX Graph `AddDependencyRow(...)` block (ends at line 872, just before `section.Add(content);`), insert:

```csharp
            // glTFast — GLB/glTF import for the asset_gen group.
            // Detect either the UPM package (manifest) or the runtime type (covers embedded/local clones).
            bool hasGltfast = IsUpmPackageInstalled("com.unity.cloud.gltfast")
                || Type.GetType("GLTFast.GltfImport, glTFast") != null;
            AddDependencyRow(content,
                "glTFast (glTF/GLB Import)",
                "Required to import GLB/glTF models produced by generate_model and import_model (asset_gen group).",
                hasGltfast,
                "Installed \u2014 GLB/glTF results import directly into Assets/",
                "Not installed \u2014 GLB jobs fail with an actionable error; choose FBX output or install glTFast",
                done => InstallUpmPackage("com.unity.cloud.gltfast", done),
                done => RemoveUpmPackage("com.unity.cloud.gltfast", done));
```

- [ ] **Step 5: Re-run the test — expect PASS.**

```bash
python tools/local_harness.py --legs editmode
```

Expected: `DependenciesSection_ContainsGltfastRow` and `DependenciesSection_GltfastRowReferencesAssetGenGroup` both PASS (the row's name label contains `glTFast`; its description label contains `asset_gen`).

- [ ] **Step 6: Commit.**

```bash
git add MCPForUnity/Editor/Windows/MCPForUnityEditorWindow.cs \
        TestProjects/UnityMCPTests/Assets/Tests/EditMode/AssetGen/DependenciesSectionGltfastTests.cs
git commit -m "feat(asset-gen): add glTFast optional-dependency row to Dependencies tab

Detects com.unity.cloud.gltfast via manifest or GLTFast.GltfImport type;
wires Install/Uninstall + Install-All/Uninstall-All; description points at
the asset_gen group and the FBX fallback when glTFast is absent.

Claude-Session: https://claude.ai/code/session_01Tjpb5gYgUe2AUJuRdXr7Lv"
```

---

### Task 8.2: Version-compat sweep — `ModelImporter` material-import shim + UnityWebRequest/UIToolkit audit + CI-matrix compile check

**Files:**
- Create: `MCPForUnity/Editor/Helpers/UnityModelImporterCompat.cs`
- Create (meta will auto-generate): `MCPForUnity/Editor/Helpers/UnityModelImporterCompat.cs.meta`
- Modify: `MCPForUnity/Editor/Services/AssetGen/Import/ModelImportPipeline.cs` (route the material-import call through the shim — Phase 3 wrote it using the obsolete `ModelImporter.importMaterials`, matching the shared contract verbatim)
- Modify: `MCPForUnity/Runtime/Helpers/UnityCompatShims.cs` (append to the catalog XML doc)
- Create (test): `TestProjects/UnityMCPTests/Assets/Tests/EditMode/AssetGen/UnityModelImporterCompatTests.cs`

**Interfaces:**
- Produces: `public static class UnityModelImporterCompat { public static void SetImportMaterials(ModelImporter importer, bool enabled); }`
- Consumed by: `ModelImportPipeline.ImportInto(AssetGenJob, string)`.

> **Why a shim, and only this one.** The shared contract for `ModelImportPipeline` says it "sets `ModelImporter` globalScale/useFileScale/**importMaterials**/animationType". `ModelImporter.importMaterials` (the `bool` property) is `[Obsolete]` since Unity 2018.3 (replaced by `materialImportMode`), so a direct call emits **CS0618** and turns into a hard break when the property is finally removed. Per CLAUDE.md we don't sprinkle `#if` — we route the one fragile call through a shim. `materialImportMode` exists on every targeted version (2021.3 floor → 6.x), so the shim is a clean static dispatch, no reflection needed.
>
> **UnityWebRequest** and **UIToolkit** were audited (see Step 5) and need **no** shim: `UnityWebRequest.result` / `UnityWebRequest.Result.Success`, `DownloadHandlerFile`, `SetRequestHeader`, and `SendWebRequest()` are all stable and non-obsolete from 2020.1 onward (we floor at 2021.3); the UIToolkit surface the GUI uses (`VisualElement`, `Button`, `Toggle`, `TextField` incl. `isPasswordField`/`maskChar`, `Foldout`, UQuery) has been stable since 2019. Documenting that audit is part of the deliverable.

Steps:

- [ ] **Step 1: Write the failing shim test.** Create `…/EditMode/AssetGen/UnityModelImporterCompatTests.cs`. It writes a tiny ASCII `.obj` cube into `Assets/` (Unity imports `.obj` through `ModelImporter`, so this needs no binary fixture), imports it, and asserts the shim flips `materialImportMode` correctly. Self-contained — cleans up after itself.

```csharp
using System.IO;
using NUnit.Framework;
using MCPForUnity.Editor.Helpers;
using UnityEditor;

namespace MCPForUnity.Editor.Tests.AssetGen
{
    public class UnityModelImporterCompatTests
    {
        private const string Dir = "Assets/Temp_AssetGenShimTest";
        private const string ObjPath = Dir + "/cube.obj";

        private const string CubeObj =
@"o cube
v -0.5 -0.5 -0.5
v 0.5 -0.5 -0.5
v 0.5 0.5 -0.5
v -0.5 0.5 -0.5
v -0.5 -0.5 0.5
v 0.5 -0.5 0.5
v 0.5 0.5 0.5
v -0.5 0.5 0.5
f 1 2 3 4
f 5 6 7 8
f 1 5 8 4
f 2 6 7 3
f 4 3 7 8
f 1 2 6 5
";

        [SetUp]
        public void SetUp()
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(ObjPath, CubeObj);
            AssetDatabase.ImportAsset(ObjPath, ImportAssetOptions.ForceSynchronousImport);
        }

        [TearDown]
        public void TearDown()
        {
            AssetDatabase.DeleteAsset(ObjPath);
            AssetDatabase.DeleteAsset(Dir);
        }

        [Test]
        public void SetImportMaterials_False_SetsNone()
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(ObjPath);
            Assert.IsNotNull(importer, "OBJ fixture must import as a ModelImporter.");

            UnityModelImporterCompat.SetImportMaterials(importer, false);
            Assert.AreEqual(ModelImporterMaterialImportMode.None, importer.materialImportMode);
        }

        [Test]
        public void SetImportMaterials_True_SetsImportStandard()
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(ObjPath);
            UnityModelImporterCompat.SetImportMaterials(importer, true);
            Assert.AreEqual(ModelImporterMaterialImportMode.ImportStandard, importer.materialImportMode);
        }
    }
}
```

- [ ] **Step 2: Run the test — expect FAIL (does not compile).**

```bash
python tools/local_harness.py --legs editmode
```

Expected: compile error / red — `UnityModelImporterCompat` does not exist yet.

- [ ] **Step 3: Create the shim.** Write `MCPForUnity/Editor/Helpers/UnityModelImporterCompat.cs`:

```csharp
using UnityEditor;

namespace MCPForUnity.Editor.Helpers
{
    /// <summary>
    /// Version-compat shim for <see cref="ModelImporter"/> material import.
    /// <c>ModelImporter.importMaterials</c> (the bool) is [Obsolete] since 2018.3 (CS0618);
    /// route every asset-gen call through here, which uses the stable
    /// <see cref="ModelImporterMaterialImportMode"/> available on every targeted version
    /// (2021.3 floor → 6.x). Editor-only API, so this lives under Editor/Helpers rather
    /// than Runtime/Helpers. Catalogued in
    /// <see cref="MCPForUnity.Runtime.Helpers.UnityCompatShims"/>.
    /// </summary>
    public static class UnityModelImporterCompat
    {
        /// <summary>
        /// Enable/disable material import without touching the obsolete bool property.
        /// </summary>
        public static void SetImportMaterials(ModelImporter importer, bool enabled)
        {
            if (importer == null) return;
            importer.materialImportMode = enabled
                ? ModelImporterMaterialImportMode.ImportStandard
                : ModelImporterMaterialImportMode.None;
        }
    }
}
```

- [ ] **Step 4: Route `ModelImportPipeline` through the shim.** In `MCPForUnity/Editor/Services/AssetGen/Import/ModelImportPipeline.cs`, replace the obsolete call that Phase 3 introduced. The Phase-3 line looks like:

```csharp
                importer.importMaterials = job. /* texture flag */ ...;   // CS0618
```

Replace it with (add `using MCPForUnity.Editor.Helpers;` at the top if absent):

```csharp
                UnityModelImporterCompat.SetImportMaterials(importer, importMaterials);
```

where `importMaterials` is the local bool the pipeline already computes (from the request's `Texture` flag / material-import decision). Confirm with a quick grep that no `importMaterials` assignment remains:

```bash
cd /Users/scriptwonder/Documents/GitHub/unity-mcp && grep -rn "\.importMaterials" MCPForUnity/Editor/
```

Expected: **no matches** (only the shim's `materialImportMode` remains).

- [ ] **Step 5: Audit UnityWebRequest + UIToolkit and document the result.** Run the audit greps and confirm no obsolete API is in use:

```bash
cd /Users/scriptwonder/Documents/GitHub/unity-mcp && \
  echo "== obsolete UWR result checks ==" && \
  grep -rn "isNetworkError\|isHttpError" MCPForUnity/Editor/Services/AssetGen/ ; \
  echo "== UWR success path (should use .result) ==" && \
  grep -rn "\.result\|Result.Success" MCPForUnity/Editor/Services/AssetGen/Http/ ; \
  echo "== UIToolkit obsolete query/style APIs ==" && \
  grep -rn "ToggleButtonGroup\|Q<\|stylesheets" MCPForUnity/Editor/Windows/Components/AssetGen/
```

Expected: the `isNetworkError`/`isHttpError` grep returns **nothing** (the Phase-2/7 transport uses `UnityWebRequest.result == UnityWebRequest.Result.Success`); the UIToolkit grep surfaces only stable APIs. If any obsolete usage appears, that is a Phase-2/5/7 defect to fix there — record it; otherwise the audit confirms **no UnityWebRequest or UIToolkit shim is required.**

- [ ] **Step 6: Append the shim to the catalog.** In `MCPForUnity/Runtime/Helpers/UnityCompatShims.cs`, add a plain-text bullet under "Active shims" (plain text, not a `<see cref>` — the Runtime assembly cannot reference the editor-only `UnityModelImporterCompat` type):

```csharp
    ///   • <see cref="UnityAssembliesCompat"/>  — AppDomain.GetAssemblies →
    ///                                            UnityEngine.Assemblies.CurrentAssemblies (Unity 6.8 CoreCLR)
    ///   • UnityModelImporterCompat (Editor/Helpers) — ModelImporter.importMaterials (bool, CS0618)
    ///                                            → materialImportMode (2018.3); used by asset-gen ModelImportPipeline
```

- [ ] **Step 7: Re-run the shim test — expect PASS.**

```bash
python tools/local_harness.py --legs editmode
```

Expected: `SetImportMaterials_False_SetsNone` → `materialImportMode == None`; `SetImportMaterials_True_SetsImportStandard` → `materialImportMode == ImportStandard`. Both PASS.

- [ ] **Step 8: Run the CI-matrix compile check.** This is mandatory whenever a shim or `#if UNITY_*` path is touched (CLAUDE.md):

```bash
cd /Users/scriptwonder/Documents/GitHub/unity-mcp && tools/check-unity-versions.sh
```

Expected: each locally-installed Hub editor from `tools/unity-versions.json` (`2021.3.45f2` floor, `2022.3.62f1`, `6000.0.75f1`, `6000.4.8f1`) compiles `TestProjects/UnityMCPTests` clean — **no CS0618** from the asset-gen code, exit code `0`. Versions not installed locally are skipped (not failures). If no editors are installed locally, run the containerized parity instead (needs a license env):

```bash
tools/check-unity-versions.sh --docker   # pulls unityci/editor images; requires UNITY_LICENSE
```

- [ ] **Step 9: Commit.**

```bash
git add MCPForUnity/Editor/Helpers/UnityModelImporterCompat.cs \
        MCPForUnity/Editor/Services/AssetGen/Import/ModelImportPipeline.cs \
        MCPForUnity/Runtime/Helpers/UnityCompatShims.cs \
        TestProjects/UnityMCPTests/Assets/Tests/EditMode/AssetGen/UnityModelImporterCompatTests.cs
git commit -m "fix(asset-gen): shim ModelImporter.importMaterials; version-compat sweep

Route the material-import flag through UnityModelImporterCompat
(materialImportMode) to drop CS0618; UnityWebRequest/UIToolkit audited and
need no shim (result-enum + UIToolkit surface stable since <=2021.3). Catalog
updated; tools/check-unity-versions.sh green across the matrix.

Claude-Session: https://claude.ai/code/session_01Tjpb5gYgUe2AUJuRdXr7Lv"
```

---

### Task 8.3: Document the feature — README section + full guide

**Files:**
- Modify: `README.md`
- Create: `docs/guides/asset-generation.md`

**Interfaces:** None (documentation). Content must name: providers, BYO-key + OS secure-store note, glTFast dependency, `manage_tools` enablement, and MCP-tool usage examples for all three tools.

Steps:

- [ ] **Step 1: Add the Advanced bullet + a short README section.** In `README.md`, append a bullet to the `## Advanced` list (right after the "Remote-hosted server with auth" line):

```markdown
- **AI asset generation (3D + 2D, bring-your-own-key)** — [AI Asset Generation](docs/guides/asset-generation.md)
```

Then insert a new section immediately **after** the Advanced list and **before** `## Star History`:

```markdown
## AI Asset Generation

Generate 3D models, import marketplace assets, and generate 2D images straight into your project using **your own** provider keys.

- **Providers** — 3D generation: **Tripo3D** (default), Meshy, Hunyuan3D · marketplace: **Sketchfab** · 2D image: **fal.ai** (default), OpenRouter.
- **Bring your own key** — enter keys in `Window → MCP for Unity → Asset Generation`. Keys are stored in your **OS secure store** (macOS Keychain, Windows Credential Manager, Linux libsecret; an AES-256-GCM encrypted file as a documented fallback) — never in plaintext, never sent over the MCP bridge, never written to logs, job records, or git.
- **Generation is MCP/CLI-only** — the GUI tab is configuration-only (keys, toggles, Test). Models and images are downloaded and imported by the Unity Editor itself; heavy bytes never cross the bridge.
- **Optional dependency** — GLB/glTF output needs **glTFast** (`com.unity.cloud.gltfast`); install it from the **Dependencies** tab, or request FBX output instead.
- **Off by default** — enable the group with `manage_tools(action="activate", group="asset_gen")`.

See the **[AI Asset Generation guide](docs/guides/asset-generation.md)** for the full tool reference and examples.
```

- [ ] **Step 2: Write the full guide.** Create `docs/guides/asset-generation.md`:

```markdown
# AI Asset Generation

MCP for Unity can generate **3D models**, import **marketplace 3D assets**, and
generate **2D images** directly into your Unity project. You bring your own
provider API keys; the Unity Editor runs the provider call, downloads the
result, and imports it with the correct importer settings.

## How it works

```
Asset Generation tab ──(write keys)──> OS secure store (Keychain / CredMan / libsecret)
AI agent / CLI ──> generate_model | import_model | generate_image   (no key, no bytes)
                       ▼ bridge
C# handler ──> AssetGenJobManager ──> provider adapter (UnityWebRequest)
                       ▼ submit → poll → download to Assets/Generated/…
                       ▼ ModelImporter / TextureImporter (+ optional normalize)
status(job_id) ──> { state, progress, assetPath | error }   (key-free, redacted)
```

The **GUI tab is configuration-only** — it stores keys, toggles providers, and
runs a Test/validate ping. **Generation is triggered only via MCP tools or the
CLI.** No tool can read a stored key back out.

## Providers

| Kind | Provider | Auth | Modes |
|------|----------|------|-------|
| 3D generate | **Tripo3D** (default) | Bearer `tsk_` | text→3D, image→3D |
| 3D generate | Meshy | Bearer | text→3D, image→3D |
| 3D generate | Hunyuan3D (Tencent) | SecretId + SecretKey (TC3-HMAC) | text→3D, image→3D |
| 3D marketplace | Sketchfab | `Authorization: Token` | search → preview → import |
| 2D image | **fal.ai** (default) | `Authorization: Key` | text→image, image→image |
| 2D image | OpenRouter | Bearer | text→image (multimodal) |

## Setup

1. **Enter keys.** `Window → MCP for Unity → Asset Generation`. Paste a key per
   provider and click **Test** to validate against a cheap auth endpoint.
   Hunyuan takes a SecretId + SecretKey pair.
2. **Install glTFast (for GLB/glTF).** `Window → MCP for Unity → Dependencies →
   glTFast → Install` (`com.unity.cloud.gltfast`). Without it, GLB jobs fail with
   an actionable message — request FBX output instead, or install glTFast.
3. **Enable the tool group** (off by default):

   ```
   manage_tools(action="activate", group="asset_gen")
   ```

## Key security

- Stored in your OS secure store: macOS Keychain (`/usr/bin/security`),
  Windows Credential Manager (DPAPI-backed), Linux libsecret (`secret-tool`).
  Fallback: an AES-256-GCM encrypted file under your user app-data dir
  (documented as weaker than an OS store).
- Keys are read into a local variable only at the moment of the HTTP call,
  never cached on job records, never serialized to SessionState.
- Keys never cross the MCP bridge, never appear in tool output, logs, or git.
  `list_providers` / `status` expose only `configured: true|false` — never a value.
- CI/headless override (read-only, never persisted): set
  `MCPFORUNITY_<PROVIDER>_API_KEY` (e.g. `MCPFORUNITY_TRIPO_API_KEY`).
  Resolution order is **env → secure store**.
- Residual risk: code running as the same OS user with the editor's entitlements
  (notably the `execute_code` tool) can ask the OS store for an item, exactly as
  the editor can. Use least-privilege provider keys and revoke easily.

## Tools

### `generate_model` — 3D generation (tripo, meshy, hunyuan)

```
# text → 3D, FBX, ~1m, with textures
generate_model(action="generate", provider="tripo", mode="text",
               prompt="a low-poly treasure chest", format="fbx",
               target_size=1.0, texture=true)
# → { "job_id": "…" }

generate_model(action="status", job_id="…")
# → { "state": "done", "progress": 1.0, "assetPath": "Assets/Generated/Models/…" }

generate_model(action="list_providers")
# → { "providers": [ { "id": "tripo", "configured": true, "capabilities": [...] }, ... ] }

generate_model(action="cancel", job_id="…")
```

### `import_model` — Sketchfab marketplace import

```
import_model(action="search", query="wooden barrel", downloadable=true, count=20)
import_model(action="preview", uid="<model-uid>")          # → base64 thumbnail
import_model(action="import", uid="<model-uid>", target_size=1.0)  # → { job_id }
import_model(action="status", job_id="…")
```

### `generate_image` — 2D image generation (fal, openrouter)

```
generate_image(action="generate", provider="fal", mode="text",
               prompt="seamless mossy stone tile, top-down", transparent=false,
               width=1024, height=1024)
# → { job_id }   (sync providers resolve immediately)

generate_image(action="status", job_id="…")
# → { "state": "done", "assetPath": "Assets/Generated/Images/…png" }
```

Default output folders: `Assets/Generated/Models/`,
`Assets/Generated/Sketchfab/`, `Assets/Generated/Images/`. Name collisions get a
numeric suffix.

## CLI

The same surface is available from the terminal (HTTP transport):

```bash
uv run mcp-for-unity asset-gen generate-model --provider tripo --mode text \
  --prompt "a low-poly treasure chest" --format fbx
uv run mcp-for-unity asset-gen status --job-id <job_id>
uv run mcp-for-unity asset-gen import-model --query "wooden barrel"
uv run mcp-for-unity asset-gen generate-image --provider fal \
  --prompt "seamless mossy stone tile"
```

## Troubleshooting

- **"Install glTFast…"** — a provider returned GLB but `com.unity.cloud.gltfast`
  is missing. Install it (Dependencies tab) or request `format="fbx"`.
- **`list_providers` shows `configured: false`** — no key stored for that
  provider; add it in the Asset Generation tab (or set the env override).
- **Job stuck `running`** — providers can queue; poll `status`. Jobs survive the
  domain reload that import triggers (SessionState-persisted).
```

- [ ] **Step 3: Sanity-check the docs (link + required terms).** No unit test for prose; run a presence check:

```bash
cd /Users/scriptwonder/Documents/GitHub/unity-mcp && \
  test -f docs/guides/asset-generation.md && \
  grep -q "docs/guides/asset-generation.md" README.md && \
  grep -q "asset_gen" docs/guides/asset-generation.md && \
  grep -q "com.unity.cloud.gltfast" docs/guides/asset-generation.md && \
  grep -q "secure store" docs/guides/asset-generation.md && \
  grep -q "MCPFORUNITY_" docs/guides/asset-generation.md && \
  echo "DOCS OK"
```

Expected: `DOCS OK` (the guide exists, the README links it, and the guide covers providers/glTFast/secure-store/env-override).

- [ ] **Step 4: Commit.**

```bash
git add README.md docs/guides/asset-generation.md
git commit -m "docs(asset-gen): add AI Asset Generation README section + guide

Covers providers, bring-your-own-key + OS secure-store, glTFast dependency,
manage_tools enablement, and MCP-tool/CLI usage examples for generate_model,
import_model, and generate_image.

Claude-Session: https://claude.ai/code/session_01Tjpb5gYgUe2AUJuRdXr7Lv"
```

---

### Task 8.4: Final test pass + EditMode inventory + manual-verification + "test ready" exit checklist

**Files:** None modified (verification only). This task produces the green-light evidence.

**Interfaces:** None.

Steps:

- [ ] **Step 1: Run the full Python asset-gen suite — expect PASS.**

```bash
cd /Users/scriptwonder/Documents/GitHub/unity-mcp/Server && uv run pytest tests/test_asset_gen_*.py -v
```

Expected: every `test_asset_gen_*` case PASSES (param→camelCase mapping, action routing, `status`/`job_id` pass-through, `None`-stripping, error shaping; provider HTTP fully mocked — no key, no bytes in any payload).

- [ ] **Step 2: Run the entire Python suite to confirm no regressions from the new `asset_gen` group registration.**

```bash
cd /Users/scriptwonder/Documents/GitHub/unity-mcp/Server && uv run pytest tests/ -q
```

Expected: all green. In particular, any test that snapshots `TOOL_GROUPS` / `DEFAULT_ENABLED_GROUPS` must now include `asset_gen` and confirm it is **not** in `DEFAULT_ENABLED_GROUPS`.

- [ ] **Step 3: Run the full EditMode suite via the harness (needs a licensed local editor).**

```bash
cd /Users/scriptwonder/Documents/GitHub/unity-mcp && python tools/local_harness.py --legs editmode
```

Expected exit code `0`. Verify the AssetGen EditMode inventory below is all present and green (these are authored across Phases 1–8; tick each):

```
TestProjects/UnityMCPTests/Assets/Tests/EditMode/AssetGen/
  Security/
    [ ] SecureKeyStoreRoundTripTests.cs       (set/get/delete/has vs EncryptedFileKeyStore)
    [ ] SecretRedactorTests.cs                (Scrub strips key values + bearer tokens)
    [ ] EnvOverrideTests.cs                   (MCPFORUNITY_<PROVIDER>_API_KEY: env → store)
  Http/
    [ ] FakeHttpTransportTests.cs             (injected transport contract)
  Providers/
    [ ] TripoAdapterTests.cs                  (submit/poll/download shape; auth header present, value NOT asserted)
    [ ] MeshyAdapterTests.cs
    [ ] HunyuanAdapterTests.cs
    [ ] TencentCloud3SignerTests.cs           (TC3-HMAC known-answer vector)
    [ ] SketchfabAdapterTests.cs              (search/preview/resolve)
    [ ] FalAdapterTests.cs
    [ ] OpenRouterAdapterTests.cs
    [ ] AssetGenProvidersRegistryTests.cs     (List(); Configured = Has(id), never the key)
  Jobs/
    [ ] AssetGenJobManagerTests.cs            (lifecycle + simulated domain-reload recovery via SessionState)
  Import/
    [ ] ModelImportPipelineTests.cs           (FBX/OBJ fixture: scale/materials/animationType + normalize; GLB-without-glTFast → actionable fail)
    [ ] ImageImportPipelineTests.cs           (PNG fixture: Sprite/Default, alphaIsTransparency, sRGB vs linear)
  Security/
    [ ] KeyLeakGuardTests.cs                  (job records / status payloads / logs contain NO key or secret)
  AssetGen/   (Phase 8)
    [ ] DependenciesSectionGltfastTests.cs    (glTFast row present, references asset_gen)
    [ ] UnityModelImporterCompatTests.cs      (materialImportMode shim mapping)
```

- [ ] **Step 4: Run the cross-version compile check one more time (full repo, not just asset-gen).**

```bash
cd /Users/scriptwonder/Documents/GitHub/unity-mcp && tools/check-unity-versions.sh
```

Expected: exit `0` across every installed matrix editor (`2021.3.45f2` … `6000.4.8f1`); no CS0618/CS0619 from asset-gen code.

- [ ] **Step 5: Hand the user the manual-verification checklist.** These require **real provider keys + a Hub-licensed editor** and cannot be run headless/mocked. Run with the bridge connected and `manage_tools(action="activate", group="asset_gen")` already done.

  **(a) Tripo text→3D (FBX, glTFast not required):**
  ```
  generate_model(action="generate", provider="tripo", mode="text",
                 prompt="a low-poly treasure chest", format="fbx", target_size=1.0, texture=true)
  # poll until done:
  generate_model(action="status", job_id="<id>")
  ```
  - [ ] `status` reaches `state:"done"` with an `assetPath` under `Assets/Generated/Models/`.
  - [ ] The FBX appears in the Project window and instantiates with sane scale (~1m).
  - [ ] No key value appears anywhere in the tool output or the Unity Console.

  **(b) Sketchfab search → import:**
  ```
  import_model(action="search", query="wooden barrel", downloadable=true, count=10)
  import_model(action="preview", uid="<uid>")          # base64 thumb renders
  import_model(action="import", uid="<uid>", target_size=1.0)
  import_model(action="status", job_id="<id>")
  ```
  - [ ] Search returns uids; preview returns a base64 thumbnail.
  - [ ] Import job reaches `done`; asset lands under `Assets/Generated/Sketchfab/`, imports cleanly (glTFast installed for the glTF zip path).

  **(c) fal image (text→image):**
  ```
  generate_image(action="generate", provider="fal", mode="text",
                 prompt="seamless mossy stone tile, top-down", width=1024, height=1024)
  generate_image(action="status", job_id="<id>")
  ```
  - [ ] PNG lands under `Assets/Generated/Images/` with correct TextureImporter settings (sRGB color map; Sprite vs Default as requested).

  **(d) glTFast-missing path (negative check):** temporarily uninstall glTFast (Dependencies tab) and run a Tripo `format="glb"` job.
  - [ ] Job fails with the actionable message ("Install glTFast from the Dependencies tab, or choose FBX output") — not a silent crash. Re-install glTFast afterward.

- [ ] **Step 6: Confirm the "test ready" exit checklist (definition of done from the spec §9).** All boxes must be ticked before opening the PR:

```
[ ] Python: `uv run pytest tests/test_asset_gen_*.py -v` green; full `tests/` green.
[ ] C# compiles across the CI matrix (`tools/check-unity-versions.sh` exit 0) — no CS0618/CS0619.
[ ] EditMode tests authored + green for: SecureKeyStore round-trip + redactor + env-override;
    AssetGenJobManager lifecycle incl. domain-reload recovery; every provider adapter against
    FakeHttpTransport (auth header present, value NOT asserted); TC3-HMAC known-answer vector;
    ModelImportPipeline + ImageImportPipeline fixtures; key-never-leaks assertion;
    glTFast deps row; ModelImporter compat shim.
[ ] `asset_gen` group registered on BOTH sides, disabled by default, toggled via manage_tools.
[ ] glTFast row live in the Dependencies tab (install/uninstall + Install-All/Uninstall-All).
[ ] No key/secret in: job records, status payloads, logs, tool output, or any git-tracked file
    (`git grep -nE 'tsk_|SecretKey|Authorization: (Bearer|Token|Key)'` over the worktree returns
    only doc/test scaffolding, never a real value).
[ ] README section + docs/guides/asset-generation.md present and linked.
[ ] Manual verification (real keys + licensed editor): Tripo text→3D, Sketchfab import, fal image
    — all PASS; glTFast-missing path fails with the actionable message.
```

- [ ] **Step 7: Final secret-leak sweep (belt-and-suspenders) and commit the verification notes only if anything changed.** Run:

```bash
cd /Users/scriptwonder/Documents/GitHub/unity-mcp && \
  git grep -nE 'tsk_[A-Za-z0-9]|"secretKey"\s*:\s*"[A-Za-z0-9]|Authorization:\s*(Bearer|Token|Key)\s+[A-Za-z0-9]' -- ':!docs/**' ':!**/Tests/**' || echo "NO LEAKS"
```

Expected: `NO LEAKS` (real key material appears nowhere in source; only redacted placeholders in tests/docs). This task modifies no source, so there is normally nothing to commit — Phase 8 ends with the three commits from Tasks 8.1–8.3 and a fully green, manually-verifiable feature.

---

**Phase 8 done →** glTFast is installable from the GUI, the one genuinely-fragile Unity API (`ModelImporter.importMaterials`) is shimmed and the rest audited clean, the matrix compile check is green, the feature is documented, the Python suite is green, the EditMode inventory is authored, and the user has a concrete real-key/licensed-editor checklist to sign off the live provider + import paths. This is the **"test ready"** state defined in the spec.