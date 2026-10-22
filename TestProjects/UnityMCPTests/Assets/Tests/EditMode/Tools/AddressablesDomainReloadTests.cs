using System;
using System.Collections;
using System.Reflection;
using MCPForUnity.Editor.Tools;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.TestTools;

namespace MCPForUnityTests.Editor.Tools
{
    [Category("domain_reload")]
    [Explicit("Reloads the managed domain; run separately from ordinary EditMode tests.")]
    public class AddressablesDomainReloadTests
    {
        private const string JobKey = "MCPForUnity.Addressables.LastBuild";
        private const string Marker = "MCPForUnity.Tests.Addressables.Reload";
        private const string Backup = Marker + ".Backup";
        private const string Token = Marker + ".Token";
        private static readonly string DomainToken = Guid.NewGuid().ToString("N");

        private static JObject Status() => JObject.FromObject(ManageAddressables.HandleCommand(
            new JObject { ["action"] = "build_status", ["job_id"] = "domain-reload-fixture" }));

        [UnityTest]
        public IEnumerator RunningJob_IsReportedAsInterruptedAfterDomainReload()
        {
            PrepareRecord("running");
            yield return new WaitForDomainReload();
            AssertDomainReloaded();
            var result = Status();
            Assert.That(result["code"].Value<string>(), Is.EqualTo("ADDRESSABLES_BUILD_FAILED"));
            Assert.That(result["data"]["state"].Value<string>(), Is.EqualTo("failed"));
            Assert.That(result["data"]["error"].Value<string>(), Does.Contain("domain reloaded"));
            var ping = JObject.FromObject(ManageAddressables.HandleCommand(new JObject { ["action"] = "ping" }));
            Assert.That(ping["success"].Value<bool>(), Is.True);
        }

        [UnityTest]
        public IEnumerator CompletedJob_KeepsItsOutputAfterDomainReload()
        {
            PrepareRecord("succeeded");
            yield return new WaitForDomainReload();
            AssertDomainReloaded();
            var result = Status();
            Assert.That(result["success"].Value<bool>(), Is.True, result.ToString());
            Assert.That(result["data"]["output_path"].Value<string>(), Is.EqualTo("fixture-output"));
        }

        [UnityTest]
        public IEnumerator UndoBeforeFirstToolCall_AfterDomainReload_IsObserved()
        {
            PrepareRecord("succeeded");
            yield return new WaitForDomainReload();
            AssertDomainReloaded();
            var mesh = new UnityEngine.Mesh { name = "BeforeUndo", hideFlags = UnityEngine.HideFlags.HideAndDontSave };
            var refreshFlag = typeof(ManageAddressables).GetField("_refreshAfterUndo", BindingFlags.NonPublic | BindingFlags.Static);
            try
            {
                Undo.RegisterCompleteObjectUndo(mesh, "Addressables Undo subscription probe");
                mesh.name = "AfterUndo";
                Undo.PerformUndo();
                Assert.That(mesh.name, Is.EqualTo("BeforeUndo"), "The probe must perform a real Undo.");
                // Reading the field earlier would initialize the handler and hide the missing subscription.
                Assert.That((bool)refreshFlag.GetValue(null), Is.True,
                    "Undo before the first tool call after a domain reload must invalidate Addressables caches.");
            }
            finally
            {
                Undo.ClearUndo(mesh);
                UnityEngine.Object.DestroyImmediate(mesh);
                refreshFlag.SetValue(null, false);
            }
        }

        private static void PrepareRecord(string state)
        {
            if (!SessionState.GetBool(Marker, false))
            {
                Status();
                SessionState.SetString(Backup, SessionState.GetString(JobKey, ""));
                SessionState.SetString(Token, DomainToken);
                SessionState.SetBool(Marker, true);
                // Persist a record as it would exist if the domain stopped before recording completion.
                SessionState.SetString(JobKey, new JObject { ["job_id"] = "domain-reload-fixture",
                    ["state"] = state, ["action"] = "build_content", ["output_path"] = "fixture-output" }.ToString());
                EditorUtility.RequestScriptReload();
            }
        }

        private static void AssertDomainReloaded()
        {
            Assert.That(DomainToken, Is.Not.EqualTo(SessionState.GetString(Token, "")), "The managed domain did not actually reload.");
        }

        [TearDown]
        public void RestorePreviousRecord()
        {
            if (SessionState.GetBool(Marker, false))
            {
                string saved = SessionState.GetString(Backup, "");
                if (string.IsNullOrEmpty(saved)) SessionState.EraseString(JobKey);
                else SessionState.SetString(JobKey, saved);
                // Restoring SessionState alone leaves the fixture record in the current domain's cache.
                typeof(ManageAddressables).Assembly.GetType("MCPForUnity.Editor.Tools.AddressablesBuildJobs")
                    .GetField("_job", BindingFlags.NonPublic | BindingFlags.Static)
                    .SetValue(null, string.IsNullOrEmpty(saved) ? null : JObject.Parse(saved));
                SessionState.EraseString(Backup);
                SessionState.EraseString(Token);
                SessionState.SetBool(Marker, false);
            }
        }
    }
}
