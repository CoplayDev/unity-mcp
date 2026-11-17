#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using MCPForUnity.Editor.Tools.Recording;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.TestTools;
using UObject = UnityEngine.Object;

namespace MCPForUnityTests.PlayMode
{
    public class RecordingPlaybackTests
    {
        private static JObject Call(JObject p) => JObject.FromObject(ManageRecording.HandleCommand(p));

        [UnityTest]
        public IEnumerator ClipStartsAtSourceFrameAndRestoresController() => VerifySource("animation_clip");

        [UnityTest]
        public IEnumerator AnimatorStartsAtSourceFrameAndRestoresSpeed() => VerifySource("animator");

        [UnityTest]
        public IEnumerator PreparedClipKeepsOriginalTargetWhenNamesAreReused() => VerifySource("animation_clip", true);

        [UnityTest]
        public IEnumerator PreparedAnimatorKeepsOriginalTargetWhenNamesAreReused() => VerifySource("animator", true);

        [UnityTest]
        public IEnumerator ReplacingPreparedAnimatorControllerFailsBeforeCapture() => VerifySource("animator", true, true);

        /// <summary>Observe source-frame poses during capture and verify binding validation and Animator restoration.</summary>
        private IEnumerator VerifySource(string type, bool prepareFirst = false, bool replaceController = false)
        {
            string suffix = Guid.NewGuid().ToString("N");
            string folder = "Assets/RecordingPlayModeTests_" + suffix;
            string output = "Library/MCPForUnity/RecordingPlayModeTests/" + suffix;
            AssetDatabase.CreateFolder("Assets", "RecordingPlayModeTests_" + suffix);
            var clip = new AnimationClip { frameRate = 30 };
            clip.SetCurve("", typeof(Transform), "m_LocalPosition.x", AnimationCurve.Linear(0, 0, 4, 4));
            AssetDatabase.CreateAsset(clip, folder + "/Motion.anim");
            var controller = AnimatorController.CreateAnimatorControllerAtPath(folder + "/Motion.controller");
            var state = controller.layers[0].stateMachine.AddState("Motion"); state.motion = clip;
            controller.layers[0].stateMachine.defaultState = state;
            var go = new GameObject("RecordingRuntime_" + suffix);
            var animator = go.AddComponent<Animator>(); animator.runtimeAnimatorController = controller;
            var camera = new GameObject("RecordingRuntimeCamera_" + suffix).AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor; camera.cullingMask = 0;
            yield return null;
            animator.Update(0);
            string id = null;
            GameObject replacementTarget = null, replacementCamera = null;
            float originalSpeed = animator.speed;
            try
            {
                var playback = new JObject { ["type"] = type, ["target"] = go.name };
                if (type == "animator") playback["state"] = "Base Layer.Motion";
                else playback["clip_path"] = folder + "/Motion.anim";
                var response = Call(new JObject
                {
                    ["action"] = prepareFirst ? "prepare" : "start", ["capture_source"] = "camera", ["camera"] = camera.name,
                    ["playback"] = playback, ["start_frame"] = 30, ["duration_seconds"] = 1,
                    ["fps"] = 10, ["width"] = 32, ["height"] = 32, ["format"] = "png_sequence", ["output_folder"] = output,
                });
                Assert.That(response.Value<bool>("success"), Is.True, response.ToString());
                id = (string)response["data"]["recording_id"];
                if (prepareFirst)
                {
                    replacementTarget = new GameObject(go.name);
                    go.name += "_Renamed";
                    replacementTarget.AddComponent<Animator>().runtimeAnimatorController = controller;
                    replacementCamera = new GameObject(camera.name);
                    camera.name += "_Renamed";
                    replacementCamera.AddComponent<Camera>().cullingMask = 0;
                    RuntimeAnimatorController replacementController = null;
                    if (replaceController)
                    {
                        var next = AnimatorController.CreateAnimatorControllerAtPath(folder + "/Replacement.controller");
                        var nextState = next.layers[0].stateMachine.AddState("Motion"); nextState.motion = clip;
                        next.layers[0].stateMachine.defaultState = nextState;
                        animator.runtimeAnimatorController = replacementController = next;
                    }
                    response = Call(new JObject { ["action"] = "start", ["recording_id"] = id });
                    if (replaceController)
                    {
                        Assert.That(response.Value<bool>("success"), Is.False, "A prepared state must not be rebound to another controller.");
                        Assert.That((string)response["data"]["state"], Is.EqualTo("failed"));
                        Assert.That(animator.runtimeAnimatorController, Is.SameAs(replacementController));
                        Assert.That(Directory.Exists(Path.GetFullPath(Path.Combine(Application.dataPath, "..", output))), Is.False);
                        yield break;
                    }
                    Assert.That(response.Value<bool>("success"), Is.True, response.ToString());
                }
                int observed = 0;
                JObject result = response;
                double deadline = EditorApplication.timeSinceStartup + 20;
                while ((string)result["data"]["state"] == "recording" && EditorApplication.timeSinceStartup < deadline)
                {
                    yield return null;
                    result = Call(new JObject { ["action"] = "status", ["recording_id"] = id });
                    int frames = result["data"].Value<int>("encoded_frames");
                    if ((string)result["data"]["state"] == "recording" && frames > 0)
                    {
                        double expected = 1 + (frames - 1) / 10.0;
                        Assert.That(go.transform.localPosition.x, Is.EqualTo(expected).Within(0.03), "The controller or graph overwrote the requested source pose.");
                        observed++;
                    }
                }
                Assert.That(observed, Is.GreaterThan(0), "Did not observe any active recording frames.");
                Assert.That((string)result["data"]["state"], Is.EqualTo("completed"), result.ToString());
                Assert.That(result["data"].Value<int>("encoded_frames"), Is.EqualTo(10));
                Assert.That(animator.runtimeAnimatorController, Is.SameAs(controller));
                Assert.That(animator.speed, Is.EqualTo(originalSpeed));
            }
            finally
            {
                if (id != null) Call(new JObject { ["action"] = "cancel", ["recording_id"] = id });
                UObject.Destroy(go); UObject.Destroy(camera.gameObject);
                if (replacementTarget != null) UObject.Destroy(replacementTarget);
                if (replacementCamera != null) UObject.Destroy(replacementCamera);
                AssetDatabase.DeleteAsset(folder);
                string absolute = Path.GetFullPath(Path.Combine(Application.dataPath, "..", output));
                if (Directory.Exists(absolute)) Directory.Delete(absolute, true);
            }
        }
    }
}
#endif
