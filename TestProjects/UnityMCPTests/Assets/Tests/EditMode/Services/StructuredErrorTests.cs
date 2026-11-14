using System;
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
