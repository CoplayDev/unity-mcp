using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace MCPForUnity.Editor.Helpers
{
    internal static class RenderPipelineUtility
    {
        internal enum PipelineKind
        {
            BuiltIn,
            Universal,
            HighDefinition,
            Custom
        }

        private static readonly string[] BuiltInLitShaders = { "Standard", "Legacy Shaders/Diffuse" };
        private static readonly string[] BuiltInUnlitShaders = { "Unlit/Color", "Unlit/Texture" };
        private static readonly string[] UrpLitShaders = { "Universal Render Pipeline/Lit", "Universal Render Pipeline/Simple Lit" };
        private static readonly string[] UrpUnlitShaders = { "Universal Render Pipeline/Unlit" };
        private static readonly string[] HdrpLitShaders = { "HDRP/Lit", "High Definition Render Pipeline/Lit" };
        private static readonly string[] HdrpUnlitShaders = { "HDRP/Unlit", "High Definition Render Pipeline/Unlit" };

        internal static PipelineKind GetActivePipeline()
        {
            var asset = GraphicsSettings.currentRenderPipeline;
            if (asset == null)
            {
                return PipelineKind.BuiltIn;
            }

            var typeName = asset.GetType().FullName ?? string.Empty;
            if (typeName.IndexOf("HighDefinition", StringComparison.OrdinalIgnoreCase) >= 0 ||
                typeName.IndexOf("HDRP", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return PipelineKind.HighDefinition;
            }

            if (typeName.IndexOf("Universal", StringComparison.OrdinalIgnoreCase) >= 0 ||
                typeName.IndexOf("URP", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return PipelineKind.Universal;
            }

            return PipelineKind.Custom;
        }

        internal static Shader ResolveShader(string requestedNameOrAlias)
        {
            var pipeline = GetActivePipeline();

            if (!string.IsNullOrWhiteSpace(requestedNameOrAlias))
            {
                var alias = requestedNameOrAlias.Trim();
                var aliasMatch = ResolveAlias(alias, pipeline);
                if (aliasMatch != null)
                {
                    WarnIfPipelineMismatch(aliasMatch.name, pipeline);
                    return aliasMatch;
                }

                var direct = Shader.Find(alias);
                if (direct != null)
                {
                    WarnIfPipelineMismatch(direct.name, pipeline);
                    return direct;
                }

                McpLog.Warn($"Shader '{alias}' not found. Falling back to {pipeline} defaults.");
            }

            var fallback = ResolveDefaultLitShader(pipeline)
                           ?? ResolveDefaultLitShader(PipelineKind.BuiltIn)
                           ?? Shader.Find("Unlit/Color");

            if (fallback != null)
            {
                WarnIfPipelineMismatch(fallback.name, pipeline);
            }

            return fallback;
        }

        internal static Shader ResolveDefaultLitShader(PipelineKind pipeline)
        {
            return pipeline switch
            {
                PipelineKind.HighDefinition => TryFindShader(HdrpLitShaders) ?? TryFindShader(UrpLitShaders),
                PipelineKind.Universal => TryFindShader(UrpLitShaders) ?? TryFindShader(HdrpLitShaders),
                PipelineKind.Custom => TryFindShader(BuiltInLitShaders) ?? TryFindShader(UrpLitShaders) ?? TryFindShader(HdrpLitShaders),
                _ => TryFindShader(BuiltInLitShaders) ?? Shader.Find("Unlit/Color")
            };
        }

        internal static Shader ResolveDefaultUnlitShader(PipelineKind pipeline)
        {
            return pipeline switch
            {
                PipelineKind.HighDefinition => TryFindShader(HdrpUnlitShaders) ?? TryFindShader(UrpUnlitShaders) ?? TryFindShader(BuiltInUnlitShaders),
                PipelineKind.Universal => TryFindShader(UrpUnlitShaders) ?? TryFindShader(HdrpUnlitShaders) ?? TryFindShader(BuiltInUnlitShaders),
                PipelineKind.Custom => TryFindShader(BuiltInUnlitShaders) ?? TryFindShader(UrpUnlitShaders) ?? TryFindShader(HdrpUnlitShaders),
                _ => TryFindShader(BuiltInUnlitShaders)
            };
        }

        private static Shader ResolveAlias(string alias, PipelineKind pipeline)
        {
            if (string.Equals(alias, "lit", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(alias, "default", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(alias, "default_lit", StringComparison.OrdinalIgnoreCase))
            {
                return ResolveDefaultLitShader(pipeline);
            }

            if (string.Equals(alias, "unlit", StringComparison.OrdinalIgnoreCase))
            {
                return ResolveDefaultUnlitShader(pipeline);
            }

            if (string.Equals(alias, "urp_lit", StringComparison.OrdinalIgnoreCase))
            {
                return TryFindShader(UrpLitShaders);
            }

            if (string.Equals(alias, "hdrp_lit", StringComparison.OrdinalIgnoreCase))
            {
                return TryFindShader(HdrpLitShaders);
            }

            if (string.Equals(alias, "built_in_lit", StringComparison.OrdinalIgnoreCase))
            {
                return TryFindShader(BuiltInLitShaders);
            }

            return null;
        }

        private static Shader TryFindShader(params string[] shaderNames)
        {
            foreach (var shaderName in shaderNames)
            {
                var shader = Shader.Find(shaderName);
                if (shader != null)
                {
                    return shader;
                }
            }
            return null;
        }

        private static void WarnIfPipelineMismatch(string shaderName, PipelineKind activePipeline)
        {
            if (string.IsNullOrEmpty(shaderName))
            {
                return;
            }

            var lowerName = shaderName.ToLowerInvariant();
            bool shaderLooksUrp = lowerName.Contains("universal render pipeline") || lowerName.Contains("urp/");
            bool shaderLooksHdrp = lowerName.Contains("high definition render pipeline") || lowerName.Contains("hdrp/");
            bool shaderLooksBuiltin = lowerName.Contains("standard") || lowerName.Contains("legacy shaders/");
            bool shaderLooksSrp = shaderLooksUrp || shaderLooksHdrp;

            switch (activePipeline)
            {
                case PipelineKind.HighDefinition:
                    if (shaderLooksUrp)
                    {
                        McpLog.Warn($"[RenderPipelineUtility] Active pipeline is HDRP but shader '{shaderName}' looks URP-based. Asset may appear incorrect.");
                    }
                    else if (shaderLooksBuiltin && !shaderLooksHdrp)
                    {
                        McpLog.Warn($"[RenderPipelineUtility] Active pipeline is HDRP but shader '{shaderName}' looks Built-in. Consider using an HDRP shader for correct results.");
                    }
                    break;
                case PipelineKind.Universal:
                    if (shaderLooksHdrp)
                    {
                        McpLog.Warn($"[RenderPipelineUtility] Active pipeline is URP but shader '{shaderName}' looks HDRP-based. Asset may appear incorrect.");
                    }
                    else if (shaderLooksBuiltin && !shaderLooksUrp)
                    {
                        McpLog.Warn($"[RenderPipelineUtility] Active pipeline is URP but shader '{shaderName}' looks Built-in. Consider using a URP shader for correct results.");
                    }
                    break;
                case PipelineKind.BuiltIn:
                    if (shaderLooksSrp)
                    {
                        McpLog.Warn($"[RenderPipelineUtility] Active pipeline is Built-in but shader '{shaderName}' targets URP/HDRP. Asset may not render as expected.");
                    }
                    break;
            }
        }
    }
}
