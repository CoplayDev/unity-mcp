using System.Linq;
using NUnit.Framework;
using UnityEditor.Compilation;

namespace MCPForUnityTests.Editor
{
    /// <summary>
    /// MCP for Unity is Editor tooling. MCPForUnity.Runtime only holds helpers used by
    /// MCPForUnity.Editor, so neither assembly should be compiled into player builds.
    /// </summary>
    public class PackageAssemblyPlatformTests
    {
        [TestCase("MCPForUnity.Runtime")]
        [TestCase("MCPForUnity.Editor")]
        public void PackageAssembly_IsNotCompiledIntoPlayer(string assemblyName)
        {
            var playerAssemblies = CompilationPipeline.GetAssemblies(AssembliesType.Player);

            Assert.That(playerAssemblies.Select(a => a.name), Has.No.Member(assemblyName));
        }

        [Test]
        public void RuntimeAssembly_IsStillCompiledForEditor()
        {
            var editorAssemblies = CompilationPipeline.GetAssemblies(AssembliesType.Editor);

            Assert.That(editorAssemblies.Select(a => a.name), Has.Member("MCPForUnity.Runtime"));
        }
    }
}
