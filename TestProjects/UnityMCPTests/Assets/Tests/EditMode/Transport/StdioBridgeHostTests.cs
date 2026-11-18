#if UNITY_EDITOR_WIN
using System;
using System.ComponentModel;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using MCPForUnity.Editor.Services.Transport.Transports;
using NUnit.Framework;

namespace MCPForUnityTests.Editor.Transport
{
    public class StdioBridgeHostTests
    {
        private const uint HandleFlagInherit = 0x00000001;

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetHandleInformation(
            IntPtr handle,
            out uint flags);

        [Test]
        public void CreateConfiguredListener_ClearsHandleInheritance()
        {
            TcpListener listener = StdioBridgeHost.CreateConfiguredListener(0);
            try
            {
                bool queried = GetHandleInformation(listener.Server.Handle, out uint flags);
                int errorCode = queried ? 0 : Marshal.GetLastWin32Error();

                Assert.IsTrue(
                    queried,
                    $"GetHandleInformation failed: {new Win32Exception(errorCode).Message}");
                Assert.AreEqual(
                    0u,
                    flags & HandleFlagInherit,
                    "The stdio listener socket must not be inherited by child processes.");
            }
            finally
            {
                listener.Stop();
            }
        }
    }
}
#endif
