using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEditor;

namespace MCPForUnity.Editor.Tools
{
    [McpForUnityTool("unity_reflect", AutoRegister = false, Group = "docs")]
    public static class UnityReflect
    {
        private static Dictionary<string, Type[]> _assemblyTypeCache;
        private static readonly object CacheLock = new();
        private static readonly ConcurrentDictionary<Type, string[]> ExtensionMethodCache = new();

        private static readonly string[] NamespacePrefixes =
        {
            "UnityEngine.",
            "UnityEditor.",
            "UnityEngine.UI.",
            "Unity.Cinemachine.",
            "UnityEngine.AI.",
            "UnityEngine.Rendering.Universal.",
            "UnityEngine.Rendering.HighDefinition.",
            "UnityEngine.InputSystem.",
            "UnityEngine.ProBuilder.",
            "UnityEngine.Tilemaps.",
            "UnityEngine.EventSystems.",
            "UnityEngine.Rendering.",
            "UnityEngine.SceneManagement.",
            "UnityEngine.Animations.",
            "UnityEngine.Playables.",
            "UnityEngine.UIElements."
        };

        private static readonly Dictionary<Type, string> FriendlyTypeNames = new()
        {
            { typeof(void), "void" },
            { typeof(int), "int" },
            { typeof(float), "float" },
            { typeof(bool), "bool" },
            { typeof(string), "string" },
            { typeof(double), "double" },
            { typeof(long), "long" },
            { typeof(object), "object" },
            { typeof(byte), "byte" },
            { typeof(short), "short" },
            { typeof(char), "char" },
            { typeof(decimal), "decimal" },
            { typeof(uint), "uint" },
            { typeof(ulong), "ulong" },
            { typeof(ushort), "ushort" },
            { typeof(sbyte), "sbyte" }
        };

        [InitializeOnLoadMethod]
        private static void OnLoad()
        {
            AssemblyReloadEvents.afterAssemblyReload += InvalidateCache;
        }

        private static void InvalidateCache()
        {
            lock (CacheLock)
            {
                _assemblyTypeCache = null;
            }
            ExtensionMethodCache.Clear();
        }

        private static Dictionary<string, Type[]> GetAssemblyTypeCache()
        {
            lock (CacheLock)
            {
                if (_assemblyTypeCache != null)
                    return _assemblyTypeCache;

                _assemblyTypeCache = new Dictionary<string, Type[]>();
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        _assemblyTypeCache[asm.FullName] = asm.GetExportedTypes();
                    }
                    catch
                    {
                        // Some assemblies throw on GetExportedTypes
                    }
                }
                return _assemblyTypeCache;
            }
        }

        public static object HandleCommand(JObject @params)
        {
            if (EditorApplication.isCompiling)
                return new ErrorResponse("Cannot reflect while Unity is compiling. Wait for domain reload to complete.");

            if (@params == null)
                return new ErrorResponse("Parameters cannot be null.");

            var p = new ToolParams(@params);

            var actionResult = p.GetRequired("action");
            if (!actionResult.IsSuccess)
                return new ErrorResponse(actionResult.ErrorMessage);

            string action = actionResult.Value.ToLowerInvariant();

            try
            {
                switch (action)
                {
                    case "get_type":
                        return GetTypeInfo(p);
                    case "get_member":
                        return GetMemberInfo(p);
                    case "search":
                        return SearchTypes(p);
                    default:
                        return new ErrorResponse(
                            $"Unknown action: '{action}'. Supported actions: get_type, get_member, search.");
                }
            }
            catch (Exception ex)
            {
                return new ErrorResponse(ex.Message, new { stackTrace = ex.StackTrace });
            }
        }

        // === get_type ===
        private static object GetTypeInfo(ToolParams p)
        {
            var classResult = p.GetRequired("class_name", "'class_name' parameter is required for get_type.");
            if (!classResult.IsSuccess)
                return new ErrorResponse(classResult.ErrorMessage);

            string className = classResult.Value;
            string normalizedName = NormalizeGenericName(className);

            // Check for ambiguity first (only for short names without namespace)
            if (!normalizedName.Contains('.') && !normalizedName.Contains('`'))
            {
                var matches = FindAllTypesByShortName(normalizedName);
                if (matches.Count > 1)
                {
                    return new SuccessResponse($"Ambiguous type name '{className}'.", new
                    {
                        found = true,
                        ambiguous = true,
                        query = className,
                        matches = matches.Select(t => t.FullName).OrderBy(n => n).ToArray(),
                        hint = "Use the fully qualified name (e.g., 'UnityEngine.UI.Button') to disambiguate."
                    });
                }
            }

            var type = ResolveType(normalizedName);
            if (type == null)
            {
                return new SuccessResponse($"Type '{className}' not found.", new
                {
                    found = false,
                    query = className
                });
            }

            var flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

            var methods = type.GetMethods(flags)
                .Where(m => !m.IsSpecialName)
                .Select(m => m.Name)
                .Distinct()
                .OrderBy(n => n)
                .ToArray();

            var properties = type.GetProperties(flags)
                .Select(pr => pr.Name)
                .Distinct()
                .OrderBy(n => n)
                .ToArray();

            var fields = type.GetFields(flags)
                .Select(f => f.Name)
                .Distinct()
                .OrderBy(n => n)
                .ToArray();

            var events = type.GetEvents(flags)
                .Select(e => e.Name)
                .Distinct()
                .OrderBy(n => n)
                .ToArray();

            var obsoleteMembers = GetObsoleteMembers(type, flags);
            var extensionMethods = FindExtensionMethods(type);

            var interfaces = type.GetInterfaces()
                .Select(i => FormatTypeName(i))
                .OrderBy(n => n)
                .ToArray();

            return new SuccessResponse($"Type info for '{FormatTypeName(type)}'.", new
            {
                found = true,
                name = FormatTypeName(type),
                full_name = type.FullName,
                @namespace = type.Namespace,
                assembly = type.Assembly.GetName().Name,
                base_class = type.BaseType != null ? FormatTypeName(type.BaseType) : null,
                interfaces,
                is_abstract = type.IsAbstract,
                is_sealed = type.IsSealed,
                is_static = type.IsAbstract && type.IsSealed,
                is_enum = type.IsEnum,
                is_interface = type.IsInterface,
                members = new
                {
                    methods,
                    properties,
                    fields,
                    events
                },
                extension_methods = extensionMethods,
                obsolete_members = obsoleteMembers
            });
        }

        // === get_member ===
        private static object GetMemberInfo(ToolParams p)
        {
            var classResult = p.GetRequired("class_name", "'class_name' parameter is required for get_member.");
            if (!classResult.IsSuccess)
                return new ErrorResponse(classResult.ErrorMessage);

            var memberResult = p.GetRequired("member_name", "'member_name' parameter is required for get_member.");
            if (!memberResult.IsSuccess)
                return new ErrorResponse(memberResult.ErrorMessage);

            string className = classResult.Value;
            string memberName = memberResult.Value;
            string normalizedName = NormalizeGenericName(className);

            if (!normalizedName.Contains('.') && !normalizedName.Contains('`'))
            {
                var matches = FindAllTypesByShortName(normalizedName);
                if (matches.Count > 1)
                {
                    return new SuccessResponse($"Ambiguous type name '{className}'.", new
                    {
                        found = true,
                        ambiguous = true,
                        query = className,
                        matches = matches.Select(t => t.FullName).OrderBy(n => n).ToArray(),
                        hint = "Use the fully qualified name to disambiguate before requesting member details."
                    });
                }
            }

            var type = ResolveType(normalizedName);
            if (type == null)
            {
                return new SuccessResponse($"Type '{className}' not found.", new
                {
                    found = false,
                    query = className
                });
            }

            // Use flags without DeclaredOnly to find inherited members
            var flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static;

            // Try methods first
            var methods = type.GetMethods(flags)
                .Where(m => !m.IsSpecialName && m.Name == memberName)
                .ToArray();

            if (methods.Length > 0)
            {
                var overloads = methods.Select(m => FormatMethodDetail(m)).ToArray();
                return new SuccessResponse($"Member '{memberName}' on '{FormatTypeName(type)}'.", new
                {
                    found = true,
                    type_name = FormatTypeName(type),
                    member_name = memberName,
                    member_type = "method",
                    overload_count = overloads.Length,
                    overloads
                });
            }

            // Try properties
            var prop = type.GetProperty(memberName, flags);
            if (prop != null)
            {
                return new SuccessResponse($"Member '{memberName}' on '{FormatTypeName(type)}'.", new
                {
                    found = true,
                    type_name = FormatTypeName(type),
                    member_name = memberName,
                    member_type = "property",
                    property_type = FormatTypeName(prop.PropertyType),
                    can_read = prop.CanRead,
                    can_write = prop.CanWrite,
                    is_static = (prop.GetMethod ?? prop.SetMethod)?.IsStatic ?? false,
                    is_obsolete = prop.GetCustomAttribute<ObsoleteAttribute>() != null,
                    declaring_type = prop.DeclaringType != type ? FormatTypeName(prop.DeclaringType) : null
                });
            }

            // Try fields
            var field = type.GetField(memberName, flags);
            if (field != null)
            {
                return new SuccessResponse($"Member '{memberName}' on '{FormatTypeName(type)}'.", new
                {
                    found = true,
                    type_name = FormatTypeName(type),
                    member_name = memberName,
                    member_type = "field",
                    field_type = FormatTypeName(field.FieldType),
                    is_static = field.IsStatic,
                    is_readonly = field.IsInitOnly,
                    is_constant = field.IsLiteral,
                    constant_value = field.IsLiteral ? field.GetRawConstantValue() : null,
                    is_obsolete = field.GetCustomAttribute<ObsoleteAttribute>() != null,
                    declaring_type = field.DeclaringType != type ? FormatTypeName(field.DeclaringType) : null
                });
            }

            // Try events
            var evt = type.GetEvent(memberName, flags);
            if (evt != null)
            {
                return new SuccessResponse($"Member '{memberName}' on '{FormatTypeName(type)}'.", new
                {
                    found = true,
                    type_name = FormatTypeName(type),
                    member_name = memberName,
                    member_type = "event",
                    event_handler_type = FormatTypeName(evt.EventHandlerType),
                    is_obsolete = evt.GetCustomAttribute<ObsoleteAttribute>() != null,
                    declaring_type = evt.DeclaringType != type ? FormatTypeName(evt.DeclaringType) : null
                });
            }

            // Try extension methods as a last resort
            var extMethods = FindExtensionMethodInfos(type, memberName);
            if (extMethods.Length > 0)
            {
                var overloads = extMethods.Select(m => FormatMethodDetail(m)).ToArray();
                return new SuccessResponse($"Extension method '{memberName}' on '{FormatTypeName(type)}'.", new
                {
                    found = true,
                    type_name = FormatTypeName(type),
                    member_name = memberName,
                    member_type = "extension_method",
                    overload_count = overloads.Length,
                    overloads,
                    declaring_type = FormatTypeName(extMethods[0].DeclaringType)
                });
            }

            return new SuccessResponse($"Member '{memberName}' not found on '{FormatTypeName(type)}'.", new
            {
                found = false,
                type_name = FormatTypeName(type),
                member_name = memberName
            });
        }

        // === search ===
        private static object SearchTypes(ToolParams p)
        {
            var queryResult = p.GetRequired("query", "'query' parameter is required for search.");
            if (!queryResult.IsSuccess)
                return new ErrorResponse(queryResult.ErrorMessage);

            string query = queryResult.Value;
            string scope = p.Get("scope", "unity").ToLowerInvariant();

            if (scope != "unity" && scope != "packages" && scope != "project" && scope != "all")
            {
                return new ErrorResponse(
                    $"Invalid scope: '{scope}'. Supported: unity, packages, project, all.");
            }

            var cache = GetAssemblyTypeCache();
            string queryLower = query.ToLowerInvariant();

            var candidates = new List<(Type type, int rank)>();

            foreach (var kvp in cache)
            {
                var asm = kvp.Value.Length > 0 ? kvp.Value[0].Assembly : null;
                if (asm == null) continue;

                string asmName = asm.GetName().Name;
                if (!MatchesScope(asmName, scope))
                    continue;

                foreach (var t in kvp.Value)
                {
                    if (t.Name == null) continue;

                    string nameLower = t.Name.ToLowerInvariant();
                    string fullNameLower = t.FullName?.ToLowerInvariant() ?? nameLower;

                    if (nameLower == queryLower || fullNameLower == queryLower)
                        candidates.Add((t, 0)); // Exact match
                    else if (nameLower.StartsWith(queryLower))
                        candidates.Add((t, 1)); // Starts with
                    else if (nameLower.Contains(queryLower) || fullNameLower.Contains(queryLower))
                        candidates.Add((t, 2)); // Contains
                }
            }

            var results = candidates
                .OrderBy(c => c.rank)
                .ThenBy(c => c.type.FullName)
                .Take(25)
                .Select(c => new
                {
                    name = c.type.Name,
                    full_name = c.type.FullName,
                    @namespace = c.type.Namespace,
                    assembly = c.type.Assembly.GetName().Name,
                    is_class = c.type.IsClass,
                    is_enum = c.type.IsEnum,
                    is_interface = c.type.IsInterface,
                    is_struct = c.type.IsValueType && !c.type.IsEnum
                })
                .ToArray();

            return new SuccessResponse($"Found {results.Length} type(s) matching '{query}' (scope: {scope}).", new
            {
                query,
                scope,
                count = results.Length,
                results,
                truncated = candidates.Count > 25
            });
        }

        // --- Type Resolution ---

        private static Type ResolveType(string className)
        {
            // Use the shared UnityTypeResolver which handles caching,
            // namespace prefixes, player-over-editor priority, and TypeCache fallback.
            var type = UnityTypeResolver.ResolveAny(className);
            if (type != null) return type;

            // UnityTypeResolver doesn't try our extended namespace prefixes,
            // so fall back to assembly cache scan for edge cases.
            var cache = GetAssemblyTypeCache();
            foreach (var prefix in NamespacePrefixes)
            {
                string fullName = prefix + className;
                foreach (var kvp in cache)
                {
                    type = Array.Find(kvp.Value, t => t.FullName == fullName);
                    if (type != null) return type;
                }
            }

            return null;
        }

        private static List<Type> FindAllTypesByShortName(string shortName)
        {
            var matches = new List<Type>();
            foreach (var kvp in GetAssemblyTypeCache())
            {
                foreach (var t in kvp.Value)
                {
                    if (t.Name == shortName)
                        matches.Add(t);
                }
            }
            return matches;
        }

        // --- Generic Name Normalization ---

        private static string NormalizeGenericName(string name)
        {
            // Parse List<T> -> List`1, Dictionary<K,V> -> Dictionary`2
            var match = Regex.Match(name, @"^(.+)<(.+)>$");
            if (!match.Success) return name;

            string baseName = match.Groups[1].Value;
            string typeArgs = match.Groups[2].Value;

            // Count generic args by tracking nesting depth
            int argCount = 1;
            int depth = 0;
            foreach (char c in typeArgs)
            {
                if (c == '<') depth++;
                else if (c == '>') depth--;
                else if (c == ',' && depth == 0) argCount++;
            }

            return $"{baseName}`{argCount}";
        }

        // --- Type Name Formatting ---

        private static string FormatTypeName(Type type)
        {
            if (type == null) return "null";

            if (FriendlyTypeNames.TryGetValue(type, out var friendly))
                return friendly;

            if (type.IsArray)
                return FormatTypeName(type.GetElementType()) + "[]";

            if (type.IsByRef)
                return FormatTypeName(type.GetElementType());

            if (type.IsGenericType)
            {
                string baseName = type.Name;
                int backtickIndex = baseName.IndexOf('`');
                if (backtickIndex > 0)
                    baseName = baseName.Substring(0, backtickIndex);

                var args = type.GetGenericArguments();
                string argsStr = string.Join(", ", args.Select(FormatTypeName));
                return $"{baseName}<{argsStr}>";
            }

            // For nested types, use dot notation
            if (type.IsNested && type.DeclaringType != null)
                return FormatTypeName(type.DeclaringType) + "." + type.Name;

            return type.Name;
        }

        // --- Method Formatting ---

        private static object FormatMethodDetail(MethodInfo m)
        {
            var parameters = m.GetParameters().Select(param =>
            {
                string prefix = "";
                if (param.IsOut) prefix = "out ";
                else if (param.ParameterType.IsByRef) prefix = "ref ";

                return new
                {
                    name = param.Name,
                    type = prefix + FormatTypeName(param.ParameterType),
                    has_default = param.HasDefaultValue,
                    default_value = param.HasDefaultValue ? FormatDefaultValue(param.DefaultValue) : null,
                    is_params = param.IsDefined(typeof(ParamArrayAttribute))
                };
            }).ToArray();

            string signature = FormatMethodSignature(m);
            var obsoleteAttr = m.GetCustomAttribute<ObsoleteAttribute>();

            return new
            {
                signature,
                return_type = FormatTypeName(m.ReturnType),
                parameters,
                is_static = m.IsStatic,
                is_virtual = m.IsVirtual && !m.IsFinal,
                is_abstract = m.IsAbstract,
                is_generic = m.IsGenericMethod,
                generic_arguments = m.IsGenericMethod
                    ? m.GetGenericArguments().Select(a => a.Name).ToArray()
                    : null,
                is_obsolete = obsoleteAttr != null,
                obsolete_message = obsoleteAttr?.Message,
                declaring_type = m.DeclaringType != m.ReflectedType
                    ? FormatTypeName(m.DeclaringType)
                    : null
            };
        }

        private static string FormatMethodSignature(MethodInfo m)
        {
            string staticPrefix = m.IsStatic ? "static " : "";
            string returnType = FormatTypeName(m.ReturnType);
            string name = m.Name;

            if (m.IsGenericMethod)
            {
                var genArgs = m.GetGenericArguments();
                name += "<" + string.Join(", ", genArgs.Select(a => a.Name)) + ">";
            }

            var paramStrings = m.GetParameters().Select(param =>
            {
                string prefix = "";
                if (param.IsOut) prefix = "out ";
                else if (param.ParameterType.IsByRef) prefix = "ref ";

                if (param.IsDefined(typeof(ParamArrayAttribute)))
                    prefix = "params ";

                return prefix + FormatTypeName(param.ParameterType) + " " + param.Name;
            });

            return $"{staticPrefix}{returnType} {name}({string.Join(", ", paramStrings)})";
        }

        private static object FormatDefaultValue(object value)
        {
            if (value == null) return "null";
            if (value is string s) return $"\"{s}\"";
            if (value is bool b) return b ? "true" : "false";
            return value;
        }

        // --- Obsolete Member Detection ---

        private static string[] GetObsoleteMembers(Type type, BindingFlags flags)
        {
            var obsolete = new HashSet<string>();

            foreach (var m in type.GetMethods(flags).Where(m => !m.IsSpecialName))
            {
                if (m.GetCustomAttribute<ObsoleteAttribute>() != null)
                    obsolete.Add(m.Name);
            }
            foreach (var pr in type.GetProperties(flags))
            {
                if (pr.GetCustomAttribute<ObsoleteAttribute>() != null)
                    obsolete.Add(pr.Name);
            }
            foreach (var f in type.GetFields(flags))
            {
                if (f.GetCustomAttribute<ObsoleteAttribute>() != null)
                    obsolete.Add(f.Name);
            }
            foreach (var e in type.GetEvents(flags))
            {
                if (e.GetCustomAttribute<ObsoleteAttribute>() != null)
                    obsolete.Add(e.Name);
            }

            return obsolete.Count > 0 ? obsolete.OrderBy(n => n).ToArray() : Array.Empty<string>();
        }

        // --- Extension Method Discovery ---

        private static string[] FindExtensionMethods(Type targetType)
        {
            if (ExtensionMethodCache.TryGetValue(targetType, out var cached))
                return cached;

            var extensionNames = new HashSet<string>();
            var cache = GetAssemblyTypeCache();

            foreach (var kvp in cache)
            {
                // Extract assembly name from the FullName key (e.g., "UnityEngine, Version=...")
                string asmName = kvp.Key.Split(',')[0];
                if (!asmName.StartsWith("UnityEngine") && !asmName.StartsWith("UnityEditor") && !asmName.StartsWith("Unity."))
                    continue;

                foreach (var t in kvp.Value)
                {
                    if (!t.IsAbstract || !t.IsSealed) continue; // Static classes only
                    if (!t.IsDefined(typeof(ExtensionAttribute), false)) continue;

                    foreach (var method in t.GetMethods(BindingFlags.Public | BindingFlags.Static))
                    {
                        if (!method.IsDefined(typeof(ExtensionAttribute), false)) continue;

                        var firstParam = method.GetParameters().FirstOrDefault();
                        if (firstParam == null) continue;

                        var paramType = firstParam.ParameterType;
                        if (paramType.IsAssignableFrom(targetType) || targetType.IsSubclassOf(paramType)
                            || paramType == targetType
                            || (paramType.IsGenericType && IsGenericMatch(paramType, targetType)))
                        {
                            extensionNames.Add(method.Name);
                        }
                    }
                }
            }

            var result = extensionNames.Count > 0
                ? extensionNames.OrderBy(n => n).ToArray()
                : Array.Empty<string>();

            ExtensionMethodCache.TryAdd(targetType, result);
            return result;
        }

        private static MethodInfo[] FindExtensionMethodInfos(Type targetType, string methodName)
        {
            var results = new List<MethodInfo>();
            var cache = GetAssemblyTypeCache();

            foreach (var kvp in cache)
            {
                string asmName = kvp.Key.Split(',')[0];
                if (!asmName.StartsWith("UnityEngine") && !asmName.StartsWith("UnityEditor") && !asmName.StartsWith("Unity."))
                    continue;

                foreach (var t in kvp.Value)
                {
                    if (!t.IsAbstract || !t.IsSealed) continue;
                    if (!t.IsDefined(typeof(ExtensionAttribute), false)) continue;

                    foreach (var method in t.GetMethods(BindingFlags.Public | BindingFlags.Static))
                    {
                        if (method.Name != methodName) continue;
                        if (!method.IsDefined(typeof(ExtensionAttribute), false)) continue;

                        var firstParam = method.GetParameters().FirstOrDefault();
                        if (firstParam == null) continue;

                        var paramType = firstParam.ParameterType;
                        if (paramType.IsAssignableFrom(targetType) || targetType.IsSubclassOf(paramType)
                            || paramType == targetType
                            || (paramType.IsGenericType && IsGenericMatch(paramType, targetType)))
                        {
                            results.Add(method);
                        }
                    }
                }
            }

            return results.ToArray();
        }

        private static bool IsGenericMatch(Type genericParamType, Type targetType)
        {
            if (!genericParamType.IsGenericType) return false;

            var genDef = genericParamType.GetGenericTypeDefinition();

            // Check if targetType implements or inherits from the generic definition
            if (targetType.IsGenericType && targetType.GetGenericTypeDefinition() == genDef)
                return true;

            foreach (var iface in targetType.GetInterfaces())
            {
                if (iface.IsGenericType && iface.GetGenericTypeDefinition() == genDef)
                    return true;
            }

            return false;
        }

        // --- Scope Matching ---

        private static bool MatchesScope(string assemblyName, string scope)
        {
            switch (scope)
            {
                case "unity":
                    return assemblyName.StartsWith("UnityEngine")
                        || assemblyName.StartsWith("UnityEditor")
                        || assemblyName.StartsWith("Unity.");

                case "packages":
                    return !assemblyName.StartsWith("System")
                        && !assemblyName.StartsWith("mscorlib")
                        && !assemblyName.StartsWith("netstandard");

                case "project":
                    return assemblyName == "Assembly-CSharp"
                        || assemblyName == "Assembly-CSharp-Editor"
                        || assemblyName.StartsWith("Assembly-CSharp-firstpass")
                        || assemblyName.StartsWith("Assembly-CSharp-Editor-firstpass");

                case "all":
                    return true;

                default:
                    return false;
            }
        }
    }
}
