using NUnit.Framework;
using Newtonsoft.Json.Linq;
using MCPForUnity.Editor.Tools;

namespace MCPForUnity.Tests.Editor
{
    [TestFixture]
    public class CommandClassifierTests
    {
        [Test]
        public void Classify_ManageScene_GetHierarchy_ReturnsInstant()
        {
            var p = new JObject { ["action"] = "get_hierarchy" };
            var tier = CommandClassifier.Classify("manage_scene", ExecutionTier.Smooth, p);
            Assert.That(tier, Is.EqualTo(ExecutionTier.Instant));
        }

        [Test]
        public void Classify_ManageScene_GetActive_ReturnsInstant()
        {
            var p = new JObject { ["action"] = "get_active" };
            var tier = CommandClassifier.Classify("manage_scene", ExecutionTier.Smooth, p);
            Assert.That(tier, Is.EqualTo(ExecutionTier.Instant));
        }

        [Test]
        public void Classify_ManageScene_Load_ReturnsHeavy()
        {
            var p = new JObject { ["action"] = "load" };
            var tier = CommandClassifier.Classify("manage_scene", ExecutionTier.Smooth, p);
            Assert.That(tier, Is.EqualTo(ExecutionTier.Heavy));
        }

        [Test]
        public void Classify_ManageScene_Save_ReturnsHeavy()
        {
            var p = new JObject { ["action"] = "save" };
            var tier = CommandClassifier.Classify("manage_scene", ExecutionTier.Smooth, p);
            Assert.That(tier, Is.EqualTo(ExecutionTier.Heavy));
        }

        [Test]
        public void Classify_RefreshUnity_CompileNone_ReturnsSmooth()
        {
            var p = new JObject { ["compile"] = "none" };
            var tier = CommandClassifier.Classify("refresh_unity", ExecutionTier.Heavy, p);
            Assert.That(tier, Is.EqualTo(ExecutionTier.Smooth));
        }

        [Test]
        public void Classify_RefreshUnity_CompileRequest_StaysHeavy()
        {
            var p = new JObject { ["compile"] = "request" };
            var tier = CommandClassifier.Classify("refresh_unity", ExecutionTier.Heavy, p);
            Assert.That(tier, Is.EqualTo(ExecutionTier.Heavy));
        }

        [Test]
        public void Classify_ManageEditor_TelemetryStatus_ReturnsInstant()
        {
            var p = new JObject { ["action"] = "telemetry_status" };
            var tier = CommandClassifier.Classify("manage_editor", ExecutionTier.Smooth, p);
            Assert.That(tier, Is.EqualTo(ExecutionTier.Instant));
        }

        [Test]
        public void Classify_ManageEditor_Play_ReturnsHeavy()
        {
            var p = new JObject { ["action"] = "play" };
            var tier = CommandClassifier.Classify("manage_editor", ExecutionTier.Smooth, p);
            Assert.That(tier, Is.EqualTo(ExecutionTier.Heavy));
        }

        [Test]
        public void Classify_UnknownTool_ReturnsAttributeTier()
        {
            var p = new JObject { ["action"] = "something" };
            var tier = CommandClassifier.Classify("unknown_tool", ExecutionTier.Smooth, p);
            Assert.That(tier, Is.EqualTo(ExecutionTier.Smooth));
        }

        [Test]
        public void Classify_NullParams_ReturnsAttributeTier()
        {
            var tier = CommandClassifier.Classify("manage_scene", ExecutionTier.Smooth, null);
            Assert.That(tier, Is.EqualTo(ExecutionTier.Smooth));
        }

        [Test]
        public void ClassifyBatch_AllInstant_ReturnsInstant()
        {
            var commands = new[]
            {
                ("find_gameobjects", ExecutionTier.Instant, new JObject()),
                ("read_console", ExecutionTier.Instant, new JObject())
            };
            var tier = CommandClassifier.ClassifyBatch(commands);
            Assert.That(tier, Is.EqualTo(ExecutionTier.Instant));
        }

        [Test]
        public void ClassifyBatch_MixedWithHeavy_ReturnsHeavy()
        {
            var commands = new[]
            {
                ("find_gameobjects", ExecutionTier.Instant, new JObject()),
                ("refresh_unity", ExecutionTier.Heavy, new JObject { ["compile"] = "request" })
            };
            var tier = CommandClassifier.ClassifyBatch(commands);
            Assert.That(tier, Is.EqualTo(ExecutionTier.Heavy));
        }

        [Test]
        public void ClassifyBatch_MixedSmoothAndInstant_ReturnsSmooth()
        {
            var commands = new[]
            {
                ("find_gameobjects", ExecutionTier.Instant, new JObject()),
                ("manage_gameobject", ExecutionTier.Smooth, new JObject())
            };
            var tier = CommandClassifier.ClassifyBatch(commands);
            Assert.That(tier, Is.EqualTo(ExecutionTier.Smooth));
        }

        [Test]
        public void CausesDomainReload_RefreshUnity_CompileRequest_ReturnsTrue()
        {
            var p = new JObject { ["compile"] = "request" };
            Assert.That(CommandClassifier.CausesDomainReload("refresh_unity", p), Is.True);
        }

        [Test]
        public void CausesDomainReload_RefreshUnity_CompileNone_ReturnsFalse()
        {
            var p = new JObject { ["compile"] = "none" };
            Assert.That(CommandClassifier.CausesDomainReload("refresh_unity", p), Is.False);
        }

        [Test]
        public void CausesDomainReload_RefreshUnity_NoCompileParam_ReturnsTrue()
        {
            var p = new JObject { ["scope"] = "all" };
            Assert.That(CommandClassifier.CausesDomainReload("refresh_unity", p), Is.True);
        }

        [Test]
        public void CausesDomainReload_ManageEditor_Play_ReturnsTrue()
        {
            var p = new JObject { ["action"] = "play" };
            Assert.That(CommandClassifier.CausesDomainReload("manage_editor", p), Is.True);
        }

        [Test]
        public void CausesDomainReload_ManageEditor_Stop_ReturnsFalse()
        {
            var p = new JObject { ["action"] = "stop" };
            Assert.That(CommandClassifier.CausesDomainReload("manage_editor", p), Is.False);
        }

        [Test]
        public void CausesDomainReload_RunTests_ReturnsFalse()
        {
            var p = new JObject { ["mode"] = "EditMode" };
            Assert.That(CommandClassifier.CausesDomainReload("run_tests", p), Is.False);
        }

        [Test]
        public void CausesDomainReload_ManageScript_Create_ReturnsFalse()
        {
            var p = new JObject { ["action"] = "create" };
            Assert.That(CommandClassifier.CausesDomainReload("manage_script", p), Is.False);
        }

        [Test]
        public void CausesDomainReload_ManageScene_Load_ReturnsFalse()
        {
            var p = new JObject { ["action"] = "load" };
            Assert.That(CommandClassifier.CausesDomainReload("manage_scene", p), Is.False);
        }

        [Test]
        public void CausesDomainReload_NullParams_ReturnsFalse()
        {
            Assert.That(CommandClassifier.CausesDomainReload("refresh_unity", null), Is.False);
        }
    }
}
