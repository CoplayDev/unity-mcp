using System.IO;
using System.Linq;
using MCPForUnity.Editor.Constants;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Security;
using MCPForUnity.Editor.Tools;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;

namespace MCPForUnityTests.Editor.Security
{
    /// <summary>
    /// Covers the execute_code delete allowlist (#1438). These tests are pure path
    /// logic so they run without entering Play Mode or touching the scene.
    /// </summary>
    public class DeletePathPolicyTests
    {
        private static readonly string[] DefaultRoots = DeletePathPolicy.DefaultAllowedRoots;

        // ──────────────────── Path resolution / allowlist ────────────────────

        [Test]
        public void LiteralUnderBuilds_IsAllowed()
        {
            Assert.IsTrue(
                DeletePathPolicy.IsDeleteAllowed("Builds/Android/app.apk", DefaultRoots, out var reason),
                reason);
        }

        [Test]
        public void NestedLibraryCache_IsAllowed()
        {
            Assert.IsTrue(
                DeletePathPolicy.IsDeleteAllowed("Library/Bee/artifacts/x.bin", DefaultRoots, out var reason),
                reason);
        }

        [Test]
        public void AllowedRootItself_IsRejected()
        {
            // Deleting "Builds" wholesale is not the same as deleting a stale file in it.
            Assert.IsFalse(
                DeletePathPolicy.IsDeleteAllowed("Builds", DefaultRoots, out var reason));
            StringAssert.Contains("allowed delete root", reason);
        }

        [Test]
        public void WholeLibrary_IsRejected()
        {
            Assert.IsFalse(
                DeletePathPolicy.IsDeleteAllowed("Library", DefaultRoots, out var reason));
            StringAssert.Contains("allowed delete root", reason);
        }

        [Test]
        public void SiblingPrefix_IsRejected()
        {
            // "BuildsEvil" must not match the "Builds" root.
            Assert.IsFalse(
                DeletePathPolicy.IsDeleteAllowed("BuildsEvil/x.dll", DefaultRoots, out var reason));
            StringAssert.Contains("allowed delete root", reason);
        }

        [Test]
        public void Assets_IsRejectedEvenWhenConfigured()
        {
            var roots = new[] { "Assets", "Builds" };

            Assert.IsFalse(
                DeletePathPolicy.IsDeleteAllowed("Assets/Scripts/Player.cs", roots, out var reason));
            StringAssert.Contains("never deletable", reason);
        }

        [Test]
        public void TraversalOutsideProject_IsRejected()
        {
            Assert.IsFalse(
                DeletePathPolicy.IsDeleteAllowed("Builds/../../outside.txt", DefaultRoots, out var reason));
            StringAssert.Contains("outside the Unity project", reason);
        }

        [Test]
        public void ProjectRootItself_IsRejected()
        {
            Assert.IsFalse(
                DeletePathPolicy.IsDeleteAllowed(DeletePathPolicy.ProjectRoot, DefaultRoots, out var reason));
            StringAssert.Contains("project root", reason);
        }

        [Test]
        public void AbsolutePathOutsideProject_IsRejected()
        {
            string outside = Path.Combine(
                Path.GetDirectoryName(DeletePathPolicy.ProjectRoot) ?? DeletePathPolicy.ProjectRoot,
                "outside.txt");

            Assert.IsFalse(
                DeletePathPolicy.IsDeleteAllowed(outside, DefaultRoots, out var reason));
            StringAssert.Contains("outside the Unity project", reason);
        }

        [Test]
        public void AbsolutePathInsideAllowedRoot_IsAllowed()
        {
            string path = Path.Combine(DeletePathPolicy.ProjectRoot, "Builds", "Android", "app.apk");

            Assert.IsTrue(
                DeletePathPolicy.IsDeleteAllowed(path, DefaultRoots, out var reason),
                reason);
        }

        [Test]
        public void UncPath_IsRejected()
        {
            Assert.IsFalse(
                DeletePathPolicy.IsDeleteAllowed(@"\\server\share\x", DefaultRoots, out var reason));
            StringAssert.Contains("UNC", reason);
        }

        [Test]
        public void AlternateDataStream_IsRejected()
        {
            Assert.IsFalse(
                DeletePathPolicy.IsDeleteAllowed("Builds/x.txt:stream", DefaultRoots, out var reason));
            StringAssert.Contains("alternate data stream", reason);
        }

        [Test]
        public void EmptyAllowedRoots_DenyEverything()
        {
            Assert.IsFalse(
                DeletePathPolicy.IsDeleteAllowed("Builds/x", new string[0], out var reason));
            StringAssert.Contains("No delete roots", reason);
        }

        // ──────────────────── Root configuration normalization ────────────────────

        [Test]
        public void Normalize_DropsUnsafeEntries()
        {
            var normalized = DeletePathPolicy.NormalizeAllowedRoots(new[]
            {
                "Builds",
                "Builds/",          // normalized to the same entry
                "/Builds",          // rooted -> dropped
                "Assets",           // hard-denied -> dropped
                "../evil",          // traversal -> dropped
                "C:\\Windows",      // rooted -> dropped
                "Library/Bee",
            });

            CollectionAssert.Contains(normalized, "Builds");
            CollectionAssert.Contains(normalized, "Library/Bee");
            Assert.IsFalse(normalized.Any(r => r.StartsWith("Assets", System.StringComparison.OrdinalIgnoreCase)));
            Assert.IsFalse(normalized.Any(r => r.Contains("..")));
            Assert.AreEqual(normalized.Count, normalized.Distinct().Count());
        }

        [Test]
        public void Normalize_UnsetFallsBackToDefaults()
        {
            var normalized = DeletePathPolicy.NormalizeAllowedRoots(null);

            CollectionAssert.AreEquivalent(DeletePathPolicy.DefaultAllowedRoots, normalized);
        }

        [Test]
        public void Normalize_ExplicitEmptyStaysEmpty()
        {
            // An explicit empty configuration is a lockdown, not "use defaults".
            Assert.AreEqual(0, DeletePathPolicy.NormalizeAllowedRoots(new string[0]).Count);
        }

        // ──────────────────── Source scanning ────────────────────

        [Test]
        public void Source_LiteralUnderBuilds_IsAllowed()
        {
            Assert.IsNull(Check(@"System.IO.File.Delete(""Builds/Android/app.apk"");"));
        }

        [Test]
        public void Source_DirectoryDeleteRecursive_IsAllowed()
        {
            Assert.IsNull(Check(@"Directory.Delete(""Temp/cache"", true);"));
        }

        [Test]
        public void Source_NoDeleteCall_IsAllowed()
        {
            Assert.IsNull(Check("return 1 + 1;"));
        }

        [Test]
        public void Source_VariablePath_IsRejected()
        {
            string error = Check(@"File.Delete(target);");

            Assert.IsNotNull(error);
            StringAssert.Contains("string literal", error);
        }

        [Test]
        public void Source_InterpolatedPath_IsRejected()
        {
            string error = Check(@"File.Delete($""Builds/{name}"");");

            Assert.IsNotNull(error);
            StringAssert.Contains("Interpolated", error);
        }

        [Test]
        public void Source_ConcatenatedPath_IsRejected()
        {
            string error = Check(@"File.Delete(""Builds/"" + name);");

            Assert.IsNotNull(error);
            StringAssert.Contains("single string literal", error);
        }

        [Test]
        public void Source_AssetsLiteral_IsRejected()
        {
            string error = Check(@"File.Delete(""Assets/Scripts/Player.cs"");");

            Assert.IsNotNull(error);
            StringAssert.Contains("never deletable", error);
        }

        [Test]
        public void Source_UsingStaticImport_IsRejected()
        {
            string error = Check("using static System.IO.File;\nDelete(\"Builds/x\");");

            Assert.IsNotNull(error);
            StringAssert.Contains("using static", error);
        }

        [Test]
        public void Source_FileInfoConstruction_IsRejected()
        {
            string error = Check(@"new System.IO.FileInfo(""Builds/x"").Delete();");

            Assert.IsNotNull(error);
            StringAssert.Contains("FileInfo", error);
        }

        [Test]
        public void Source_MethodGroup_IsRejected()
        {
            // A method group has no inspectable path argument.
            string error = Check(@"System.Action d = System.IO.File.Delete; d(""Assets/Scripts/Player.cs"");");

            Assert.IsNotNull(error);
            StringAssert.Contains("cannot be checked", error);
        }

        [Test]
        public void Source_CommentBetweenDeleteAndParen_IsRejected()
        {
            // The old "substring" check caught this form; the member-reference check must too.
            string error = Check(@"File.Delete/**/(""Assets/Scripts/Player.cs"");");

            Assert.IsNotNull(error);
            StringAssert.Contains("cannot be checked", error);
        }

        // ──────────────────── End-to-end through the tool ────────────────────

        [Test]
        public void ExecuteCode_DeletesFileUnderTemp()
        {
            string directory = Path.Combine(DeletePathPolicy.ProjectRoot, "Temp");
            Directory.CreateDirectory(directory);

            string file = Path.Combine(directory, "mcp-delete-allowlist-test.tmp");
            File.WriteAllText(file, "delete me");

            bool hadPreference = EditorPrefs.HasKey(EditorPrefKeys.ExecuteCodeDeleteRoots);
            string previousPreference = EditorPrefs.GetString(
                EditorPrefKeys.ExecuteCodeDeleteRoots, string.Empty);

            try
            {
                // The developer's saved allowlist must not decide whether this test passes.
                ExecuteCodePrefs.ResetAllowedDeleteRoots();

                // Absolute literal: the compiled snippet's working directory is not part of
                // this contract, and the allowlist accepts any literal inside the root.
                string literal = file.Replace('\\', '/');
                var result = MCPForUnityTests.Editor.TestUtilities.ToJObject(
                    ExecuteCode.HandleCommand(new JObject
                    {
                        ["action"] = "execute",
                        ["code"] = $"System.IO.File.Delete(\"{literal}\");\nreturn null;",
                    }));

                Assert.IsTrue(result.Value<bool>("success"), result.ToString());
                Assert.IsFalse(File.Exists(file), "File should have been deleted through execute_code.");
            }
            finally
            {
                if (File.Exists(file))
                    File.Delete(file);

                if (hadPreference)
                    EditorPrefs.SetString(EditorPrefKeys.ExecuteCodeDeleteRoots, previousPreference);
                else
                    EditorPrefs.DeleteKey(EditorPrefKeys.ExecuteCodeDeleteRoots);
            }
        }

        [Test]
        public void ExecuteCode_BlocksDeleteOutsideAllowedRoots()
        {
            var result = MCPForUnityTests.Editor.TestUtilities.ToJObject(
                ExecuteCode.HandleCommand(new JObject
                {
                    ["action"] = "execute",
                    ["code"] = "System.IO.File.Delete(\"Assets/should-not-be-deleted.txt\");\nreturn null;",
                }));

            Assert.IsFalse(result.Value<bool>("success"), result.ToString());
            StringAssert.Contains("Blocked pattern", result.Value<string>("error"));
        }

        [Test]
        public void ExecuteCode_BlocksCurrentDirectorySwitch()
        {
            // Without this block a snippet could validate "Builds/x" against the project
            // root and then change CWD to Assets before deleting it.
            var result = MCPForUnityTests.Editor.TestUtilities.ToJObject(
                ExecuteCode.HandleCommand(new JObject
                {
                    ["action"] = "execute",
                    ["code"] = "System.IO.Directory.SetCurrentDirectory(\"Assets\");\nreturn null;",
                }));

            Assert.IsFalse(result.Value<bool>("success"), result.ToString());
            StringAssert.Contains("CurrentDirectory", result.Value<string>("error"));
        }

        private static string Check(string code)
        {
            // Pass the project root as the runtime directory so the relative-path
            // consistency check behaves like a normal Editor session.
            return DeletePathPolicy.CheckSource(code, DefaultRoots, DeletePathPolicy.ProjectRoot);
        }
    }
}
