using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine.TestTools;

namespace MCPForUnityTests.Editor.Services
{
    [TestFixture]
    public class EditorStateCacheCompilationReentryTests
    {
        // AssemblyBuilder.Build() walks the editor's live builder list to see whether
        // another build is still running, and reading a builder's status from that
        // walk is what raises compilationFinished for a build that has just ended.
        // A compilationFinished handler that reads EditorApplication.isCompiling
        // re-enters the same walk, which drops the finished builder from the list and
        // makes the outer enumeration throw "Collection was modified" (issue #1485).
        // Project Auditor hits this while compiling the player assemblies.
        [UnityTest]
        public IEnumerator StartingABuildWhileAnotherFinishes_DoesNotThrow()
        {
            string dir = Path.Combine("Temp", "MCPForUnityTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            string source = Path.Combine(dir, "Reentry.cs");
            File.WriteAllText(source, "public static class Reentry { public static int Value => 1; }");

            var first = new AssemblyBuilder(Path.Combine(dir, "First.dll"), source);
            var second = new AssemblyBuilder(Path.Combine(dir, "Second.dll"), source);
            double deadline = EditorApplication.timeSinceStartup + 120;

            try
            {
                Assert.IsTrue(first.Build(), "first build did not start");

                // Build() returns false while the first build is still running and
                // true once the walk has seen it finish; with the bug it throws there.
                while (!second.Build())
                {
                    Assert.Less(EditorApplication.timeSinceStartup, deadline, "second build never started");
                    yield return null;
                }

                while (second.status != AssemblyBuilderStatus.Finished)
                {
                    Assert.Less(EditorApplication.timeSinceStartup, deadline, "second build never finished");
                    yield return null;
                }
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }
    }
}
