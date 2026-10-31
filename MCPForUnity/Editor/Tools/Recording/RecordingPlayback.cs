using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MCPForUnity.Runtime.Helpers;
using MCPForUnity.Runtime.Recording;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UObject = UnityEngine.Object;

namespace MCPForUnity.Editor.Tools.Recording
{
    internal abstract class RecordingPlayback : IDisposable
    {
        public virtual double FrameRate => 0;
        public virtual string TargetId => null;
        public virtual double Duration => 0;
        public virtual bool CanSeek => true;
        public virtual object Info => new { type = "none", can_seek = CanSeek, source_fps = FrameRate, duration_seconds = Duration };
        /// <summary>Check source-duration and pre-roll bounds before playback ownership is acquired.</summary>
        public virtual void Validate(RecordingPlan plan)
        {
            RecordingPlan.Require(CanSeek || plan.StartTime <= 300, "Non-seekable sources allow at most 300 seconds of pre-roll.");
            if (Duration > 0)
                RecordingPlan.Require(plan.StartTime + plan.Duration <= Duration + 0.00001, "The recording range exceeds the source duration.");
        }
        public virtual void Begin(RecordingPlan plan) { }
        public virtual void Sample(double time, double delta) { }
        public virtual void Dispose() { }

        /// <summary>Resolve an explicit source to one live instance and reject ambiguous scene or asset selectors.</summary>
        public static RecordingPlayback Resolve(JObject spec)
        {
            string type = (string)spec?["type"] ?? "none";
            switch (type)
            {
                case "none": return new NoPlayback();
                case "timeline": return new TimelinePlayback(ResolveComponent<PlayableDirector>((string)spec["target"]));
                case "animation_clip":
                    string clipName = (string)spec["clip_name"];
                    var clips = AssetDatabase.LoadAllAssetsAtPath((string)spec["clip_path"] ?? "")
                        .OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview__", StringComparison.Ordinal) && (string.IsNullOrEmpty(clipName) || c.name == clipName)).ToArray();
                    RecordingPlan.Require(clips.Length == 1, "clip_path must identify one AnimationClip. For assets with multiple clips, supply clip_name from discover.");
                    var clip = clips[0];
                    return new ClipPlayback(ResolveComponent<Transform>((string)spec["target"]).gameObject, clip);
                case "animator": return new AnimatorPlayback(ResolveComponent<Animator>((string)spec["target"]), spec);
                case "custom":
                    var candidates = SceneComponents<MonoBehaviour>().Where(x => x is IRecordingPlaybackSource).ToArray();
                    string key = (string)spec["target"];
                    var matches = candidates.Where(x => Matches(x, key) || ((IRecordingPlaybackSource)x).RecordingId == key).ToArray();
                    RecordingPlan.Require(matches.Length == 1, "Custom target must identify exactly one IRecordingPlaybackSource. Use discover and its component instance_id.");
                    return new CustomPlayback((IRecordingPlaybackSource)matches[0], (string)spec["state"]);
                case "dotween": return new DotweenPlayback((string)spec["tween_id"]);
                default: throw new ArgumentException("playback.type must be none, timeline, animation_clip, animator, dotween or custom.");
            }
        }

        /// <summary>Enumerate loaded scene components while excluding persistent prefab and asset objects.</summary>
        public static IEnumerable<T> SceneComponents<T>() where T : Component =>
            UnityEngine.Resources.FindObjectsOfTypeAll<T>().Where(x => x != null && x.gameObject.scene.IsValid() && !EditorUtility.IsPersistent(x));

        /// <summary>Require one matching component so capture cannot silently choose among duplicate names.</summary>
        public static T ResolveComponent<T>(string key) where T : Component
        {
            RecordingPlan.Require(!string.IsNullOrWhiteSpace(key), "A playback/camera target is required. Use discover to get an instance_id or hierarchy path.");
            T[] matches = SceneComponents<T>().Where(x => Matches(x, key)).ToArray();
            RecordingPlan.Require(matches.Length == 1, $"Target '{key}' matched {matches.Length} {typeof(T).Name} components. Use a unique component instance_id from discover.");
            return matches[0];
        }

        /// <summary>Accept an explicit component or object ID, or a scene name/path requiring later uniqueness checks.</summary>
        internal static bool Matches(Component c, string key) => c.GetInstanceIDCompat().ToString() == key ||
            c.gameObject.GetInstanceIDCompat().ToString() == key || c.name == key || Path(c.transform) == key;

        /// <summary>Build a hierarchy path used for discovery and human-readable target selection.</summary>
        internal static string Path(Transform t) => t.parent == null ? t.name : Path(t.parent) + "/" + t.name;
        internal static object Identity(Component c) => new { instance_id = c.GetInstanceIDCompat(), name = c.name, path = Path(c.transform), scene = c.gameObject.scene.path };

        /// <summary>Describe available targets, states, source rates and cameras without acquiring playback clocks.</summary>
        public static List<object> Discover(string query)
        {
            var items = new List<object>();
            foreach (var d in SceneComponents<PlayableDirector>())
                if (Filter(d, query)) items.Add(new { type = "timeline", target = Identity(d), asset = AssetDatabase.GetAssetPath(d.playableAsset), info = new TimelinePlayback(d).Info });
            foreach (var a in SceneComponents<Animator>())
                if (Filter(a, query)) items.Add(new
                {
                    type = "animator", target = Identity(a), requires_play_mode = true,
                    states = AnimatorPlayback.States(a).Select(s =>
                    {
                        var clip = AnimatorPlayback.ResolveClip(a, s.Value.motion);
                        return new { name = s.Key, clip = AssetDatabase.GetAssetPath(clip != null ? (UObject)clip : s.Value.motion), source_fps = clip?.frameRate };
                    }),
                    clips = a.runtimeAnimatorController == null ? new object[0] : a.runtimeAnimatorController.animationClips.Distinct().Select(c => (object)new { type = "animation_clip", clip_path = AssetDatabase.GetAssetPath(c), c.name, source_fps = c.frameRate, duration_seconds = c.length }).ToArray(),
                    parameters = a.parameters.Select(p => new { p.name, type = p.type.ToString() }),
                });
            foreach (var b in SceneComponents<MonoBehaviour>())
                if (b is IRecordingPlaybackSource source && (Filter(b, query) || (!string.IsNullOrEmpty(query) && source.RecordingId.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)))
                    items.Add(new { type = "custom", target = Identity(b), recording_id = source.RecordingId, states = source.States, can_seek = source.CanSeek, source_fps = source.FrameRate, duration_seconds = source.DurationSeconds });
            foreach (var c in SceneComponents<Camera>())
                if (Filter(c, query)) items.Add(new { type = "camera", target = Identity(c), c.enabled });
            items.AddRange(DotweenPlayback.DiscoverTweens(query));
            return items;
        }

        private static bool Filter(Component c, string query) => string.IsNullOrWhiteSpace(query) || Path(c.transform).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        private sealed class NoPlayback : RecordingPlayback { }
    }

    internal sealed class TimelinePlayback : RecordingPlayback
    {
        private readonly PlayableDirector director;
        private readonly PlayableAsset asset;
        private double originalTime;
        private PlayState originalState;
        private DirectorUpdateMode originalMode;
        private bool restore;
        public TimelinePlayback(PlayableDirector value) { director = value; asset = value.playableAsset; }
        public override string TargetId => director.GetInstanceIDCompat().ToString();
        public override double Duration => director.playableAsset == null ? 0 : director.duration;
        public override double FrameRate
        {
            get
            {
                object settings = director.playableAsset?.GetType().GetProperty("editorSettings")?.GetValue(director.playableAsset);
                object rate = settings?.GetType().GetProperty("frameRate")?.GetValue(settings);
                return rate == null ? 0 : Convert.ToDouble(rate);
            }
        }
        public override object Info => new { type = "timeline", target = Identity(director), source_fps = FrameRate, duration_seconds = Duration, can_seek = true };
        /// <summary>Ensure the originally bound Director and Timeline asset still match the prepared plan.</summary>
        public override void Validate(RecordingPlan p)
        {
            RecordingPlan.Require(director != null, "The prepared Timeline target no longer exists. Prepare a new recording.");
            RecordingPlan.Require(director.playableAsset != null, "Timeline target has no playable asset.");
            RecordingPlan.Require(director.playableAsset == asset, "The prepared Timeline asset changed. Prepare a new recording.");
            RecordingPlan.Require(director.gameObject.activeInHierarchy, "Timeline target must be active.");
            base.Validate(p);
        }
        /// <summary>Snapshot Director timing and take exclusive ownership of its manual evaluation clock.</summary>
        public override void Begin(RecordingPlan plan)
        {
            originalTime = director.time; originalState = director.state; originalMode = director.timeUpdateMode; restore = plan.RestoreState;
            director.timeUpdateMode = DirectorUpdateMode.Manual;
            director.Pause();
        }
        public override void Sample(double time, double delta) { director.time = time; director.Evaluate(); }
        /// <summary>Release the Director clock and optionally restore its source time and playing state.</summary>
        public override void Dispose()
        {
            if (director == null) return;
            // Always restore clock ownership, even when the user keeps the final visual state.
            director.timeUpdateMode = originalMode;
            if (restore) { director.time = originalTime; director.Evaluate(); }
            if (originalState == PlayState.Playing) director.Play(); else director.Pause();
        }
    }

    internal sealed class ClipPlayback : RecordingPlayback
    {
        private readonly GameObject target;
        private readonly AnimationClip clip;
        private PlayableGraph graph;
        private AnimationClipPlayable playable;
        private Animator animator;
        private bool addedAnimator;
        private bool animationMode;
        private bool originalEnabled;
        private RuntimeAnimatorController originalController;
        private AnimatorStateInfo[] originalStates;
        private AnimatorCullingMode originalCulling;
        private float originalSpeed;
        private readonly List<Action> restoreProperties = new List<Action>();
        public ClipPlayback(GameObject go, AnimationClip value) { target = go; clip = value; }
        public override string TargetId => target.transform.GetInstanceIDCompat().ToString();
        public override double Duration => clip.length;
        public override double FrameRate => clip.frameRate;
        /// <summary>Require a live bound clip/target and a reversible sampling mode before altering the Animator.</summary>
        public override void Validate(RecordingPlan p)
        {
            RecordingPlan.Require(target != null && clip != null,
                "The prepared AnimationClip or target no longer exists. Prepare a new recording.");
            base.Validate(p);
            RecordingPlan.Require(target.activeInHierarchy, "Clip target must be active.");
            RecordingPlan.Require(!AnimationMode.InAnimationMode(), "Stop the current Animation preview before recording a clip.");
            RecordingPlan.Require(p.RestoreState, "AnimationClip sampling requires restore_state=true to leave Edit Mode properties intact.");
            RecordingPlan.Require(!Application.isPlaying || !clip.legacy, "Legacy clips are supported in Edit Mode only.");
        }
        /// <summary>Capture animated properties and isolate a manual clip graph from the normal Animator controller.</summary>
        public override void Begin(RecordingPlan plan)
        {
            if (!Application.isPlaying) { AnimationMode.StartAnimationMode(); animationMode = true; return; }
            foreach (var binding in AnimationUtility.GetCurveBindings(clip).Concat(AnimationUtility.GetObjectReferenceCurveBindings(clip)))
            {
                var animated = AnimationUtility.GetAnimatedObject(target, binding);
                if (animated == null) continue;
                var serialized = new SerializedObject(animated);
                var property = serialized.FindProperty(binding.propertyName);
                if (property == null) continue;
                string path = property.propertyPath;
                SerializedPropertyType kind = property.propertyType;
                float number = kind == SerializedPropertyType.Float ? property.floatValue : 0;
                int integer = kind == SerializedPropertyType.Integer ? property.intValue : 0;
                bool boolean = kind == SerializedPropertyType.Boolean && property.boolValue;
                UObject reference = kind == SerializedPropertyType.ObjectReference ? property.objectReferenceValue : null;
                if (kind != SerializedPropertyType.Float && kind != SerializedPropertyType.Integer && kind != SerializedPropertyType.Boolean && kind != SerializedPropertyType.ObjectReference) continue;
                restoreProperties.Add(() =>
                {
                    if (animated == null) return;
                    var current = new SerializedObject(animated);
                    var value = current.FindProperty(path);
                    if (value == null) return;
                    if (kind == SerializedPropertyType.Float) value.floatValue = number;
                    if (kind == SerializedPropertyType.Integer) value.intValue = integer;
                    if (kind == SerializedPropertyType.Boolean) value.boolValue = boolean;
                    if (kind == SerializedPropertyType.ObjectReference) value.objectReferenceValue = reference;
                    current.ApplyModifiedPropertiesWithoutUndo();
                });
            }
            Transform root = target.transform;
            Vector3 position = root.localPosition, scale = root.localScale;
            Quaternion rotation = root.localRotation;
            restoreProperties.Add(() => { if (root != null) { root.localPosition = position; root.localScale = scale; root.localRotation = rotation; } });
            animator = target.GetComponent<Animator>();
            if (animator == null) { animator = target.AddComponent<Animator>(); addedAnimator = true; }
            originalEnabled = animator.enabled; originalController = animator.runtimeAnimatorController;
            originalSpeed = animator.speed; originalCulling = animator.cullingMode;
            originalStates = originalController == null ? new AnimatorStateInfo[0] : Enumerable.Range(0, animator.layerCount).Select(animator.GetCurrentAnimatorStateInfo).ToArray();
            animator.enabled = true; animator.speed = 1; animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            // The controller's automatic graph would otherwise overwrite our manual clip before rendering.
            animator.runtimeAnimatorController = null;
            graph = PlayableGraph.Create("MCP recording clip");
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            playable = AnimationClipPlayable.Create(graph, clip);
            var output = AnimationPlayableOutput.Create(graph, "Recording", animator);
            output.SetSourcePlayable(playable);
            graph.Play();
            graph.Evaluate(0);
        }
        /// <summary>Evaluate the clip at absolute source time through reversible Edit Mode preview or a manual graph.</summary>
        public override void Sample(double time, double delta)
        {
            if (animationMode)
            {
                AnimationMode.BeginSampling();
                try { AnimationMode.SampleAnimationClip(target, clip, (float)time); }
                finally { AnimationMode.EndSampling(); }
            }
            else { playable.SetTime(time); graph.Evaluate(0); }
        }
        /// <summary>Destroy temporary playback graphs and restore captured properties and Animator settings.</summary>
        public override void Dispose()
        {
            if (graph.IsValid()) graph.Destroy();
            if (animationMode) { AnimationMode.StopAnimationMode(); animationMode = false; }
            if (animator != null)
            {
                animator.runtimeAnimatorController = originalController;
                if (originalController != null)
                {
                    for (int i = 0; i < originalStates.Length; i++) if (originalStates[i].fullPathHash != 0) animator.Play(originalStates[i].fullPathHash, i, originalStates[i].normalizedTime);
                    animator.Update(0);
                }
                animator.speed = originalSpeed; animator.cullingMode = originalCulling; animator.enabled = originalEnabled;
            }
            foreach (var restoreProperty in restoreProperties) restoreProperty();
            if (animator != null && addedAnimator) UObject.Destroy(animator);
        }
    }

    internal sealed class AnimatorPlayback : RecordingPlayback
    {
        private readonly Animator animator;
        private readonly JObject spec;
        private readonly KeyValuePair<string, AnimatorState> selected;
        private readonly RuntimeAnimatorController controller;
        private readonly Motion motion;
        private readonly AnimationClip sourceClip;
        private AnimatorStateInfo[] states;
        private Dictionary<string, object> parameters;
        private float speed;
        private AnimatorCullingMode culling;
        private bool enabled;
        private bool restore;
        private bool begun;
        /// <summary>Bind one state path and retain the controller and motion identities used for preparation.</summary>
        public AnimatorPlayback(Animator value, JObject options)
        {
            animator = value; spec = (JObject)options.DeepClone(); controller = value.runtimeAnimatorController;
            string name = (string)spec["state"];
            var matches = States(animator).Where(s => s.Key == name || s.Value.name == name).ToArray();
            RecordingPlan.Require(matches.Length == 1, "Animator state must identify exactly one full state path from discover.");
            selected = matches[0];
            motion = selected.Value.motion;
            sourceClip = ResolveClip(animator, motion);
        }
        public override string TargetId => animator.GetInstanceIDCompat().ToString();
        public override double FrameRate => ResolveClip(animator, selected.Value.motion)?.frameRate ?? 0;
        // State transitions/loops can run beyond a single clip; do not impose clip duration here.
        /// <summary>Reject replaced state resources and validate restorable parameters on the selected layer.</summary>
        public override void Validate(RecordingPlan p)
        {
            RecordingPlan.Require(animator != null, "The prepared Animator target no longer exists. Prepare a new recording.");
            RecordingPlan.Require(animator.runtimeAnimatorController == controller && selected.Value != null &&
                selected.Value.motion == motion && ResolveClip(animator, motion) == sourceClip &&
                States(animator).Any(s => s.Key == selected.Key && s.Value == selected.Value),
                "The prepared Animator controller, state or motion changed. Prepare a new recording.");
            base.Validate(p);
            RecordingPlan.Require(Application.isPlaying && animator.gameObject.activeInHierarchy, "Animator state playback requires Play Mode and an active target.");
            int layer = RecordingPlan.ReadInt(spec, "layer", 0);
            RecordingPlan.Require(layer >= 0 && layer < animator.layerCount && selected.Key.StartsWith(animator.GetLayerName(layer) + ".", StringComparison.Ordinal), "state must belong to the selected Animator layer.");
            if (spec["parameters"] is JObject values)
                foreach (var property in values.Properties())
                {
                    var found = animator.parameters.Where(x => x.name == property.Name).ToArray();
                    RecordingPlan.Require(found.Length == 1, "Unknown Animator parameter: " + property.Name);
                    RecordingPlan.Require(found[0].type != AnimatorControllerParameterType.Trigger, "Trigger parameters cannot be restored reliably. Select a state directly, or use a custom adapter for event-driven transitions.");
                    if (found[0].type == AnimatorControllerParameterType.Bool) property.Value.Value<bool>();
                    else if (found[0].type == AnimatorControllerParameterType.Int) RecordingPlan.ReadInt(values, property.Name, 0);
                    else RecordingPlan.Require(RecordingPlan.Finite(property.Value.Value<double>()), "Animator float parameters must be finite.");
                }
        }
        /// <summary>Snapshot parameters and layer states, select the source offset, and suppress automatic Animator advancement.</summary>
        public override void Begin(RecordingPlan plan)
        {
            restore = plan.RestoreState; speed = animator.speed; enabled = animator.enabled; culling = animator.cullingMode;
            parameters = new Dictionary<string, object>();
            foreach (var p in animator.parameters)
            {
                if (p.type == AnimatorControllerParameterType.Float) parameters[p.name] = animator.GetFloat(p.name);
                if (p.type == AnimatorControllerParameterType.Int) parameters[p.name] = animator.GetInteger(p.name);
                if (p.type == AnimatorControllerParameterType.Bool) parameters[p.name] = animator.GetBool(p.name);
            }
            states = Enumerable.Range(0, animator.layerCount).Select(animator.GetCurrentAnimatorStateInfo).ToArray();
            begun = true;
            animator.enabled = true; animator.cullingMode = AnimatorCullingMode.AlwaysAnimate; animator.speed = 1;
            if (spec["parameters"] is JObject values)
                foreach (var p in values.Properties()) SetParameter(p.Name, p.Value.ToObject<object>());
            int layer = RecordingPlan.ReadInt(spec, "layer", 0);
            animator.Play(selected.Key, layer, 0); animator.Update(0);
            float length = animator.GetCurrentAnimatorStateInfo(layer).length;
            animator.Play(selected.Key, layer, length > 0 ? (float)(plan.StartTime / length) : 0); animator.Update(0);
            animator.speed = 0;
        }
        /// <summary>Advance the selected Animator manually, then return its clock to the recorder-owned paused state.</summary>
        public override void Sample(double time, double delta)
        {
            animator.speed = 1;
            try { animator.Update((float)delta); }
            finally { animator.speed = 0; }
        }
        /// <summary>Apply a validated bool, int or float parameter using its declared Animator type.</summary>
        private void SetParameter(string key, object value)
        {
            var p = animator.parameters.First(x => x.name == key);
            if (p.type == AnimatorControllerParameterType.Float) animator.SetFloat(key, Convert.ToSingle(value));
            if (p.type == AnimatorControllerParameterType.Int) animator.SetInteger(key, Convert.ToInt32(value));
            if (p.type == AnimatorControllerParameterType.Bool) animator.SetBool(key, Convert.ToBoolean(value));
        }
        /// <summary>Restore captured Animator state when requested and always release playback settings.</summary>
        public override void Dispose()
        {
            if (!begun || animator == null) return;
            if (restore)
            {
                foreach (var p in parameters) SetParameter(p.Key, p.Value);
                for (int i = 0; i < states.Length; i++) if (states[i].fullPathHash != 0) animator.Play(states[i].fullPathHash, i, states[i].normalizedTime);
                animator.Update(0);
            }
            animator.speed = speed; animator.enabled = enabled; animator.cullingMode = culling;
        }
        /// <summary>Resolve a direct state clip through any AnimatorOverrideController before inferring its source rate.</summary>
        public static AnimationClip ResolveClip(Animator animator, Motion motion)
        {
            var clip = motion as AnimationClip;
            if (clip != null && animator.runtimeAnimatorController is AnimatorOverrideController overrides)
                return overrides[clip] ?? clip;
            return clip;
        }
        /// <summary>Enumerate full state paths across controller layers, including override controllers.</summary>
        public static IEnumerable<KeyValuePair<string, AnimatorState>> States(Animator a)
        {
            RuntimeAnimatorController runtime = a.runtimeAnimatorController;
            if (runtime is AnimatorOverrideController overrides) runtime = overrides.runtimeAnimatorController;
            var controller = runtime as AnimatorController;
            if (controller == null) yield break;
            foreach (var layer in controller.layers)
                foreach (var state in Walk(layer.stateMachine, layer.name + ".")) yield return state;
        }
        /// <summary>Walk nested state machines while retaining their full layer-qualified paths.</summary>
        private static IEnumerable<KeyValuePair<string, AnimatorState>> Walk(AnimatorStateMachine machine, string prefix)
        {
            foreach (var s in machine.states) yield return new KeyValuePair<string, AnimatorState>(prefix + s.state.name, s.state);
            foreach (var m in machine.stateMachines) foreach (var s in Walk(m.stateMachine, prefix + m.stateMachine.name + ".")) yield return s;
        }
    }

    internal sealed class CustomPlayback : RecordingPlayback
    {
        private readonly IRecordingPlaybackSource source;
        private readonly MonoBehaviour component;
        private readonly string state;
        private object snapshot;
        private bool begun;
        private bool restore;
        public CustomPlayback(IRecordingPlaybackSource value, string selectedState)
        {
            source = value; component = value as MonoBehaviour; state = selectedState;
        }
        public override string TargetId => component.GetInstanceIDCompat().ToString();
        public override double Duration => source.DurationSeconds;
        public override double FrameRate => source.FrameRate;
        public override bool CanSeek => source.CanSeek;
        /// <summary>Require the original adapter component and a declared state before acquiring its clock.</summary>
        public override void Validate(RecordingPlan p)
        {
            RecordingPlan.Require(component != null, "The prepared custom playback source no longer exists. Prepare a new recording.");
            base.Validate(p);
            RecordingPlan.Require(string.IsNullOrEmpty(state) || (source.States ?? new string[0]).Contains(state), "Unknown custom playback state. Use discover to list available states.");
        }
        /// <summary>Capture the project adapter snapshot before asking it to select and reset a state.</summary>
        public override void Begin(RecordingPlan plan)
        {
            snapshot = source.CaptureState(); restore = plan.RestoreState; begun = true; source.Prepare(state);
        }
        /// <summary>Seek directly when supported; otherwise advance the adapter by the supplied source-time delta.</summary>
        public override void Sample(double time, double delta) { if (source.CanSeek) source.Seek(time); else source.Advance(delta); }
        /// <summary>Release adapter ownership, passing null only when the caller keeps the final state.</summary>
        public override void Dispose() { if (begun) source.RestoreState(restore ? snapshot : null); }
    }

    internal sealed class DotweenPlayback : RecordingPlayback
    {
        private static Type DotweenType => AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("DG.Tweening.DOTween")).FirstOrDefault(t => t != null);
        private static Type Extensions => AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("DG.Tweening.TweenExtensions")).FirstOrDefault(t => t != null);
        public static bool Available => DotweenType != null && Extensions != null;
        private readonly object tween;
        private readonly string tweenId;
        private double originalTime;
        private bool wasPlaying;
        private bool begun;
        private bool restore;
        /// <summary>Bind exactly one live root tween or sequence carrying the requested explicit string ID.</summary>
        public DotweenPlayback(string id)
        {
            RecordingPlan.Require(Available, "DOTween is not installed. Install it separately or use a custom playback adapter.");
            RecordingPlan.Require(Application.isPlaying && !string.IsNullOrEmpty(id), "DOTween recording requires Play Mode and an explicit string tween_id assigned with SetId.");
            var values = QueryTweens(id);
            RecordingPlan.Require(values != null && values.Count == 1, "tween_id must match exactly one live root Tween or Sequence.");
            tween = values[0];
            tweenId = id;
        }
        /// <summary>Resolve the installed DOTween extension signature without adding a compile-time dependency.</summary>
        private object Invoke(string name, params object[] args)
        {
            var method = Extensions.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(m => m.Name == name && m.GetParameters().Length == args.Length + 1)
                .Select(m => m.IsGenericMethodDefinition && m.GetGenericArguments().Length == 1 ? m.MakeGenericMethod(tween.GetType()) : m)
                .FirstOrDefault(m => !m.ContainsGenericParameters && m.GetParameters()[0].ParameterType.IsInstanceOfType(tween));
            if (method == null) throw new NotSupportedException("This DOTween version lacks TweenExtensions." + name);
            return method.Invoke(null, new[] { tween }.Concat(args).ToArray());
        }
        public override double Duration
        {
            get { double value = Convert.ToDouble(Invoke("Duration", true)); return value > 0 && RecordingPlan.Finite(value) ? value : 0; }
        }
        /// <summary>Reject a killed or replaced tween even when a new instance reuses its string ID.</summary>
        public override void Validate(RecordingPlan p)
        {
            var matches = QueryTweens(tweenId);
            RecordingPlan.Require(matches != null && matches.Count == 1 && ReferenceEquals(matches[0], tween),
                "The prepared DOTween target no longer exists or its ID changed. Prepare a new recording.");
            base.Validate(p);
        }
        /// <summary>Snapshot tween time and playing state before pausing its automatic clock.</summary>
        public override void Begin(RecordingPlan p)
        {
            originalTime = Convert.ToDouble(Invoke("Elapsed", true)); wasPlaying = Convert.ToBoolean(Invoke("IsPlaying")); restore = p.RestoreState; begun = true;
            Invoke("Pause");
        }
        public override void Sample(double time, double delta) { Invoke("Goto", (float)time, false); }
        /// <summary>Optionally restore tween time and always release the original playing/paused state.</summary>
        public override void Dispose()
        {
            if (!begun) return;
            if (restore) Invoke("Goto", (float)originalTime, false);
            if (wasPlaying) Invoke("Play"); else Invoke("Pause");
        }
        /// <summary>Query live root tweens across DOTween versions with two- or three-argument lookup overloads.</summary>
        private static IList QueryTweens(string id)
        {
            // DOTween added an optional fillableList argument in newer releases.
            var method = DotweenType.GetMethods(BindingFlags.Public | BindingFlags.Static).FirstOrDefault(m =>
            {
                var p = m.GetParameters();
                return m.Name == "TweensById" && (p.Length == 2 || p.Length == 3) && p[0].ParameterType == typeof(object) && p[1].ParameterType == typeof(bool);
            });
            if (method == null) throw new NotSupportedException("This DOTween version lacks TweensById.");
            return method.Invoke(null, method.GetParameters().Length == 2 ? new object[] { id, false } : new object[] { id, false, null }) as IList;
        }
        /// <summary>Resolve only explicitly queried tween IDs and report ambiguity without traversing unrelated tweens.</summary>
        public static IEnumerable<object> DiscoverTweens(string query)
        {
            if (!Available || !Application.isPlaying) yield break;
            // Query by explicit ID avoids walking unrelated project-owned tweens or invoking arbitrary code.
            if (string.IsNullOrWhiteSpace(query)) yield break;
            var matches = QueryTweens(query);
            if (matches == null) yield break;
            foreach (object item in matches) yield return new { type = "dotween", tween_id = query, unique = matches.Count == 1, requires_play_mode = true, can_seek = true, source_fps = (double?)null };
        }
    }
}
