using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using MCPForUnity.Editor.Tools.Recording;
using MCPForUnity.Runtime.Helpers;
using MCPForUnity.Runtime.Recording;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.TestTools;
using UObject = UnityEngine.Object;

namespace MCPForUnityTests.Editor.Tools
{
    public class ManageRecordingTests
    {
        private GameObject target;
        private Camera camera;
        private readonly List<string> jobs = new List<string>();
        private string folder;
        private static JObject Call(JObject p) => JObject.FromObject(ManageRecording.HandleCommand(p));
        private JObject Spec(string action = "prepare") => new JObject
        {
            ["action"] = action, ["capture_source"] = "camera", ["camera"] = target.name,
            ["format"] = "png_sequence", ["width"] = 64, ["height"] = 64, ["fps"] = 6,
            ["duration_seconds"] = 0.5, ["output_folder"] = folder,
        };
        private string Track(JObject response)
        {
            Assert.That(response.Value<bool>("success"), Is.True, response.ToString());
            string id = response["data"].Value<string>("recording_id"); jobs.Add(id); return id;
        }
        private JObject Status(string id) => Call(new JObject { ["action"] = "status", ["recording_id"] = id });

        [SetUp]
        public void SetUp()
        {
            folder = "Library/MCPForUnity/RecordingTests/" + Guid.NewGuid().ToString("N");
            target = new GameObject("RecordingTest_" + Guid.NewGuid().ToString("N"));
            camera = target.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.blue; camera.cullingMask = 0;
        }
        /// <summary>Cancel fixture-owned jobs before deleting their source objects and generated artifacts.</summary>
        [TearDown]
        public void TearDown()
        {
            foreach (string id in jobs) Call(new JObject { ["action"] = "cancel", ["recording_id"] = id });
            jobs.Clear();
            UObject.DestroyImmediate(target);
            string absolute = Path.Combine(Application.dataPath, "..", folder);
            if (Directory.Exists(absolute)) Directory.Delete(absolute, true);
        }

        [TestCase("fps", "0")]
        [TestCase("fps", "2.5")]
        [TestCase("width", "63")]
        [TestCase("duration_seconds", "0")]
        [TestCase("duration_seconds", "301")]
        [TestCase("start_frame", "-1")]
        public void RejectsInvalidNumericSpecifications(string key, string value)
        {
            var spec = Spec(); spec[key] = JToken.Parse(value);
            Assert.That(Call(spec).Value<bool>("success"), Is.False);
        }
        [TestCase("../outside")]
        [TestCase("Assets/Recordings")]
        [TestCase("Packages/Recordings")]
        [TestCase("ProjectSettings/Recordings")]
        [TestCase("assets/Recordings")]
        [TestCase("ASSETS")]
        [TestCase("aSsEtS/Recordings")]
        [TestCase("packages/Recordings")]
        [TestCase("PACKAGES")]
        [TestCase("pAcKaGeS/Recordings")]
        [TestCase("projectsettings/Recordings")]
        [TestCase("PROJECTSETTINGS")]
        [TestCase("pRoJeCtSeTtInGs/Recordings")]
        public void RejectsUnsafeOutputLocations(string path)
        {
            var spec = Spec(); spec["output_folder"] = path;
            Assert.That(Call(spec).Value<bool>("success"), Is.False);
        }

        [TestCase("AssetsExport")]
        [TestCase("packages-backup")]
        [TestCase("projectsettings-copy")]
        public void ProtectedFolderCheckDoesNotRejectUnrelatedFolderNames(string path)
        {
            path += "_" + Guid.NewGuid().ToString("N");
            var spec = Spec(); spec["output_folder"] = path;
            var plan = RecordingPlan.Parse(spec);
            Assert.That(Path.GetDirectoryName(plan.OutputPath), Is.EqualTo(Path.GetFullPath(Path.Combine(Application.dataPath, "..", path))));
            Assert.That(Directory.Exists(Path.GetDirectoryName(plan.OutputPath)), Is.False, "Parsing must not create output folders.");
        }
        [Test]
        public void SourceAndOutputFpsAreIndependentAndEndFrameIsExclusive()
        {
            var spec = Spec(); spec.Remove("duration_seconds"); spec["source_fps"] = 30;
            spec["start_frame"] = 30; spec["end_frame"] = 120; spec["fps"] = 60;
            spec["playback"] = new JObject { ["type"] = "timeline", ["target"] = "Director" };
            var plan = RecordingPlan.Parse(spec);
            Assert.That(plan.StartTime, Is.EqualTo(1)); Assert.That(plan.Duration, Is.EqualTo(3));
            Assert.That(plan.FrameCount, Is.EqualTo(180));
            Assert.That(plan.TimeForFrame(179), Is.EqualTo(1 + 179.0 / 60).Within(0.000001));
        }
        [Test]
        public void DoesNotGuessSourceFpsOrSilentlyRoundDuration()
        {
            var spec = Spec(); spec["start_frame"] = 30;
            Assert.Throws<ArgumentException>(() => RecordingPlan.Parse(spec));
            spec.Remove("start_frame"); spec["duration_seconds"] = 0.25;
            Assert.Throws<ArgumentException>(() => RecordingPlan.Parse(spec));
        }
        [Test]
        public void PrepareDoesNotMutatePlaybackOrCreateOutput()
        {
            var source = target.AddComponent<TestRecordingSource>(); source.Time = 2; source.Camera = camera;
            var spec = Spec(); spec["playback"] = new JObject { ["type"] = "custom", ["target"] = target.name, ["state"] = "Open" };
            string id = Track(Call(spec));
            Assert.That(source.Time, Is.EqualTo(2)); Assert.That(source.Prepared, Is.False);
            Assert.That(Directory.Exists(Path.Combine(Application.dataPath, "..", folder)), Is.False);
            Assert.That((string)Status(id)["data"]["state"], Is.EqualTo("prepared"));
        }
        /// <summary>A duplicate scene name must never cause capture to pick an arbitrary camera.</summary>
        [Test]
        public void AmbiguousNamesReturnAnErrorInsteadOfChoosingAnObject()
        {
            var duplicate = new GameObject(target.name); duplicate.AddComponent<Camera>();
            try { Assert.That(Call(Spec()).Value<bool>("success"), Is.False); }
            finally { UObject.DestroyImmediate(duplicate); }
        }
        [Test]
        public void NonSeekableCustomSourcesAllowBoundedPreroll()
        {
            var source = target.AddComponent<TestRecordingSource>(); source.Seekable = false;
            var spec = Spec(); spec["start_time"] = 1;
            spec["playback"] = new JObject { ["type"] = "custom", ["target"] = target.name };
            Track(Call(spec));
        }
        [Test]
        public void DiscoveryExposesCustomStateAndFrameRate()
        {
            target.AddComponent<TestRecordingSource>();
            var result = Call(new JObject { ["action"] = "discover", ["query"] = target.name });
            Assert.That(result.Value<bool>("success"), Is.True);
            Assert.That(result.ToString(), Does.Contain("Open").And.Contain("source_fps"));
        }

        /// <summary>Exercise admission failure and slot reuse while retaining the oldest unfinished plan.</summary>
        [Test]
        public void FullCapacityPreservesPreparedJobsAndRejectsAdditionalPlans()
        {
            string first = Track(Call(Spec()));
            for (int i = 1; i < 32; i++) Track(Call(Spec()));
            var overflow = Call(Spec());
            if (overflow.Value<bool>("success")) Track(overflow);
            Assert.That(overflow.Value<bool>("success"), Is.False, "Capacity must not silently invalidate a prepared ID.");
            Assert.That((string)overflow["error"], Does.Contain("capacity"));
            Assert.That((string)Status(first)["data"]["state"], Is.EqualTo("prepared"));
            var started = Call(new JObject { ["action"] = "start", ["recording_id"] = first });
            Assert.That(started.Value<bool>("success"), Is.True, started.ToString());
            var cancelled = Call(new JObject { ["action"] = "cancel", ["recording_id"] = first });
            Assert.That((string)cancelled["data"]["state"], Is.EqualTo("cancelled"));
            Track(Call(Spec()));
        }

        /// <summary>History pressure must preserve both cancellation access and retry-key identity.</summary>
        [Test]
        public void ActiveJobCanBeCancelledAndRetriedAtFullCapacity()
        {
            var spec = Spec("start"); spec["duration_seconds"] = 60;
            spec["request_id"] = "capacity-" + Guid.NewGuid().ToString("N");
            string id = Track(Call(spec));
            for (int i = 1; i < 32; i++) Track(Call(Spec()));
            var overflow = Call(Spec());
            if (overflow.Value<bool>("success")) Track(overflow);
            Assert.That(overflow.Value<bool>("success"), Is.False);
            Assert.That((string)Status(id)["data"]["state"], Is.EqualTo("recording"));
            Assert.That((string)Call(spec)["data"]["recording_id"], Is.EqualTo(id));
            var cancelled = Call(new JObject { ["action"] = "cancel", ["recording_id"] = id });
            Assert.That((string)cancelled["data"]["state"], Is.EqualTo("cancelled"));
            Assert.That((string)Call(spec)["data"]["state"], Is.EqualTo("cancelled"), "Retry must not create another recorder.");
        }

        /// <summary>A full job table must still finalize an active capture into a usable early-stop artifact.</summary>
        [UnityTest]
        public IEnumerator ActiveJobCanBeStoppedAtFullCapacity()
        {
            var spec = Spec("start"); spec["duration_seconds"] = 60;
            string id = Track(Call(spec));
            double deadline = EditorApplication.timeSinceStartup + 5;
            while (Status(id)["data"].Value<int>("encoded_frames") == 0 && EditorApplication.timeSinceStartup < deadline)
                yield return null;
            Assert.That(Status(id)["data"].Value<int>("encoded_frames"), Is.GreaterThan(0));
            for (int i = 1; i < 32; i++) Track(Call(Spec()));
            var stopped = Call(new JObject { ["action"] = "stop", ["recording_id"] = id });
            Assert.That((string)stopped["data"]["state"], Is.EqualTo("completed"), stopped.ToString());
            Assert.That(stopped["data"].Value<bool>("stopped_early"), Is.True);
            Assert.That(stopped["data"].Value<bool>("ready"), Is.True);
        }

        /// <summary>Read captured pixels to detect a silent switch to a replacement camera after preparation.</summary>
        [UnityTest]
        public IEnumerator PreparedRecordingKeepsOriginalSourceAndCameraWhenNamesAreReused()
        {
            var source = target.AddComponent<TestRecordingSource>(); source.Time = 2;
            camera.backgroundColor = Color.green;
            string name = target.name;
            var spec = Spec();
            spec["playback"] = new JObject { ["type"] = "custom", ["target"] = name, ["state"] = "Open" };
            var prepared = Call(spec); string id = Track(prepared);
            Assert.That((string)prepared["data"]["plan"]["camera"], Is.EqualTo(camera.GetInstanceIDCompat().ToString()));
            Assert.That((string)prepared["data"]["plan"]["playback"]["target"], Is.EqualTo(source.GetInstanceIDCompat().ToString()));
            target.name = name + "_Renamed";
            var replacement = new GameObject(name);
            var otherCamera = replacement.AddComponent<Camera>();
            otherCamera.cullingMask = 0; otherCamera.clearFlags = CameraClearFlags.SolidColor; otherCamera.backgroundColor = Color.blue;
            var otherSource = replacement.AddComponent<TestRecordingSource>(); otherSource.Time = 3;
            try
            {
                var started = Call(new JObject { ["action"] = "start", ["recording_id"] = id });
                Assert.That(started.Value<bool>("success"), Is.True, started.ToString());
                Assert.That(source.Prepared, Is.True); Assert.That(otherSource.Prepared, Is.False);
                double deadline = EditorApplication.timeSinceStartup + 10;
                JObject result;
                do { yield return null; result = Status(id); }
                while ((string)result["data"]["state"] == "recording" && EditorApplication.timeSinceStartup < deadline);
                Assert.That((string)result["data"]["state"], Is.EqualTo("completed"), result.ToString());
                var frame = new Texture2D(2, 2);
                try
                {
                    frame.LoadImage(File.ReadAllBytes(Path.Combine((string)result["data"]["path"], "000000.png")));
                    Color pixel = frame.GetPixel(32, 32);
                    Assert.That(pixel.g, Is.GreaterThan(0.8f), "Capture switched to the same-named replacement camera.");
                    Assert.That(pixel.b, Is.LessThan(0.1f));
                }
                finally { UObject.DestroyImmediate(frame); }
                Assert.That(source.Time, Is.EqualTo(2)); Assert.That(otherSource.Time, Is.EqualTo(3));
            }
            finally { UObject.DestroyImmediate(replacement); }
        }

        /// <summary>Destroyed bindings must fail without artifacts, including when the output directory already exists.</summary>
        [TestCase("camera", false)]
        [TestCase("custom", false)]
        [TestCase("camera", true)]
        [TestCase("custom", true)]
        public void ReplacingAPreparedComponentFailsBeforeCreatingOutput(string component, bool outputFolderExists)
        {
            var source = target.AddComponent<TestRecordingSource>();
            var spec = Spec(); spec["playback"] = new JObject { ["type"] = "custom", ["target"] = target.name };
            string id = Track(Call(spec));
            string absolute = Path.Combine(Application.dataPath, "..", folder);
            if (outputFolderExists) Directory.CreateDirectory(absolute);
            if (component == "camera") { UObject.DestroyImmediate(camera); target.AddComponent<Camera>(); }
            else { UObject.DestroyImmediate(source); source = target.AddComponent<TestRecordingSource>(); }
            var result = Call(new JObject { ["action"] = "start", ["recording_id"] = id });
            Assert.That(result.Value<bool>("success"), Is.False, "A same-named component must not replace a prepared binding.");
            Assert.That((string)result["data"]["state"], Is.EqualTo("failed"));
            Assert.That(source.Prepared, Is.False);
            Assert.That(result["data"]["manifest_path"].Type, Is.EqualTo(JTokenType.Null));
            Assert.That(result["data"]["warning"].Type, Is.EqualTo(JTokenType.Null));
            if (outputFolderExists) Assert.That(Directory.GetFiles(absolute), Is.Empty);
            else Assert.That(Directory.Exists(absolute), Is.False);
        }

        /// <summary>Reusing an asset path must not replace the clip instance held by an existing plan.</summary>
        [Test]
        public void ReplacingTheClipAtAPreparedAssetPathDoesNotRetargetPlayback()
        {
            string path = "Assets/RecordingBoundClip_" + Guid.NewGuid().ToString("N") + ".anim";
            try
            {
                var clip = new AnimationClip { frameRate = 30 };
                clip.SetCurve("", typeof(Transform), "m_LocalPosition.x", AnimationCurve.Linear(0, 0, 4, 4));
                AssetDatabase.CreateAsset(clip, path);
                var spec = Spec(); spec["playback"] = new JObject { ["type"] = "animation_clip", ["target"] = target.name, ["clip_path"] = path };
                string id = Track(Call(spec));
                AssetDatabase.DeleteAsset(path);
                var replacement = new AnimationClip { frameRate = 60 };
                replacement.SetCurve("", typeof(Transform), "m_LocalPosition.x", AnimationCurve.Linear(0, 4, 4, 8));
                AssetDatabase.CreateAsset(replacement, path);
                var result = Call(new JObject { ["action"] = "start", ["recording_id"] = id });
                Assert.That(result.Value<bool>("success"), Is.False, "A new asset at the old path is a different motion source.");
                Assert.That((string)result["data"]["state"], Is.EqualTo("failed"));
                Assert.That(Directory.Exists(Path.Combine(Application.dataPath, "..", folder)), Is.False);
            }
            finally { AssetDatabase.DeleteAsset(path); }
        }

        /// <summary>A prepared source-time plan cannot silently adopt another Timeline asset.</summary>
        [Test]
        public void ReplacingAPreparedTimelineAssetRequiresANewPlan()
        {
            var director = target.AddComponent<PlayableDirector>();
            var original = ScriptableObject.CreateInstance<TestRecordingPlayable>();
            var replacement = ScriptableObject.CreateInstance<TestRecordingPlayable>();
            try
            {
                director.playableAsset = original;
                var spec = Spec(); spec["playback"] = new JObject { ["type"] = "timeline", ["target"] = target.name };
                string id = Track(Call(spec));
                director.playableAsset = replacement;
                var result = Call(new JObject { ["action"] = "start", ["recording_id"] = id });
                Assert.That(result.Value<bool>("success"), Is.False);
                Assert.That((string)result["data"]["state"], Is.EqualTo("failed"));
                Assert.That(Directory.Exists(Path.Combine(Application.dataPath, "..", folder)), Is.False);
            }
            finally { UObject.DestroyImmediate(original); UObject.DestroyImmediate(replacement); }
        }
        /// <summary>An imported multi-clip asset requires explicit selection and the selected clip supplies source FPS.</summary>
        [Test]
        public void ClipNameDisambiguatesMultiClipAssetsAndUsesTheirFrameRate()
        {
            string path = "Assets/RecordingMultiClip_" + Guid.NewGuid().ToString("N") + ".anim";
            var first = new AnimationClip { name = "First", frameRate = 30 };
            var second = new AnimationClip { name = "Second", frameRate = 60 };
            foreach (var clip in new[] { first, second }) clip.SetCurve("", typeof(Transform), "m_LocalPosition.x", AnimationCurve.Linear(0, 0, 4, 4));
            try
            {
                AssetDatabase.CreateAsset(first, path); AssetDatabase.AddObjectToAsset(second, first);
                var spec = Spec(); spec["playback"] = new JObject { ["type"] = "animation_clip", ["target"] = target.name, ["clip_path"] = path };
                Assert.That(Call(spec).Value<bool>("success"), Is.False, "An ambiguous clip path must never pick the first clip silently.");
                spec["playback"]["clip_name"] = "Second";
                var result = Call(spec); Track(result);
                Assert.That(result["data"]["plan"].Value<double>("source_fps"), Is.EqualTo(60));
            }
            finally { AssetDatabase.DeleteAsset(path); }
        }

        /// <summary>Compare decoded PNG colors across source times and verify retry identity and snapshot restoration.</summary>
        [UnityTest]
        public IEnumerator RecordsExactSourceFramesRestoresStateAndIsIdempotent()
        {
            var source = target.AddComponent<TestRecordingSource>(); source.Camera = camera; source.Time = 2;
            var spec = Spec("start"); spec["start_frame"] = 30; spec["request_id"] = "test-" + Guid.NewGuid().ToString("N");
            spec["playback"] = new JObject { ["type"] = "custom", ["target"] = target.name, ["state"] = "Open" };
            string id = Track(Call(spec));
            Assert.That((string)Call(spec)["data"]["recording_id"], Is.EqualTo(id));
            double deadline = EditorApplication.timeSinceStartup + 15;
            JObject status;
            do { yield return null; status = Status(id); }
            while ((string)status["data"]["state"] == "recording" && EditorApplication.timeSinceStartup < deadline);
            Assert.That((string)status["data"]["state"], Is.EqualTo("completed"), status.ToString());
            Assert.That(status["data"].Value<int>("encoded_frames"), Is.EqualTo(3));
            Assert.That(status["data"].Value<double>("duration_seconds"), Is.EqualTo(0.5));
            Assert.That(source.Time, Is.EqualTo(2)); Assert.That(source.Prepared, Is.False);
            string path = status["data"].Value<string>("path");
            Assert.That(Directory.GetFiles(path, "*.png").Length, Is.EqualTo(3));
            var first = new Texture2D(2, 2); var last = new Texture2D(2, 2);
            try
            {
                first.LoadImage(File.ReadAllBytes(Path.Combine(path, "000000.png")));
                last.LoadImage(File.ReadAllBytes(Path.Combine(path, "000002.png")));
                Assert.That(first.width, Is.EqualTo(64));
                Assert.That(first.GetPixel(32, 32).r, Is.GreaterThan(0.01), "Captured frame was black.");
                Assert.That(last.GetPixel(32, 32).r, Is.GreaterThan(first.GetPixel(32, 32).r), "Source time did not advance.");
            }
            finally { UObject.DestroyImmediate(first); UObject.DestroyImmediate(last); }
        }
        /// <summary>Pre-roll must reach the requested source offset without counting warm-up steps as captured frames.</summary>
        [UnityTest]
        public IEnumerator NonSeekableSourcePrerollsBeforeCapturing()
        {
            var source = target.AddComponent<TestRecordingSource>(); source.Camera = camera; source.Seekable = false; source.Time = 2;
            var spec = Spec("start"); spec["start_frame"] = 30;
            spec["playback"] = new JObject { ["type"] = "custom", ["target"] = target.name };
            string id = Track(Call(spec));
            double deadline = EditorApplication.timeSinceStartup + 15;
            JObject result;
            do { yield return null; result = Status(id); }
            while (((string)result["data"]["state"] == "warming_up" || (string)result["data"]["state"] == "recording") && EditorApplication.timeSinceStartup < deadline);
            Assert.That((string)result["data"]["state"], Is.EqualTo("completed"), result.ToString());
            Assert.That(result["data"].Value<double>("pre_roll_seconds"), Is.EqualTo(1).Within(0.000001));
            Assert.That(result["data"].Value<int>("encoded_frames"), Is.EqualTo(3));
            Assert.That(source.Time, Is.EqualTo(2));
        }

        [Test]
        public void FailedPreparationRestoresOwnershipAndReportsFailure()
        {
            var source = target.AddComponent<TestRecordingSource>(); source.Camera = camera; source.Time = 2; source.FailPrepare = true;
            var spec = Spec("start"); spec["playback"] = new JObject { ["type"] = "custom", ["target"] = target.name };
            var result = Call(spec);
            Assert.That(result.Value<bool>("success"), Is.False);
            jobs.Add((string)result["data"]["recording_id"]);
            Assert.That((string)result["data"]["state"], Is.EqualTo("failed"));
            Assert.That(source.Prepared, Is.False); Assert.That(source.Time, Is.EqualTo(2));
        }

        /// <summary>Cancellation releases playback ownership while keeping any partial output unusable.</summary>
        [UnityTest]
        public IEnumerator CancelRestoresPlaybackAndKeepsPartialOutputUnready()
        {
            var source = target.AddComponent<TestRecordingSource>(); source.Camera = camera; source.Time = 2;
            var spec = Spec("start"); spec["duration_seconds"] = 3;
            spec["playback"] = new JObject { ["type"] = "custom", ["target"] = target.name };
            string id = Track(Call(spec)); yield return null; yield return null;
            var result = Call(new JObject { ["action"] = "cancel", ["recording_id"] = id });
            Assert.That((string)result["data"]["state"], Is.EqualTo("cancelled"));
            Assert.That(result["data"].Value<bool>("ready"), Is.False);
            Assert.That(source.Time, Is.EqualTo(2));
        }
    }

    public sealed class TestRecordingPlayable : PlayableAsset
    {
        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner) => Playable.Null;
    }

    public sealed class TestRecordingSource : MonoBehaviour, IRecordingPlaybackSource
    {
        public double Time;
        public bool Prepared;
        public bool Seekable = true;
        public bool FailPrepare;
        public Camera Camera;
        public string RecordingId => gameObject.name;
        public string[] States => new[] { "Open", "Close" };
        public double FrameRate => 30;
        public double DurationSeconds => 4;
        public bool CanSeek => Seekable;
        public object CaptureState() => Time;
        /// <summary>Inject a preparation failure after acquiring ownership to exercise rollback.</summary>
        public void Prepare(string state) { Prepared = true; Time = 0; if (FailPrepare) throw new InvalidOperationException("Test preparation failure"); }
        /// <summary>Encode source time in camera color so captured pixels can verify sampling progression.</summary>
        public void Seek(double time) { Time = time; if (Camera != null) Camera.backgroundColor = new Color((float)(time / 4), 0.1f, 0.3f); }
        public void Advance(double delta) => Seek(Time + delta);
        /// <summary>Restore the fixture snapshot or retain its last sampled time when ownership is released.</summary>
        public void RestoreState(object snapshot) { Prepared = false; if (snapshot != null) Seek((double)snapshot); }
    }
}
