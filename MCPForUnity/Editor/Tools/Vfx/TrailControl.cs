using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using MCPForUnity.Editor.Helpers;

namespace MCPForUnity.Editor.Tools.Vfx
{
    internal static class TrailControl
    {
        public static object Clear(JObject @params)
        {
            TrailRenderer tr = TrailRead.FindTrailRenderer(@params);
            if (tr == null) return new { success = false, message = "TrailRenderer not found" };

            Undo.RecordObject(tr, "Clear Trail");
            tr.Clear();
            return new { success = true, message = "Trail cleared" };
        }

        public static object Emit(JObject @params)
        {
            TrailRenderer tr = TrailRead.FindTrailRenderer(@params);
            if (tr == null) return new { success = false, message = "TrailRenderer not found" };

            RendererHelpers.EnsureMaterial(tr);

#if UNITY_2021_1_OR_NEWER
            Vector3 pos = ManageVfxCommon.ParseVector3(@params["position"]);
            tr.AddPosition(pos);
            return new { success = true, message = $"Emitted at ({pos.x}, {pos.y}, {pos.z})" };
#else
            return new { success = false, message = "AddPosition requires Unity 2021.1+" };
#endif
        }
    }
}
