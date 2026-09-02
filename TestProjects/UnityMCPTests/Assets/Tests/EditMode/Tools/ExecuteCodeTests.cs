using System;
using System.CodeDom.Compiler;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CSharp;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using MCPForUnity.Editor.Tools;
using static MCPForUnityTests.Editor.TestUtilities;

namespace MCPForUnityTests.Editor.Tools
{
    public class ExecuteCodeTests
    {
        [SetUp]
        public void SetUp()
        {
            ExecuteCode.HandleCommand(new JObject { ["action"] = "clear_history" });
        }

        // ──────────────────── Execute: success cases ────────────────────

        [Test]
        public void Execute_ReturnString_ReturnsSuccess()
        {
            var result = Execute("return \"hello\";");

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            Assert.AreEqual("hello", result["data"]["result"].Value<string>());
        }

        [Test]
        public void Execute_ReturnInt_ReturnsSuccess()
        {
            var result = Execute("return 42;");

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            Assert.AreEqual(42, result["data"]["result"].Value<int>());
        }

        [Test]
        public void Execute_ReturnNull_NoResultValue()
        {
            var result = Execute("int x = 1; return null;");

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            // data may contain compiler info but should not have a "result" key
            var data = result["data"] as JObject;
            if (data != null)
                Assert.IsNull(data["result"], "Expected no 'result' key when code returns null");
        }

        [Test]
        public void Execute_VoidReturn_Succeeds()
        {
            var result = Execute("UnityEngine.Debug.Log(\"test\"); return null;");

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
        }

        [Test]
        public void Execute_UnityAPI_CanAccessSceneManager()
        {
            var result = Execute(
                "var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();\n" +
                "return scene.name;");

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            Assert.IsNotNull(result["data"]["result"]);
        }

        [Test]
        public void Execute_Generics_ListOfString()
        {
            var result = Execute(
                "var list = new System.Collections.Generic.List<string>();\n" +
                "list.Add(\"a\"); list.Add(\"b\");\n" +
                "return list;");

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            var arr = result["data"]["result"] as JArray;
            Assert.IsNotNull(arr, "Expected array result");
            Assert.AreEqual(2, arr.Count);
        }

        [Test]
        public void Execute_LINQ_SelectWorks()
        {
            var result = Execute(
                "var nums = new int[] { 1, 2, 3 };\n" +
                "return nums.Select(n => n * 2).ToList();");

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            var arr = result["data"]["result"] as JArray;
            Assert.IsNotNull(arr);
            Assert.AreEqual(3, arr.Count);
            Assert.AreEqual(2, arr[0].Value<int>());
            Assert.AreEqual(6, arr[2].Value<int>());
        }

        [Test]
        public void Execute_Dictionary_ReturnsStructured()
        {
            var result = Execute(
                "var dict = new Dictionary<string, int> { { \"a\", 1 }, { \"b\", 2 } };\n" +
                "return dict;");

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            Assert.IsNotNull(result["data"]["result"]);
        }

        // ──────────────────── Execute: error cases ────────────────────

        [Test]
        public void Execute_CompilationError_ReturnsErrors()
        {
            var result = Execute("int x = \"not an int\";");

            Assert.IsFalse(result.Value<bool>("success"), result.ToString());
            StringAssert.Contains("Compilation failed", result.Value<string>("error"));
            Assert.IsNotNull(result["data"]["errors"]);
        }

        [Test]
        public void Execute_RuntimeException_ReturnsError()
        {
            var result = Execute("throw new System.Exception(\"boom\");");

            Assert.IsFalse(result.Value<bool>("success"), result.ToString());
            StringAssert.Contains("boom", result.Value<string>("error"));
        }

        [Test]
        public void Execute_MissingCode_ReturnsError()
        {
            var result = ToJObject(ExecuteCode.HandleCommand(new JObject
            {
                ["action"] = "execute"
            }));

            Assert.IsFalse(result.Value<bool>("success"), result.ToString());
            StringAssert.Contains("code", result.Value<string>("error").ToLowerInvariant());
        }

        [Test]
        public void Execute_EmptyCode_ReturnsError()
        {
            var result = ToJObject(ExecuteCode.HandleCommand(new JObject
            {
                ["action"] = "execute",
                ["code"] = "   "
            }));

            Assert.IsFalse(result.Value<bool>("success"), result.ToString());
        }

        // ──────────────────── Safety checks ────────────────────

        [Test]
        public void Execute_SafetyChecks_BlocksFileDelete()
        {
            var result = Execute("System.IO.File.Delete(\"x\");");

            Assert.IsFalse(result.Value<bool>("success"), result.ToString());
            StringAssert.Contains("Blocked pattern", result.Value<string>("error"));
        }

        [Test]
        public void Execute_SafetyChecks_BlocksProcessStart()
        {
            var result = Execute("Process.Start(\"cmd\");");

            Assert.IsFalse(result.Value<bool>("success"), result.ToString());
            StringAssert.Contains("Blocked pattern", result.Value<string>("error"));
        }

        [Test]
        public void Execute_SafetyChecks_BlocksInfiniteLoop()
        {
            var result = Execute("while (true) { }");

            Assert.IsFalse(result.Value<bool>("success"), result.ToString());
            StringAssert.Contains("Blocked pattern", result.Value<string>("error"));
        }

        [Test]
        public void Execute_SafetyChecksDisabled_AllowsBlockedPattern()
        {
            var result = ToJObject(ExecuteCode.HandleCommand(new JObject
            {
                ["action"] = "execute",
                ["code"] = "while (true) { break; }  return null;",
                ["safety_checks"] = false
            }));

            if (!result.Value<bool>("success"))
            {
                var error = result.Value<string>("error") ?? "";
                Assert.IsFalse(error.Contains("Blocked pattern"),
                    "Safety checks should be disabled but still blocked");
            }
        }

        // ──────────────────── History ────────────────────

        [Test]
        public void GetHistory_Empty_ReturnsZero()
        {
            var result = ToJObject(ExecuteCode.HandleCommand(new JObject
            {
                ["action"] = "get_history"
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            Assert.AreEqual(0, result["data"]["total"].Value<int>());
        }

        [Test]
        public void GetHistory_AfterExecution_RecordsEntry()
        {
            Execute("return 1;");

            var result = ToJObject(ExecuteCode.HandleCommand(new JObject
            {
                ["action"] = "get_history"
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            Assert.AreEqual(1, result["data"]["total"].Value<int>());
            var entries = result["data"]["entries"] as JArray;
            Assert.IsNotNull(entries);
            Assert.AreEqual(1, entries.Count);
            Assert.IsTrue(entries[0]["success"].Value<bool>());
        }

        [Test]
        public void GetHistory_Limit_RespectsParameter()
        {
            Execute("return 1;");
            Execute("return 2;");
            Execute("return 3;");

            var result = ToJObject(ExecuteCode.HandleCommand(new JObject
            {
                ["action"] = "get_history",
                ["limit"] = 2
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            Assert.AreEqual(3, result["data"]["total"].Value<int>());
            var entries = result["data"]["entries"] as JArray;
            Assert.AreEqual(2, entries.Count);
        }

        [Test]
        public void ClearHistory_RemovesAll()
        {
            Execute("return 1;");
            Execute("return 2;");

            var clearResult = ToJObject(ExecuteCode.HandleCommand(new JObject
            {
                ["action"] = "clear_history"
            }));
            Assert.IsTrue(clearResult.Value<bool>("success"), clearResult.ToString());

            var historyResult = ToJObject(ExecuteCode.HandleCommand(new JObject
            {
                ["action"] = "get_history"
            }));
            Assert.AreEqual(0, historyResult["data"]["total"].Value<int>());
        }

        // ──────────────────── Replay ────────────────────

        [Test]
        public void Replay_ValidIndex_ReExecutes()
        {
            Execute("return 42;");

            var result = ToJObject(ExecuteCode.HandleCommand(new JObject
            {
                ["action"] = "replay",
                ["index"] = 0
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            Assert.AreEqual(42, result["data"]["result"].Value<int>());
        }

        [Test]
        public void Replay_InvalidIndex_ReturnsError()
        {
            Execute("return 1;");

            var result = ToJObject(ExecuteCode.HandleCommand(new JObject
            {
                ["action"] = "replay",
                ["index"] = 99
            }));

            Assert.IsFalse(result.Value<bool>("success"), result.ToString());
            StringAssert.Contains("Invalid history index", result.Value<string>("error"));
        }

        [Test]
        public void Replay_EmptyHistory_ReturnsError()
        {
            var result = ToJObject(ExecuteCode.HandleCommand(new JObject
            {
                ["action"] = "replay",
                ["index"] = 0
            }));

            Assert.IsFalse(result.Value<bool>("success"), result.ToString());
        }

        // ──────────────────── Action validation ────────────────────

        [Test]
        public void UnknownAction_ReturnsError()
        {
            var result = ToJObject(ExecuteCode.HandleCommand(new JObject
            {
                ["action"] = "invalid_action"
            }));

            Assert.IsFalse(result.Value<bool>("success"), result.ToString());
            StringAssert.Contains("Unknown action", result.Value<string>("error"));
        }

        [Test]
        public void NullParams_ReturnsError()
        {
            var result = ToJObject(ExecuteCode.HandleCommand(null));

            Assert.IsFalse(result.Value<bool>("success"), result.ToString());
        }

        // ──────────────────── CodeDom backend ────────────────────

        // Regression for CoplayDev/unity-mcp#1144: large projects (~100+ asmdefs) blew past the
        // Windows 32 KB CreateProcess limit because every reference became an inline /r: flag.
        // The fix routes references through a @responsefile, so this just verifies that the
        // codedom path still compiles and runs end-to-end.
        [Test]
        public void Execute_CodedomBackend_CompilesAndRuns()
        {
            var result = ToJObject(ExecuteCode.HandleCommand(new JObject
            {
                ["action"] = "execute",
                ["code"] = "return 1 + 1;",
                ["compiler"] = "codedom"
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            Assert.AreEqual(2, result["data"]["result"].Value<int>());
            Assert.AreEqual("codedom", result["data"]["compiler"].Value<string>());
        }

        [Test]
        public void Execute_CodedomBackend_ResolvesUnityTypes()
        {
            var result = ToJObject(ExecuteCode.HandleCommand(new JObject
            {
                ["action"] = "execute",
                ["code"] = "return UnityEngine.Application.unityVersion;",
                ["compiler"] = "codedom"
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            Assert.IsNotNull(result["data"]["result"]);
        }

        [Test]
        public void FilterAssemblyPathsForCodeDom_WithNetstandard_PreservesSystemSecurity()
        {
            var tempRoot = CreateTempDirectory();
            try
            {
                var netstandardPath = CompileVersionedAssembly(tempRoot, "netstandard", "2.0.0.0");
                var securityFixturePath = CompileVersionedAssembly(tempRoot, "SystemSecurityFixture", "4.0.0.0");
                var systemSecurityPath = Path.Combine(
                    Path.GetDirectoryName(securityFixturePath),
                    "System.Security.dll");
                File.Copy(securityFixturePath, systemSecurityPath);

                var filtered = ExecuteCode.FilterAssemblyPathsForCodeDom(new[]
                {
                    netstandardPath,
                    systemSecurityPath,
                });

                CollectionAssert.Contains(filtered, systemSecurityPath);
            }
            finally
            {
                Directory.Delete(tempRoot, true);
            }
        }

        [Test]
        public void FilterAssemblyPathsForCodeDom_DuplicateNames_PrefersReferencedVersion()
        {
            var tempRoot = CreateTempDirectory();
            try
            {
                var assemblyName = "McpCodeDomDuplicate" + Guid.NewGuid().ToString("N");
                var referencedPath = CompileVersionedAssembly(tempRoot, assemblyName, "1.0.0.0");
                var newerPath = CompileVersionedAssembly(tempRoot, assemblyName, "2.0.0.0");
                LoadAssemblyReferencing(referencedPath);

                var filtered = ExecuteCode.FilterAssemblyPathsForCodeDom(new[]
                {
                    newerPath,
                    referencedPath,
                });

                Assert.AreEqual(1, filtered.Length);
                Assert.AreEqual(referencedPath, filtered[0]);
            }
            finally
            {
                Directory.Delete(tempRoot, true);
            }
        }

        [Test]
        public void FilterAssemblyPathsForCodeDom_CachedAssemblyPaths_ReusesResultUntilDomainReload()
        {
            var tempRoot = CreateTempDirectory();
            var cachedAssemblyPathsField = typeof(ExecuteCode).GetField(
                "_cachedAssemblyPaths",
                BindingFlags.NonPublic | BindingFlags.Static);
            var cachedCodeDomAssemblyPathsField = typeof(ExecuteCode).GetField(
                "_cachedCodeDomAssemblyPaths",
                BindingFlags.NonPublic | BindingFlags.Static);
            var onDomainReload = typeof(ExecuteCode).GetMethod(
                "OnDomainReload",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(cachedAssemblyPathsField);
            Assert.IsNotNull(cachedCodeDomAssemblyPathsField);
            Assert.IsNotNull(onDomainReload);

            try
            {
                onDomainReload.Invoke(null, null);
                var assemblyName = "McpCodeDomCache" + Guid.NewGuid().ToString("N");
                var olderPath = CompileVersionedAssembly(tempRoot, assemblyName, "1.0.0.0");
                var newerPath = CompileVersionedAssembly(tempRoot, assemblyName, "2.0.0.0");
                var cachedAssemblyPaths = new[] { olderPath, newerPath };
                cachedAssemblyPathsField.SetValue(null, cachedAssemblyPaths);

                var first = ExecuteCode.FilterAssemblyPathsForCodeDom(cachedAssemblyPaths);
                Assert.AreEqual(1, first.Length);

                File.WriteAllText(olderPath, "invalidated");
                File.WriteAllText(newerPath, "invalidated");
                var second = ExecuteCode.FilterAssemblyPathsForCodeDom(cachedAssemblyPaths);
                Assert.AreSame(first, second);

                onDomainReload.Invoke(null, null);
                cachedAssemblyPathsField.SetValue(null, cachedAssemblyPaths);
                var afterReload = ExecuteCode.FilterAssemblyPathsForCodeDom(cachedAssemblyPaths);
                Assert.AreNotSame(first, afterReload);
                Assert.AreEqual(2, afterReload.Length);
            }
            finally
            {
                onDomainReload.Invoke(null, null);
                Directory.Delete(tempRoot, true);
            }
        }

        // ──────────────────── Helpers ────────────────────

        private static string CreateTempDirectory()
        {
            var path = Path.Combine(Path.GetTempPath(), "UnityMCPTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        private static string CompileVersionedAssembly(string tempRoot, string assemblyName, string version)
        {
            var outputDirectory = Path.Combine(tempRoot, version);
            Directory.CreateDirectory(outputDirectory);
            var outputPath = Path.Combine(outputDirectory, assemblyName + ".dll");
            var source =
                "using System.Reflection;\n" +
                "[assembly: AssemblyVersion(\"" + version + "\")]\n" +
                "public sealed class VersionMarker { }";

            using (var provider = new CSharpCodeProvider())
            {
                var parameters = new CompilerParameters
                {
                    GenerateExecutable = false,
                    GenerateInMemory = false,
                    OutputAssembly = outputPath,
                };
                var results = provider.CompileAssemblyFromSource(parameters, source);
                AssertCompilerSuccess(results);
            }

            return outputPath;
        }

        private static void LoadAssemblyReferencing(string referencedAssemblyPath)
        {
            using (var provider = new CSharpCodeProvider())
            {
                var parameters = new CompilerParameters
                {
                    GenerateExecutable = false,
                    GenerateInMemory = true,
                };
                parameters.ReferencedAssemblies.Add(referencedAssemblyPath);

                var results = provider.CompileAssemblyFromSource(
                    parameters,
                    "public static class ReferenceHolder { " +
                    "public static System.Type Get() { return typeof(VersionMarker); } }");
                AssertCompilerSuccess(results);
                Assert.IsNotNull(results.CompiledAssembly);
            }
        }

        private static void AssertCompilerSuccess(CompilerResults results)
        {
            var errors = results.Errors
                .Cast<CompilerError>()
                .Where(error => !error.IsWarning)
                .Select(error => error.ToString())
                .ToArray();
            Assert.IsFalse(results.Errors.HasErrors, string.Join("\n", errors));
        }


        // ──────────────────── Execute: using directives ────────────────────

        [Test]
        public void Execute_LeadingUsingDirective_IsHoistedAndCompiles()
        {
            var result = Execute("using System.IO;\nreturn Path.GetFileName(\"/a/b.txt\");");

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            Assert.AreEqual("b.txt", result["data"]["result"].Value<string>());
        }

        [Test]
        public void Execute_UsingStaticDirective_IsHoisted()
        {
            var result = Execute("using static System.Math;\nreturn Max(3, 4);");

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            Assert.AreEqual(4, result["data"]["result"].Value<int>());
        }

        [Test]
        public void Execute_UsingAliasWithGenericTarget_IsHoisted()
        {
            var result = Execute("using L = System.Collections.Generic.List<int>;\nreturn new L { 1, 2 }.Count;");

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            Assert.AreEqual(2, result["data"]["result"].Value<int>());
        }

        [Test]
        public void Execute_UsingAfterComment_IsStillHoisted()
        {
            var result = Execute("// leading comment\nusing System.IO;\nreturn Path.GetFileName(\"/x/y.txt\");");

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            Assert.AreEqual("y.txt", result["data"]["result"].Value<string>());
        }


        [Test]
        public void Execute_RedundantBuiltinUsing_DoesNotDuplicate()
        {
            var result = Execute("using System;\nreturn String.Concat(\"a\", \"b\");");

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            Assert.AreEqual("ab", result["data"]["result"].Value<string>());
        }

        [Test]
        public void Execute_ErrorLineNumber_CountsFromCallerCodeAfterHoisting()
        {
            var result = Execute("using System.IO;\nint ok = 1;\nnope();\nreturn ok;");

            Assert.IsFalse(result.Value<bool>("success"), result.ToString());
            var errors = string.Join("\n", (result["data"]["errors"] as JArray).Select(e => e.Value<string>()));
            StringAssert.Contains("Line 3", errors);
        }

        [Test]
        public void Execute_UsingWithTrailingComment_IsHoisted()
        {
            var result = Execute("using System.IO; // needed for Path\nreturn Path.GetFileName(\"/a/b.txt\");");

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            Assert.AreEqual("b.txt", result["data"]["result"].Value<string>());
        }

        [Test]
        public void Execute_UsingAfterBlockComment_IsStillHoisted()
        {
            var result = Execute("/* lead\n   in */\nusing System.IO;\nreturn Path.GetFileName(\"/a/c.txt\");");

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            Assert.AreEqual("c.txt", result["data"]["result"].Value<string>());
        }

        [Test]
        public void Execute_UsingBehindInlineBlockComment_IsHoisted()
        {
            var result = Execute("/* why */ using System.IO;\nreturn Path.GetFileName(\"/a/d.txt\");");

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            Assert.AreEqual("d.txt", result["data"]["result"].Value<string>());
        }

        // ──────────────────── Execute: hoisting must not widen safety_checks ────────────────────

        [Test]
        public void Execute_HoistedNamespace_DoesNotBypassSafetyChecks()
        {
            var result = Execute("using System.IO;\nFile.Delete(\"/tmp/mcp-does-not-exist\");\nreturn 1;");

            Assert.IsFalse(result.Value<bool>("success"), result.ToString());
            StringAssert.Contains("Blocked pattern", result.Value<string>("error"));
        }

        [Test]
        public void Execute_AliasedType_DoesNotBypassSafetyChecks()
        {
            var result = Execute("using F = System.IO.File;\nF.Delete(\"/tmp/mcp-does-not-exist\");\nreturn 1;");

            Assert.IsFalse(result.Value<bool>("success"), result.ToString());
            StringAssert.Contains("Blocked pattern", result.Value<string>("error"));
        }

        [Test]
        public void Execute_UsingStatic_DoesNotBypassSafetyChecks()
        {
            var result = Execute("using static System.Diagnostics.Process;\nStart(\"ls\");\nreturn 1;");

            Assert.IsFalse(result.Value<bool>("success"), result.ToString());
            StringAssert.Contains("Blocked pattern", result.Value<string>("error"));
        }

        private static JObject Execute(string code)
        {
            return ToJObject(ExecuteCode.HandleCommand(new JObject
            {
                ["action"] = "execute",
                ["code"] = code
            }));
        }


        // ──────────────────── Compiled-assembly cache (issue #1351) ────────────────────

        [Test]
        public void Execute_SameSnippetTwice_DoesNotLoadASecondAssembly()
        {
            // Every compile Assembly.Load()s a fresh "MCPDynamic" image that Mono cannot
            // unload, so an uncached recompile leaks one assembly per call.
            const string snippet = "return 41 + 1;";

            // Warm the cache: this call is expected to add exactly one assembly.
            var first = Execute(snippet);
            Assert.IsTrue(first.Value<bool>("success"), first.ToString());

            int loadedBefore = AppDomain.CurrentDomain.GetAssemblies().Length;

            var second = Execute(snippet);
            Assert.IsTrue(second.Value<bool>("success"), second.ToString());
            Assert.AreEqual(42, second["data"]["result"].Value<int>());

            int loadedAfter = AppDomain.CurrentDomain.GetAssemblies().Length;
            Assert.AreEqual(loadedBefore, loadedAfter,
                "Re-executing an identical snippet loaded another assembly; the compile cache did not hit.");
        }

        [Test]
        public void Execute_DifferentSnippets_StillCompileIndependently()
        {
            var a = Execute("return 1;");
            var b = Execute("return 2;");

            Assert.IsTrue(a.Value<bool>("success"), a.ToString());
            Assert.IsTrue(b.Value<bool>("success"), b.ToString());
            Assert.AreEqual(1, a["data"]["result"].Value<int>());
            Assert.AreEqual(2, b["data"]["result"].Value<int>());
        }

    }
}
