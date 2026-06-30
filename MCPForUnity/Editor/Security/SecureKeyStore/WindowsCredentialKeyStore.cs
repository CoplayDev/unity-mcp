using System;
using System.Runtime.InteropServices;
using System.Text;

namespace MCPForUnity.Editor.Security
{
    /// <summary>
    /// Windows Credential Manager-backed key store (DPAPI-protected by the OS) via advapi32
    /// Cred* APIs. Target = "MCPForUnity.AssetGen:&lt;provider&gt;", blob = UTF-8 key bytes.
    /// </summary>
    internal sealed class WindowsCredentialKeyStore : ISecureKeyStore
    {
        private const int CRED_TYPE_GENERIC = 1;
        private const int CRED_PERSIST_LOCAL_MACHINE = 2;

        private static string Target(string providerId)
            => SecureKeyStoreConstants.ServiceName + ":" + providerId;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct CREDENTIAL
        {
            public int Flags;
            public int Type;
            public string TargetName;
            public string Comment;
            public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
            public int CredentialBlobSize;
            public IntPtr CredentialBlob;
            public int Persist;
            public int AttributeCount;
            public IntPtr Attributes;
            public string TargetAlias;
            public string UserName;
        }

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "CredReadW")]
        private static extern bool CredRead(string target, int type, int reservedFlag, out IntPtr credentialPtr);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "CredWriteW")]
        private static extern bool CredWrite([In] ref CREDENTIAL credential, int flags);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "CredDeleteW")]
        private static extern bool CredDelete(string target, int type, int flags);

        [DllImport("advapi32.dll", EntryPoint = "CredFree")]
        private static extern void CredFree(IntPtr buffer);

        public bool Has(string providerId) => TryGet(providerId, out _);

        public bool TryGet(string providerId, out string apiKey)
        {
            apiKey = null;
            if (string.IsNullOrEmpty(providerId)) return false;
            if (!CredRead(Target(providerId), CRED_TYPE_GENERIC, 0, out IntPtr ptr)) return false;
            try
            {
                var cred = Marshal.PtrToStructure<CREDENTIAL>(ptr);
                if (cred.CredentialBlobSize <= 0 || cred.CredentialBlob == IntPtr.Zero) return false;
                byte[] bytes = new byte[cred.CredentialBlobSize];
                Marshal.Copy(cred.CredentialBlob, bytes, 0, cred.CredentialBlobSize);
                apiKey = Encoding.UTF8.GetString(bytes);
                return !string.IsNullOrEmpty(apiKey);
            }
            finally
            {
                CredFree(ptr);
            }
        }

        public void Set(string providerId, string apiKey)
        {
            if (string.IsNullOrEmpty(providerId)) return;
            if (string.IsNullOrEmpty(apiKey)) { Delete(providerId); return; }
            byte[] blob = Encoding.UTF8.GetBytes(apiKey);
            IntPtr blobPtr = Marshal.AllocHGlobal(blob.Length);
            try
            {
                Marshal.Copy(blob, 0, blobPtr, blob.Length);
                var cred = new CREDENTIAL
                {
                    Type = CRED_TYPE_GENERIC,
                    TargetName = Target(providerId),
                    CredentialBlobSize = blob.Length,
                    CredentialBlob = blobPtr,
                    Persist = CRED_PERSIST_LOCAL_MACHINE,
                    UserName = providerId,
                };
                if (!CredWrite(ref cred, 0))
                {
                    throw new InvalidOperationException(
                        "CredWrite failed (Win32 " + Marshal.GetLastWin32Error() + ")");
                }
            }
            finally
            {
                Marshal.FreeHGlobal(blobPtr);
            }
        }

        public void Delete(string providerId)
        {
            if (string.IsNullOrEmpty(providerId)) return;
            CredDelete(Target(providerId), CRED_TYPE_GENERIC, 0);
        }
    }
}
