using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEditor.Compilation;
using MCPForUnity.Editor.Setup;

namespace MCPForUnityTests.Editor.Setup
{
    /// <summary>
    /// MCPForUnity.Editor sets overrideReferences, so it only sees precompiled assemblies it
    /// names explicitly. RoslynInstaller drops its DLLs into the consuming project's
    /// Assets/Plugins/Roslyn, which is outside the package — if those names are missing from
    /// the asmdef, defining USE_ROSLYN breaks the build for UPM installs (issue #1295).
    /// </summary>
    public class RoslynAsmdefReferenceTests
    {
        [Test]
        public void EditorAsmdef_ReferencesEveryDllRoslynInstallerInstalls()
        {
            List<string> installedDlls = GetInstallerDllNames();
            CollectionAssert.IsNotEmpty(installedDlls, "RoslynInstaller should declare at least one DLL");

            string asmdefJson = ReadEditorAsmdefJson();

            List<string> missing = new List<string>();
            foreach (string dll in installedDlls)
            {
                if (asmdefJson.IndexOf($"\"{dll}\"", StringComparison.Ordinal) < 0)
                {
                    missing.Add(dll);
                }
            }

            CollectionAssert.IsEmpty(missing,
                "MCPForUnity.Editor.asmdef must list every DLL RoslynInstaller installs in "
                + "precompiledReferences, otherwise USE_ROSLYN cannot compile against them. Missing: "
                + string.Join(", ", missing));
        }

        /// <summary>
        /// RoslynInstaller ships the compiler layer only (Microsoft.CodeAnalysis + CSharp). The
        /// Workspaces layer (AdhocWorkspace, Formatter, Microsoft.CodeAnalysis.Formatting) lives in
        /// Microsoft.CodeAnalysis.Workspaces.dll / Microsoft.CodeAnalysis.CSharp.Workspaces.dll, which
        /// the installer does not download and the asmdef does not reference. Any use of it under
        /// USE_ROSLYN turns into CS0234 the moment the define is enabled (issue #1391).
        /// </summary>
        [Test]
        public void EditorSources_DoNotUseRoslynWorkspacesLayer()
        {
            string[] workspacesApis =
            {
                "Microsoft.CodeAnalysis.Formatting",
                "Microsoft.CodeAnalysis.Workspaces",
                "AdhocWorkspace",
            };

            string editorRoot = Path.GetDirectoryName(ReadEditorAsmdefPath());
            List<string> offenders = new List<string>();
            foreach (string file in Directory.GetFiles(editorRoot, "*.cs", SearchOption.AllDirectories))
            {
                string source = File.ReadAllText(file);
                foreach (string api in workspacesApis)
                {
                    if (source.IndexOf(api, StringComparison.Ordinal) >= 0)
                    {
                        offenders.Add($"{Path.GetFileName(file)} uses {api}");
                    }
                }
            }

            CollectionAssert.IsEmpty(offenders,
                "MCPForUnity.Editor must only use the Roslyn compiler layer that RoslynInstaller installs; "
                + "the Workspaces layer is not shipped or referenced, so USE_ROSLYN would fail to compile. Found: "
                + string.Join(", ", offenders));
        }

        private static List<string> GetInstallerDllNames()
        {
            FieldInfo field = typeof(RoslynInstaller).GetField(
                "NuGetEntries", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(field, "RoslynInstaller.NuGetEntries not found — was it renamed?");

            List<string> names = new List<string>();
            foreach (object entry in (Array)field.GetValue(null))
            {
                // Tuple field: (packageId, version, dllPath, dllName)
                FieldInfo dllName = entry.GetType().GetField("Item4");
                Assert.IsNotNull(dllName, "NuGetEntries tuple shape changed — expected Item4 to be the DLL name");
                names.Add((string)dllName.GetValue(entry));
            }
            return names;
        }

        private static string ReadEditorAsmdefJson()
        {
            return File.ReadAllText(ReadEditorAsmdefPath());
        }

        private static string ReadEditorAsmdefPath()
        {
            string path = CompilationPipeline.GetAssemblyDefinitionFilePathFromAssemblyName("MCPForUnity.Editor");
            Assert.IsFalse(string.IsNullOrEmpty(path), "Could not locate MCPForUnity.Editor.asmdef");
            Assert.IsTrue(File.Exists(path), $"asmdef path does not exist: {path}");
            return path;
        }
    }
}
