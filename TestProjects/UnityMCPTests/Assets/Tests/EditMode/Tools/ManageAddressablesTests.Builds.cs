using System;
using System.Collections;
using System.IO;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace MCPForUnityTests.Editor.Tools
{
    public partial class ManageAddressablesTests
    {
        [UnityTest]
        public IEnumerator QueuedBuild_RejectsDuplicateAndWritesThenReleasesLock()
        {
            CreateSettings();
            var started = Query("build_clean");
            string id = started["data"]?["job_id"]?.Value<string>();
            Assert.That(id, Is.Not.Empty, started.ToString());
            Assert.That(Query("build_clean")["code"].Value<string>(), Is.EqualTo("ADDRESSABLES_BUILD_BUSY"));
            Assert.That(Query("set_settings", new JObject { ["settings"] = new JObject { ["unique_bundle_ids"] = true } })["code"].Value<string>(), Is.EqualTo("ADDRESSABLES_BUILD_BUSY"));
            Succeeded(Query("ping"));
            JObject completed = null;
            yield return WaitForBuild(id, value => completed = value);
            Succeeded(completed);
            Succeeded(Query("label_add", new JObject { ["name"] = "after-build" }));
            var latest = Query("build_clean");
            string latestId = latest["data"]["job_id"].Value<string>();
            Assert.That(latestId, Is.Not.EqualTo(id));
            Assert.That(Query("build_status", new JObject { ["job_id"] = id })["code"].Value<string>(), Is.EqualTo("ADDRESSABLES_BUILD_NOT_FOUND"));
            yield return WaitForBuild(latestId, value => completed = value);
            Succeeded(completed);
        }

        [UnityTest]
        public IEnumerator QueuedBuild_ConfigChangedByEditorFailsAndCanBeRetried()
        {
            CreateSettings();
            string original = Query("get_settings")["data"]["active_profile_id"].Value<string>();
            var started = Query("build_clean");
            // Simulate Inspector changes while the operation is queued; MCP writes are locked.
            using (var serialized = new SerializedObject((UnityEngine.Object)_settings))
            {
                serialized.FindProperty("m_ActiveProfileId").stringValue = "changed-outside-mcp";
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            JObject completed = null;
            yield return WaitForBuild(started["data"]["job_id"].Value<string>(), value => completed = value);
            Assert.That(completed["code"].Value<string>(), Is.EqualTo("ADDRESSABLES_BUILD_FAILED"));
            Assert.That(completed["data"]["error"].Value<string>(), Does.Contain("configuration changed"));
            _settings.GetType().GetProperty("activeProfileId").SetValue(_settings, original);
            var retry = Query("build_clean");
            yield return WaitForBuild(retry["data"]["job_id"].Value<string>(), value => completed = value);
            Succeeded(completed);
        }

        [UnityTest]
        public IEnumerator InvalidContentState_FailsJobAndDoesNotKeepEditorLocked()
        {
            CreateSettings();
            string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".bin");
            File.WriteAllText(path, "not an Addressables content state");
            try
            {
                if (_settings.GetType().Assembly.GetType("UnityEditor.AddressableAssets.Build.LegacyFormatConverter") != null)
                {
                    // Packages with the legacy converter log errors from both restriction checking and the update builder.
                    for (int attempt = 0; attempt < 2; attempt++)
                    {
                        LogAssert.Expect(LogType.Error, new Regex("^Format converter failed with exit code"));
                        LogAssert.Expect(LogType.Error, "Failed to convert legacy content state file. The file may be corrupted or the migration tool is unavailable.");
                    }
                }
                var started = Query("build_update", new JObject { ["content_state_path"] = path });
                Assert.That(started["data"]?["job_id"], Is.Not.Null, started.ToString());
                JObject completed = null;
                yield return WaitForBuild(started["data"]["job_id"].Value<string>(), value => completed = value);
                Assert.That(completed["code"].Value<string>(), Is.EqualTo("ADDRESSABLES_BUILD_FAILED"));
                Assert.That(completed["data"]["error"].Value<string>(), Is.Not.Empty);
                Assert.That(completed["data"]["completed_at"].Value<string>(), Is.Not.Empty);
                Succeeded(Query("label_add", new JObject { ["name"] = "after-failure" }));
                var retry = Query("build_clean");
                yield return WaitForBuild(retry["data"]["job_id"].Value<string>(), value => completed = value);
                Succeeded(completed);
            }
            finally
            {
                // Addressables 1.x may leave a stream for finalization when its deserializer throws.
                GC.Collect();
                GC.WaitForPendingFinalizers();
                File.Delete(path);
            }
        }

        [Test]
        public void BuildWithoutActiveProfile_DoesNotReplaceLastJob()
        {
            CreateSettings();
            string before = Query("build_status").ToString();
            using (var serialized = new SerializedObject((UnityEngine.Object)_settings))
            {
                serialized.FindProperty("m_ActiveProfileId").stringValue = "";
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            Assert.That(Query("build_content")["code"].Value<string>(), Is.EqualTo("INVALID_ARGUMENT"));
            Assert.That(Query("build_status").ToString(), Is.EqualTo(before));
        }
    }
}
