using AssetStoreTools.Previews.Utility;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace AssetStoreTools.Previews.Generators.Custom.Screenshotters
{
    internal abstract class SceneScreenshotterBase : ISceneScreenshotter
    {
        public SceneScreenshotterSettings Settings { get; }

        protected Camera Camera => GetCamera();
        private Camera _camera;

        public SceneScreenshotterBase(SceneScreenshotterSettings settings)
        {
            Settings = settings;
        }

        private Camera GetCamera()
        {
            if (_camera == null)
            {
#if UNITY_2022_3_OR_NEWER
                _camera = GameObject.FindFirstObjectByType<Camera>(FindObjectsInactive.Include);
#else
                _camera = GameObject.FindObjectOfType<Camera>();
#endif
            }

            return _camera;
        }

        public virtual void ValidateSettings()
        {
            if (Settings.Width <= 0)
                throw new ArgumentException("Width should be larger than 0");

            if (Settings.Height <= 0)
                throw new ArgumentException("Height should be larger than 0");

            if (Settings.Depth <= 0)
                throw new ArgumentException("Depth should be larger than 0");

            if (Settings.NativeWidth <= 0)
                throw new ArgumentException("Native width should be larger than 0");

            if (Settings.NativeHeight <= 0)
                throw new ArgumentException("Native height should be larger than 0");
        }

        public abstract void PositionCamera(GameObject target);

        public string Screenshot(string outputPath)
        {
            ValidateSettings();

            var texture = GraphicsUtility.GetTextureFromCamera(Camera, Settings.NativeWidth, Settings.NativeHeight, Settings.Depth);

            if (Settings.Width < Settings.NativeWidth || Settings.Height < Settings.NativeHeight)
                texture = GraphicsUtility.ResizeTexture(texture, Settings.Width, Settings.Height);

            var extension = PreviewConvertUtility.ConvertExtension(Settings.Format);
            var writtenPath = $"{outputPath}.{extension}";
            var bytes = PreviewConvertUtility.ConvertTexture(texture, Settings.Format);
            File.WriteAllBytes(writtenPath, bytes);

            return writtenPath;
        }

        public string Screenshot(GameObject target, string outputPath)
        {
            PositionCamera(target);
            PositionLighting(target);
            return Screenshot(outputPath);
        }

        private void PositionLighting(GameObject target)
        {
#if UNITY_2022_3_OR_NEWER
            var light = GameObject.FindFirstObjectByType<Light>(FindObjectsInactive.Include);
#else
            var light = GameObject.FindObjectOfType<Light>();
#endif
            light.transform.position = Camera.transform.position;
            light.transform.LookAt(target.transform);
            light.transform.RotateAround(target.transform.position, Vector3.forward, 60f);
        }

        protected Bounds GetGlobalBounds(IEnumerable<Renderer> renderers)
        {
            var center = Vector3.zero;

            foreach (var renderer in renderers)
            {
                center += renderer.bounds.center;
            }
            center /= renderers.Count();

            var globalBounds = new Bounds(center, Vector3.zero);

            foreach (var renderer in renderers)
            {
                globalBounds.Encapsulate(renderer.bounds);
            }

            return globalBounds;
        }

        protected Bounds GetNormalizedBounds(Bounds bounds)
        {
            var largestExtent = Mathf.Max(bounds.extents.x, bounds.extents.y, bounds.extents.z);
            var normalizedBounds = new Bounds()
            {
                center = bounds.center,
                extents = new Vector3(largestExtent, largestExtent, largestExtent)
            };

            return normalizedBounds;
        }
    }
}