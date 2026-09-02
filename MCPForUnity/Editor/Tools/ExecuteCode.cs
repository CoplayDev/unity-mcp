using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Runtime.Helpers;
using Microsoft.CSharp;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace MCPForUnity.Editor.Tools
{
    [McpForUnityTool("execute_code", AutoRegister = false, Group = "scripting_ext")]
    public static class ExecuteCode
    {
        private const int MaxCodeLength = 50000;
        private const int MaxHistoryEntries = 50;
        private const int MaxHistoryCodePreview = 500;
        private const string WrapperClassName = "MCPDynamicCode";
        private const string WrapperMethodName = "Execute";

        private const string ActionExecute = "execute";
        private const string ActionGetHistory = "get_history";
        private const string ActionClearHistory = "clear_history";
        private const string ActionReplay = "replay";

        private static readonly List<HistoryEntry> _history = new List<HistoryEntry>();
        private static string[] _cachedAssemblyPaths;
        private static string[] _cachedCodeDomAssemblyPaths;

        // Every compile emits a fresh in-memory "MCPDynamic" assembly, and Mono cannot unload
        // one, so recompiling an identical snippet leaks an assembly per call until the next
        // domain reload (see issue #1351). Cache the compiled output keyed on the wrapped
        // source so repeated calls reuse one assembly.
        private const int MaxCompiledCacheEntries = 64;
        private static readonly Dictionary<string, CompiledSnippet> _compiledCache =
            new Dictionary<string, CompiledSnippet>(StringComparer.Ordinal);

        private readonly struct CompiledSnippet
        {
            public CompiledSnippet(Assembly assembly, string compiler)
            {
                Assembly = assembly;
                Compiler = compiler;
            }

            public Assembly Assembly { get; }
            public string Compiler { get; }
        }

        [UnityEditor.InitializeOnLoadMethod]
        private static void OnDomainReload()
        {
            _cachedAssemblyPaths = null;
            _cachedCodeDomAssemblyPaths = null;
            _compiledCache.Clear();
            RoslynCompiler.ResetCache();
        }

        private static readonly HashSet<string> _blockedPatterns = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "System.IO.File.Delete",
            "System.IO.Directory.Delete",
            "FileUtil.DeleteFileOrDirectory",
            "AssetDatabase.DeleteAsset",
            "AssetDatabase.MoveAssetToTrash",
            "EditorApplication.Exit",
            "Process.Start",
            "Process.Kill",
            "while(true)",
            "while (true)",
            "for(;;)",
            "for (;;)",
        };

        public static object HandleCommand(JObject @params)
        {
            if (@params == null)
                return new ErrorResponse("Parameters cannot be null.");

            var p = new ToolParams(@params);
            var actionResult = p.GetRequired("action");
            if (!actionResult.IsSuccess)
                return new ErrorResponse(actionResult.ErrorMessage);

            string action = actionResult.Value.ToLowerInvariant();

            switch (action)
            {
                case ActionExecute:
                    return HandleExecute(@params);
                case ActionGetHistory:
                    return HandleGetHistory(@params);
                case ActionClearHistory:
                    return HandleClearHistory();
                case ActionReplay:
                    return HandleReplay(@params);
                default:
                    return new ErrorResponse(
                        $"Unknown action: '{action}'. Valid actions: {ActionExecute}, {ActionGetHistory}, {ActionClearHistory}, {ActionReplay}");
            }
        }

        private static object HandleExecute(JObject @params)
        {
            string code = @params["code"]?.ToString();
            if (string.IsNullOrWhiteSpace(code))
                return new ErrorResponse("Required parameter 'code' is missing or empty.");

            if (code.Length > MaxCodeLength)
                return new ErrorResponse($"Code exceeds maximum length of {MaxCodeLength} characters.");

            bool safetyChecks = @params["safety_checks"]?.Value<bool>() ?? true;
            string compiler = @params["compiler"]?.ToString()?.ToLowerInvariant() ?? "auto";

            if (safetyChecks)
            {
                var violation = CheckBlockedPatterns(code);
                if (violation != null)
                    return new ErrorResponse($"Blocked pattern detected: {violation}");
            }

            try
            {
                var startTime = DateTime.UtcNow;
                var result = CompileAndExecute(code, compiler);
                var elapsed = (DateTime.UtcNow - startTime).TotalMilliseconds;

                AddToHistory(code, result, elapsed, safetyChecks, compiler);
                return result;
            }
            catch (Exception e)
            {
                McpLog.Error($"[ExecuteCode] Execution failed: {e}");
                var errorResult = new ErrorResponse($"Execution failed: {e.Message}");
                AddToHistory(code, errorResult, 0, safetyChecks, compiler);
                return errorResult;
            }
        }

        private static object HandleGetHistory(JObject @params)
        {
            int limit = @params["limit"]?.Value<int>() ?? 10;
            limit = Math.Clamp(limit, 1, MaxHistoryEntries);

            if (_history.Count == 0)
                return new SuccessResponse("No execution history.", new { total = 0, entries = new object[0] });

            var entries = _history.Skip(Math.Max(0, _history.Count - limit)).ToList();
            return new SuccessResponse($"Returning {entries.Count} of {_history.Count} history entries.", new
            {
                total = _history.Count,
                entries = entries.Select((e, i) => new
                {
                    index = _history.Count - entries.Count + i,
                    codePreview = e.code.Length > MaxHistoryCodePreview
                        ? e.code.Substring(0, MaxHistoryCodePreview) + "..."
                        : e.code,
                    e.success,
                    e.resultPreview,
                    e.elapsedMs,
                    e.timestamp,
                    e.safetyChecksEnabled,
                    e.compiler,
                }).ToList(),
            });
        }

        private static object HandleClearHistory()
        {
            int count = _history.Count;
            _history.Clear();
            return new SuccessResponse($"Cleared {count} history entries.");
        }

        private static object HandleReplay(JObject @params)
        {
            if (_history.Count == 0)
                return new ErrorResponse("No execution history to replay.");

            int? index = @params["index"]?.Value<int>();
            if (index == null || index < 0 || index >= _history.Count)
                return new ErrorResponse($"Invalid history index. Valid range: 0-{_history.Count - 1}");

            var entry = _history[index.Value];
            var replayParams = JObject.FromObject(new
            {
                action = ActionExecute,
                code = entry.code,
                safety_checks = entry.safetyChecksEnabled,
                compiler = entry.compiler ?? "auto",
            });
            return HandleExecute(replayParams);
        }

        // ──────────────────── Compilation ────────────────────

        private static object CompileAndExecute(string code, string compiler)
        {
            string wrappedSource = WrapUserCode(code, out int lineOffset);

            string cacheKey = compiler + "\n" + wrappedSource;
            if (_compiledCache.TryGetValue(cacheKey, out CompiledSnippet cached))
                return InvokeCompiled(cached.Assembly, cached.Compiler);

            string[] assemblyPaths = GetAssemblyPaths();

            Assembly compiled;
            string usedCompiler;

            switch (compiler)
            {
                case "roslyn":
                    if (!RoslynCompiler.IsAvailable)
                        return new ErrorResponse("Roslyn (Microsoft.CodeAnalysis) is not available. Install it via NuGet or use compiler='codedom'.");
                    compiled = RoslynCompiler.Compile(wrappedSource, assemblyPaths, lineOffset, out var roslynErrors);
                    if (compiled == null)
                        return new ErrorResponse("Compilation failed", new { errors = OffsetErrors(roslynErrors), compiler = "roslyn" });
                    usedCompiler = "roslyn";
                    break;

                case "codedom":
                    compiled = CodeDomCompile(wrappedSource, assemblyPaths, lineOffset, out var codedomErrors);
                    if (compiled == null)
                        return new ErrorResponse("Compilation failed", new { errors = OffsetErrors(codedomErrors), compiler = "codedom" });
                    usedCompiler = "codedom";
                    break;

                default: // "auto"
                    if (RoslynCompiler.IsAvailable)
                    {
                        compiled = RoslynCompiler.Compile(wrappedSource, assemblyPaths, lineOffset, out var autoErrors);
                        if (compiled == null)
                            return new ErrorResponse("Compilation failed", new { errors = OffsetErrors(autoErrors), compiler = "roslyn" });
                        usedCompiler = "roslyn";
                    }
                    else
                    {
                        compiled = CodeDomCompile(wrappedSource, assemblyPaths, lineOffset, out var autoFallbackErrors);
                        if (compiled == null)
                            return new ErrorResponse("Compilation failed", new { errors = OffsetErrors(autoFallbackErrors), compiler = "codedom" });
                        usedCompiler = "codedom";
                    }
                    break;
            }

            // Bound the cache so a stream of genuinely distinct snippets cannot itself become
            // the leak. Clearing wholesale is enough: the entries are only a compile shortcut.
            if (_compiledCache.Count >= MaxCompiledCacheEntries)
                _compiledCache.Clear();
            _compiledCache[cacheKey] = new CompiledSnippet(compiled, usedCompiler);

            return InvokeCompiled(compiled, usedCompiler);
        }

        private static object InvokeCompiled(Assembly assembly, string compilerUsed)
        {
            var type = assembly.GetType(WrapperClassName);
            if (type == null)
                return new ErrorResponse("Internal error: failed to find compiled type.");

            var method = type.GetMethod(WrapperMethodName, BindingFlags.Public | BindingFlags.Static);
            if (method == null)
                return new ErrorResponse("Internal error: failed to find Execute method.");

            object result = null;
            Exception executionError = null;

            try
            {
                result = method.Invoke(null, null);
            }
            catch (TargetInvocationException tie)
            {
                executionError = tie.InnerException ?? tie;
            }
            catch (Exception e)
            {
                executionError = e;
            }

            if (executionError != null)
                return new ErrorResponse($"Runtime error: {executionError.Message}",
                    new { exceptionType = executionError.GetType().Name, stackTrace = executionError.StackTrace, compiler = compilerUsed });

            if (result != null)
                return new SuccessResponse("Code executed successfully.",
                    new { result = SerializeResult(result), compiler = compilerUsed });

            return new SuccessResponse("Code executed successfully.", new { compiler = compilerUsed });
        }

        private static List<string> OffsetErrors(List<string> errors)
        {
            // Errors already have line numbers adjusted by the compiler-specific code
            return errors;
        }

        // ──────────────────── CodeDom compiler ────────────────────

        private static Assembly CodeDomCompile(string source, string[] assemblyPaths, int lineOffset, out List<string> errors)
        {
            errors = new List<string>();

            // CodeDom needs the netstandard-aware filtered paths
            var filtered = FilterAssemblyPathsForCodeDom(assemblyPaths);

            // CSharpCodeProvider turns every ReferencedAssemblies entry into a literal /r:"..." flag
            // on the csc command line. Projects with ~100+ asmdefs blow past Windows' 32 KB
            // CreateProcess argument limit and fail with "The filename or extension is too long."
            // Route references through a response file (@responsefile is supported by both mcs and
            // Roslyn csc) so we pass exactly one short argument regardless of reference count.
            string responseFilePath = Path.Combine(Path.GetTempPath(), $"mcp-codedom-{Guid.NewGuid():N}.rsp");

            try
            {
                using (var writer = new StreamWriter(responseFilePath, append: false, Encoding.UTF8))
                {
                    foreach (var path in filtered)
                    {
                        writer.Write("/r:\"");
                        writer.Write(path);
                        writer.WriteLine("\"");
                    }
                }

                // Compile to a controlled DLL path instead of in-memory. mcs prints a stray BOM line on
                // stdout that Mono's CodeDom can't parse, so it fabricates a bogus error (no error number,
                // text is just the BOM) and refuses to surface the assembly — even though mcs exits 0 and
                // wrote the DLL. We skip that bogus error and Assembly.Load the produced DLL ourselves.
                string outputAssemblyPath = Path.Combine(Path.GetTempPath(), $"mcp-codedom-{Guid.NewGuid():N}.dll");
                using (var provider = new CSharpCodeProvider())
                {
                    var parameters = new CompilerParameters
                    {
                        GenerateInMemory = false,
                        OutputAssembly = outputAssemblyPath,
                        GenerateExecutable = false,
                        TreatWarningsAsErrors = false,
                        CompilerOptions = "@\"" + responseFilePath + "\"",
                    };

                    try
                    {
                        var results = provider.CompileAssemblyFromSource(parameters, source);

                        bool hasRealErrors = false;
                        foreach (CompilerError error in results.Errors)
                        {
                            if (error.IsWarning)
                                continue;

                            // The bogus BOM "error": no error number, text is just the BOM/whitespace.
                            string text = (error.ErrorText ?? "").Trim('\uFEFF', ' ', '\t', '\r', '\n');
                            if (string.IsNullOrEmpty(error.ErrorNumber) && string.IsNullOrEmpty(text))
                                continue;

                            hasRealErrors = true;
                            int userLine = Math.Max(1, error.Line - lineOffset);
                            errors.Add($"Line {userLine}: {error.ErrorText}");
                        }

                        if (hasRealErrors)
                            return null;

                        if (!File.Exists(outputAssemblyPath))
                        {
                            errors.Add("CodeDom reported success but produced no assembly.");
                            return null;
                        }

                        return Assembly.Load(File.ReadAllBytes(outputAssemblyPath));
                    }
                    finally
                    {
                        try { if (File.Exists(outputAssemblyPath)) File.Delete(outputAssemblyPath); }
                        catch { /* best effort cleanup */ }
                    }
                }
            }
            finally
            {
                try { if (File.Exists(responseFilePath)) File.Delete(responseFilePath); }
                catch { /* best effort */ }
            }
        }

        // CSharpCodeProvider can't resolve type-forwarding, so when netstandard.dll is loaded
        // alongside mscorlib/System.Runtime/System.Collections, types like List<T> appear in
        // multiple assemblies causing "type defined multiple times" errors.
        private static readonly HashSet<string> _codedomDuplicateAssemblies = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "mscorlib",
            "System.Runtime",
            "System.Private.CoreLib",
            "System.Collections",
        };

        internal static string[] FilterAssemblyPathsForCodeDom(string[] allPaths)
        {
            var useCache = ReferenceEquals(allPaths, _cachedAssemblyPaths);
            if (useCache && _cachedCodeDomAssemblyPaths != null)
                return _cachedCodeDomAssemblyPaths;

            var hasNetstandard = allPaths.Any(p =>
                string.Equals(Path.GetFileNameWithoutExtension(p), "netstandard", StringComparison.OrdinalIgnoreCase));

            var filtered = hasNetstandard
                ? allPaths.Where(p =>
                    !_codedomDuplicateAssemblies.Contains(Path.GetFileNameWithoutExtension(p))).ToArray()
                : allPaths;

            var result = DeduplicateAssemblyPathsForCodeDom(filtered);
            if (useCache)
                _cachedCodeDomAssemblyPaths = result;
            return result;
        }

        private static string[] DeduplicateAssemblyPathsForCodeDom(string[] paths)
        {
            var candidates = new List<CodeDomAssemblyCandidate>();
            var unresolvedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var path in paths)
            {
                try
                {
                    candidates.Add(new CodeDomAssemblyCandidate(path, AssemblyName.GetAssemblyName(path)));
                }
                catch
                {
                    unresolvedPaths.Add(path);
                }
            }

            var groups = candidates
                .GroupBy(candidate => candidate.AssemblyName.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (groups.All(group => group.Count() == 1))
                return paths;

            var referenceCounts = GetLoadedAssemblyReferenceCounts();
            var selectedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var group in groups)
            {
                var selected = group
                    .OrderByDescending(candidate => GetReferenceCount(referenceCounts, candidate.AssemblyName.FullName))
                    .ThenByDescending(candidate => candidate.AssemblyName.Version)
                    .ThenBy(candidate => candidate.Path, StringComparer.OrdinalIgnoreCase)
                    .First();
                selectedPaths.Add(selected.Path);
            }

            return paths.Where(path => unresolvedPaths.Contains(path) || selectedPaths.Contains(path)).ToArray();
        }

        private static Dictionary<string, int> GetLoadedAssemblyReferenceCounts()
        {
            var referenceCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var assembly in UnityAssembliesCompat.GetLoadedAssemblies())
            {
                if (assembly.IsDynamic) continue;

                AssemblyName[] referencedAssemblies;
                try
                {
                    referencedAssemblies = assembly.GetReferencedAssemblies();
                }
                catch (NotSupportedException)
                {
                    continue;
                }

                foreach (var referencedAssembly in referencedAssemblies)
                {
                    var fullName = referencedAssembly.FullName;
                    referenceCounts.TryGetValue(fullName, out var count);
                    referenceCounts[fullName] = count + 1;
                }
            }

            return referenceCounts;
        }

        private static int GetReferenceCount(Dictionary<string, int> referenceCounts, string fullName)
        {
            return referenceCounts.TryGetValue(fullName, out var count) ? count : 0;
        }

        private sealed class CodeDomAssemblyCandidate
        {
            public CodeDomAssemblyCandidate(string path, AssemblyName assemblyName)
            {
                Path = path;
                AssemblyName = assemblyName;
            }

            public string Path { get; }
            public AssemblyName AssemblyName { get; }
        }

        // ──────────────────── Shared helpers ────────────────────

        private static readonly string[] BuiltinUsings =
        {
            "using System;",
            "using System.Collections.Generic;",
            "using System.Linq;",
            "using System.Reflection;",
            "using System.Text;",
            "using UnityEngine;",
            "using UnityEditor;",
        };

        private static readonly Regex UsingDirectiveLine = new Regex(
            @"^\s*using\s+(?:static\s+)?[A-Za-z_]\w*(?:\s*\.\s*[A-Za-z_]\w*)*\s*;\s*$", RegexOptions.Compiled);

        // Must not match the C# 8 `using var x = ...` declaration, which has two tokens before the =
        private static readonly Regex UsingAliasLine = new Regex(
            @"^\s*using\s+[A-Za-z_]\w*\s*=\s*[A-Za-z_][\w.]*\s*(?:<[^;]*>)?\s*(?:\[\s*\])*\s*;\s*$", RegexOptions.Compiled);

        private static bool IsUsingDirective(string line)
            => UsingDirectiveLine.IsMatch(line) || UsingAliasLine.IsMatch(line);

        // A directive can carry comments, but the regexes above must see a bare `using ...;`
        private static string StripComments(string line, ref bool inBlock)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < line.Length; i++)
            {
                bool pair = i + 1 < line.Length;
                if (inBlock)
                {
                    if (pair && line[i] == '*' && line[i + 1] == '/') { inBlock = false; i++; }
                    continue;
                }
                if (pair && line[i] == '/' && line[i + 1] == '*') { inBlock = true; i++; continue; }
                if (pair && line[i] == '/' && line[i + 1] == '/') break;
                sb.Append(line[i]);
            }
            return sb.ToString();
        }

        // Blanked, not removed, so compiler line numbers still map to the caller's
        private static List<string> HoistLeadingUsings(string[] body)
        {
            var hoisted = new List<string>();
            bool inBlock = false;
            for (int i = 0; i < body.Length; i++)
            {
                string directive = StripComments(body[i], ref inBlock).Trim();
                if (directive.Length == 0) continue;
                if (!IsUsingDirective(directive)) break;
                if (!BuiltinUsings.Contains(directive) && !hoisted.Contains(directive)) hoisted.Add(directive);
                body[i] = string.Empty;
            }
            return hoisted;
        }

        // A using directive is illegal in a method body, so the parser reads it as a using-statement and wants a '('
        private static string WrapUserCode(string code, out int lineOffset)
        {
            var body = code.Replace("\r\n", "\n").Split('\n');
            var hoisted = HoistLeadingUsings(body);

            var sb = new StringBuilder();
            foreach (string u in BuiltinUsings) sb.AppendLine(u);
            foreach (string u in hoisted) sb.AppendLine(u);
            sb.AppendLine($"public static class {WrapperClassName}");
            sb.AppendLine("{");
            sb.AppendLine($"    public static object {WrapperMethodName}()");
            sb.AppendLine("    {");
            sb.AppendLine(string.Join("\n", body));
            sb.AppendLine("    }");
            sb.AppendLine("}");
            // 4 = the class line, its brace, the method line, its brace
            lineOffset = BuiltinUsings.Length + hoisted.Count + 4;
            return sb.ToString();
        }

        private static string[] GetAssemblyPaths()
        {
            if (_cachedAssemblyPaths == null)
                _cachedAssemblyPaths = ResolveAssemblyPaths();
            return _cachedAssemblyPaths;
        }

        private static string[] ResolveAssemblyPaths()
        {
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var assembly in UnityAssembliesCompat.GetLoadedAssemblies())
            {
                try
                {
                    if (assembly.IsDynamic) continue;
                    var location = assembly.Location;
                    if (string.IsNullOrEmpty(location)) continue;
                    if (!File.Exists(location)) continue;
                    paths.Add(location);
                }
                catch (NotSupportedException)
                {
                    // Some assemblies don't support Location property
                }
            }

            var result = new string[paths.Count];
            paths.CopyTo(result);
            return result;
        }

        // A hoisted `using System.IO;` puts File.Delete in scope, which no fully-qualified
        // pattern in _blockedPatterns matches. Derive the short forms the directives create.
        private static IEnumerable<string> ShortFormsFromUsings(IEnumerable<string> directives)
        {
            foreach (string directive in directives)
            {
                string target = directive.Substring("using".Length).Trim().TrimEnd(';').Trim();
                bool isStatic = target.StartsWith("static ", StringComparison.Ordinal);
                if (isStatic) target = target.Substring("static ".Length).Trim();

                string alias = null;
                int eq = target.IndexOf('=');
                if (eq >= 0)
                {
                    alias = target.Substring(0, eq).Trim();
                    target = target.Substring(eq + 1).Trim();
                }

                int lastDot = target.LastIndexOf('.');
                string leaf = lastDot >= 0 ? target.Substring(lastDot + 1) : target;

                foreach (string pattern in _blockedPatterns)
                {
                    if (pattern.StartsWith(target + ".", StringComparison.OrdinalIgnoreCase))
                    {
                        string tail = pattern.Substring(target.Length + 1);
                        yield return alias == null ? tail : alias + "." + tail;
                    }
                    // A blocked pattern already written short, like AssetDatabase.DeleteAsset,
                    // needs the leaf instead: `using static ...Process;` makes Process.Start bare Start
                    else if (pattern.StartsWith(leaf + ".", StringComparison.OrdinalIgnoreCase))
                    {
                        string tail = pattern.Substring(leaf.Length + 1);
                        if (isStatic) yield return tail;
                        else if (alias != null) yield return alias + "." + tail;
                    }
                }
            }
        }

        private static string CheckBlockedPatterns(string code)
        {
            var directives = HoistLeadingUsings(code.Replace("\r\n", "\n").Split('\n'));
            foreach (var pattern in _blockedPatterns.Concat(ShortFormsFromUsings(directives)))
            {
                if (code.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0)
                    return $"Code contains blocked pattern: '{pattern}'. Disable safety checks with safety_checks=false if this is intentional.";
            }
            return null;
        }

        private static void AddToHistory(string code, object result, double elapsedMs, bool safetyChecks, string compiler = "auto")
        {
            string preview;
            if (result is SuccessResponse sr)
                preview = sr.Data?.ToString() ?? sr.Message;
            else if (result is ErrorResponse er)
                preview = er.Error;
            else
                preview = result?.ToString() ?? "null";

            if (preview != null && preview.Length > 200)
                preview = preview.Substring(0, 200) + "...";

            _history.Add(new HistoryEntry
            {
                code = code,
                success = result is SuccessResponse,
                resultPreview = preview,
                elapsedMs = Math.Round(elapsedMs, 1),
                timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                safetyChecksEnabled = safetyChecks,
                compiler = compiler,
            });

            while (_history.Count > MaxHistoryEntries)
                _history.RemoveAt(0);
        }

        private static object SerializeResult(object result)
        {
            if (result == null) return null;

            var type = result.GetType();
            if (type.IsPrimitive || result is string || result is decimal)
                return result;

            try
            {
                return JToken.FromObject(result);
            }
            catch
            {
                return result.ToString();
            }
        }

        private class HistoryEntry
        {
            public string code;
            public bool success;
            public string resultPreview;
            public double elapsedMs;
            public string timestamp;
            public bool safetyChecksEnabled;
            public string compiler;
        }
    }

    /// <summary>
    /// Roslyn compiler backend accessed entirely via reflection.
    /// No compile-time dependency on Microsoft.CodeAnalysis — works only if the package is installed.
    /// </summary>
    internal static class RoslynCompiler
    {
        private static bool? _isAvailable;
        private static Type _syntaxTreeType;
        private static Type _compilationType;
        private static Type _compilationOptionsType;
        private static Type _parseOptionsType;
        private static Type _metadataReferenceType;
        private static Type _outputKindEnum;
        private static Type _languageVersionEnum;
        private static MethodInfo _parseText;
        private static MethodInfo _createCompilation;
        private static MethodInfo _createFromFile;
        private static MethodInfo _emit;
        private static object _parseOptions;
        private static object _compilationOptions;

        public static bool IsAvailable
        {
            get
            {
                if (_isAvailable == null)
                    _isAvailable = Initialize();
                return _isAvailable.Value;
            }
        }

        public static void ResetCache()
        {
            _isAvailable = null;
        }


        private static bool Initialize()
        {
            try
            {
                _syntaxTreeType = Type.GetType("Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree, Microsoft.CodeAnalysis.CSharp");
                _compilationType = Type.GetType("Microsoft.CodeAnalysis.CSharp.CSharpCompilation, Microsoft.CodeAnalysis.CSharp");
                _compilationOptionsType = Type.GetType("Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions, Microsoft.CodeAnalysis.CSharp");
                _parseOptionsType = Type.GetType("Microsoft.CodeAnalysis.CSharp.CSharpParseOptions, Microsoft.CodeAnalysis.CSharp");
                _metadataReferenceType = Type.GetType("Microsoft.CodeAnalysis.MetadataReference, Microsoft.CodeAnalysis");
                _outputKindEnum = Type.GetType("Microsoft.CodeAnalysis.OutputKind, Microsoft.CodeAnalysis");
                _languageVersionEnum = Type.GetType("Microsoft.CodeAnalysis.CSharp.LanguageVersion, Microsoft.CodeAnalysis.CSharp");

                if (_syntaxTreeType == null || _compilationType == null || _compilationOptionsType == null ||
                    _parseOptionsType == null || _metadataReferenceType == null || _outputKindEnum == null ||
                    _languageVersionEnum == null)
                    return false;

                // CSharpSyntaxTree.ParseText(string, CSharpParseOptions, string, Encoding, CancellationToken)
                var syntaxTreeBase = Type.GetType("Microsoft.CodeAnalysis.SyntaxTree, Microsoft.CodeAnalysis");
                _parseText = _syntaxTreeType.GetMethod("ParseText", new[] { typeof(string), _parseOptionsType, typeof(string), typeof(Encoding), typeof(System.Threading.CancellationToken) });
                if (_parseText == null)
                    return false;

                // CSharpCompilation.Create(string, IEnumerable<SyntaxTree>, IEnumerable<MetadataReference>, CSharpCompilationOptions)
                var metadataRefBase = _metadataReferenceType;
                var syntaxTreeEnumerable = typeof(IEnumerable<>).MakeGenericType(syntaxTreeBase);
                var metadataRefEnumerable = typeof(IEnumerable<>).MakeGenericType(metadataRefBase);
                _createCompilation = _compilationType.GetMethod("Create", new[] { typeof(string), syntaxTreeEnumerable, metadataRefEnumerable, _compilationOptionsType });
                if (_createCompilation == null)
                    return false;

                // MetadataReference.CreateFromFile(string, MetadataReferenceProperties, DocumentationProvider)
                _createFromFile = _metadataReferenceType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .FirstOrDefault(m => m.Name == "CreateFromFile");
                if (_createFromFile == null)
                    return false;

                // Emit has no single-param overload; the simplest is
                // Emit(Stream, Stream, Stream, Stream, IEnumerable<ResourceDescription>, EmitOptions, CancellationToken)
                var compilationBase = Type.GetType("Microsoft.CodeAnalysis.Compilation, Microsoft.CodeAnalysis");
                if (compilationBase == null) return false;
                _emit = compilationBase.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .Where(m => m.Name == "Emit")
                    .OrderBy(m => m.GetParameters().Length)
                    .FirstOrDefault();
                if (_emit == null)
                    return false;

                // Build CSharpParseOptions — constructor has optional params, use reflection
                var latestValue = Enum.Parse(_languageVersionEnum, "Latest");
                var parseOptionsCtor = _parseOptionsType.GetConstructors(BindingFlags.Public | BindingFlags.Instance)[0];
                var parseCtorParams = parseOptionsCtor.GetParameters();
                var parseArgs = new object[parseCtorParams.Length];
                for (int i = 0; i < parseCtorParams.Length; i++)
                {
                    if (parseCtorParams[i].Name == "languageVersion")
                        parseArgs[i] = latestValue;
                    else if (parseCtorParams[i].HasDefaultValue)
                        parseArgs[i] = parseCtorParams[i].DefaultValue;
                    else
                        parseArgs[i] = null;
                }
                _parseOptions = parseOptionsCtor.Invoke(parseArgs);

                // Build CSharpCompilationOptions — use the first constructor (has most defaults)
                var dllKind = Enum.Parse(_outputKindEnum, "DynamicallyLinkedLibrary");
                var compOptionsCtor = _compilationOptionsType.GetConstructors(BindingFlags.Public | BindingFlags.Instance)[0];
                var compCtorParams = compOptionsCtor.GetParameters();
                var compArgs = new object[compCtorParams.Length];
                for (int i = 0; i < compCtorParams.Length; i++)
                {
                    if (compCtorParams[i].Name == "outputKind")
                        compArgs[i] = dllKind;
                    else if (compCtorParams[i].HasDefaultValue)
                        compArgs[i] = compCtorParams[i].DefaultValue;
                    else
                        compArgs[i] = null;
                }
                _compilationOptions = compOptionsCtor.Invoke(compArgs);

                return true;
            }
            catch (Exception e)
            {
                McpLog.Warn($"[ExecuteCode] Roslyn initialization failed: {e.Message}");
                return false;
            }
        }

        public static Assembly Compile(string source, string[] assemblyPaths, int lineOffset, out List<string> errors)
        {
            errors = new List<string>();

            try
            {
                // Parse source
                var syntaxTree = _parseText.Invoke(null, new object[] { source, _parseOptions, null, null, default(System.Threading.CancellationToken) });

                // Build metadata references
                var metadataRefBase = _metadataReferenceType;
                var listType = typeof(List<>).MakeGenericType(metadataRefBase);
                var refs = (System.Collections.IList)Activator.CreateInstance(listType);

                foreach (var path in assemblyPaths)
                {
                    try
                    {
                        var cfParams = _createFromFile.GetParameters();
                        var cfArgs = new object[cfParams.Length];
                        cfArgs[0] = path; // string path
                        for (int i = 1; i < cfParams.Length; i++)
                            cfArgs[i] = cfParams[i].HasDefaultValue ? cfParams[i].DefaultValue : null;
                        var metaRef = _createFromFile.Invoke(null, cfArgs);
                        refs.Add(metaRef);
                    }
                    catch
                    {
                        // Skip assemblies that can't be loaded as metadata
                    }
                }

                // Build syntax tree array
                var syntaxTreeBase = Type.GetType("Microsoft.CodeAnalysis.SyntaxTree, Microsoft.CodeAnalysis");
                var treeArray = Array.CreateInstance(syntaxTreeBase, 1);
                treeArray.SetValue(syntaxTree, 0);

                // Create compilation
                var compilation = _createCompilation.Invoke(null, new object[] { "MCPDynamic", treeArray, refs, _compilationOptions });

                // Emit to memory
                using (var ms = new MemoryStream())
                {
                    // Build args for the Emit overload (fill non-stream params with defaults)
                    var emitParams = _emit.GetParameters();
                    var emitArgs = new object[emitParams.Length];
                    emitArgs[0] = ms; // peStream
                    for (int i = 1; i < emitParams.Length; i++)
                    {
                        if (emitParams[i].HasDefaultValue)
                            emitArgs[i] = emitParams[i].DefaultValue;
                        else
                            emitArgs[i] = null;
                    }
                    var emitResult = _emit.Invoke(compilation, emitArgs);

                    // Check emitResult.Success
                    var successProp = emitResult.GetType().GetProperty("Success");
                    bool success = (bool)successProp.GetValue(emitResult);

                    if (!success)
                    {
                        // Read emitResult.Diagnostics
                        var diagProp = emitResult.GetType().GetProperty("Diagnostics");
                        var diagnostics = (System.Collections.IEnumerable)diagProp.GetValue(emitResult);
                        var severityError = Enum.Parse(Type.GetType("Microsoft.CodeAnalysis.DiagnosticSeverity, Microsoft.CodeAnalysis"), "Error");

                        foreach (var diag in diagnostics)
                        {
                            var sevProp = diag.GetType().GetProperty("Severity");
                            var severity = sevProp.GetValue(diag);
                            if (!severity.Equals(severityError)) continue;

                            var locProp = diag.GetType().GetProperty("Location");
                            var loc = locProp.GetValue(diag);
                            var spanProp = loc.GetType().GetMethod("GetLineSpan");
                            var lineSpan = spanProp.Invoke(loc, null);
                            var startProp = lineSpan.GetType().GetProperty("StartLinePosition");
                            var startPos = startProp.GetValue(lineSpan);
                            var lineProp = startPos.GetType().GetProperty("Line");
                            int line = (int)lineProp.GetValue(startPos);

                            var msgProp = diag.GetType().GetMethod("GetMessage", new[] { typeof(System.Globalization.CultureInfo) });
                            string msg = (string)msgProp.Invoke(diag, new object[] { null });

                            int userLine = Math.Max(1, line + 1 - lineOffset);
                            errors.Add($"Line {userLine}: {msg}");
                        }
                        return null;
                    }

                    ms.Seek(0, SeekOrigin.Begin);
                    return Assembly.Load(ms.ToArray());
                }
            }
            catch (Exception e)
            {
                // Walk to the deepest cause: TargetInvocationException (and friends) wrap the real
                // failure inside .InnerException, and reporting only e.Message hides everything
                // useful (e.g. a missing transitive dep manifests as the generic "Exception has been
                // thrown by the target of an invocation.").
                Exception root = e;
                while (root.InnerException != null) root = root.InnerException;
                errors.Add($"Roslyn compilation error: {root.GetType().Name}: {root.Message}");
                return null;
            }
        }
    }
}
