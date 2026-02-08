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
        private static readonly string[] ValidPresets = { "bounce", "rotate", "pulse", "fade", "shake", "hover", "spin", "sway", "bob", "wiggle", "blink", "slide_in", "elastic" };

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
                    ApplyBounce(clip, duration, amplitude);
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
                    ApplyShake(clip, duration, amplitude);
                    break;
                case "hover":
                    ApplyHover(clip, duration, amplitude);
                    break;
                case "spin":
                    ApplySpin(clip, duration, amplitude);
                    break;
                case "sway":
                    ApplySway(clip, duration, amplitude);
                    break;
                case "bob":
                    ApplyBob(clip, duration, amplitude);
                    break;
                case "wiggle":
                    ApplyWiggle(clip, duration, amplitude);
                    break;
                case "blink":
                    ApplyBlink(clip, duration);
                    break;
                case "slide_in":
                    ApplySlideIn(clip, duration, amplitude);
                    break;
                case "elastic":
                    ApplyElastic(clip, duration, amplitude);
                    break;
                default:
                    return new { success = false, message = $"Unknown preset '{preset}'. Valid: {string.Join(", ", ValidPresets)}" };
            }

            AssetDatabase.CreateAsset(clip, clipPath);
            AssetDatabase.SaveAssets();

            return new
            {
                success = true,
                message = $"Created '{preset}' preset clip at '{clipPath}'",
                data = new
                {
                    path = clipPath,
                    name = clip.name,
                    preset,
                    duration,
                    amplitude,
                    isLooping = loop,
                    curveCount = AnimationUtility.GetCurveBindings(clip).Length
                }
            };
        }

        private static void ApplyBounce(AnimationClip clip, float duration, float amplitude)
        {
            // localPosition.y sine wave oscillation
            float half = duration * 0.5f;
            var curve = new AnimationCurve(
                new Keyframe(0f, 0f),
                new Keyframe(half * 0.5f, amplitude),
                new Keyframe(half, 0f),
                new Keyframe(half + half * 0.5f, amplitude),
                new Keyframe(duration, 0f)
            );
            clip.SetCurve("", typeof(Transform), "localPosition.y", curve);
        }

        private static void ApplyRotate(AnimationClip clip, float duration, float amplitude)
        {
            // localEulerAngles.y full 360 rotation (amplitude acts as multiplier)
            var curve = new AnimationCurve(
                new Keyframe(0f, 0f),
                new Keyframe(duration, 360f * amplitude)
            );
            // Linear tangents for smooth rotation
            curve.keys[0].outTangent = 360f * amplitude / duration;
            var keys = curve.keys;
            keys[1].inTangent = 360f * amplitude / duration;
            curve.keys = keys;
            clip.SetCurve("", typeof(Transform), "localEulerAngles.y", curve);
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
            clip.SetCurve("", typeof(Transform), "localScale.x", curve);
            clip.SetCurve("", typeof(Transform), "localScale.y", curve);
            clip.SetCurve("", typeof(Transform), "localScale.z", curve);
        }

        private static void ApplyFade(AnimationClip clip, float duration)
        {
            // CanvasGroup alpha 1 -> 0
            var curve = new AnimationCurve(
                new Keyframe(0f, 1f),
                new Keyframe(duration, 0f)
            );
            clip.SetCurve("", typeof(CanvasGroup), "m_Alpha", curve);
        }

        private static void ApplyShake(AnimationClip clip, float duration, float amplitude)
        {
            // localPosition.x/z oscillation simulating shake
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
                xKeys[i] = new Keyframe(t, sign * amplitude * decay);
                zKeys[i] = new Keyframe(t, -sign * amplitude * 0.5f * decay);
            }

            // End at zero
            xKeys[steps] = new Keyframe(duration, 0f);
            zKeys[steps] = new Keyframe(duration, 0f);

            clip.SetCurve("", typeof(Transform), "localPosition.x", new AnimationCurve(xKeys));
            clip.SetCurve("", typeof(Transform), "localPosition.z", new AnimationCurve(zKeys));
        }

        private static void ApplyHover(AnimationClip clip, float duration, float amplitude)
        {
            // localPosition.y gentle sine wave (4 samples for smooth sine approximation)
            float q = duration * 0.25f;
            var curve = new AnimationCurve(
                new Keyframe(0f, 0f),
                new Keyframe(q, amplitude * 0.5f),
                new Keyframe(q * 2f, 0f),
                new Keyframe(q * 3f, -amplitude * 0.5f),
                new Keyframe(duration, 0f)
            );
            clip.SetCurve("", typeof(Transform), "localPosition.y", curve);
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
            clip.SetCurve("", typeof(Transform), "localEulerAngles.z", curve);
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
            clip.SetCurve("", typeof(Transform), "localEulerAngles.z", curve);
        }

        private static void ApplyBob(AnimationClip clip, float duration, float amplitude)
        {
            // localPosition.z gentle forward/back movement
            float q = duration * 0.25f;
            var curve = new AnimationCurve(
                new Keyframe(0f, 0f),
                new Keyframe(q, amplitude * 0.5f),
                new Keyframe(q * 2f, 0f),
                new Keyframe(q * 3f, -amplitude * 0.5f),
                new Keyframe(duration, 0f)
            );
            clip.SetCurve("", typeof(Transform), "localPosition.z", curve);
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
            clip.SetCurve("", typeof(Transform), "localEulerAngles.z", new AnimationCurve(keys));
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
            clip.SetCurve("", typeof(Transform), "localScale.x", curve);
            clip.SetCurve("", typeof(Transform), "localScale.y", curve);
            clip.SetCurve("", typeof(Transform), "localScale.z", curve);
        }

        private static void ApplySlideIn(AnimationClip clip, float duration, float amplitude)
        {
            // localPosition.x slide from -amplitude to 0 (linear)
            var curve = new AnimationCurve(
                new Keyframe(0f, -amplitude),
                new Keyframe(duration, 0f)
            );
            // Set linear tangents for smooth slide
            var keys = curve.keys;
            keys[0].outTangent = amplitude / duration;
            keys[1].inTangent = amplitude / duration;
            curve.keys = keys;
            clip.SetCurve("", typeof(Transform), "localPosition.x", curve);
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
            clip.SetCurve("", typeof(Transform), "localScale.x", curve);
            clip.SetCurve("", typeof(Transform), "localScale.y", curve);
            clip.SetCurve("", typeof(Transform), "localScale.z", curve);
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
