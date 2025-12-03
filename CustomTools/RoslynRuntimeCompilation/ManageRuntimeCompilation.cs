#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEditor;
using MCPForUnity.Editor.Helpers;

#if USE_ROSLYN
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
#endif

namespace MCPForUnity.Editor.Tools
{
    /// <summary>
    /// Runtime compilation tool for MCP Unity.
    /// Compiles and loads C# code at runtime without triggering domain reload.
    /// </summary>
    [McpForUnityTool("runtime_compilation")]
    public static class ManageRuntimeCompilation
    {
        private static readonly Dictionary<string, LoadedAssemblyInfo> LoadedAssemblies = new Dictionary<string, LoadedAssemblyInfo>();
        private static string DynamicAssembliesPath => Path.Combine(Application.temporaryCachePath, "DynamicAssemblies");
        
        private class LoadedAssemblyInfo
        {
            public string Name;
            public Assembly Assembly;
            public string DllPath;
            public DateTime LoadedAt;
            public List<string> TypeNames;
        }
        
        public static object HandleCommand(JObject @params)
        {
            string action = @params["action"]?.ToString()?.ToLower();
            
            if (string.IsNullOrEmpty(action))
            {
                return Response.Error("Action parameter is required. Valid actions: compile_and_load, list_loaded, get_types, execute_with_roslyn, get_history, save_history, clear_history");
            }
            
            switch (action)
            {
                case "compile_and_load":
                    return CompileAndLoad(@params);
                
                case "list_loaded":
                    return ListLoadedAssemblies();
                
                case "get_types":
                    return GetAssemblyTypes(@params);
                
                case "execute_with_roslyn":
                    return ExecuteWithRoslyn(@params);
                
                case "get_history":
                    return GetCompilationHistory();
                
                case "save_history":
                    return SaveCompilationHistory();
                
                case "clear_history":
                    return ClearCompilationHistory();
                
                default:
                    return Response.Error($"Unknown action '{action}'. Valid actions: compile_and_load, list_loaded, get_types, execute_with_roslyn, get_history, save_history, clear_history");
            }
        }
        
        private static object CompileAndLoad(JObject @params)
        {
#if !USE_ROSLYN
            return Response.Error(
                "Runtime compilation requires Roslyn. Please install Microsoft.CodeAnalysis.CSharp NuGet package and add USE_ROSLYN to Scripting Define Symbols. " +
                "See ManageScript.cs header for installation instructions."
            );
#else
            try
            {
                string code = @params["code"]?.ToString();
                var assemblyToken = @params["assembly_name"];
                string assemblyName = assemblyToken == null || string.IsNullOrWhiteSpace(assemblyToken.ToString())
                    ? $"DynamicAssembly_{DateTime.Now.Ticks}"
                    : assemblyToken.ToString().Trim();
                string attachTo = @params["attach_to"]?.ToString();
                bool loadImmediately = @params["load_immediately"]?.ToObject<bool>() ?? true;
                
                if (string.IsNullOrEmpty(code))
                {
                    return Response.Error("'code' parameter is required");
                }
                
                // Ensure unique assembly name
                if (LoadedAssemblies.ContainsKey(assemblyName))
                {
                    assemblyName = $"{assemblyName}_{DateTime.Now.Ticks}";
                }
                
                // Create output directory
                Directory.CreateDirectory(DynamicAssembliesPath);
                string basePath = Path.GetFullPath(DynamicAssembliesPath);
                Directory.CreateDirectory(basePath);
                string safeFileName = SanitizeAssemblyFileName(assemblyName);
                string dllPath = Path.GetFullPath(Path.Combine(basePath, $"{safeFileName}.dll"));

                if (!dllPath.StartsWith(basePath, StringComparison.Ordinal))
                {
                    return Response.Error("Assembly name must resolve inside the dynamic assemblies directory.");
                }

                if (File.Exists(dllPath))
                {
                    dllPath = Path.GetFullPath(Path.Combine(basePath, $"{safeFileName}_{DateTime.Now.Ticks}.dll"));
                }

                // Parse code
                var syntaxTree = CSharpSyntaxTree.ParseText(code);
                
                // Get references
                var references = GetDefaultReferences();
                
                // Create compilation
                var compilation = CSharpCompilation.Create(
                    assemblyName,
                    new[] { syntaxTree },
                    references,
                    new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                        .WithOptimizationLevel(OptimizationLevel.Debug)
                        .WithPlatform(Platform.AnyCpu)
                );
                
                // Emit to file
                EmitResult emitResult;
                using (var stream = new FileStream(dllPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    emitResult = compilation.Emit(stream);
                }
                
                // Check for compilation errors
                if (!emitResult.Success)
                {
                    var errors = emitResult.Diagnostics
                        .Where(d => d.Severity == DiagnosticSeverity.Error)
                        .Select(d => new
                        {
                            line = d.Location.GetLineSpan().StartLinePosition.Line + 1,
                            column = d.Location.GetLineSpan().StartLinePosition.Character + 1,
                            message = d.GetMessage(),
                            id = d.Id
                        })
                        .ToList();
                    
                    return Response.Error("Compilation failed", new
                    {
                        errors = errors,
                        error_count = errors.Count
                    });
                }
                
                // Load assembly if requested
                Assembly loadedAssembly = null;
                List<string> typeNames = new List<string>();
                
                if (loadImmediately)
                {
                    loadedAssembly = Assembly.LoadFrom(dllPath);
                    typeNames = loadedAssembly.GetTypes().Select(t => t.FullName).ToList();
                    
                    // Store info
                    LoadedAssemblies[assemblyName] = new LoadedAssemblyInfo
                    {
                        Name = assemblyName,
                        Assembly = loadedAssembly,
                        DllPath = dllPath,
                        LoadedAt = DateTime.Now,
                        TypeNames = typeNames
                    };
                    
                    Debug.Log($"[MCP] Runtime compilation successful: {assemblyName} ({typeNames.Count} types)");
                }
                
                // Optionally attach to GameObject
                GameObject attachedTo = null;
                Type attachedType = null;
                
                if (!string.IsNullOrEmpty(attachTo) && loadedAssembly != null)
                {
                    var go = GameObject.Find(attachTo);
                    if (go == null)
                    {
                        // Try hierarchical path search
                        go = FindGameObjectByPath(attachTo);
                    }
                    
                    if (go != null)
                    {
                        // Find first MonoBehaviour type
                        var behaviourType = loadedAssembly.GetTypes()
                            .FirstOrDefault(t => t.IsSubclassOf(typeof(MonoBehaviour)) && !t.IsAbstract);
                        
                        if (behaviourType != null)
                        {
                            go.AddComponent(behaviourType);
                            attachedTo = go;
                            attachedType = behaviourType;
                            Debug.Log($"[MCP] Attached {behaviourType.Name} to {go.name}");
                        }
                        else
                        {
                            Debug.LogWarning($"[MCP] No MonoBehaviour types found in {assemblyName} to attach");
                        }
                    }
                    else
                    {
                        Debug.LogWarning($"[MCP] GameObject '{attachTo}' not found");
                    }
                }
                
                return Response.Success("Runtime compilation completed successfully", new
                {
                    assembly_name = assemblyName,
                    dll_path = dllPath,
                    loaded = loadImmediately,
                    type_count = typeNames.Count,
                    types = typeNames,
                    attached_to = attachedTo != null ? attachedTo.name : null,
                    attached_type = attachedType != null ? attachedType.FullName : null
                });
            }
            catch (Exception ex)
            {
                return Response.Error($"Runtime compilation failed: {ex.Message}", new
                {
                    exception = ex.GetType().Name,
                    stack_trace = ex.StackTrace
                });
            }
#endif
        }

        private static object ListLoadedAssemblies()
        {
            var assemblies = LoadedAssemblies.Values.Select(info => new
            {
                name = info.Name,
                dll_path = info.DllPath,
                loaded_at = info.LoadedAt.ToString("o"),
                type_count = info.TypeNames.Count,
                types = info.TypeNames
            }).ToList();

            return Response.Success($"Found {assemblies.Count} loaded dynamic assemblies", new
            {
                count = assemblies.Count,
                assemblies = assemblies
            });
        }
        
        private static string SanitizeAssemblyFileName(string assemblyName)
        {
            var invalidChars = Path.GetInvalidFileNameChars();
            var sanitized = new string(assemblyName.Where(c => !invalidChars.Contains(c)).ToArray());
            return string.IsNullOrWhiteSpace(sanitized) ? $"DynamicAssembly_{DateTime.Now.Ticks}" : sanitized;
        }

        private static object GetAssemblyTypes(JObject @params)
        {
            string assemblyName = @params["assembly_name"]?.ToString();
            
            if (string.IsNullOrEmpty(assemblyName))
            {
                return Response.Error("'assembly_name' parameter is required");
            }
            
            if (!LoadedAssemblies.TryGetValue(assemblyName, out var info))
            {
                return Response.Error($"Assembly '{assemblyName}' not found in loaded assemblies");
            }
            
            var types = info.Assembly.GetTypes().Select(t => new
            {
                full_name = t.FullName,
                name = t.Name,
                @namespace = t.Namespace,
                is_class = t.IsClass,
                is_abstract = t.IsAbstract,
                is_monobehaviour = t.IsSubclassOf(typeof(MonoBehaviour)),
                base_type = t.BaseType?.FullName
            }).ToList();
            
            return Response.Success($"Retrieved {types.Count} types from {assemblyName}", new
            {
                assembly_name = assemblyName,
                type_count = types.Count,
                types = types
            });
        }
        
        /// <summary>
        /// Execute code using RoslynRuntimeCompiler with full GUI tool integration
        /// Supports MonoBehaviours, static methods, and coroutines
        /// </summary>
        private static object ExecuteWithRoslyn(JObject @params)
        {
            try
            {
                string code = @params["code"]?.ToString();
                string className = @params["class_name"]?.ToString() ?? "AIGenerated";
                string methodName = @params["method_name"]?.ToString() ?? "Run";
                string targetObjectName = @params["target_object"]?.ToString();
                bool attachAsComponent = @params["attach_as_component"]?.ToObject<bool>() ?? false;
                
                if (string.IsNullOrEmpty(code))
                {
                    return Response.Error("'code' parameter is required");
                }
                
                // Get or create the RoslynRuntimeCompiler instance
                var compiler = GetOrCreateRoslynCompiler();
                
                // Find target GameObject if specified
                GameObject targetObject = null;
                if (!string.IsNullOrEmpty(targetObjectName))
                {
                    targetObject = GameObject.Find(targetObjectName);
                    if (targetObject == null)
                    {
                        targetObject = FindGameObjectByPath(targetObjectName);
                    }
                    
                    if (targetObject == null)
                    {
                        return Response.Error($"Target GameObject '{targetObjectName}' not found");
                    }
                }
                
                // Use the RoslynRuntimeCompiler's CompileAndExecute method
                bool success = compiler.CompileAndExecute(
                    code,
                    className,
                    methodName,
                    targetObject,
                    attachAsComponent,
                    out string errorMessage
                );
                
                if (success)
                {
                    return Response.Success($"Code compiled and executed successfully", new
                    {
                        class_name = className,
                        method_name = methodName,
                        target_object = targetObject != null ? targetObject.name : "compiler_host",
                        attached_as_component = attachAsComponent,
                        diagnostics = compiler.lastCompileDiagnostics
                    });
                }
                else
                {
                    return Response.Error($"Execution failed: {errorMessage}", new
                    {
                        diagnostics = compiler.lastCompileDiagnostics
                    });
                }
            }
            catch (Exception ex)
            {
                return Response.Error($"Failed to execute with Roslyn: {ex.Message}", new
                {
                    exception = ex.GetType().Name,
                    stack_trace = ex.StackTrace
                });
            }
        }
        
        /// <summary>
        /// Get compilation history from RoslynRuntimeCompiler
        /// </summary>
        private static object GetCompilationHistory()
        {
            try
            {
                var compiler = GetOrCreateRoslynCompiler();
                var history = compiler.CompilationHistory;
                
                var historyData = history.Select(entry => new
                {
                    timestamp = entry.timestamp,
                    type_name = entry.typeName,
                    method_name = entry.methodName,
                    success = entry.success,
                    diagnostics = entry.diagnostics,
                    execution_target = entry.executionTarget,
                    source_code_preview = entry.sourceCode.Length > 200 
                        ? entry.sourceCode.Substring(0, 200) + "..." 
                        : entry.sourceCode
                }).ToList();
                
                return Response.Success($"Retrieved {historyData.Count} history entries", new
                {
                    count = historyData.Count,
                    history = historyData
                });
            }
            catch (Exception ex)
            {
                return Response.Error($"Failed to get history: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Save compilation history to JSON file
        /// </summary>
        private static object SaveCompilationHistory()
        {
            try
            {
                var compiler = GetOrCreateRoslynCompiler();
                
                if (compiler.SaveHistoryToFile(out string savedPath, out string error))
                {
                    return Response.Success($"History saved successfully", new
                    {
                        path = savedPath,
                        entry_count = compiler.CompilationHistory.Count
                    });
                }
                else
                {
                    return Response.Error($"Failed to save history: {error}");
                }
            }
            catch (Exception ex)
            {
                return Response.Error($"Failed to save history: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Clear compilation history
        /// </summary>
        private static object ClearCompilationHistory()
        {
            try
            {
                var compiler = GetOrCreateRoslynCompiler();
                int count = compiler.CompilationHistory.Count;
                compiler.ClearHistory();
                
                return Response.Success($"Cleared {count} history entries");
            }
            catch (Exception ex)
            {
                return Response.Error($"Failed to clear history: {ex.Message}");
            }
        }
        
#if USE_ROSLYN
        private static List<MetadataReference> GetDefaultReferences()
        {
            var references = new List<MetadataReference>();
            
            // Add core .NET references
            references.Add(MetadataReference.CreateFromFile(typeof(object).Assembly.Location));
            references.Add(MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location));
            
            // Add Unity references
            var unityEngine = typeof(UnityEngine.Object).Assembly.Location;
            references.Add(MetadataReference.CreateFromFile(unityEngine));
            
            // Add UnityEditor if available
            try
            {
                var unityEditor = typeof(UnityEditor.Editor).Assembly.Location;
                references.Add(MetadataReference.CreateFromFile(unityEditor));
            }
            catch { /* Editor assembly not always needed */ }
            
            // Add Assembly-CSharp (user scripts)
            try
            {
                var assemblyCSharp = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name == "Assembly-CSharp");
                if (assemblyCSharp != null)
                {
                    references.Add(MetadataReference.CreateFromFile(assemblyCSharp.Location));
                }
            }
            catch { /* User assembly not always needed */ }
            
            return references;
        }
#endif
        
        private static GameObject FindGameObjectByPath(string path)
        {
            // Handle hierarchical paths like "Canvas/Panel/Button"
            var parts = path.Split('/');
            GameObject current = null;
            
            foreach (var part in parts)
            {
                if (current == null)
                {
                    // Find root object
                    current = GameObject.Find(part);
                }
                else
                {
                    // Find child
                    var transform = current.transform.Find(part);
                    if (transform == null)
                        return null;
                    current = transform.gameObject;
                }
            }
            
            return current;
        }

        /// <summary>
        /// Get or create a RoslynRuntimeCompiler instance for GUI integration
        /// This allows MCP commands to leverage the existing GUI tool
        /// </summary>
        private static RoslynRuntimeCompiler GetOrCreateRoslynCompiler()
        {
            var existing = UnityEngine.Object.FindFirstObjectByType<RoslynRuntimeCompiler>();
            if (existing != null)
            {
                return existing;
            }
            
            var go = new GameObject("MCPRoslynCompiler");
            var compiler = go.AddComponent<RoslynRuntimeCompiler>();
            compiler.enableHistory = true; // Enable history tracking for MCP operations
            if (!Application.isPlaying)
            {
                go.hideFlags = HideFlags.HideAndDontSave;
            }
            return compiler;
        }
    }
}
