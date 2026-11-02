using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Runtime.Helpers;
using UnityEditor;
using UnityEngine;

namespace MCPForUnity.Editor.Tools.Animation
{
    internal static class ClipCreate
    {
        public static object Create(JObject @params)
        {
            string clipPath = @params["clipPath"]?.ToString();
            if (string.IsNullOrEmpty(clipPath))
                return new { success = false, message = "'clipPath' is required (e.g. 'Assets/Animations/Walk.anim')" };

            clipPath = AssetPathUtility.SanitizeAssetPath(clipPath);
            if (clipPath == null)
                return new { success = false, message = "Invalid asset path" };

            if (!clipPath.EndsWith(".anim", StringComparison.OrdinalIgnoreCase))
                clipPath += ".anim";

            // Ensure directory exists
            string dir = Path.GetDirectoryName(clipPath)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(dir) && !AssetDatabase.IsValidFolder(dir))
            {
                CreateFoldersRecursive(dir);
            }

            // Check if already exists
            var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
            if (existing != null)
                return new { success = false, message = $"AnimationClip already exists at '{clipPath}'. Delete it first or use a different path." };

            var clip = new AnimationClip();
            string name = @params["name"]?.ToString();
            clip.name = !string.IsNullOrEmpty(name)
                ? name
                : Path.GetFileNameWithoutExtension(clipPath);

            float length = @params["length"]?.ToObject<float>() ?? 1f;
            clip.frameRate = @params["frameRate"]?.ToObject<float>() ?? 60f;

            bool loop = @params["loop"]?.ToObject<bool>() ?? false;
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = loop;
            settings.stopTime = length;
            AnimationUtility.SetAnimationClipSettings(clip, settings);

            AssetDatabase.CreateAsset(clip, clipPath);

            // Set m_WrapMode via SerializedObject — clip.wrapMode is a runtime property
            // that doesn't serialize to m_WrapMode, so we set it directly for the legacy system
            if (loop)
            {
                var so = new SerializedObject(clip);
                var wrapProp = so.FindProperty("m_WrapMode");
                if (wrapProp != null)
                {
                    wrapProp.intValue = (int)WrapMode.Loop;
                    so.ApplyModifiedProperties();
                }
            }

            AssetDatabase.SaveAssets();

            return new
            {
                success = true,
                message = $"Created AnimationClip at '{clipPath}'",
                data = new
                {
                    path = clipPath,
                    name = clip.name,
                    length,
                    frameRate = clip.frameRate,
                    isLooping = loop
                }
            };
        }

        public static object GetInfo(JObject @params)
        {
            string clipPath = @params["clipPath"]?.ToString();
            if (string.IsNullOrEmpty(clipPath))
                return new { success = false, message = "'clipPath' is required" };

            clipPath = AssetPathUtility.SanitizeAssetPath(clipPath);
            if (clipPath == null)
                return new { success = false, message = "Invalid asset path" };

            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
            if (clip == null)
                return new { success = false, message = $"AnimationClip not found at '{clipPath}'" };

            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            var bindings = AnimationUtility.GetCurveBindings(clip);

            var curves = new List<object>();
            foreach (var binding in bindings)
            {
                var curve = AnimationUtility.GetEditorCurve(clip, binding);
                curves.Add(new
                {
                    path = binding.path,
                    propertyName = binding.propertyName,
                    type = binding.type.Name,
                    keyCount = curve?.length ?? 0
                });
            }

            var events = AnimationUtility.GetAnimationEvents(clip);
            var eventList = events.Select(e => new
            {
                time = e.time,
                functionName = e.functionName,
                stringParameter = e.stringParameter,
                floatParameter = e.floatParameter,
                intParameter = e.intParameter
            }).ToArray();

            return new
            {
                success = true,
                data = new
                {
                    path = clipPath,
                    name = clip.name,
                    length = clip.length,
                    frameRate = clip.frameRate,
                    isLooping = settings.loopTime,
                    wrapMode = clip.wrapMode.ToString(),
                    curveCount = bindings.Length,
                    curves,
                    eventCount = events.Length,
                    events = eventList
                }
            };
        }

        // Samples a clip's ROOT MOTION trajectory over time and returns the path plus
        // summary metrics (net displacement, path length). Resolves the clip BY REFERENCE
        // via clipInstanceId (string handle) so it works on FBX sub-asset clips that an
        // asset-path load cannot reach. Root motion is read from the importer-baked
        // 'RootT.x/y/z' (position) and 'RootQ.x/y/z/w' (rotation) editor curves; if those
        // bindings are absent the clip has no baked root motion and we say so.
        // Params: { clipInstanceId (string), samples? (int, default 30) }.
        public static object GetRootMotion(JObject @params)
        {
            string clipId = @params["clipInstanceId"]?.ToString();
            if (string.IsNullOrEmpty(clipId))
                return new { success = false, message = "'clipInstanceId' is required (string handle from a state's motionInstanceId)" };

            var clip = UnityObjectIdCompat.InstanceIDFromString(clipId) as AnimationClip;
            if (clip == null)
                return new { success = false, message = $"AnimationClip not resolved from clipInstanceId '{clipId}'" };

            int samples = @params["samples"]?.ToObject<int>() ?? 30;
            if (samples < 2) samples = 2;
            if (samples > 240) samples = 240;

            // Build name->curve map for the root-motion bindings only.
            var rootCurves = new Dictionary<string, AnimationCurve>();
            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
            {
                if (binding.propertyName.StartsWith("RootT.") || binding.propertyName.StartsWith("RootQ."))
                    rootCurves[binding.propertyName] = AnimationUtility.GetEditorCurve(clip, binding);
            }

            bool hasRootT = rootCurves.ContainsKey("RootT.x") || rootCurves.ContainsKey("RootT.y") || rootCurves.ContainsKey("RootT.z");
            if (!hasRootT)
            {
                return new
                {
                    success = true,
                    data = new
                    {
                        name = clip.name,
                        clipInstanceId = clipId,
                        length = clip.length,
                        frameRate = clip.frameRate,
                        isLooping = AnimationUtility.GetAnimationClipSettings(clip).loopTime,
                        hasRootMotion = false,
                        message = "No baked RootT curves — clip carries no root translation (in-place / no root motion)."
                    }
                };
            }

            float Eval(string key, float t) => rootCurves.TryGetValue(key, out var c) && c != null && c.length > 0 ? c.Evaluate(t) : 0f;

            var path = new List<object>();
            float len = clip.length;
            Vector3 prev = Vector3.zero;
            Vector3 first = Vector3.zero, last = Vector3.zero;
            float pathLength = 0f;
            for (int i = 0; i < samples; i++)
            {
                float t = len * i / (samples - 1);
                var p = new Vector3(Eval("RootT.x", t), Eval("RootT.y", t), Eval("RootT.z", t));
                if (i == 0) { first = p; }
                else { pathLength += Vector3.Distance(prev, p); }
                prev = p;
                last = p;
                path.Add(new { t, x = p.x, y = p.y, z = p.z });
            }

            Vector3 net = last - first;
            return new
            {
                success = true,
                data = new
                {
                    name = clip.name,
                    clipInstanceId = clipId,
                    length = len,
                    frameRate = clip.frameRate,
                    isLooping = AnimationUtility.GetAnimationClipSettings(clip).loopTime,
                    hasRootMotion = true,
                    samples,
                    netDisplacement = new { x = net.x, y = net.y, z = net.z, magnitude = net.magnitude },
                    pathLength,
                    path
                }
            };
        }

        public static object AddCurve(JObject @params)
        {
            return SetOrAddCurve(@params, append: true);
        }

        public static object SetCurve(JObject @params)
        {
            return SetOrAddCurve(@params, append: false);
        }

        private static object SetOrAddCurve(JObject @params, bool append)
        {
            string clipPath = @params["clipPath"]?.ToString();
            if (string.IsNullOrEmpty(clipPath))
                return new { success = false, message = "'clipPath' is required" };

            clipPath = AssetPathUtility.SanitizeAssetPath(clipPath);
            if (clipPath == null)
                return new { success = false, message = "Invalid asset path" };

            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
            if (clip == null)
                return new { success = false, message = $"AnimationClip not found at '{clipPath}'" };

            string propertyPath = @params["propertyPath"]?.ToString();
            if (string.IsNullOrEmpty(propertyPath))
                return new { success = false, message = "'propertyPath' is required (e.g. 'localPosition.x')" };

            string typeName = @params["type"]?.ToString() ?? "Transform";
            Type componentType = ResolveType(typeName);
            if (componentType == null)
                return new { success = false, message = $"Could not resolve type '{typeName}'" };

            string relativePath = @params["relativePath"]?.ToString() ?? "";

            JToken keysToken = @params["keys"];
            if (keysToken == null)
                return new { success = false, message = "'keys' is required" };

            var keyframes = ParseKeyframes(keysToken);
            if (keyframes == null || keyframes.Length == 0)
                return new { success = false, message = "Failed to parse keyframes. Use [{\"time\":0,\"value\":0},...] or [[0,0],[1,1],...]" };

            AnimationCurve curve;
            var binding = EditorCurveBinding.FloatCurve(relativePath, componentType, propertyPath);

            if (append)
            {
                curve = AnimationUtility.GetEditorCurve(clip, binding) ?? new AnimationCurve();
                foreach (var kf in keyframes)
                {
                    curve.AddKey(kf);
                }
            }
            else
            {
                curve = new AnimationCurve(keyframes);
            }

            // Use AnimationUtility.SetEditorCurve instead of clip.SetCurve to avoid
            // marking the clip as legacy — legacy clips cannot be used in Mecanim BlendTrees.
            Undo.RecordObject(clip, append ? "Add Animation Curve" : "Set Animation Curve");
            AnimationUtility.SetEditorCurve(clip, binding, curve);
            EditorUtility.SetDirty(clip);
            AssetDatabase.SaveAssets();

            string verb = append ? "Added" : "Set";
            return new
            {
                success = true,
                message = $"{verb} curve on '{propertyPath}' ({typeName}) with {keyframes.Length} keyframes",
                data = new
                {
                    clipPath,
                    propertyPath,
                    type = typeName,
                    keyframeCount = curve.length
                }
            };
        }

        public static object SetVectorCurve(JObject @params)
        {
            string clipPath = @params["clipPath"]?.ToString();
            if (string.IsNullOrEmpty(clipPath))
                return new { success = false, message = "'clipPath' is required" };

            clipPath = AssetPathUtility.SanitizeAssetPath(clipPath);
            if (clipPath == null)
                return new { success = false, message = "Invalid asset path" };

            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
            if (clip == null)
                return new { success = false, message = $"AnimationClip not found at '{clipPath}'" };

            // Accept both 'property' and 'propertyPath' for consistency with add_curve/set_curve
            string property = @params["property"]?.ToString() ?? @params["propertyPath"]?.ToString();
            if (string.IsNullOrEmpty(property))
                return new { success = false, message = "'property' (or 'propertyPath') is required (e.g. 'localPosition', 'localEulerAngles', 'localScale')" };

            string typeName = @params["type"]?.ToString() ?? "Transform";
            Type componentType = ResolveType(typeName);
            if (componentType == null)
                return new { success = false, message = $"Could not resolve type '{typeName}'" };

            string relativePath = @params["relativePath"]?.ToString() ?? "";

            JToken keysToken = @params["keys"];
            if (keysToken == null || keysToken is not JArray keysArray || keysArray.Count == 0)
                return new { success = false, message = "'keys' is required. Use [{\"time\":0,\"value\":[0,1,0]},...]" };

            // Map property group to axis suffixes
            string[] suffixes;
            switch (property.ToLowerInvariant())
            {
                case "localposition":
                    property = "localPosition";
                    suffixes = new[] { ".x", ".y", ".z" };
                    break;
                case "localeulerangles":
                    property = "localEulerAngles";
                    suffixes = new[] { ".x", ".y", ".z" };
                    break;
                case "localscale":
                    property = "localScale";
                    suffixes = new[] { ".x", ".y", ".z" };
                    break;
                default:
                    suffixes = new[] { ".x", ".y", ".z" };
                    break;
            }

            var xKeys = new List<Keyframe>();
            var yKeys = new List<Keyframe>();
            var zKeys = new List<Keyframe>();

            foreach (var item in keysArray)
            {
                if (item is not JObject keyObj)
                    return new { success = false, message = "Each key must be an object with 'time' and 'value' (Vector3 array)" };

                float time = keyObj["time"]?.ToObject<float>() ?? 0f;
                JToken valueToken = keyObj["value"];
                if (valueToken is not JArray valArray || valArray.Count < 3)
                    return new { success = false, message = $"Key at time {time}: 'value' must be a 3-element array [x, y, z]" };

                float vx = valArray[0].ToObject<float>();
                float vy = valArray[1].ToObject<float>();
                float vz = valArray[2].ToObject<float>();

                xKeys.Add(new Keyframe(time, vx));
                yKeys.Add(new Keyframe(time, vy));
                zKeys.Add(new Keyframe(time, vz));
            }

            // Use AnimationUtility.SetEditorCurve instead of clip.SetCurve to avoid
            // marking the clip as legacy — legacy clips cannot be used in Mecanim BlendTrees.
            Undo.RecordObject(clip, "Set Vector Curve");
            var bindingX = EditorCurveBinding.FloatCurve(relativePath, componentType, property + suffixes[0]);
            var bindingY = EditorCurveBinding.FloatCurve(relativePath, componentType, property + suffixes[1]);
            var bindingZ = EditorCurveBinding.FloatCurve(relativePath, componentType, property + suffixes[2]);
            AnimationUtility.SetEditorCurve(clip, bindingX, new AnimationCurve(xKeys.ToArray()));
            AnimationUtility.SetEditorCurve(clip, bindingY, new AnimationCurve(yKeys.ToArray()));
            AnimationUtility.SetEditorCurve(clip, bindingZ, new AnimationCurve(zKeys.ToArray()));
            EditorUtility.SetDirty(clip);
            AssetDatabase.SaveAssets();

            return new
            {
                success = true,
                message = $"Set 3 curves on '{property}' ({typeName}) with {keysArray.Count} vector keyframes",
                data = new
                {
                    clipPath,
                    property,
                    type = typeName,
                    curves = new[] { property + suffixes[0], property + suffixes[1], property + suffixes[2] },
                    keyframeCount = keysArray.Count
                }
            };
        }

        public static object Assign(JObject @params)
        {
            var go = ObjectResolver.ResolveGameObject(@params["target"], @params["searchMethod"]?.ToString());
            if (go == null)
                return new { success = false, message = "Target GameObject not found" };

            string clipPath = @params["clipPath"]?.ToString();
            if (string.IsNullOrEmpty(clipPath))
                return new { success = false, message = "'clipPath' is required" };

            clipPath = AssetPathUtility.SanitizeAssetPath(clipPath);
            if (clipPath == null)
                return new { success = false, message = "Invalid asset path" };

            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
            if (clip == null)
                return new { success = false, message = $"AnimationClip not found at '{clipPath}'" };

            // Try legacy Animation component first
            var legacyAnim = go.GetComponent<UnityEngine.Animation>();
            if (legacyAnim != null)
            {
                var wasLegacy = clip.legacy;
                SetupLegacyClip(clip);
                Undo.RecordObject(legacyAnim, "Assign Animation Clip");
                legacyAnim.clip = clip;
                legacyAnim.AddClip(clip, clip.name);
                legacyAnim.playAutomatically = true;
                EditorUtility.SetDirty(legacyAnim);
                AssetDatabase.SaveAssets();

                // Warn about AnimationEvents if present — they require a MonoBehaviour receiver
                var events = AnimationUtility.GetAnimationEvents(clip);
                string warning = "";
                if (events != null && events.Length > 0)
                {
                    var eventNames = new System.Collections.Generic.List<string>();
                    foreach (var e in events)
                        eventNames.Add(e.functionName);
                    warning = $" Warning: This clip has {events.Length} AnimationEvent(s) ({string.Join(", ", eventNames)}). " +
                              $"'{go.name}' must have a MonoBehaviour with matching method(s) to receive them, " +
                              "otherwise Unity will log 'AnimationEvent has no receiver' errors.";
                }

                if (!wasLegacy) warning += " Warning: clip was converted to legacy and will not be usable in Mecanim/BlendTrees.";

                return new { success = true, message = $"Assigned clip '{clip.name}' to Animation component on '{go.name}'.{warning}" };
            }

            // Add Animation component if no Animator or Animation exists
            var animator = go.GetComponent<Animator>();
            if (animator == null)
            {
                var wasLegacy = clip.legacy;
                SetupLegacyClip(clip);
                Undo.RecordObject(go, "Add Animation Component");
                legacyAnim = Undo.AddComponent<UnityEngine.Animation>(go);
                legacyAnim.clip = clip;
                legacyAnim.AddClip(clip, clip.name);
                legacyAnim.playAutomatically = true;
                EditorUtility.SetDirty(go);
                AssetDatabase.SaveAssets();
                var legacyWarning = !wasLegacy ? " Warning: clip was converted to legacy and will not be usable in Mecanim/BlendTrees." : "";
                return new { success = true, message = $"Added Animation component and assigned clip '{clip.name}' to '{go.name}'.{legacyWarning}" };
            }

            // Has Animator - we can't programmatically assign clips to Animator states easily,
            // so report what the user should do
            return new
            {
                success = true,
                message = $"GameObject '{go.name}' has an Animator component. The clip '{clip.name}' is available at '{clipPath}'. " +
                          "Assign it to an Animator Controller state via the Animator window or create an AnimatorOverrideController."
            };
        }

        private static void SetupLegacyClip(AnimationClip clip)
        {
            var so = new SerializedObject(clip);
            bool changed = false;

            if (!clip.legacy)
            {
                var legacyProp = so.FindProperty("m_Legacy");
                if (legacyProp != null)
                {
                    legacyProp.boolValue = true;
                    changed = true;
                }
            }

            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            if (settings.loopTime)
            {
                var wrapProp = so.FindProperty("m_WrapMode");
                if (wrapProp != null && wrapProp.intValue != (int)WrapMode.Loop)
                {
                    wrapProp.intValue = (int)WrapMode.Loop;
                    changed = true;
                }
            }

            if (changed)
                so.ApplyModifiedProperties();
        }

        private static Keyframe[] ParseKeyframes(JToken keysToken)
        {
            if (keysToken is JArray array && array.Count > 0)
            {
                var keyframes = new List<Keyframe>();

                foreach (var item in array)
                {
                    if (item is JArray pair && pair.Count >= 2)
                    {
                        // Shorthand: [time, value]
                        float time = pair[0].ToObject<float>();
                        float value = pair[1].ToObject<float>();
                        keyframes.Add(new Keyframe(time, value));
                    }
                    else if (item is JObject obj)
                    {
                        // Full form: {"time":0, "value":0, "inTangent":0, "outTangent":0}
                        float time = obj["time"]?.ToObject<float>() ?? 0f;
                        float value = obj["value"]?.ToObject<float>() ?? 0f;

                        var kf = new Keyframe(time, value);
                        if (obj["inTangent"] != null)
                            kf.inTangent = obj["inTangent"].ToObject<float>();
                        if (obj["outTangent"] != null)
                            kf.outTangent = obj["outTangent"].ToObject<float>();
                        if (obj["inWeight"] != null)
                            kf.inWeight = obj["inWeight"].ToObject<float>();
                        if (obj["outWeight"] != null)
                            kf.outWeight = obj["outWeight"].ToObject<float>();

                        keyframes.Add(kf);
                    }
                }

                return keyframes.ToArray();
            }

            return null;
        }

        private static Type ResolveType(string typeName)
        {
            if (string.IsNullOrEmpty(typeName))
                return typeof(Transform);

            // Try common Unity types
            Type type = Type.GetType($"UnityEngine.{typeName}, UnityEngine.CoreModule");
            if (type != null) return type;

            type = Type.GetType($"UnityEngine.{typeName}, UnityEngine.AnimationModule");
            if (type != null) return type;

            type = Type.GetType($"UnityEngine.{typeName}, UnityEngine");
            if (type != null) return type;

            // Try fully qualified
            type = Type.GetType(typeName);
            if (type != null) return type;

            // Fallback: search all loaded assemblies
            foreach (var assembly in UnityAssembliesCompat.GetLoadedAssemblies())
            {
                type = assembly.GetType(typeName);
                if (type != null) return type;

                type = assembly.GetType($"UnityEngine.{typeName}");
                if (type != null) return type;
            }

            return null;
        }

        public static object AddEvent(JObject @params)
        {
            string clipPath = @params["clipPath"]?.ToString();
            if (string.IsNullOrEmpty(clipPath))
                return new { success = false, message = "'clipPath' is required" };

            clipPath = AssetPathUtility.SanitizeAssetPath(clipPath);
            if (clipPath == null)
                return new { success = false, message = "Invalid asset path" };

            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
            if (clip == null)
                return new { success = false, message = $"AnimationClip not found at '{clipPath}'" };

            float time = @params["time"]?.ToObject<float>() ?? 0f;
            string functionName = @params["functionName"]?.ToString();
            if (string.IsNullOrEmpty(functionName))
                return new { success = false, message = "'functionName' is required" };

            var animEvent = new AnimationEvent
            {
                time = time,
                functionName = functionName,
                stringParameter = @params["stringParameter"]?.ToString() ?? "",
                floatParameter = @params["floatParameter"]?.ToObject<float>() ?? 0f,
                intParameter = @params["intParameter"]?.ToObject<int>() ?? 0
            };

            var events = AnimationUtility.GetAnimationEvents(clip).ToList();
            events.Add(animEvent);
            AnimationUtility.SetAnimationEvents(clip, events.ToArray());

            EditorUtility.SetDirty(clip);
            AssetDatabase.SaveAssets();

            return new
            {
                success = true,
                message = $"Added event '{functionName}' at time {time} to '{clipPath}'",
                data = new
                {
                    clipPath,
                    time,
                    functionName,
                    stringParameter = animEvent.stringParameter,
                    floatParameter = animEvent.floatParameter,
                    intParameter = animEvent.intParameter
                }
            };
        }

        public static object RemoveEvent(JObject @params)
        {
            string clipPath = @params["clipPath"]?.ToString();
            if (string.IsNullOrEmpty(clipPath))
                return new { success = false, message = "'clipPath' is required" };

            clipPath = AssetPathUtility.SanitizeAssetPath(clipPath);
            if (clipPath == null)
                return new { success = false, message = "Invalid asset path" };

            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
            if (clip == null)
                return new { success = false, message = $"AnimationClip not found at '{clipPath}'" };

            var events = AnimationUtility.GetAnimationEvents(clip).ToList();
            int originalCount = events.Count;

            int? eventIndex = @params["eventIndex"]?.ToObject<int?>();
            if (eventIndex.HasValue)
            {
                if (eventIndex.Value < 0 || eventIndex.Value >= events.Count)
                    return new { success = false, message = $"Event index {eventIndex.Value} out of range (0-{events.Count - 1})" };

                events.RemoveAt(eventIndex.Value);
            }
            else
            {
                string functionName = @params["functionName"]?.ToString();
                if (string.IsNullOrEmpty(functionName))
                    return new { success = false, message = "Either 'eventIndex' or 'functionName' is required" };

                float? timeFilter = @params["time"]?.ToObject<float?>();
                events.RemoveAll(e =>
                {
                    bool matchesFunction = e.functionName == functionName;
                    bool matchesTime = !timeFilter.HasValue || Mathf.Approximately(e.time, timeFilter.Value);
                    return matchesFunction && matchesTime;
                });
            }

            int removedCount = originalCount - events.Count;
            if (removedCount == 0)
                return new { success = false, message = "No matching events found to remove" };

            AnimationUtility.SetAnimationEvents(clip, events.ToArray());
            EditorUtility.SetDirty(clip);
            AssetDatabase.SaveAssets();

            return new
            {
                success = true,
                message = $"Removed {removedCount} event(s) from '{clipPath}'",
                data = new
                {
                    clipPath,
                    removedCount,
                    remainingCount = events.Count
                }
            };
        }

        private static void CreateFoldersRecursive(string folderPath)
        {
            if (AssetDatabase.IsValidFolder(folderPath))
                return;

            string parent = Path.GetDirectoryName(folderPath)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(parent) && parent != "Assets" && !AssetDatabase.IsValidFolder(parent))
            {
                CreateFoldersRecursive(parent);
            }

            string folderName = Path.GetFileName(folderPath);
            if (!string.IsNullOrEmpty(parent) && !string.IsNullOrEmpty(folderName))
            {
                AssetDatabase.CreateFolder(parent, folderName);
            }
        }
    }
}
