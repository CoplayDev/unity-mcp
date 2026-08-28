using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

namespace MCPForUnity.Editor.Helpers
{
    /// <summary>
    /// Describes a native modal dialog blocking the Editor's main thread.
    /// </summary>
    internal sealed class ModalDialogInfo
    {
        public bool Supported { get; set; }
        public bool Blocked { get; set; }

        /// <summary>
        /// "dialog" for a native <c>EditorUtility.DisplayDialog</c> box, whose buttons are real OS
        /// controls; "editor_window" for a Unity-drawn modal (<c>EditorWindow.ShowModal</c>), which
        /// blocks identically but paints its own buttons with IMGUI.
        /// </summary>
        public string Kind { get; set; }

        /// <summary>Whether a specific button can actually be pressed programmatically.</summary>
        public bool Answerable { get; set; }

        public long Handle { get; set; }
        public string Title { get; set; }
        public string Body { get; set; }
        public List<string> Buttons { get; } = new List<string>();
    }

    /// <summary>
    /// Reads (and can answer) the native modal dialog that blocks Unity's main thread.
    ///
    /// Windows only. <c>EditorUtility.DisplayDialog</c> raises a standard Win32 dialog (class
    /// <c>#32770</c>) owned by the Editor process, whose title, body and button labels are all
    /// readable, and whose buttons are real <c>Button</c> controls that accept <c>BM_CLICK</c>.
    ///
    /// Every text read goes through <see cref="SendMessageTimeoutW"/>: <c>GetWindowText</c> on a
    /// window owned by the calling process is a synchronous <c>WM_GETTEXT</c>, which hangs when the
    /// owning thread is not pumping messages — exactly the case this probe reports on.
    /// </summary>
    internal static class ModalDialogProbe
    {
        private const string DialogWindowClass = "#32770";
        private const string ButtonWindowClass = "Button";
        private const uint WM_GETTEXT = 0x000D;
        private const uint BM_CLICK = 0x00F5;
        private const uint SMTO_ABORTIFHUNG = 0x0002;
        private const int TextReadTimeoutMs = 400;
        private const int MaxTopLevelScan = 5000;
        private const int MaxChildScan = 200;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindowExW(IntPtr parent, IntPtr childAfter, IntPtr className, IntPtr windowName);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsWindowEnabled(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassNameW(IntPtr hWnd, StringBuilder buffer, int maxCount);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessageTimeoutW(
            IntPtr hWnd, uint msg, IntPtr wParam, StringBuilder lParam, uint flags, uint timeoutMs, out IntPtr result);

        [DllImport("user32.dll")]
        private static extern bool PostMessageW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        internal static bool IsSupported =>
            Application.platform == RuntimePlatform.WindowsEditor;

        /// <summary>
        /// Look for a modal dialog owned by this process. Safe to call from a background thread.
        /// </summary>
        internal static ModalDialogInfo Capture()
        {
            var info = new ModalDialogInfo { Supported = IsSupported };
            if (!info.Supported)
            {
                return info;
            }

            try
            {
                uint myPid = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;

                // Windows disables a modal's owner while the modal is up. That — not the window
                // class — is what "a modal is blocking the Editor" means, and it is what makes a
                // Unity-drawn EditorWindow.ShowModal visible here too.
                var enabled = new List<IntPtr>();
                bool anyDisabled = false;

                IntPtr window = IntPtr.Zero;
                for (int i = 0; i < MaxTopLevelScan; i++)
                {
                    window = FindWindowExW(IntPtr.Zero, window, IntPtr.Zero, IntPtr.Zero);
                    if (window == IntPtr.Zero)
                    {
                        break;
                    }

                    GetWindowThreadProcessId(window, out uint pid);
                    if (pid != myPid || !IsWindowVisible(window))
                    {
                        continue;
                    }

                    if (IsWindowEnabled(window))
                    {
                        enabled.Add(window);
                    }
                    else
                    {
                        anyDisabled = true;
                    }
                }

                if (!anyDisabled || enabled.Count == 0)
                {
                    return info;
                }

                // Prefer a native dialog when one is up: it is the only kind whose buttons can be
                // read and pressed.
                IntPtr modal = IntPtr.Zero;
                foreach (var candidate in enabled)
                {
                    if (string.Equals(ClassOf(candidate), DialogWindowClass, StringComparison.Ordinal))
                    {
                        modal = candidate;
                        break;
                    }
                }

                bool isNativeDialog = modal != IntPtr.Zero;
                if (!isNativeDialog)
                {
                    modal = enabled[0];
                }

                info.Blocked = true;
                info.Handle = modal.ToInt64();
                info.Title = TextOf(modal);
                info.Kind = isNativeDialog ? "dialog" : "editor_window";

                if (!isNativeDialog)
                {
                    // EditorWindow.ShowModal paints its buttons with IMGUI, so there are no child
                    // controls to enumerate. Measured: it keeps repainting while blocked yet ignores
                    // synthetic WM_LBUTTONDOWN/UP from both PostMessage and SendMessage, activated or
                    // not. Reported as blocked, but not answerable.
                    return info;
                }

                var bodyParts = new List<string>();
                foreach (var child in ChildrenOf(modal))
                {
                    string cls = ClassOf(child);
                    string text = TextOf(child);
                    if (string.IsNullOrEmpty(text))
                    {
                        continue;
                    }

                    if (string.Equals(cls, ButtonWindowClass, StringComparison.Ordinal))
                    {
                        info.Buttons.Add(StripAccelerator(text));
                    }
                    else
                    {
                        bodyParts.Add(text);
                    }
                }

                info.Body = bodyParts.Count > 0 ? string.Join("\n", bodyParts.ToArray()) : null;
                info.Answerable = info.Buttons.Count > 0;
            }
            catch (Exception ex)
            {
                McpLog.Warn($"Modal dialog probe failed: {ex.Message}");
            }

            return info;
        }

        /// <summary>
        /// Press a button on the currently open modal dialog. Safe to call from a background thread.
        /// The dialog is re-identified and its title re-checked so the press cannot land on a
        /// different dialog than the caller was shown.
        /// </summary>
        internal static bool TryAnswer(string expectedTitle, string buttonLabel, out string error, out ModalDialogInfo observed)
        {
            observed = Capture();
            error = null;

            if (!observed.Supported)
            {
                error = "Answering modal dialogs is only supported in the Windows Editor.";
                return false;
            }

            if (!observed.Blocked)
            {
                error = "No modal dialog is currently open in the Unity Editor.";
                return false;
            }

            if (!observed.Answerable)
            {
                error = $"Modal window '{observed.Title}' can't be answered programmatically; dismiss it in the Editor.";
                return false;
            }

            if (!string.IsNullOrEmpty(expectedTitle)
                && !string.Equals(expectedTitle, observed.Title, StringComparison.Ordinal))
            {
                error = $"Dialog changed: expected '{expectedTitle}' but '{observed.Title}' is open.";
                return false;
            }

            var handle = new IntPtr(observed.Handle);
            if (!IsWindow(handle))
            {
                error = "The dialog closed before it could be answered.";
                return false;
            }

            foreach (var child in ChildrenOf(handle))
            {
                if (!string.Equals(ClassOf(child), ButtonWindowClass, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!string.Equals(StripAccelerator(TextOf(child)), buttonLabel, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                PostMessageW(child, BM_CLICK, IntPtr.Zero, IntPtr.Zero);
                return true;
            }

            error = $"Dialog '{observed.Title}' has no button labelled '{buttonLabel}'. Available: {string.Join(", ", observed.Buttons.ToArray())}";
            return false;
        }

        private static List<IntPtr> ChildrenOf(IntPtr parent)
        {
            var children = new List<IntPtr>();
            IntPtr child = IntPtr.Zero;
            for (int i = 0; i < MaxChildScan; i++)
            {
                child = FindWindowExW(parent, child, IntPtr.Zero, IntPtr.Zero);
                if (child == IntPtr.Zero)
                {
                    break;
                }

                children.Add(child);
            }

            return children;
        }

        private static string ClassOf(IntPtr hWnd)
        {
            var buffer = new StringBuilder(256);
            GetClassNameW(hWnd, buffer, buffer.Capacity);
            return buffer.ToString();
        }

        private static string TextOf(IntPtr hWnd)
        {
            var buffer = new StringBuilder(2048);
            IntPtr sent = SendMessageTimeoutW(
                hWnd, WM_GETTEXT, new IntPtr(buffer.Capacity), buffer, SMTO_ABORTIFHUNG, TextReadTimeoutMs, out _);
            return sent == IntPtr.Zero ? string.Empty : buffer.ToString();
        }

        private static string StripAccelerator(string label)
        {
            return string.IsNullOrEmpty(label) ? label : label.Replace("&", string.Empty);
        }
    }
}
