using System;
using System.Linq;
using System.Reflection;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace MCPForUnity.Editor.Tools
{
    /// <summary>
    /// First-class MCP tool for SDF graph operations. Works directly with asset paths,
    /// no scene GameObject required. Uses reflection to call into the SDF package
    /// (Utility.SDF.Graph / Utility.SDF.Mermaid) to avoid compile-time coupling.
    ///
    /// Actions:
    ///   export_mermaid — Serialize graph to Mermaid flowchart text
    ///   apply_mermaid  — Build/replace graph from Mermaid text
    ///   get_graph      — Get graph topology (node count, connections, output index)
    ///   compile_hlsl   — Compile graph to HLSL shader code
    /// </summary>
    [McpForUnityTool("manage_sdf_graph",
        Description = "SDF graph operations: export/apply Mermaid notation, inspect topology, compile to HLSL. " +
                      "Works with asset paths directly (e.g., 'Assets/SDF Graphs/MyGraph.asset').",
        Tier = ExecutionTier.Smooth)]
    public static class ManageSDFGraph
    {
        // Cached reflection types and methods
        static Type _graphAssetType;
        static Type _mermaidCodecType;
        static Type _hlslCompilerType;
        static MethodInfo _exportMethod;
        static MethodInfo _parseMethod;
        static MethodInfo _compileMethod;
        static bool _reflectionResolved;
        static string _reflectionError;

        static bool ResolveReflection()
        {
            if (_reflectionResolved) return _reflectionError == null;

            _reflectionResolved = true;

            _graphAssetType = FindType("Utility.SDF.Graph.SDFGraphAsset");
            if (_graphAssetType == null)
            {
                _reflectionError = "SDFGraphAsset type not found. Is the com.utility.sdf package installed?";
                return false;
            }

            _mermaidCodecType = FindType("Utility.SDF.Mermaid.SDFMermaidCodec");
            if (_mermaidCodecType == null)
            {
                _reflectionError = "SDFMermaidCodec type not found. Is the com.utility.sdf.Editor assembly loaded?";
                return false;
            }

            // Export has optional FlowDirection param — find by name only
            var exportMethods = _mermaidCodecType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(m => m.Name == "Export").ToArray();
            _exportMethod = exportMethods.Length > 0 ? exportMethods[0] : null;

            _parseMethod = _mermaidCodecType.GetMethod("Parse",
                BindingFlags.Public | BindingFlags.Static,
                null, new[] { typeof(string), _graphAssetType }, null);

            if (_exportMethod == null || _parseMethod == null)
            {
                _reflectionError = "SDFMermaidCodec.Export/Parse methods not found. API may have changed.";
                return false;
            }

            // HLSL compiler is optional
            _hlslCompilerType = FindType("Utility.SDF.Compiler.SDFGraphCompiler");
            if (_hlslCompilerType != null)
            {
                _compileMethod = _hlslCompilerType.GetMethod("CompileToHLSL",
                    BindingFlags.Public | BindingFlags.Static,
                    null, new[] { _graphAssetType }, null);
            }

            return true;
        }

        static Type FindType(string fullName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (asm.IsDynamic) continue;
                try
                {
                    var t = asm.GetType(fullName);
                    if (t != null) return t;
                }
                catch { }
            }
            return null;
        }

        public static object HandleCommand(JObject @params)
        {
            if (@params == null)
                return new ErrorResponse("Parameters required.");

            string action = @params.Value<string>("action");
            if (string.IsNullOrWhiteSpace(action))
                return new ErrorResponse("'action' is required. Options: export_mermaid, apply_mermaid, get_graph, compile_hlsl");

            if (!ResolveReflection())
                return new ErrorResponse(_reflectionError);

            string assetPath = @params.Value<string>("asset_path");
            if (string.IsNullOrWhiteSpace(assetPath))
                return new ErrorResponse("'asset_path' is required (e.g., 'Assets/SDF Graphs/MyGraph.asset').");

            var asset = AssetDatabase.LoadAssetAtPath(assetPath, _graphAssetType);
            if (asset == null)
                return new ErrorResponse($"No SDFGraphAsset found at '{assetPath}'.");

            return action switch
            {
                "export_mermaid" => ExecuteExportMermaid(asset),
                "apply_mermaid" => ExecuteApplyMermaid(asset, @params, assetPath),
                "get_graph" => ExecuteGetGraph(asset),
                "compile_hlsl" => ExecuteCompileHlsl(asset),
                _ => new ErrorResponse($"Unknown action '{action}'. Options: export_mermaid, apply_mermaid, get_graph, compile_hlsl")
            };
        }

        static object ExecuteExportMermaid(UnityEngine.Object asset)
        {
            try
            {
                // Export(SDFGraphAsset, FlowDirection = LR) — pass default for optional param
                var exportParams = _exportMethod.GetParameters();
                var args = exportParams.Length == 1
                    ? new object[] { asset }
                    : new object[] { asset, Type.Missing };
                var mermaid = _exportMethod.Invoke(null, args);
                if (mermaid == null)
                    return new ErrorResponse("Export returned null.");

                return new SuccessResponse("Mermaid exported.", new { mermaid = mermaid.ToString() });
            }
            catch (TargetInvocationException ex)
            {
                return new ErrorResponse($"Export failed: {ex.InnerException?.Message ?? ex.Message}");
            }
        }

        static object ExecuteApplyMermaid(UnityEngine.Object asset, JObject @params, string assetPath)
        {
            string mermaidText = @params.Value<string>("mermaid");
            if (string.IsNullOrWhiteSpace(mermaidText))
                return new ErrorResponse("'mermaid' parameter is required.");

            try
            {
                Undo.RecordObject(asset, "Apply Mermaid Graph");
                var parseResult = _parseMethod.Invoke(null, new object[] { mermaidText, asset });
                EditorUtility.SetDirty(asset);
                AssetDatabase.SaveAssets();

                // Extract result properties via reflection
                var resultType = parseResult.GetType();
                bool success = (bool)(resultType.GetProperty("Success")?.GetValue(parseResult) ?? false);
                int nodeCount = (int)(resultType.GetProperty("NodeCount")?.GetValue(parseResult) ?? 0);
                int connectionCount = (int)(resultType.GetProperty("ConnectionCount")?.GetValue(parseResult) ?? 0);

                var warnings = resultType.GetProperty("Warnings")?.GetValue(parseResult);
                string warningText = warnings != null ? string.Join("; ", (System.Collections.IEnumerable)warnings) : "";

                if (!success)
                    return new ErrorResponse($"Parse failed: {warningText}");

                return new SuccessResponse(
                    $"Applied Mermaid to {assetPath}: {nodeCount} nodes, {connectionCount} connections.",
                    new { nodeCount, connectionCount, warnings = warningText });
            }
            catch (TargetInvocationException ex)
            {
                return new ErrorResponse($"Apply failed: {ex.InnerException?.Message ?? ex.Message}");
            }
        }

        static object ExecuteGetGraph(UnityEngine.Object asset)
        {
            try
            {
                var assetType = asset.GetType();
                int nodeCount = (int)(assetType.GetProperty("NodeCount")?.GetValue(asset) ?? 0);
                int outputIndex = (int)(assetType.GetProperty("OutputNodeIndex")?.GetValue(asset) ?? -1);

                // Get nodes via reflection
                var nodesProperty = assetType.GetProperty("Nodes");
                var nodes = nodesProperty?.GetValue(asset) as System.Collections.IList;

                var nodeInfos = new JArray();
                if (nodes != null)
                {
                    for (int i = 0; i < nodes.Count; i++)
                    {
                        var node = nodes[i];
                        if (node == null)
                        {
                            nodeInfos.Add(new JObject { ["index"] = i, ["type"] = "null" });
                            continue;
                        }

                        var nodeType = node.GetType();
                        var info = new JObject
                        {
                            ["index"] = i,
                            ["type"] = nodeType.Name
                        };

                        // Try to get display name
                        var nameField = nodeType.GetField("displayName",
                            BindingFlags.Public | BindingFlags.Instance);
                        if (nameField != null)
                            info["name"] = nameField.GetValue(node)?.ToString();

                        nodeInfos.Add(info);
                    }
                }

                return new SuccessResponse($"Graph has {nodeCount} nodes.",
                    new { nodeCount, outputIndex, nodes = nodeInfos });
            }
            catch (Exception ex)
            {
                return new ErrorResponse($"Get graph failed: {ex.Message}");
            }
        }

        static object ExecuteCompileHlsl(UnityEngine.Object asset)
        {
            if (_compileMethod == null)
                return new ErrorResponse("HLSL compiler not found. SDFGraphCompiler may not be available.");

            try
            {
                var hlsl = _compileMethod.Invoke(null, new object[] { asset });
                if (hlsl == null)
                    return new ErrorResponse("Compilation returned null.");

                return new SuccessResponse("Compiled to HLSL.", new { hlsl = hlsl.ToString() });
            }
            catch (TargetInvocationException ex)
            {
                return new ErrorResponse($"Compilation failed: {ex.InnerException?.Message ?? ex.Message}");
            }
        }
    }
}
