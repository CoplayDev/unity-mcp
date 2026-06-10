using System.Linq;
using System.Reflection;
using NUnit.Framework;

namespace MCPForUnityTests.Editor.Windows
{
    public class InternalTerminalContextMenuTests
    {
        [Test]
        public void InternalTerminal_DoesNotRegisterSceneViewContextMenuHook()
        {
            var assembly = System.AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "WTL.InternalTerminal.Editor");

            Assert.IsNotNull(assembly, "Expected the internal terminal editor assembly to be loaded.");

            var sceneViewHookTypes = assembly.GetTypes()
                .Where(t => t.GetMethods(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static)
                    .Any(m => m.GetParameters().Any(p => p.ParameterType.FullName == "UnityEditor.SceneView")))
                .Select(t => t.FullName)
                .ToArray();

            CollectionAssert.IsEmpty(
                sceneViewHookTypes,
                "Internal Terminal must not inject Add to Agent into the Scene view right-click menu.");
        }
    }
}
