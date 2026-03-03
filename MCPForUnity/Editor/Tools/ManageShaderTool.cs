using System;
using System.Collections.Generic;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace MCPForUnity.Editor.Tools
{
    /// <summary>
    /// MCP tool for shader inspection, error checking, and reimport operations.
    /// Actions: reimport, get_errors, get_info, get_passes, find, is_compiling.
    /// Note: Named manage_shader_tool to avoid conflict with upstream manage_shader (CRUD).
    /// </summary>
    [McpForUnityTool("manage_shader_tool", AutoRegister = true)]
    public static class ManageShaderTool
    {
        public static object HandleCommand(JObject @params)
        {
            if (@params == null)
                return new ErrorResponse("Parameters cannot be null.");

            var p = new ToolParams(@params);

            var actionResult = p.GetRequired("action");
            if (!actionResult.IsSuccess)
                return new ErrorResponse(actionResult.ErrorMessage);

            string action = actionResult.Value.ToLowerInvariant();

            try
            {
                return action switch
                {
                    "reimport"     => Reimport(p),
                    "get_errors"   => GetErrors(p),
                    "get_info"     => GetInfo(p),
                    "get_passes"   => GetPasses(p),
                    "find"         => Find(p),
                    "is_compiling" => IsCompiling(),
                    _ => new ErrorResponse(
                        $"Unknown action: '{action}'. Supported: reimport, get_errors, get_info, get_passes, find, is_compiling")
                };
            }
            catch (Exception e)
            {
                McpLog.Error($"[ManageShaderTool] Action '{action}' failed: {e}");
                return new ErrorResponse($"Internal error processing action '{action}': {e.Message}");
            }
        }

        #region Actions

        private static object Reimport(ToolParams p)
        {
            string path = p.Get("path");
            if (string.IsNullOrEmpty(path))
                return new ErrorResponse("'path' parameter is required for reimport.");

            path = AssetPathUtility.SanitizeAssetPath(path);
            if (path == null)
                return new ErrorResponse("Invalid asset path (contains traversal sequence).");

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            AssetDatabase.Refresh();

            return new SuccessResponse($"Reimported shader at '{path}'.", new Dictionary<string, object>
            {
                ["path"] = path
            });
        }

        private static object GetErrors(ToolParams p)
        {
            var shader = ResolveShader(p, out string resolveError);
            if (shader == null)
                return new ErrorResponse(resolveError);

            var messages = ShaderUtil.GetShaderMessages(shader);
            var result = new List<object>();

            foreach (var msg in messages)
            {
                result.Add(new Dictionary<string, object>
                {
                    ["message"]  = msg.message,
                    ["severity"] = msg.severity.ToString(),
                    ["file"]     = msg.file,
                    ["line"]     = msg.line,
                    ["platform"] = msg.platform.ToString()
                });
            }

            string summary = result.Count == 0
                ? $"No errors found for shader '{shader.name}'."
                : $"Found {result.Count} message(s) for shader '{shader.name}'.";

            return new SuccessResponse(summary, new Dictionary<string, object>
            {
                ["shader_name"]    = shader.name,
                ["message_count"]  = result.Count,
                ["messages"]       = result
            });
        }

        private static object GetInfo(ToolParams p)
        {
            var shader = ResolveShader(p, out string resolveError);
            if (shader == null)
                return new ErrorResponse(resolveError);

            var shaderData   = ShaderUtil.GetShaderData(shader);
            int subshaderCount = shaderData.SubshaderCount;
            int passCount = 0;
            for (int i = 0; i < subshaderCount; i++)
                passCount += shaderData.GetSubshader(i).PassCount;

            string assetPath = AssetDatabase.GetAssetPath(shader);

            return new SuccessResponse($"Info for shader '{shader.name}'.", new Dictionary<string, object>
            {
                ["name"]             = shader.name,
                ["asset_path"]       = assetPath,
                ["is_supported"]     = shader.isSupported,
                ["subshader_count"]  = subshaderCount,
                ["total_pass_count"] = passCount,
                ["render_queue"]     = shader.renderQueue
            });
        }

        private static object GetPasses(ToolParams p)
        {
            var shader = ResolveShader(p, out string resolveError);
            if (shader == null)
                return new ErrorResponse(resolveError);

            var shaderData     = ShaderUtil.GetShaderData(shader);
            int subshaderCount = shaderData.SubshaderCount;
            var subshaders     = new List<object>();

            for (int si = 0; si < subshaderCount; si++)
            {
                var subshader  = shaderData.GetSubshader(si);
                int passCount  = subshader.PassCount;
                var passes     = new List<object>();

                for (int pi = 0; pi < passCount; pi++)
                {
                    var pass = subshader.GetPass(pi);
                    passes.Add(new Dictionary<string, object>
                    {
                        ["name"] = pass.Name
                    });
                }

                subshaders.Add(new Dictionary<string, object>
                {
                    ["subshader_index"] = si,
                    ["pass_count"]      = passCount,
                    ["passes"]          = passes
                });
            }

            return new SuccessResponse($"Passes for shader '{shader.name}'.", new Dictionary<string, object>
            {
                ["shader_name"]    = shader.name,
                ["subshader_count"] = subshaderCount,
                ["subshaders"]     = subshaders
            });
        }

        private static object Find(ToolParams p)
        {
            string name = p.Get("name");
            if (string.IsNullOrEmpty(name))
                return new ErrorResponse("'name' parameter is required for find.");

            var shader = Shader.Find(name);
            if (shader == null)
                return new ErrorResponse($"Shader '{name}' not found. Ensure the name matches the shader declaration.");

            string assetPath = AssetDatabase.GetAssetPath(shader);

            return new SuccessResponse($"Found shader '{shader.name}'.", new Dictionary<string, object>
            {
                ["name"]         = shader.name,
                ["asset_path"]   = assetPath,
                ["is_supported"] = shader.isSupported,
                ["render_queue"] = shader.renderQueue
            });
        }

        private static object IsCompiling()
        {
            bool compiling = ShaderUtil.anythingCompiling;
            return new SuccessResponse(
                compiling ? "Shaders are currently compiling." : "No shader compilation in progress.",
                new Dictionary<string, object>
                {
                    ["is_compiling"] = compiling
                });
        }

        #endregion

        #region Helpers

        /// <summary>
        /// Resolves a Shader from either a 'path' or 'name' parameter.
        /// Sets resolveError to a user-facing message when null is returned.
        /// </summary>
        private static Shader ResolveShader(ToolParams p, out string resolveError)
        {
            string path = p.Get("path");
            if (!string.IsNullOrEmpty(path))
            {
                string sanitized = AssetPathUtility.SanitizeAssetPath(path);
                if (sanitized == null)
                {
                    resolveError = "Invalid asset path (contains traversal sequence).";
                    return null;
                }

                var shader = AssetDatabase.LoadAssetAtPath<Shader>(sanitized);
                if (shader == null)
                {
                    resolveError = $"No shader asset found at path '{sanitized}'.";
                    return null;
                }

                resolveError = null;
                return shader;
            }

            string name = p.Get("name");
            if (!string.IsNullOrEmpty(name))
            {
                var shader = Shader.Find(name);
                if (shader == null)
                {
                    resolveError = $"Shader '{name}' not found via Shader.Find(). Check the shader name declaration.";
                    return null;
                }

                resolveError = null;
                return shader;
            }

            resolveError = "Either 'path' or 'name' parameter is required.";
            return null;
        }

        #endregion
    }
}
