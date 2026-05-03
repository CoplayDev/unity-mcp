using NUnit.Framework;
using MCPForUnity.Editor.Tools;

namespace MCPForUnity.Tests.Editor
{
    [TestFixture]
    public class ExecutionTierTests
    {
        [Test]
        public void ExecutionTier_DefaultValue_IsSmooth()
        {
            var attr = new McpForUnityToolAttribute("test_tool");
            Assert.That(attr.Tier, Is.EqualTo(ExecutionTier.Smooth));
        }

        [Test]
        public void ExecutionTier_CanSetToHeavy()
        {
            var attr = new McpForUnityToolAttribute("test_tool") { Tier = ExecutionTier.Heavy };
            Assert.That(attr.Tier, Is.EqualTo(ExecutionTier.Heavy));
        }

        [Test]
        public void ExecutionTier_CanSetToInstant()
        {
            var attr = new McpForUnityToolAttribute("test_tool") { Tier = ExecutionTier.Instant };
            Assert.That(attr.Tier, Is.EqualTo(ExecutionTier.Instant));
        }

        [Test]
        public void ExecutionTier_Ordering_InstantLessThanSmooth()
        {
            Assert.That(ExecutionTier.Instant, Is.LessThan(ExecutionTier.Smooth));
        }

        [Test]
        public void ExecutionTier_Ordering_SmoothLessThanHeavy()
        {
            Assert.That(ExecutionTier.Smooth, Is.LessThan(ExecutionTier.Heavy));
        }
    }
}
