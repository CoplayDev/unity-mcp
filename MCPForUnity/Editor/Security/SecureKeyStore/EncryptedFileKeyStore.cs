using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace MCPForUnity.Editor.Security
{
    /// <summary>
    /// Cross-platform fallback key store used when no OS secret service is available
    /// (e.g. Linux without libsecret, or headless CI). Encrypts each key at rest with
    /// AES-256-CBC + HMAC-SHA256 (encrypt-then-MAC). The derivation passphrase is a
    /// per-user random master secret (persisted once, 0600) combined with a machine id,
    /// run through PBKDF2. Ciphertext lives under the user app-data dir — never under the
    /// repo / Assets. This is weaker than an OS keychain (the master secret sits on disk)
    /// but far better than plaintext, and it never leaks keys to git or the bridge.
    /// </summary>
    internal sealed class EncryptedFileKeyStore : ISecureKeyStore
    {
        private const int Iterations = 200_000;
        private readonly string _dir;

        public EncryptedFileKeyStore() : this(DefaultDir()) { }

        /// <summary>Test seam: point the store at a throwaway directory.</summary>
        internal EncryptedFileKeyStore(string storageDir)
        {
            _dir = storageDir;
            Directory.CreateDirectory(_dir);
            TryChmod(_dir, "700");
        }

        internal static string DefaultDir()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (string.IsNullOrEmpty(appData))
            {
                string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                appData = Path.Combine(home, ".config");
            }
            return Path.Combine(appData, "MCPForUnity", "AssetGenKeys");
        }

        private string KeyFile(string providerId) => Path.Combine(_dir, "key_" + providerId + ".bin");

        public bool Has(string providerId)
            => !string.IsNullOrEmpty(providerId) && File.Exists(KeyFile(providerId));

        public bool TryGet(string providerId, out string apiKey)
        {
            apiKey = null;
            if (string.IsNullOrEmpty(providerId)) return false;
            string path = KeyFile(providerId);
            if (!File.Exists(path)) return false;
            try
            {
                byte[] blob = Convert.FromBase64String(File.ReadAllText(path).Trim());
                apiKey = Encoding.UTF8.GetString(Decrypt(blob));
                return true;
            }
            catch
            {
                return false;
            }
        }

        public void Set(string providerId, string apiKey)
        {
            if (string.IsNullOrEmpty(providerId)) return;
            if (string.IsNullOrEmpty(apiKey)) { Delete(providerId); return; }
            byte[] blob = Encrypt(Encoding.UTF8.GetBytes(apiKey));
            string path = KeyFile(providerId);
            File.WriteAllText(path, Convert.ToBase64String(blob));
            TryChmod(path, "600");
        }

        public void Delete(string providerId)
        {
            if (string.IsNullOrEmpty(providerId)) return;
            try
            {
                string path = KeyFile(providerId);
                if (File.Exists(path)) File.Delete(path);
            }
            catch { /* best effort */ }
        }

        // ---------- crypto ----------

        private void DeriveKeys(out byte[] encKey, out byte[] macKey)
        {
            byte[] master = LoadOrCreate(Path.Combine(_dir, "secret.bin"), 32);
            byte[] salt = LoadOrCreate(Path.Combine(_dir, "salt.bin"), 16);
            string password = Convert.ToBase64String(master) + "|" + MachineId();
            using (var kdf = new Rfc2898DeriveBytes(password, salt, Iterations, HashAlgorithmName.SHA256))
            {
                byte[] material = kdf.GetBytes(64);
                encKey = new byte[32];
                macKey = new byte[32];
                Buffer.BlockCopy(material, 0, encKey, 0, 32);
                Buffer.BlockCopy(material, 32, macKey, 0, 32);
            }
        }

        private byte[] Encrypt(byte[] plaintext)
        {
            DeriveKeys(out byte[] encKey, out byte[] macKey);
            byte[] iv = RandomBytes(16);
            byte[] ct;
            using (var aes = Aes.Create())
            {
                aes.Key = encKey; aes.IV = iv; aes.Mode = CipherMode.CBC; aes.Padding = PaddingMode.PKCS7;
                using (var enc = aes.CreateEncryptor())
                    ct = enc.TransformFinalBlock(plaintext, 0, plaintext.Length);
            }
            byte[] mac;
            using (var h = new HMACSHA256(macKey))
                mac = h.ComputeHash(Concat(iv, ct));
            return Concat(iv, mac, ct);
        }

        private byte[] Decrypt(byte[] blob)
        {
            if (blob.Length < 48) throw new CryptographicException("ciphertext too short");
            DeriveKeys(out byte[] encKey, out byte[] macKey);
            byte[] iv = new byte[16];
            byte[] mac = new byte[32];
            byte[] ct = new byte[blob.Length - 48];
            Buffer.BlockCopy(blob, 0, iv, 0, 16);
            Buffer.BlockCopy(blob, 16, mac, 0, 32);
            Buffer.BlockCopy(blob, 48, ct, 0, ct.Length);
            using (var h = new HMACSHA256(macKey))
            {
                byte[] expected = h.ComputeHash(Concat(iv, ct));
                if (!FixedTimeEquals(expected, mac))
                    throw new CryptographicException("MAC verification failed");
            }
            using (var aes = Aes.Create())
            {
                aes.Key = encKey; aes.IV = iv; aes.Mode = CipherMode.CBC; aes.Padding = PaddingMode.PKCS7;
                using (var dec = aes.CreateDecryptor())
                    return dec.TransformFinalBlock(ct, 0, ct.Length);
            }
        }

        // ---------- helpers ----------

        private byte[] LoadOrCreate(string path, int len)
        {
            if (File.Exists(path))
            {
                byte[] existing = File.ReadAllBytes(path);
                if (existing.Length == len) return existing;
            }
            byte[] fresh = RandomBytes(len);
            File.WriteAllBytes(path, fresh);
            TryChmod(path, "600");
            return fresh;
        }

        private static string MachineId()
        {
            try
            {
                string id = SystemInfo.deviceUniqueIdentifier;
                if (!string.IsNullOrEmpty(id) && id != SystemInfo.unsupportedIdentifier) return id;
            }
            catch { /* not in a Unity runtime context */ }
            return Environment.MachineName ?? "unknown-machine";
        }

        private static byte[] RandomBytes(int n)
        {
            byte[] b = new byte[n];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(b);
            return b;
        }

        private static byte[] Concat(params byte[][] parts)
        {
            int total = 0;
            foreach (byte[] p in parts) total += p.Length;
            byte[] result = new byte[total];
            int offset = 0;
            foreach (byte[] p in parts)
            {
                Buffer.BlockCopy(p, 0, result, offset, p.Length);
                offset += p.Length;
            }
            return result;
        }

        private static bool FixedTimeEquals(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            int diff = 0;
            for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
            return diff == 0;
        }

        private static void TryChmod(string path, string mode)
        {
            try
            {
                if (Application.platform == RuntimePlatform.WindowsEditor) return;
                var psi = new System.Diagnostics.ProcessStartInfo("/bin/chmod")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                };
                psi.ArgumentList.Add(mode);
                psi.ArgumentList.Add(path);
                using (var p = System.Diagnostics.Process.Start(psi)) p?.WaitForExit(2000);
            }
            catch { /* hardening is best-effort */ }
        }
    }
}
