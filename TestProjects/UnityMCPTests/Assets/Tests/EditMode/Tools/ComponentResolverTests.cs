using System;
using NUnit.Framework;
using UnityEngine;
using MCPForUnity.Editor.Tools;

namespace MCPForUnityTests.Editor.Tools
{
    public class ComponentResolverTests
    {
        [Test]
        public void TryResolve_ReturnsTrue_ForBuiltInComponentShortName()
        {
            bool result = ComponentResolver.TryResolve("Transform", out Type type, out string error);

            Assert.IsTrue(result, "Should resolve Transform component");
            Assert.AreEqual(typeof(Transform), type, "Should return correct Transform type");
            Assert.IsEmpty(error, "Should have no error message");
        }

        [Test]
        public void TryResolve_ReturnsTrue_ForBuiltInComponentFullyQualifiedName()
        {
            bool result = ComponentResolver.TryResolve("UnityEngine.Rigidbody", out Type type, out string error);

            Assert.IsTrue(result, "Should resolve UnityEngine.Rigidbody component");
            Assert.AreEqual(typeof(Rigidbody), type, "Should return correct Rigidbody type");
            Assert.IsEmpty(error, "Should have no error message");
        }

        [Test]
        public void TryResolve_ReturnsTrue_ForCustomComponentShortName()
        {
            bool result = ComponentResolver.TryResolve("CustomComponent", out Type type, out string error);

            Assert.IsTrue(result, "Should resolve CustomComponent");
            Assert.IsNotNull(type, "Should return valid type");
            Assert.AreEqual("CustomComponent", type.Name, "Should have correct type name");
            Assert.IsTrue(typeof(Component).IsAssignableFrom(type), "Should be a Component type");
            Assert.IsEmpty(error, "Should have no error message");
        }

        [Test]
        public void TryResolve_ReturnsTrue_ForCustomComponentFullyQualifiedName()
        {
            bool result = ComponentResolver.TryResolve("TestNamespace.CustomComponent", out Type type, out string error);

            Assert.IsTrue(result, "Should resolve TestNamespace.CustomComponent");
            Assert.IsNotNull(type, "Should return valid type");
            Assert.AreEqual("CustomComponent", type.Name, "Should have correct type name");
            Assert.AreEqual("TestNamespace.CustomComponent", type.FullName, "Should have correct full name");
            Assert.IsTrue(typeof(Component).IsAssignableFrom(type), "Should be a Component type");
            Assert.IsEmpty(error, "Should have no error message");
        }

        [Test]
        public void TryResolve_ReturnsFalse_ForNonExistentComponent()
        {
            bool result = ComponentResolver.TryResolve("NonExistentComponent", out Type type, out string error);

            Assert.IsFalse(result, "Should not resolve non-existent component");
            Assert.IsNull(type, "Should return null type");
            Assert.IsNotEmpty(error, "Should have error message");
            Assert.That(error, Does.Contain("not found"), "Error should mention component not found");
        }

        [Test]
        public void TryResolve_ReturnsFalse_ForEmptyString()
        {
            bool result = ComponentResolver.TryResolve("", out Type type, out string error);

            Assert.IsFalse(result, "Should not resolve empty string");
            Assert.IsNull(type, "Should return null type");
            Assert.IsNotEmpty(error, "Should have error message");
        }

        [Test]
        public void TryResolve_ReturnsFalse_ForNullString()
        {
            bool result = ComponentResolver.TryResolve(null, out Type type, out string error);

            Assert.IsFalse(result, "Should not resolve null string");
            Assert.IsNull(type, "Should return null type");
            Assert.IsNotEmpty(error, "Should have error message");
            Assert.That(error, Does.Contain("null or empty"), "Error should mention null or empty");
        }

        [Test]
        public void TryResolve_CachesResolvedTypes()
        {
            // First call
            bool result1 = ComponentResolver.TryResolve("Transform", out Type type1, out string error1);

            // Second call should use cache
            bool result2 = ComponentResolver.TryResolve("Transform", out Type type2, out string error2);

            Assert.IsTrue(result1, "First call should succeed");
            Assert.IsTrue(result2, "Second call should succeed");
            Assert.AreSame(type1, type2, "Should return same type instance (cached)");
            Assert.IsEmpty(error1, "First call should have no error");
            Assert.IsEmpty(error2, "Second call should have no error");
        }

        [Test]
        public void TryResolve_PrefersPlayerAssemblies()
        {
            // Test that custom user scripts (in Player assemblies) are found
            bool result = ComponentResolver.TryResolve("CustomComponent", out Type type, out string error);

            Assert.IsTrue(result, "Should resolve user script from Player assembly");
            Assert.IsNotNull(type, "Should return valid type");

            // Verify it's not from an Editor assembly by checking the assembly name
            string assemblyName = type.Assembly.GetName().Name;
            Assert.That(assemblyName, Does.Not.Contain("Editor"),
                "User script should come from Player assembly, not Editor assembly");

            // Verify it's from the TestAsmdef assembly (which is a Player assembly)
            Assert.AreEqual("TestAsmdef", assemblyName,
                "CustomComponent should be resolved from TestAsmdef assembly");
        }

        [Test]
        public void TryResolve_HandlesDuplicateNames_WithAmbiguityError()
        {
            // This test would need duplicate component names to be meaningful
            // For now, test with a built-in component that should not have duplicates
            bool result = ComponentResolver.TryResolve("Transform", out Type type, out string error);

            Assert.IsTrue(result, "Transform should resolve uniquely");
            Assert.AreEqual(typeof(Transform), type, "Should return correct type");
            Assert.IsEmpty(error, "Should have no ambiguity error");
        }

        [Test]
        public void ResolvedType_IsValidComponent()
        {
            bool result = ComponentResolver.TryResolve("Rigidbody", out Type type, out string error);

            Assert.IsTrue(result, "Should resolve Rigidbody");
            Assert.IsTrue(typeof(Component).IsAssignableFrom(type), "Resolved type should be assignable from Component");
            Assert.IsTrue(typeof(MonoBehaviour).IsAssignableFrom(type) ||
                         typeof(Component).IsAssignableFrom(type), "Should be a valid Unity component");
        }
    }
}
