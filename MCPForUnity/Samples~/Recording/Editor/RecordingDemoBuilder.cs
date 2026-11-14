using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.UI;

namespace MCPForUnity.Samples.Recording
{
    public static class RecordingDemoBuilder
    {
        /// <summary>Create or reopen the isolated additive recording demo without overwriting existing scenes.</summary>
        [MenuItem("Window/MCP for Unity/Create Recording Demo")]
        public static void Create()
        {
            const string root = "Assets/McpRecordingDemo/Generated";
            if (!AssetDatabase.IsValidFolder("Assets/McpRecordingDemo")) AssetDatabase.CreateFolder("Assets", "McpRecordingDemo");
            if (!AssetDatabase.IsValidFolder(root)) AssetDatabase.CreateFolder("Assets/McpRecordingDemo", "Generated");
            string scenePath = root + "/RecordingDemo.unity";
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath) != null)
            {
                EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive); return;
            }
            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
                if (string.IsNullOrEmpty(UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).path))
                    throw new InvalidOperationException("Save untitled scenes before creating the additive recording demo.");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            UnityEngine.SceneManagement.SceneManager.SetActiveScene(scene);
            var camera = new GameObject("RecordingDemoCamera").AddComponent<Camera>();
            camera.transform.position = new Vector3(0, 0, -10); camera.orthographic = true; camera.orthographicSize = 5;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(0.035f, 0.05f, 0.09f); camera.depth = 100;
            var canvas = new GameObject("RecordingDemoCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvas.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1280, 720);
            Label(canvas.transform, "MCP / MOTION RECORDING", new Vector2(0, 265), 38, Color.white);
            Label(canvas.transform, "SOURCE 30 FPS  /  OUTPUT 60 FPS  /  FRAME-ACCURATE CAPTURE", new Vector2(0, 215), 16, new Color(0.45f, 0.65f, 0.8f));
            var left = Card(canvas.transform, "TimelineCard", -360, new Color(0.15f, 0.7f, 1));
            var center = Card(canvas.transform, "AnimatorCard", 0, new Color(0.65f, 0.45f, 1));
            var right = Card(canvas.transform, "StateCard", 360, new Color(0.1f, 0.9f, 0.7f));
            Label(canvas.transform, "TIMELINE", new Vector2(-360, -170), 22, Color.white);
            Label(canvas.transform, "ANIMATOR", new Vector2(0, -170), 22, Color.white);
            Label(canvas.transform, "CUSTOM STATE / TWEEN", new Vector2(360, -170), 22, Color.white);
            var source = right.gameObject.AddComponent<RecordingDemoSource>(); source.Card = right; source.State = "Pulse";
            var clip = new AnimationClip { frameRate = 30, name = "Open" };
            foreach (string axis in new[] { "x", "y", "z" }) clip.SetCurve("", typeof(Transform), "m_LocalScale." + axis, AnimationCurve.EaseInOut(0, 0.05f, 4, 1));
            AssetDatabase.CreateAsset(clip, root + "/Open.anim");
            var controller = AnimatorController.CreateAnimatorControllerAtPath(root + "/Card.controller");
            var state = controller.layers[0].stateMachine.AddState("Open"); state.motion = clip;
            controller.layers[0].stateMachine.defaultState = state;
            center.gameObject.AddComponent<Animator>().runtimeAnimatorController = controller;
            CreateTimeline(left.gameObject, clip, root);
            EditorSceneManager.SaveScene(scene, scenePath); AssetDatabase.SaveAssets();
            Selection.activeGameObject = camera.gameObject;
        }

        private static RectTransform Card(Transform parent, string name, float x, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image)); go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform; rect.sizeDelta = new Vector2(260, 220); rect.anchoredPosition = new Vector2(x, 0);
            go.GetComponent<Image>().color = color; return rect;
        }
        /// <summary>Create a demo label with a built-in font fallback for older supported Editors.</summary>
        private static void Label(Transform parent, string text, Vector2 position, int size, Color color)
        {
            var go = new GameObject(text, typeof(RectTransform), typeof(Text)); go.transform.SetParent(parent, false);
            var label = go.GetComponent<Text>(); label.text = text; label.fontSize = size; label.color = color; label.alignment = TextAnchor.MiddleCenter;
            try { label.font = UnityEngine.Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); }
            catch (ArgumentException) { label.font = UnityEngine.Resources.GetBuiltinResource<Font>("Arial.ttf"); }
            var rect = (RectTransform)go.transform; rect.sizeDelta = new Vector2(1200, 80); rect.anchoredPosition = position;
        }
        /// <summary>Build the optional Timeline example through reflection without requiring its assembly at compile time.</summary>
        private static void CreateTimeline(GameObject target, AnimationClip clip, string folder)
        {
            Type timelineType = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("UnityEngine.Timeline.TimelineAsset")).FirstOrDefault(t => t != null);
            if (timelineType == null) return;
            var asset = ScriptableObject.CreateInstance(timelineType) as PlayableAsset;
            AssetDatabase.CreateAsset(asset, folder + "/Intro.playable");
            Type animationTrack = timelineType.Assembly.GetType("UnityEngine.Timeline.AnimationTrack");
            var createTrack = timelineType.GetMethods().First(m => m.Name == "CreateTrack" && !m.IsGenericMethod && m.GetParameters().Length == 3);
            object track = createTrack.Invoke(asset, new object[] { animationTrack, null, "Open" });
            var createClip = track.GetType().GetMethods().First(m => m.Name == "CreateClip" && m.IsGenericMethodDefinition && m.GetParameters().Length == 0);
            Type playableType = timelineType.Assembly.GetType("UnityEngine.Timeline.AnimationPlayableAsset");
            object timelineClip = createClip.MakeGenericMethod(playableType).Invoke(track, null);
            object animationAsset = timelineClip.GetType().GetProperty("asset").GetValue(timelineClip);
            playableType.GetProperty("clip").SetValue(animationAsset, clip);
            timelineClip.GetType().GetProperty("duration").SetValue(timelineClip, 4.0);
            object settings = timelineType.GetProperty("editorSettings").GetValue(asset);
            settings.GetType().GetProperty("frameRate").SetValue(settings, 30.0);
            var director = target.AddComponent<PlayableDirector>(); director.playOnAwake = false; director.playableAsset = asset;
            director.SetGenericBinding((UnityEngine.Object)track, target.AddComponent<Animator>());
        }
    }
}
