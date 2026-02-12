using System;
using System.IO;
using Newtonsoft.Json.Linq;
using MCPForUnity.Editor.Helpers;
using UnityEditor;
using UnityEngine;

namespace MCPForUnity.Editor.Tools.Animation
{
    internal static class ClipPresets
    {
        private static readonly string[] ValidPresets = { "bounce", "rotate", "pulse", "fade", "shake", "hover", "spin", "sway", "bob", "wiggle", "blink", "slide_in", "elastic", "grow", "shrink" };

        public static object CreatePreset(JObject @params)
        {
            string clipPath = @params["clipPath"]?.ToString();
            if (string.IsNullOrEmpty(clipPath))
                return new { success = false, message = "'clipPath' is required (e.g. 'Assets/Animations/Bounce.anim')" };

            clipPath = AssetPathUtility.SanitizeAssetPath(clipPath);
            if (clipPath == null)
                return new { success = false, message = "Invalid asset path" };

            if (!clipPath.EndsWith(".anim", StringComparison.OrdinalIgnoreCase))
                clipPath += ".anim";

            string preset = @params["preset"]?.ToString()?.ToLowerInvariant();
            if (string.IsNullOrEmpty(preset))
                return new { success = false, message = $"'preset' is required. Valid: {string.Join(", ", ValidPresets)}" };

            float duration = @params["duration"]?.ToObject<float>() ?? 1f;
            float amplitude = @params["amplitude"]?.ToObject<float>() ?? 1f;
            bool loop = @params["loop"]?.ToObject<bool>() ?? true;

            // Resolve position offset from target GameObject or explicit offset parameter.
            // localPosition rather than absolute origin, preventing objects from jumping to (0,0,0).
            Vector3 offset = Vector3.zero;
            var targetToken = @params["target"];
            if (targetToken != null && targetToken.Type != JTokenType.Null)
            {
                string searchMethod = @params["searchMethod"]?.ToString();
                var go = ObjectResolver.ResolveGameObject(targetToken, searchMethod);
                if (go != null)
                    offset = go.transform.localPosition;
            }
            var offsetToken = @params["offset"];
            if (offsetToken is JArray offsetArray && offsetArray.Count >= 3)
            {
                offset = new Vector3(
                    offsetArray[0].ToObject<float>(),
                    offsetArray[1].ToObject<float>(),
                    offsetArray[2].ToObject<float>()
                );
            }

            string dir = Path.GetDirectoryName(clipPath)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(dir) && !AssetDatabase.IsValidFolder(dir))
                CreateFoldersRecursive(dir);

            var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
            if (existing != null)
                return new { success = false, message = $"AnimationClip already exists at '{clipPath}'. Delete it first or use a different path." };

            var clip = new AnimationClip();
            clip.name = Path.GetFileNameWithoutExtension(clipPath);
            clip.frameRate = 60f;

            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = loop;
            settings.stopTime = duration;
            AnimationUtility.SetAnimationClipSettings(clip, settings);

            switch (preset)
            {
                case "bounce":
                    ApplyBounce(clip, duration, amplitude, offset);
                    break;
                case "rotate":
                    ApplyRotate(clip, duration, amplitude);
                    break;
                case "pulse":
                    ApplyPulse(clip, duration, amplitude);
                    break;
                case "fade":
                    ApplyFade(clip, duration);
                    break;
                case "shake":
                    ApplyShake(clip, duration, amplitude, offset);
                    break;
                case "hover":
                    ApplyHover(clip, duration, amplitude, offset);
                    break;
                case "spin":
                    ApplySpin(clip, duration, amplitude);
                    break;
                case "sway":
                    ApplySway(clip, duration, amplitude);
                    break;
                case "bob":
                    ApplyBob(clip, duration, amplitude, offset);
                    break;
                case "wiggle":
                    ApplyWiggle(clip, duration, amplitude);
                    break;
                case "blink":
                    ApplyBlink(clip, duration);
                    break;
                case "slide_in":
                    ApplySlideIn(clip, duration, amplitude, offset);
                    break;
                case "elastic":
                    ApplyElastic(clip, duration, amplitude);
                    break;
                case "grow":
                    ApplyGrow(clip, duration, amplitude);
                    break;
                case "shrink":
                    ApplyShrink(clip, duration, amplitude);
                    break;
                default:
                    return new { success = false, message = $"Unknown preset '{preset}'. Valid: {string.Join(", ", ValidPresets)}" };
            }

            AssetDatabase.CreateAsset(clip, clipPath);
            AssetDatabase.SaveAssets();

            return new
            {
                success = true,
                message = $"Created '{preset}' preset clip at '{clipPath}'" + (offset != Vector3.zero ? $" (offset: {offset})" : ""),
                data = new
                {
                    path = clipPath,
                    name = clip.name,
                    preset,
                    duration,
                    amplitude,
                    isLooping = loop,
                    offset = new { x = offset.x, y = offset.y, z = offset.z },
                    curveCount = AnimationUtility.GetCurveBindings(clip).Length
                }
            };
        }

        private static void ApplyBounce(AnimationClip clip, float duration, float amplitude, Vector3 offset)
        {
            // localPosition.y sine wave oscillation, offset by target's current position
            float half = duration * 0.5f;
            var curve = new AnimationCurve(
                new Keyframe(0f, offset.y),
                new Keyframe(half * 0.5f, offset.y + amplitude),
                new Keyframe(half, offset.y),
                new Keyframe(half + half * 0.5f, offset.y + amplitude),
                new Keyframe(duration, offset.y)
            );
            SetTransformCurve(clip, "localPosition.y", curve);
        }

        private static void ApplyRotate(AnimationClip clip, float duration, float amplitude)
        {
            // localEulerAngles.y full 360 rotation (amplitude acts as multiplier)
            var curve = new AnimationCurve(
                new Keyframe(0f, 0f),
                new Keyframe(duration, 360f * amplitude)
            );
            // Linear tangents for smooth rotation
            var keys = curve.keys;
            keys[0].outTangent = 360f * amplitude / duration;
            keys[1].inTangent = 360f * amplitude / duration;
            curve.keys = keys;
            SetTransformCurve(clip, "localEulerAngles.y", curve);
        }

        private static void ApplyPulse(AnimationClip clip, float duration, float amplitude)
        {
            // localScale uniform scale up/down
            float peak = 1f + amplitude * 0.5f;
            float half = duration * 0.5f;
            var curve = new AnimationCurve(
                new Keyframe(0f, 1f),
                new Keyframe(half, peak),
                new Keyframe(duration, 1f)
            );
            SetTransformCurve(clip, "localScale.x", curve);
            SetTransformCurve(clip, "localScale.y", curve);
            SetTransformCurve(clip, "localScale.z", curve);
        }

        private static void ApplyFade(AnimationClip clip, float duration)
        {
            // CanvasGroup alpha 1 -> 0
            var curve = new AnimationCurve(
                new Keyframe(0f, 1f),
                new Keyframe(duration, 0f)
            );
            var binding = EditorCurveBinding.FloatCurve("", typeof(CanvasGroup), "m_Alpha");
            AnimationUtility.SetEditorCurve(clip, binding, curve);
        }

        private static void ApplyShake(AnimationClip clip, float duration, float amplitude, Vector3 offset)
        {
            // localPosition.x/z oscillation simulating shake, centered on target's current position
            int steps = 8;
            float stepTime = duration / steps;
            var xKeys = new Keyframe[steps + 1];
            var zKeys = new Keyframe[steps + 1];

            for (int i = 0; i <= steps; i++)
            {
                float t = i * stepTime;
                float decay = 1f - (float)i / steps;
                // Alternating direction with decay
                float sign = (i % 2 == 0) ? 1f : -1f;
                xKeys[i] = new Keyframe(t, offset.x + sign * amplitude * decay);
                zKeys[i] = new Keyframe(t, offset.z - sign * amplitude * 0.5f * decay);
            }

            // End at offset position
            xKeys[steps] = new Keyframe(duration, offset.x);
            zKeys[steps] = new Keyframe(duration, offset.z);

            SetTransformCurve(clip, "localPosition.x", new AnimationCurve(xKeys));
            SetTransformCurve(clip, "localPosition.z", new AnimationCurve(zKeys));
        }

        private static void ApplyHover(AnimationClip clip, float duration, float amplitude, Vector3 offset)
        {
            // localPosition.y gentle sine wave, offset by target's current position
            float q = duration * 0.25f;
            var curve = new AnimationCurve(
                new Keyframe(0f, offset.y),
                new Keyframe(q, offset.y + amplitude * 0.5f),
                new Keyframe(q * 2f, offset.y),
                new Keyframe(q * 3f, offset.y - amplitude * 0.5f),
                new Keyframe(duration, offset.y)
            );
            SetTransformCurve(clip, "localPosition.y", curve);
        }

        private static void ApplySpin(AnimationClip clip, float duration, float amplitude)
        {
            // localEulerAngles.z continuous rotation
            var curve = new AnimationCurve(
                new Keyframe(0f, 0f),
                new Keyframe(duration, 360f * amplitude)
            );
            var keys = curve.keys;
            keys[0].outTangent = 360f * amplitude / duration;
            keys[1].inTangent = 360f * amplitude / duration;
            curve.keys = keys;
            SetTransformCurve(clip, "localEulerAngles.z", curve);
        }

        private static void ApplySway(AnimationClip clip, float duration, float amplitude)
        {
            // localEulerAngles.z gentle side-to-side rotation (sine wave)
            float q = duration * 0.25f;
            var curve = new AnimationCurve(
                new Keyframe(0f, 0f),
                new Keyframe(q, amplitude),
                new Keyframe(q * 2f, 0f),
                new Keyframe(q * 3f, -amplitude),
                new Keyframe(duration, 0f)
            );
            SetTransformCurve(clip, "localEulerAngles.z", curve);
        }

        private static void ApplyBob(AnimationClip clip, float duration, float amplitude, Vector3 offset)
        {
            // localPosition.z gentle forward/back movement, offset by target's current position
            float q = duration * 0.25f;
            var curve = new AnimationCurve(
                new Keyframe(0f, offset.z),
                new Keyframe(q, offset.z + amplitude * 0.5f),
                new Keyframe(q * 2f, offset.z),
                new Keyframe(q * 3f, offset.z - amplitude * 0.5f),
                new Keyframe(duration, offset.z)
            );
            SetTransformCurve(clip, "localPosition.z", curve);
        }

        private static void ApplyWiggle(AnimationClip clip, float duration, float amplitude)
        {
            // localEulerAngles.z rapid oscillation (similar to shake but rotation)
            int steps = 8;
            float stepTime = duration / steps;
            var keys = new Keyframe[steps + 1];

            for (int i = 0; i <= steps; i++)
            {
                float t = i * stepTime;
                float decay = 1f - (float)i / steps;
                float sign = (i % 2 == 0) ? 1f : -1f;
                keys[i] = new Keyframe(t, sign * amplitude * decay);
            }

            keys[steps] = new Keyframe(duration, 0f);
            SetTransformCurve(clip, "localEulerAngles.z", new AnimationCurve(keys));
        }

        private static void ApplyBlink(AnimationClip clip, float duration)
        {
            // localScale uniform scale to near-zero and back
            float mid = duration * 0.5f;
            var curve = new AnimationCurve(
                new Keyframe(0f, 1f),
                new Keyframe(mid, 0.05f),
                new Keyframe(duration, 1f)
            );
            SetTransformCurve(clip, "localScale.x", curve);
            SetTransformCurve(clip, "localScale.y", curve);
            SetTransformCurve(clip, "localScale.z", curve);
        }

        private static void ApplySlideIn(AnimationClip clip, float duration, float amplitude, Vector3 offset)
        {
            // localPosition.x slide from offset-amplitude to offset (linear)
            var curve = new AnimationCurve(
                new Keyframe(0f, offset.x - amplitude),
                new Keyframe(duration, offset.x)
            );
            // Set linear tangents for smooth slide
            var keys = curve.keys;
            keys[0].outTangent = amplitude / duration;
            keys[1].inTangent = amplitude / duration;
            curve.keys = keys;
            SetTransformCurve(clip, "localPosition.x", curve);
        }

        private static void ApplyElastic(AnimationClip clip, float duration, float amplitude)
        {
            // localScale uniform with overshoot effect
            float third = duration / 3f;
            float peak = 1f + amplitude * 1.2f;
            float settle = 1f + amplitude * 0.8f;
            var curve = new AnimationCurve(
                new Keyframe(0f, 1f),
                new Keyframe(third, peak),
                new Keyframe(third * 2f, settle),
                new Keyframe(duration, 1f)
            );
            SetTransformCurve(clip, "localScale.x", curve);
            SetTransformCurve(clip, "localScale.y", curve);
            SetTransformCurve(clip, "localScale.z", curve);
        }

        private static void ApplyGrow(AnimationClip clip, float duration, float amplitude)
        {
            // localScale uniform from a reduced value up to 1.0
            float clamped = Mathf.Max(0f, amplitude);
            float start = Mathf.Clamp01(1f - clamped);
            var curve = new AnimationCurve(
                new Keyframe(0f, start),
                new Keyframe(duration, 1f)
            );
            SetTransformCurve(clip, "localScale.x", curve);
            SetTransformCurve(clip, "localScale.y", curve);
            SetTransformCurve(clip, "localScale.z", curve);
        }

        private static void ApplyShrink(AnimationClip clip, float duration, float amplitude)
        {
            // localScale uniform from 1.0 down to a reduced value
            float clamped = Mathf.Max(0f, amplitude);
            float end = Mathf.Clamp01(1f - clamped);
            var curve = new AnimationCurve(
                new Keyframe(0f, 1f),
                new Keyframe(duration, end)
            );
            SetTransformCurve(clip, "localScale.x", curve);
            SetTransformCurve(clip, "localScale.y", curve);
            SetTransformCurve(clip, "localScale.z", curve);
        }

        /// <summary>
        /// Sets an animation curve on a Transform property using AnimationUtility.SetEditorCurve
        /// instead of clip.SetCurve to avoid marking the clip as legacy. Legacy clips cannot be
        /// used with Mecanim AnimatorControllers, and legacy Animation components take control of
        /// the entire Vector3 property (zeroing non-animated axes).
        /// </summary>
        private static void SetTransformCurve(AnimationClip clip, string propertyName, AnimationCurve curve)
        {
            var binding = EditorCurveBinding.FloatCurve("", typeof(Transform), propertyName);
            AnimationUtility.SetEditorCurve(clip, binding, curve);
        }

        private static void CreateFoldersRecursive(string folderPath)
        {
            if (AssetDatabase.IsValidFolder(folderPath))
                return;

            string parent = Path.GetDirectoryName(folderPath)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(parent) && parent != "Assets" && !AssetDatabase.IsValidFolder(parent))
                CreateFoldersRecursive(parent);

            string folderName = Path.GetFileName(folderPath);
            if (!string.IsNullOrEmpty(parent) && !string.IsNullOrEmpty(folderName))
                AssetDatabase.CreateFolder(parent, folderName);
        }
    }
}
