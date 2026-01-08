using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace MCPForUnity.Editor.Tools.Vfx
{
    internal static class LineRead
    {
        public static LineRenderer FindLineRenderer(JObject @params)
        {
            GameObject go = ManageVfxCommon.FindTargetGameObject(@params);
            return go?.GetComponent<LineRenderer>();
        }

        public static object GetInfo(JObject @params)
        {
            LineRenderer lr = FindLineRenderer(@params);
            if (lr == null) return new { success = false, message = "LineRenderer not found" };

            var positions = new Vector3[lr.positionCount];
            lr.GetPositions(positions);

            return new
            {
                success = true,
                data = new
                {
                    gameObject = lr.gameObject.name,
                    positionCount = lr.positionCount,
                    positions = positions.Select(p => new { x = p.x, y = p.y, z = p.z }).ToArray(),
                    startWidth = lr.startWidth,
                    endWidth = lr.endWidth,
                    loop = lr.loop,
                    useWorldSpace = lr.useWorldSpace,
                    alignment = lr.alignment.ToString(),
                    textureMode = lr.textureMode.ToString(),
                    numCornerVertices = lr.numCornerVertices,
                    numCapVertices = lr.numCapVertices,
                    generateLightingData = lr.generateLightingData,
                    material = lr.sharedMaterial?.name,
                    shadowCastingMode = lr.shadowCastingMode.ToString(),
                    receiveShadows = lr.receiveShadows,
                    lightProbeUsage = lr.lightProbeUsage.ToString(),
                    reflectionProbeUsage = lr.reflectionProbeUsage.ToString(),
                    sortingOrder = lr.sortingOrder,
                    sortingLayerName = lr.sortingLayerName,
                    renderingLayerMask = lr.renderingLayerMask
                }
            };
        }
    }
}
