using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace WTL.InternalTerminal.Editor
{
    internal sealed class InternalTerminalSettings : ScriptableSingleton<InternalTerminalSettings>
    {
        [SerializeField] private string nodeExecutable = "node";
        [SerializeField] private string npmExecutable = "npm";
        [SerializeField] private string shellExecutable = string.Empty;
        [SerializeField] private string workingDirectory = string.Empty;
        [SerializeField] private string terminalFontName = "auto";
        [SerializeField] private int terminalFontSize = 13;
        [SerializeField] private int preferredPort = 0;

        public string NodeExecutable => string.IsNullOrWhiteSpace(nodeExecutable) ? "node" : nodeExecutable;
        public string NpmExecutable => string.IsNullOrWhiteSpace(npmExecutable) ? "npm" : npmExecutable;
        public string ShellExecutable => shellExecutable ?? string.Empty;
        public string WorkingDirectory => ResolveWorkingDirectory();
        public string TerminalFontName => string.IsNullOrWhiteSpace(terminalFontName) ? "auto" : terminalFontName.Trim();
        public int TerminalFontSize => Mathf.Clamp(terminalFontSize, 8, 28);
        public bool UseAutoTerminalFont => string.Equals(TerminalFontName, "auto", StringComparison.OrdinalIgnoreCase);
        public int PreferredPort => Mathf.Clamp(preferredPort, 0, 65535);

        public void DrawPreferences()
        {
            EditorGUI.BeginChangeCheck();

            nodeExecutable = EditorGUILayout.TextField("Node Executable", NodeExecutable);
            npmExecutable = EditorGUILayout.TextField("npm Executable", NpmExecutable);
            shellExecutable = EditorGUILayout.TextField("Shell Executable", shellExecutable);
            workingDirectory = EditorGUILayout.TextField("Working Directory", workingDirectory);
            terminalFontName = EditorGUILayout.TextField("Terminal Font", TerminalFontName);
            terminalFontSize = EditorGUILayout.IntSlider("Terminal Font Size", TerminalFontSize, 8, 28);
            preferredPort = EditorGUILayout.IntField("Preferred Port", PreferredPort);

            if (EditorGUI.EndChangeCheck())
            {
                Save(true);
            }

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox("Terminal Font defaults to the packaged Sarasa Mono SC font. Use os:Font Name only if you explicitly want an installed OS font. Leave Shell Executable blank to use the platform default shell.", MessageType.Info);
        }

        public void ResetTerminalFont()
        {
            terminalFontName = "auto";
            Save(true);
        }

        private string ResolveWorkingDirectory()
        {
            if (!string.IsNullOrWhiteSpace(workingDirectory))
            {
                var expanded = Environment.ExpandEnvironmentVariables(workingDirectory);
                if (Directory.Exists(expanded))
                {
                    return expanded;
                }
            }

            return Application.dataPath;
        }

        [SettingsProvider]
        public static SettingsProvider CreateSettingsProvider()
        {
            return new SettingsProvider("Preferences/WTL/Internal Terminal", SettingsScope.User)
            {
                label = "Internal Terminal",
                guiHandler = _ => instance.DrawPreferences()
            };
        }
    }
}
