using System;
using MCPForUnity.Editor.Tools.Build;
using NUnit.Framework;
using UnityEditor;

namespace MCPForUnity.Tests.EditMode.Tools
{
    [TestFixture]
    public class BuildTargetMappingTests
    {
        [TestCase("windows64", BuildTarget.StandaloneWindows64)]
        [TestCase("macos", BuildTarget.StandaloneOSX)]
        [TestCase("linux", BuildTarget.StandaloneLinux64)]
        [TestCase("tvos", BuildTarget.tvOS)]
        public void TryResolveBuildTarget_KnownAliasesResolve(string name, BuildTarget expected)
        {
            Assert.IsTrue(BuildTargetMapping.TryResolveBuildTarget(name, out var target));
            Assert.AreEqual(expected, target);
        }

        [Test]
        public void TryResolveNamedBuildTarget_UnknownTargetListsOnlyAvailableTargets()
        {
            string error = BuildTargetMapping.TryResolveNamedBuildTarget("not-a-target", out _);

            Assert.IsNotNull(error);
            StringAssert.Contains("windows64", error);

            bool visionOSAvailable = Enum.TryParse("VisionOS", true, out BuildTarget _);
            if (visionOSAvailable)
            {
                StringAssert.Contains("visionos", error);
            }
            else
            {
                Assert.IsFalse(error.Contains("visionos"));
            }
        }

        [Test]
        public void TryResolveNamedBuildTarget_VisionOSUnavailableReturnsHelpfulError()
        {
            bool visionOSAvailable = Enum.TryParse("VisionOS", true, out BuildTarget _);
            string error = BuildTargetMapping.TryResolveNamedBuildTarget("visionos", out _);

            if (visionOSAvailable)
            {
                Assert.IsTrue(BuildTargetMapping.TryResolveBuildTarget("visionos", out _));
                if (error != null)
                {
                    StringAssert.Contains("VisionOS", error);
                }
            }
            else
            {
                Assert.IsFalse(BuildTargetMapping.TryResolveBuildTarget("visionos", out _));
                Assert.IsNotNull(error);
                StringAssert.Contains("VisionOS build target is not available", error);
            }
        }
    }
}
