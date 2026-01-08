using UnityEngine;

namespace AssetStoreTools.Previews.Generators.Custom.Screenshotters
{
    internal class MeshScreenshotter : SceneScreenshotterBase
    {
        public MeshScreenshotter(SceneScreenshotterSettings settings) : base(settings) { }

        public override void PositionCamera(GameObject target)
        {
            var renderers = target.GetComponentsInChildren<Renderer>();
            if (renderers == null || renderers.Length == 0)
                return;

            var bounds = GetGlobalBounds(renderers);

            var encapsulatingSphereDiameter = (bounds.max - bounds.min).magnitude;
            var encapsulatingSphereRadius = encapsulatingSphereDiameter / 2;

            var angle = Camera.fieldOfView / 2;
            var sinAngle = Mathf.Sin(angle * Mathf.Deg2Rad);
            var distance = encapsulatingSphereRadius / sinAngle;

            Camera.transform.position = new Vector3(bounds.center.x, bounds.center.y + distance, bounds.center.z);
            Camera.transform.LookAt(bounds.center);
            Camera.transform.RotateAround(bounds.center, Vector3.left, 65);
            Camera.transform.RotateAround(bounds.center, Vector3.up, 235);

            Camera.nearClipPlane = 0.01f;
            Camera.farClipPlane = 10000;
        }
    }
}