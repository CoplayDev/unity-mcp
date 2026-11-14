using System;
using System.Linq;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Services;
using MCPForUnity.Editor.Tools;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor.Compilation;

namespace MCPForUnityTests.Editor.Services
{
    [TestFixture]
    public class StructuredErrorTests
    {
        [TearDown]
        public void TearDown()
        {
            // Leave a clean "last compilation" behind for whatever runs next.
            CompilationDiagnostics.RecordForTests(Array.Empty<CompilerMessage>(), "Assembly-CSharp");
        }

        [Test]
        public void ExceptionLocation_ParsesMonoFrame()
        {
            const string trace =
                "  at Foo.Bar (Newtonsoft.Json.Linq.JObject p) [0x00012] in /proj/Packages/x/Editor/Tools/Foo.cs:42 \n" +
                "  at Baz.Qux () [0x00000] in <00000000000000000000000000000000>:0 ";
            Assert.IsTrue(ExceptionLocation.TryParse(trace, out var file, out var line));
            Assert.AreEqual("/proj/Packages/x/Editor/Tools/Foo.cs", file);
            Assert.AreEqual(42, line);
        }

        [Test]
        public void ExceptionLocation_SkipsUnknownFramesBeforeTheFirstSourceFrame()
        {
            const string trace =
                "  at System.Reflection.RuntimeMethodInfo.Invoke () [0x00000] in <00000000000000000000000000000000>:0 \n" +
                "  at Zero.Line () [0x00000] in /proj/Gen.cs:0 \n" +
                "  at Foo.Bar () [0x00012] in /proj/Packages/x/Editor/Tools/Foo.cs:42 ";
            Assert.IsTrue(ExceptionLocation.TryParse(trace, out var file, out var line));
            Assert.AreEqual("/proj/Packages/x/Editor/Tools/Foo.cs", file);
            Assert.AreEqual(42, line);
            Assert.IsFalse(ExceptionLocation.TryParse("  at X () [0x0] in <00000000000000000000000000000000>:0 ", out _, out _));
        }

        [Test]
        public void ExceptionLocation_ParsesDotNetFrame()
        {
            Assert.IsTrue(ExceptionLocation.TryParse(@"   at Foo.Bar() in C:\proj\Foo.cs:line 7", out var file, out var line));
            Assert.AreEqual("C:/proj/Foo.cs", file);
            Assert.AreEqual(7, line);
        }

        [Test]
        public void ExceptionLocation_WithoutFileInfo_ReturnsFalse()
        {
            Assert.IsFalse(ExceptionLocation.TryParse("  at Foo.Bar () [0x00000] in <filename unknown>:0x", out _, out _));
            Assert.IsFalse(ExceptionLocation.TryParse(null, out _, out _));
        }

        [Test]
        public void Describe_ReportsTypeAndThrowSite()
        {
            Exception caught = null;
            try
            {
                throw new InvalidOperationException("boom");
            }
            catch (Exception ex)
            {
                caught = ex;
            }

            var envelope = JObject.FromObject(ExceptionLocation.Describe("my_tool", caught));
            Assert.AreEqual("error", (string)envelope["status"]);
            Assert.AreEqual("boom", (string)envelope["error"]);
            Assert.AreEqual("my_tool", (string)envelope["command"]);
            Assert.AreEqual("System.InvalidOperationException", (string)envelope["exception_type"]);
            StringAssert.EndsWith("StructuredErrorTests.cs", (string)envelope["file"]);
            Assert.Greater((int)envelope["line"], 0);
        }

        [Test]
        public void CompilationDiagnostics_KeepsStructuredMessages()
        {
            CompilationDiagnostics.RecordForTests(new[]
            {
                new CompilerMessage
                {
                    type = CompilerMessageType.Warning,
                    message = "Assets/Scripts/A.cs(3,5): warning CS0168: The variable 'x' is declared but never used",
                    file = "Assets/Scripts/A.cs", line = 3, column = 5,
                },
                new CompilerMessage
                {
                    type = CompilerMessageType.Error,
                    message = "Assets/Scripts/B.cs(10,13): error CS0103: The name 'foo' does not exist in the current context",
                    file = "Assets/Scripts/B.cs", line = 10, column = 13,
                },
            }, "Library/ScriptAssemblies/Assembly-CSharp.dll");

            var errors = CompilationDiagnostics.GetMessages(includeWarnings: false);
            Assert.AreEqual(1, errors.Count);
            Assert.AreEqual("CS0103", errors[0].code);
            Assert.AreEqual("Assets/Scripts/B.cs", errors[0].file);
            Assert.AreEqual(10, errors[0].line);
            Assert.AreEqual(13, errors[0].column);
            Assert.AreEqual("Assembly-CSharp", errors[0].assembly);

            var all = CompilationDiagnostics.GetMessages(includeWarnings: true);
            Assert.AreEqual(2, all.Count);
            Assert.AreEqual("error", all[0].severity, "errors are listed first");
        }

        [Test]
        public void CompilationDiagnostics_ErrorsSurviveTheMessageCapAcrossAssemblies()
        {
            CompilerMessage Warning(int i) => new CompilerMessage
            {
                type = CompilerMessageType.Warning,
                message = $"Assets/W.cs({i},1): warning CS0168: unused", file = "Assets/W.cs", line = i, column = 1,
            };
            var warnings = Enumerable.Range(1, CompilationDiagnostics.MaxMessages + 50).Select(Warning).ToArray();
            var error = new CompilerMessage
            {
                type = CompilerMessageType.Error,
                message = "Assets/E.cs(5,1): error CS0103: The name 'x' does not exist", file = "Assets/E.cs", line = 5, column = 1,
            };
            CompilationDiagnostics.RecordAssembliesForTests(("Assembly-A", warnings), ("Assembly-B", new[] { error }));

            var errors = CompilationDiagnostics.GetMessages(includeWarnings: false);
            Assert.AreEqual(1, errors.Count, "the error after 250 warnings must not be dropped");
            Assert.AreEqual("CS0103", errors[0].code);
            var data = JObject.FromObject(CompilationDiagnostics.ToResponseData(includeWarnings: true));
            Assert.AreEqual(1, (int)data["error_count"]);
            Assert.AreEqual(CompilationDiagnostics.MaxMessages, ((JArray)data["messages"]).Count);
            Assert.AreEqual(51, (int)data["dropped_messages"]);
        }

        [Test]
        public void ReadConsole_GetCompileErrors_WorksWhenConsoleReflectionFailed()
        {
            var field = typeof(ReadConsole).GetField("_startGettingEntriesMethod",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            Assert.IsNotNull(field, "test needs the reflection member");
            object saved = field.GetValue(null);
            try
            {
                field.SetValue(null, null);
                CompilationDiagnostics.RecordForTests(Array.Empty<CompilerMessage>(), "Assembly-CSharp");
                var json = JObject.FromObject(ReadConsole.HandleCommand(new JObject { ["action"] = "get_compile_errors" }));
                Assert.IsTrue((bool)json["success"], json.ToString());
                UnityEngine.TestTools.LogAssert.Expect(UnityEngine.LogType.Error,
                    new System.Text.RegularExpressions.Regex("reflection members are not initialized"));
                var get = JObject.FromObject(ReadConsole.HandleCommand(new JObject { ["action"] = "get" }));
                Assert.IsFalse((bool)get["success"], "get still needs the console reflection");
            }
            finally
            {
                field.SetValue(null, saved);
            }
        }

        [Test]
        public void ReadConsole_GetCompileErrors_ReturnsData()
        {
            CompilationDiagnostics.RecordForTests(new[]
            {
                new CompilerMessage
                {
                    type = CompilerMessageType.Error,
                    message = "Assets/C.cs(1,1): error CS1002: ; expected",
                    file = "Assets/C.cs", line = 1, column = 1,
                },
            }, "Assembly-CSharp");

            var result = ReadConsole.HandleCommand(new JObject { ["action"] = "get_compile_errors" });
            var json = JObject.FromObject(result);
            Assert.IsTrue((bool)json["success"], json.ToString());
            Assert.AreEqual(1, (int)json["data"]["error_count"]);
            Assert.AreEqual("CS1002", (string)json["data"]["messages"][0]["code"]);
            Assert.IsNotNull(json["data"]["last_compile_finished_unix_ms"]);
        }
    }
}
