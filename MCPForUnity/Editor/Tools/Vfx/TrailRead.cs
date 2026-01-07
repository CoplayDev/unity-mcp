using Newtonsoft.Json.Linq;
using UnityEngine;

namespace MCPForUnity.Editor.Tools.Vfx
{
    internal static class TrailRead
    {
        public static TrailRenderer FindTrailRenderer(JObject @params)
        {
            GameObject go = ManageVfxCommon.FindTargetGameObject(@params);
            return go?.GetComponent<TrailRenderer>();
        }

        public static object GetInfo(JObject @params)
        {
            TrailRenderer tr = FindTrailRenderer(@params);
            if (tr == null) return new { success = false, message = "TrailRenderer not found" };

            return new
            {
                success = true,
                data = new
                {
                    gameObject = tr.gameObject.name,
                    time = tr.time,
                    startWidth = tr.startWidth,
                    endWidth = tr.endWidth,
                    minVertexDistance = tr.minVertexDistance,
                    emitting = tr.emitting,
                    autodestruct = tr.autodestruct,
                    positionCount = tr.positionCount,
                    alignment = tr.alignment.ToString(),
                    textureMode = tr.textureMode.ToString(),
                    numCornerVertices = tr.numCornerVertices,
                    numCapVertices = tr.numCapVertices,
                    generateLightingData = tr.generateLightingData,
                    material = tr.sharedMaterial?.name,
                    shadowCastingMode = tr.shadowCastingMode.ToString(),
                    receiveShadows = tr.receiveShadows,
                    lightProbeUsage = tr.lightProbeUsage.ToString(),
                    reflectionProbeUsage = tr.reflectionProbeUsage.ToString(),
                    sortingOrder = tr.sortingOrder,
                    sortingLayerName = tr.sortingLayerName,
                    renderingLayerMask = tr.renderingLayerMask
                }
            };
        }
    }
}
