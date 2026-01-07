using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using Object = UnityEngine.Object;

namespace AssetStoreTools.Validator.Services.Validation
{
    internal class ScriptUtilityService : IScriptUtilityService
    {
        private const int ScriptTimeoutMs = 10000;
        private const string IgnoredAssemblyCharacters = "!@#$%^*&()-+=[]{}\\|;:'\",.<>/?";

        /// <summary>
        /// For a given list of script assets, retrieves a list of types and their namespaces
        /// </summary>
        /// <param name="monoScripts"></param>
        /// <returns>A dictionary mapping each script asset with a list of its types.
        /// The type tuple contains a name (e.g. <i>class MyClass</i>) and its namespace (e.g. <i>MyNamespace</i>)
        /// </returns>
        public IReadOnlyDictionary<MonoScript, IList<(string Name, string Namespace)>> GetTypeNamespacesFromScriptAssets(IList<MonoScript> monoScripts)
        {
            var typesAndNamespaces = new Dictionary<MonoScript, IList<(string Name, string Namespace)>>();
            var typeInfos = GetTypeInfosFromScriptAssets(monoScripts);

            foreach (var kvp in typeInfos)
            {
                var namespacesInScript = new List<(string Name, string Namespace)>();
                foreach (var typeInfo in kvp.Value)
                {
                    bool isValidType = typeInfo.TypeName == ScriptParser.TypeName.Class || typeInfo.TypeName == ScriptParser.TypeName.Struct ||
                        typeInfo.TypeName == ScriptParser.TypeName.Interface || typeInfo.TypeName == ScriptParser.TypeName.Enum;

                    if (isValidType)
                        namespacesInScript.Add(($"{typeInfo.TypeName.ToString().ToLower()} {typeInfo.Name}", typeInfo.Namespace));
                }

                typesAndNamespaces.Add(kvp.Key, namespacesInScript);
            }

            return typesAndNamespaces;
        }

        /// <summary>
        /// Scans the given precompiled assembly assets to retrieve a list of their contained types
        /// </summary>
        /// <param name="assemblies"></param>
        /// <returns>A dictionary mapping each precompiled assembly asset with a list of <see cref="Type"> System.Type </see> objects.</returns>
        public IReadOnlyDictionary<Object, IList<Type>> GetTypesFromAssemblies(IList<Object> assemblies)
        {
            var dllPaths = assemblies.ToDictionary(t => AssetDatabase.GetAssetPath(t), t => t);
            var types = new ConcurrentDictionary<Object, IList<Type>>();
            var failedDllPaths = new ConcurrentBag<string>();

            var allAssemblies = AppDomain.CurrentDomain.GetAssemblies();

            Parallel.ForEach(dllPaths.Keys,
                (assemblyPath) =>
                {
                    try
                    {
                        var assembly = allAssemblies.FirstOrDefault(x => Path.GetFullPath(x.Location).Equals(Path.GetFullPath(assemblyPath), StringComparison.OrdinalIgnoreCase));
                        if (assembly == null)
                            return;

                        var assemblyTypes = assembly.GetTypes().Where(x => !IgnoredAssemblyCharacters.Any(c => x.Name.Contains(c))).ToList();
                        types.TryAdd(dllPaths[assemblyPath], assemblyTypes);
                    }
                    catch
                    {
                        failedDllPaths.Add(assemblyPath);
                    }
                });

            if (failedDllPaths.Count > 0)
            {
                var message = new StringBuilder("The following precompiled assemblies could not be checked:");
                foreach (var path in failedDllPaths)
                    message.Append($"\n{path}");
                UnityEngine.Debug.LogWarning(message);
            }

            // Types are sorted randomly due to parallelism, therefore need to be sorted before returning
            var sortedTypes = dllPaths.Where(x => types.ContainsKey(x.Value))
                .Select(x => new KeyValuePair<Object, IList<Type>>(x.Value, types[x.Value]))
                .ToDictionary(t => t.Key, t => t.Value);

            return sortedTypes;
        }

        /// <summary>
        /// Scans the given script assets to retrieve a list of their contained types
        /// </summary>
        /// <param name="monoScripts"></param>
        /// <returns>A dictionary mapping each precompiled assembly asset with a list of <see cref="Type"> System.Type </see> objects.</returns>
        public IReadOnlyDictionary<MonoScript, IList<Type>> GetTypesFromScriptAssets(IList<MonoScript> monoScripts)
        {
            var realTypes = new Dictionary<MonoScript, IList<Type>>();
            var typeInfos = GetTypeInfosFromScriptAssets(monoScripts);
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();

            foreach (var kvp in typeInfos)
            {
                var realTypesInScript = new List<Type>();
                foreach (var typeInfo in kvp.Value)
                {
                    bool isValidType = typeInfo.TypeName == ScriptParser.TypeName.Class || typeInfo.TypeName == ScriptParser.TypeName.Struct ||
                        typeInfo.TypeName == ScriptParser.TypeName.Interface || typeInfo.TypeName == ScriptParser.TypeName.Enum;

                    if (isValidType)
                    {
                        var realType = assemblies.Where(a => a.GetType(typeInfo.GetReflectionFriendlyFullName()) != null)
                            .Select(a => a.GetType(typeInfo.GetReflectionFriendlyFullName())).FirstOrDefault();
                        if (realType != null)
                            realTypesInScript.Add(realType);
                    }
                }

                realTypes.Add(kvp.Key, realTypesInScript);
            }

            return realTypes;
        }

        /// <summary>
        /// Scans the given MonoScript assets to retrieve a list of their contained types
        /// </summary>
        /// <param name="monoScripts"></param>
        /// <returns>A dictionary mapping each script asset with a list of <see cref="TypeInfo"> TypeInfo </see> objects. </returns>
        private IReadOnlyDictionary<MonoScript, IList<ScriptParser.BlockInfo>> GetTypeInfosFromScriptAssets(IList<MonoScript> monoScripts)
        {
            var types = new ConcurrentDictionary<MonoScript, IList<ScriptParser.BlockInfo>>();
            var monoScriptContents = new Dictionary<MonoScript, string>();
            var failedScripts = new ConcurrentBag<MonoScript>();

            // A separate dictionary is needed because MonoScript contents cannot be accessed outside of the main thread
            foreach (var kvp in monoScripts)
                monoScriptContents.Add(kvp, kvp.text);

            var tasks = new List<Tuple<Task, CancellationTokenSource>>();

            try
            {
                foreach (var kvp in monoScriptContents)
                {
                    var cancellationTokenSource = new CancellationTokenSource(ScriptTimeoutMs);

                    var task = Task.Run(() =>
                    {
                        var parsingTask = new ScriptParser(cancellationTokenSource.Token);
                        var parsed = parsingTask.GetTypesInScript(kvp.Value, out IList<ScriptParser.BlockInfo> parsedTypes);
                        if (parsed)
                            types.TryAdd(kvp.Key, parsedTypes);
                        else
                            failedScripts.Add(kvp.Key);
                    });

                    tasks.Add(new Tuple<Task, CancellationTokenSource>(task, cancellationTokenSource));
                }

                foreach (var t in tasks)
                    t.Item1.Wait();
            }
            finally
            {
                foreach (var t in tasks)
                    t.Item2.Dispose();
            }

            if (failedScripts.Count > 0)
            {
                var message = new StringBuilder("The following scripts could not be checked:");
                foreach (var s in failedScripts)
                    message.Append($"\n{AssetDatabase.GetAssetPath(s)}");
                UnityEngine.Debug.LogWarning(message);
            }

            // Types are sorted randomly due to parallelism, therefore need to be sorted before returning
            var sortedTypes = monoScriptContents.Where(x => types.ContainsKey(x.Key))
                .Select(x => new KeyValuePair<MonoScript, IList<ScriptParser.BlockInfo>>(x.Key, types[x.Key]))
                .ToDictionary(t => t.Key, t => t.Value);

            return sortedTypes;
        }

        /// <summary>
        /// A simple script parser class to detect types declared within a script
        /// </summary>
        private class ScriptParser
        {
            /// <summary>
            /// Types that can be identified by the script parser
            /// </summary>
            public enum TypeName
            {
                Undefined,
                Namespace,
                Class,
                Struct,
                Interface,
                Enum,
                IdentationStart,
                IdentationEnd
            }

            /// <summary>
            /// A class containing information about each block of a C# script
            /// </summary>
            /// <remarks> A block in this context is defined as script text that is contained within curly brackets.
            /// If it's a type, it may have a preceding name and a namespace
            /// </remarks>
            public class BlockInfo
            {
                public TypeName TypeName = TypeName.Undefined;
                public string Name = string.Empty;
                public string FullName = string.Empty;
                public string Namespace = string.Empty;
                public int FoundIndex;
                public int StartIndex;

                public BlockInfo ParentBlock;

                public string GetReflectionFriendlyFullName()
                {
                    StringBuilder sb = new StringBuilder(FullName);
                    for (int i = sb.Length - 1; i >= Namespace.Length + 1; i--)
                        if (sb[i] == '.')
                            sb[i] = '+';

                    return sb.ToString();
                }
            }

            private CancellationToken _token;

            public ScriptParser(CancellationToken token)
            {
                _token = token;
            }

            public bool GetTypesInScript(string text, out IList<BlockInfo> types)
            {
                types = null;

                try
                {
                    var sanitized = SanitizeScript(text);
                    types = ScanForTypes(sanitized);
                    return true;
                }
                catch
                {
                    return false;
                }
            }

            private string SanitizeScript(string source)
            {
                var sb = new StringBuilder(source);

                // Remove comments and strings
                sb = RemoveStringsAndComments(sb);

                // Replace newlines with spaces
                sb.Replace("\r", " ").Replace("\n", " ");

                // Space out the brackets
                sb.Replace("{", " { ").Replace("}", " } ");

                // Insert a space at the start for more convenient parsing
                sb.Insert(0, " ");

                // Remove repeating spaces
                var sanitized = Regex.Replace(sb.ToString(), @"\s{2,}", " ");

                return sanitized;
            }

            private StringBuilder RemoveStringsAndComments(StringBuilder sb)
            {
                void CheckStringIdentifiers(int index, out bool isVerbatim, out bool isInterpolated)
                {
                    isVerbatim = false;
                    isInterpolated = false;

                    string precedingChars = string.Empty;
                    for (int i = index - 1; i >= 0; i--)
                    {
                        if (sb[i] == ' ')
                            break;
                        precedingChars += sb[i];
                    }

                    if (precedingChars.Contains("@"))
                        isVerbatim = true;
                    if (precedingChars.Contains("$"))
                        isInterpolated = true;
                }

                bool IsRegion(int index)
                {
                    if (sb.Length - index < "#region".Length)
                        return false;
                    if (sb[index] == '#' && sb[index + 1] == 'r' && sb[index + 2] == 'e' && sb[index + 3] == 'g' && sb[index + 4] == 'i' &&
                        sb[index + 5] == 'o' && sb[index + 6] == 'n')
                        return true;
                    return false;
                }

                var removeRanges = new List<Tuple<int, int>>();

                for (int i = 0; i < sb.Length; i++)
                {
                    _token.ThrowIfCancellationRequested();

                    // Comment code
                    if (sb[i] == '/')
                    {
                        if (sb[i + 1] == '/')
                        {
                            for (int j = i + 1; j < sb.Length; j++)
                            {
                                _token.ThrowIfCancellationRequested();
                                if (sb[j] == '\n' || j == sb.Length - 1)
                                {
                                    removeRanges.Add(new Tuple<int, int>(i, j - i + 1));
                                    i = j;
                                    break;
                                }
                            }
                        }
                        else if (sb[i + 1] == '*')
                        {
                            for (int j = i + 2; j < sb.Length; j++)
                            {
                                _token.ThrowIfCancellationRequested();
                                if (sb[j] == '/' && sb[j - 1] == '*')
                                {
                                    removeRanges.Add(new Tuple<int, int>(i, j - i + 1));
                                    i = j + 1;
                                    break;
                                }
                            }
                        }
                    }
                    // Char code
                    else if (sb[i] == '\'')
                    {
                        for (int j = i + 1; j < sb.Length; j++)
                        {
                            _token.ThrowIfCancellationRequested();
                            if (sb[j] == '\'')
                            {
                                if (sb[j - 1] == '\\')
                                {
                                    int slashCount = 0;
                                    int k = j - 1;
                                    while (sb[k--] == '\\')
                                        slashCount++;
                                    if (slashCount % 2 != 0)
                                        continue;
                                }
                                removeRanges.Add(new Tuple<int, int>(i, j - i + 1));
                                i = j;
                                break;
                            }
                        }
                    }
                    // String code
                    else if (sb[i] == '"')
                    {
                        if (sb[i - 1] == '\'' && sb[i + 1] == '\'' || (sb[i - 2] == '\'' && sb[i - 1] == '\\' && sb[i + 1] == '\''))
                            continue;

                        CheckStringIdentifiers(i, out bool isVerbatim, out bool isInterpolated);

                        var bracketCount = 0;
                        bool interpolationEnd = true;
                        for (int j = i + 1; j < sb.Length; j++)
                        {
                            _token.ThrowIfCancellationRequested();
                            if (isInterpolated && (sb[j] == '{' || sb[j] == '}'))
                            {
                                if (sb[j] == '{')
                                {
                                    if (sb[j + 1] != '{')
                                        bracketCount++;
                                    else
                                        j += 1;
                                }
                                else if (sb[j] == '}')
                                {
                                    if (sb[j + 1] != '}')
                                        bracketCount--;
                                    else
                                        j += 1;
                                }

                                if (bracketCount == 0)
                                    interpolationEnd = true;
                                else
                                    interpolationEnd = false;

                                continue;
                            }

                            if (sb[j] == '\"')
                            {
                                if (isVerbatim)
                                {
                                    if (sb[j + 1] != '\"')
                                    {
                                        if (!isInterpolated || isInterpolated && interpolationEnd == true)
                                        {
                                            removeRanges.Add(new Tuple<int, int>(i, j - i + 1));
                                            i = j + 1;
                                            break;
                                        }
                                    }
                                    else
                                        j += 1;
                                }
                                else
                                {
                                    bool endOfComment = false;
                                    if (sb[j - 1] != '\\')
                                        endOfComment = true;
                                    else
                                    {
                                        int slashCount = 0;
                                        int k = j - 1;
                                        while (sb[k--] == '\\')
                                            slashCount++;
                                        if (slashCount % 2 == 0)
                                            endOfComment = true;
                                    }

                                    if (!isInterpolated && endOfComment || (isInterpolated && interpolationEnd && endOfComment))
                                    {
                                        removeRanges.Add(new Tuple<int, int>(i, j - i + 1));
                                        i = j + 1;
                                        break;
                                    }
                                }
                            }
                        }
                    }
                    // Region code
                    else if (IsRegion(i))
                    {
                        i += "#region".Length;
                        for (int j = i; j < sb.Length; j++)
                        {
                            _token.ThrowIfCancellationRequested();
                            if (sb[j] == '\n')
                            {
                                removeRanges.Add(new Tuple<int, int>(i, j - i + 1));
                                i = j;
                                break;
                            }
                        }
                    }
                }

                for (int i = removeRanges.Count - 1; i >= 0; i--)
                    sb = sb.Remove(removeRanges[i].Item1, removeRanges[i].Item2);

                return sb;
            }

            private IList<BlockInfo> ScanForTypes(string script)
            {
                var typeList = new SortedList<int, BlockInfo>();
                BlockInfo currentActiveBlock = new BlockInfo();

                int i = 0;

                BlockInfo nextNamespace = null;
                BlockInfo nextClass = null;
                BlockInfo nextStruct = null;
                BlockInfo nextInterface = null;
                BlockInfo nextEnum = null;

                while (i < script.Length)
                {
                    _token.ThrowIfCancellationRequested();
                    if (nextNamespace == null)
                        nextNamespace = FindNextTypeBlock(script, i, TypeName.Namespace);
                    if (nextClass == null)
                        nextClass = FindNextTypeBlock(script, i, TypeName.Class);
                    if (nextStruct == null)
                        nextStruct = FindNextTypeBlock(script, i, TypeName.Struct);
                    if (nextInterface == null)
                        nextInterface = FindNextTypeBlock(script, i, TypeName.Interface);
                    if (nextEnum == null)
                        nextEnum = FindNextTypeBlock(script, i, TypeName.Enum);

                    var nextIdentationIncrease = FindNextTypeBlock(script, i, TypeName.IdentationStart);
                    var nextIdentationDecrease = FindNextTypeBlock(script, i, TypeName.IdentationEnd);

                    if (!TryFindClosestBlock(out var closestBlock, nextNamespace, nextClass,
                        nextStruct, nextInterface, nextEnum, nextIdentationIncrease, nextIdentationDecrease))
                        break;

                    switch (closestBlock)
                    {
                        case var _ when closestBlock == nextIdentationIncrease:
                            closestBlock.ParentBlock = currentActiveBlock;
                            currentActiveBlock = closestBlock;
                            break;
                        case var _ when closestBlock == nextIdentationDecrease:
                            if (currentActiveBlock.TypeName != TypeName.Undefined)
                                typeList.Add(currentActiveBlock.StartIndex, currentActiveBlock);
                            currentActiveBlock = currentActiveBlock.ParentBlock;
                            break;
                        case var _ when closestBlock == nextNamespace:
                            closestBlock.Namespace = currentActiveBlock.TypeName == TypeName.Namespace ? currentActiveBlock.FullName : currentActiveBlock.Namespace;
                            closestBlock.FullName = string.IsNullOrEmpty(currentActiveBlock.FullName) ? closestBlock.Name : $"{currentActiveBlock.FullName}.{closestBlock.Name}";
                            closestBlock.ParentBlock = currentActiveBlock;
                            currentActiveBlock = closestBlock;
                            nextNamespace = null;
                            break;
                        case var _ when closestBlock == nextClass:
                        case var _ when closestBlock == nextStruct:
                        case var _ when closestBlock == nextInterface:
                        case var _ when closestBlock == nextEnum:
                            closestBlock.FullName = string.IsNullOrEmpty(currentActiveBlock.FullName) ? closestBlock.Name : $"{currentActiveBlock.FullName}.{closestBlock.Name}";
                            closestBlock.Namespace = currentActiveBlock.TypeName == TypeName.Namespace ? currentActiveBlock.FullName : currentActiveBlock.Namespace;
                            closestBlock.ParentBlock = currentActiveBlock;
                            currentActiveBlock = closestBlock;
                            switch (closestBlock)
                            {
                                case var _ when closestBlock == nextClass:
                                    nextClass = null;
                                    break;
                                case var _ when closestBlock == nextStruct:
                                    nextStruct = null;
                                    break;
                                case var _ when closestBlock == nextInterface:
                                    nextInterface = null;
                                    break;
                                case var _ when closestBlock == nextEnum:
                                    nextEnum = null;
                                    break;
                            }
                            break;
                    }

                    i = closestBlock.StartIndex;
                }

                return typeList.Select(x => x.Value).ToList();
            }

            private bool TryFindClosestBlock(out BlockInfo closestBlock, params BlockInfo[] blocks)
            {
                closestBlock = null;
                for (int i = 0; i < blocks.Length; i++)
                {
                    if (blocks[i].FoundIndex == -1)
                        continue;

                    if (closestBlock == null || closestBlock.FoundIndex > blocks[i].FoundIndex)
                        closestBlock = blocks[i];
                }

                return closestBlock != null;
            }

            private BlockInfo FindNextTypeBlock(string text, int startIndex, TypeName blockType)
            {
                string typeKeyword;
                switch (blockType)
                {
                    case TypeName.Namespace:
                        typeKeyword = "namespace";
                        break;
                    case TypeName.Class:
                        typeKeyword = "class";
                        break;
                    case TypeName.Struct:
                        typeKeyword = "struct";
                        break;
                    case TypeName.Interface:
                        typeKeyword = "interface";
                        break;
                    case TypeName.Enum:
                        typeKeyword = "enum";
                        break;
                    case TypeName.IdentationStart:
                        var identationStart = text.IndexOf("{", startIndex);
                        return new BlockInfo() { FoundIndex = identationStart, StartIndex = identationStart + 1, TypeName = TypeName.Undefined };
                    case TypeName.IdentationEnd:
                        var identationEnd = text.IndexOf("}", startIndex);
                        return new BlockInfo() { FoundIndex = identationEnd, StartIndex = identationEnd + 1, TypeName = TypeName.Undefined };
                    default:
                        throw new ArgumentException("Invalid block type provided");
                }

                int start = -1;
                int blockStart = -1;
                string name = string.Empty;
                while (startIndex < text.Length)
                {
                    _token.ThrowIfCancellationRequested();
                    start = text.IndexOf($" {typeKeyword} ", startIndex);
                    if (start == -1)
                        return new BlockInfo { FoundIndex = -1 };

                    // Check if the caught type keyword matches the type definition
                    var openingBracket = text.IndexOf("{", start);
                    if (openingBracket == -1)
                        return new BlockInfo { FoundIndex = -1 };

                    var declaration = text.Substring(start, openingBracket - start);
                    var split = declaration.Split(' ');

                    // Namespace detection
                    if (typeKeyword == "namespace")
                    {
                        // Expected result: [null] [namespace] [null]
                        if (split.Length == 4)
                        {
                            name = split[2];
                            blockStart = openingBracket + 1;
                            break;
                        }
                        else
                            startIndex = openingBracket + 1;
                    }
                    // Class, Interface, Struct, Enum detection
                    else
                    {
                        // Expected result: [null] [keywordName] [typeName] ... [null]
                        // Skip any keywords that only contains [null] [keywordName] [null]
                        if (split.Length != 3)
                        {
                            name = split[2];
                            blockStart = openingBracket + 1;
                            break;
                        }
                        else
                            startIndex = openingBracket + 1;
                    }
                }

                var info = new BlockInfo() { FoundIndex = start, StartIndex = blockStart, Name = name, TypeName = blockType };
                return info;
            }
        }
    }
}