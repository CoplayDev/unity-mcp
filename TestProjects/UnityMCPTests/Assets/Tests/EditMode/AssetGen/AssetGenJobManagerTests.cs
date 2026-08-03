using System;
using System.IO;
using MCPForUnity.Editor.Services.AssetGen;
using MCPForUnity.Editor.Services.AssetGen.Http;
using MCPForUnity.Editor.Services.AssetGen.Providers;
using Newtonsoft.Json;
using NUnit.Framework;
using UnityEngine;

namespace MCPForUnityTests.Editor.AssetGen
{
    /// <summary>
    /// Drives the real TripoAdapter through FakeHttpTransport and pumps the job state machine
    /// deterministically (no editor loop, no network). Import is stubbed so no real model bytes
    /// are needed; the end-to-end test writes a tiny file under a throwaway Assets subfolder and
    /// cleans it up.
    /// </summary>
    public class AssetGenJobManagerTests
    {
        private const string EnvVar = "MCPFORUNITY_TRIPO_API_KEY";
        private const string Secret = "tsk_e2e_secret_value";
        private const string FalEnvVar = "MCPFORUNITY_FAL_API_KEY";
        private const string FalSecret = "fal_e2e_secret_value";
        private const string AudioResp = "https://queue.fal.run/fal-ai/stable-audio-25/text-to-audio/requests/r1";
        private const string TestFolder = "Assets/Generated/__assetgen_jobtest";

        private FakeHttpTransport _fake;

        [SetUp]
        public void SetUp()
        {
            AssetGenJobManager.ResetForTests();
            Environment.SetEnvironmentVariable(EnvVar, Secret);
            Environment.SetEnvironmentVariable(FalEnvVar, FalSecret);
            _fake = new FakeHttpTransport();
            AssetGenJobManager.TransportOverrideForTests = _fake;
            AssetGenJobManager.PollIntervalSeconds = 0;
            // Stub import: mark the asset path without touching AssetDatabase.
            AssetGenJobManager.ImportOverrideForTests = (job, path) => { job.AssetPath = path; return job; };
        }

        [TearDown]
        public void TearDown()
        {
            AssetGenJobManager.ResetForTests();
            Environment.SetEnvironmentVariable(EnvVar, null);
            Environment.SetEnvironmentVariable(FalEnvVar, null);
            try
            {
                string abs = Path.Combine(ProjectRoot(), TestFolder);
                if (Directory.Exists(abs)) Directory.Delete(abs, true);
                if (File.Exists(abs + ".meta")) File.Delete(abs + ".meta");
            }
            catch { /* ignore */ }
        }

        private static string ProjectRoot()
        {
            string dp = Application.dataPath.Replace('\\', '/');
            return dp.Substring(0, dp.Length - "Assets".Length);
        }

        private static HttpResult Json(string body) => new HttpResult { Status = 200, IsSuccess = true, Text = body };

        private static ModelGenRequest Req() => new ModelGenRequest
        {
            Provider = "tripo",
            Mode = "text",
            Prompt = "a low-poly oak tree",
            Format = "glb",
            TargetSize = 1f,
            Name = "jobtest",
            OutputFolder = TestFolder,
        };

        private static AudioGenRequest AudioReq() => new AudioGenRequest
        {
            Provider = "fal",
            Prompt = "gentle rain",
            Name = "audiotest",
            OutputFolder = TestFolder,
        };

        private static void Pump(string jobId)
        {
            int guard = 0;
            while (!AssetGenJobManager.TryAdvanceForTests(jobId) && guard++ < 50) { }
            Assert.Less(guard, 50, "state machine did not reach a terminal state");
        }

        [Test]
        public void EndToEnd_ReachesDone_WithAssetPath()
        {
            _fake.Handler = spec =>
            {
                if (spec.Method == "POST" && spec.Url.EndsWith("/openapi/task"))
                    return Json("{\"code\":0,\"data\":{\"task_id\":\"task_abc\"}}");
                if (spec.Url.Contains("/openapi/task/"))
                    return Json("{\"code\":0,\"data\":{\"status\":\"success\",\"progress\":100,\"output\":{\"pbr_model\":\"https://cdn.example.com/model.glb\"}}}");
                return new HttpResult { Status = 200, IsSuccess = true, Body = new byte[] { 1, 2, 3, 4 } }; // download
            };

            AssetGenJob job = AssetGenJobManager.StartModelGeneration(Req());
            Pump(job.JobId);

            Assert.AreEqual(AssetGenJobState.Done, job.State);
            Assert.IsNotNull(job.AssetPath);
            StringAssert.EndsWith("jobtest.glb", job.AssetPath);
            Assert.AreEqual(1f, job.Progress);
        }

        [Test]
        public void Audio_EndToEnd_ReachesDone_WithWavAssetPath_AndLeaksNoKey()
        {
            _fake.Handler = spec =>
            {
                if (spec.Method == "POST") return Json("{\"response_url\":\"" + AudioResp + "\"}");
                if (spec.Url.EndsWith("/status")) return Json("{\"status\":\"COMPLETED\"}");
                if (spec.Url.Contains("cdn.example.com"))
                    return new HttpResult { Status = 200, IsSuccess = true, Body = new byte[] { 1, 2, 3, 4 } };
                return Json("{\"audio_file\":{\"url\":\"https://cdn.example.com/a.wav\"}}"); // result payload
            };

            AssetGenJob job = AssetGenJobManager.StartAudioGeneration(AudioReq());
            Pump(job.JobId);

            Assert.AreEqual(AssetGenJobState.Done, job.State);
            Assert.IsNotNull(job.AssetPath);
            StringAssert.EndsWith("audiotest.wav", job.AssetPath);
            Assert.AreEqual(1f, job.Progress);

            string serialized = JsonConvert.SerializeObject(job);
            StringAssert.DoesNotContain(FalSecret, serialized);
        }

        [Test]
        public void Audio_Mp3Result_DownloadsWithMp3Ext()
        {
            _fake.Handler = spec =>
            {
                if (spec.Method == "POST") return Json("{\"response_url\":\"" + AudioResp + "\"}");
                if (spec.Url.EndsWith("/status")) return Json("{\"status\":\"COMPLETED\"}");
                if (spec.Url.Contains("cdn.example.com"))
                    return new HttpResult { Status = 200, IsSuccess = true, Body = new byte[] { 1, 2, 3, 4 } };
                return Json("{\"audio_file\":{\"url\":\"https://cdn.example.com/track.mp3\"}}");
            };

            AssetGenJob job = AssetGenJobManager.StartAudioGeneration(AudioReq());
            Pump(job.JobId);

            Assert.AreEqual(AssetGenJobState.Done, job.State);
            StringAssert.EndsWith("audiotest.mp3", job.AssetPath);
        }

        [Test]
        public void Audio_NoKey_FailsImmediately()
        {
            Environment.SetEnvironmentVariable(FalEnvVar, null); // remove the env key
            string dir = Path.Combine(Path.GetTempPath(), "mcp_jobmgr_audio_nokey_" + Guid.NewGuid().ToString("N"));
            MCPForUnity.Editor.Security.SecureKeyStore.OverrideForTests(new MCPForUnity.Editor.Security.EncryptedFileKeyStore(dir));
            try
            {
                AssetGenJob job = AssetGenJobManager.StartAudioGeneration(AudioReq());
                Assert.AreEqual(AssetGenJobState.Failed, job.State);
                StringAssert.Contains("No API key", job.Error);
            }
            finally
            {
                MCPForUnity.Editor.Security.SecureKeyStore.ResetForTests();
                try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
            }
        }

        [Test]
        public void FailedPoll_FailsJob_AndLeaksNoKey()
        {
            _fake.Handler = spec =>
            {
                if (spec.Method == "POST" && spec.Url.EndsWith("/openapi/task"))
                    return Json("{\"code\":0,\"data\":{\"task_id\":\"task_abc\"}}");
                return Json("{\"code\":0,\"data\":{\"status\":\"failed\",\"error\":\"out of credits\"}}");
            };

            AssetGenJob job = AssetGenJobManager.StartModelGeneration(Req());
            Pump(job.JobId);

            Assert.AreEqual(AssetGenJobState.Failed, job.State);
            Assert.IsNotEmpty(job.Error);

            string serialized = JsonConvert.SerializeObject(job);
            StringAssert.DoesNotContain(Secret, serialized);
        }

        [Test]
        public void IsAllowedResultExtension_AllowsSafeTypes_RejectsCodeTypes()
        {
            // H2/P8: a provider-controlled result extension must be gated per kind so a rogue provider
            // can't land a .cs/.asmdef/.meta/.asset under Assets/ and get it compiled/imported.
            Assert.IsTrue(AssetGenJobManager.IsAllowedResultExtension("audio", "wav"));
            Assert.IsTrue(AssetGenJobManager.IsAllowedResultExtension("audio", ".mp3"));
            Assert.IsFalse(AssetGenJobManager.IsAllowedResultExtension("audio", "cs"));
            Assert.IsFalse(AssetGenJobManager.IsAllowedResultExtension("audio", "asmdef"));
            Assert.IsFalse(AssetGenJobManager.IsAllowedResultExtension("audio", "meta"));

            Assert.IsTrue(AssetGenJobManager.IsAllowedResultExtension("image", "png"));
            Assert.IsFalse(AssetGenJobManager.IsAllowedResultExtension("image", "cs"));

            Assert.IsTrue(AssetGenJobManager.IsAllowedResultExtension("model", "glb"));
            Assert.IsTrue(AssetGenJobManager.IsAllowedResultExtension("model", "zip"));
            Assert.IsFalse(AssetGenJobManager.IsAllowedResultExtension("model", "cs"));
            // marketplace shares the model allowlist.
            Assert.IsTrue(AssetGenJobManager.IsAllowedResultExtension("marketplace", "zip"));
            Assert.IsFalse(AssetGenJobManager.IsAllowedResultExtension("marketplace", "dll"));

            // Fail closed: an unexpected/unknown kind allows nothing at the RCE boundary.
            Assert.IsFalse(AssetGenJobManager.IsAllowedResultExtension("bogus_kind", "glb"));
            Assert.IsFalse(AssetGenJobManager.IsAllowedResultExtension(null, "png"));
            Assert.IsFalse(AssetGenJobManager.IsAllowedResultExtension("", "wav"));
        }

        [Test]
        public void Audio_DisallowedResultExt_FailsJob_WritesNoAsset()
        {
            // H2/P8: a poll returning a .cs result extension must fail the job before any file is
            // written under Assets/ — never a compilable/importable payload.
            _fake.Handler = spec =>
            {
                if (spec.Method == "POST") return Json("{\"response_url\":\"" + AudioResp + "\"}");
                if (spec.Url.EndsWith("/status")) return Json("{\"status\":\"COMPLETED\"}");
                if (spec.Url.Contains("cdn.example.com"))
                    return new HttpResult { Status = 200, IsSuccess = true, Body = new byte[] { 1, 2, 3, 4 } };
                // Provider hands back a payload whose URL implies a .cs extension.
                return Json("{\"audio_file\":{\"url\":\"https://cdn.example.com/payload.cs\"}}");
            };

            AssetGenJob job = AssetGenJobManager.StartAudioGeneration(AudioReq());
            Pump(job.JobId);

            Assert.AreEqual(AssetGenJobState.Failed, job.State);
            StringAssert.Contains("disallowed", job.Error.ToLowerInvariant());
            Assert.IsTrue(string.IsNullOrEmpty(job.AssetPath), "no asset path should be recorded for a rejected type");

            // Nothing named payload.cs may have been written under the test output folder.
            string abs = Path.Combine(ProjectRoot(), TestFolder);
            if (Directory.Exists(abs))
                foreach (string f in Directory.GetFiles(abs))
                    StringAssert.DoesNotEndWith(".cs", f);
        }

        [Test]
        public void FileSchemeDownloadUrl_Rejected_FailsJob()
        {
            _fake.Handler = spec =>
            {
                if (spec.Method == "POST" && spec.Url.EndsWith("/openapi/task"))
                    return Json("{\"code\":0,\"data\":{\"task_id\":\"task_abc\"}}");
                // Poll succeeds but hands back a malicious local-file URL as the model.
                return Json("{\"code\":0,\"data\":{\"status\":\"success\",\"progress\":100,\"output\":{\"pbr_model\":\"file:///etc/passwd\"}}}");
            };

            AssetGenJob job = AssetGenJobManager.StartModelGeneration(Req());
            Pump(job.JobId);

            Assert.AreEqual(AssetGenJobState.Failed, job.State);
            StringAssert.Contains("http", job.Error.ToLowerInvariant());
        }

        [Test]
        public void Cancel_BeforeRun_MarksCanceled()
        {
            AssetGenJob job = AssetGenJobManager.StartModelGeneration(Req());
            Assert.IsTrue(AssetGenJobManager.Cancel(job.JobId));
            AssetGenJobManager.TryAdvanceForTests(job.JobId);
            Assert.AreEqual(AssetGenJobState.Canceled, job.State);
        }

        [Test]
        public void MissingKey_FailsImmediately()
        {
            Environment.SetEnvironmentVariable(EnvVar, null); // remove the env key
            // Ensure no stored key resolves for tripo by routing through an empty temp store.
            string dir = Path.Combine(Path.GetTempPath(), "mcp_jobmgr_nokey_" + Guid.NewGuid().ToString("N"));
            MCPForUnity.Editor.Security.SecureKeyStore.OverrideForTests(new MCPForUnity.Editor.Security.EncryptedFileKeyStore(dir));
            try
            {
                AssetGenJob job = AssetGenJobManager.StartModelGeneration(Req());
                Assert.AreEqual(AssetGenJobState.Failed, job.State);
                StringAssert.Contains("No API key", job.Error);
            }
            finally
            {
                MCPForUnity.Editor.Security.SecureKeyStore.ResetForTests();
                try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
            }
        }
    }
}
