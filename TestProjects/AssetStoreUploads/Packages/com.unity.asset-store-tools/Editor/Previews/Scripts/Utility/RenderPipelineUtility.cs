using UnityEngine.Rendering;
#if AST_URP_AVAILABLE
using UnityEngine.Rendering.Universal;
#endif
#if AST_HDRP_AVAILABLE
using UnityEngine.Rendering.HighDefinition;
#endif

namespace AssetStoreTools.Previews.Utility
{
    internal static class RenderPipelineUtility
    {
        public static RenderPipeline GetCurrentPipeline()
        {
            var currentPipelineAsset = GraphicsSettings.currentRenderPipeline;
            if (currentPipelineAsset == null)
                return RenderPipeline.BiRP;

#if AST_URP_AVAILABLE
            if (currentPipelineAsset is UniversalRenderPipelineAsset)
                return RenderPipeline.URP;
#endif

#if AST_HDRP_AVAILABLE
            if (currentPipelineAsset is HDRenderPipelineAsset)
                return RenderPipeline.HDRP;
#endif

            return RenderPipeline.Unknown;
        }
    }
}