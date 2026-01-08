using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Compilation;
#endif

namespace MCPForUnity.Editor.Helpers
{
    /// <summary>
    /// Unified type resolution for Unity types (Components, ScriptableObjects, etc.).
    /// Extracted from ComponentResolver in ManageGameObject and ResolveType in ManageScriptableObject.
    /// Features: caching, prioritizes Player assemblies over Editor assemblies, uses TypeCache.
    /// </summary>
    public static class UnityTypeResolver
    {
        private static readonly Dictionary<string, Type> CacheByFqn = new(StringComparer.Ordinal);
        private static readonly Dictionary<string, Type> CacheByName = new(StringComparer.Ordinal);

        /// <summary>
        /// Resolves a type by name, with optional base type constraint.
        /// Caches results for performance. Prefers runtime assemblies over Editor assemblies.
        /// </summary>
        /// <param name="typeName">The short name or fully-qualified name of the type</param>
        /// <param name="type">The resolved type, or null if not found</param>
        /// <param name="error">Error message if resolution failed</param>
        /// <param name="requiredBaseType">Optional base type constraint (e.g., typeof(Component))</param>
        /// <returns>True if type was resolved successfully</returns>
        public static bool TryResolve(string typeName, out Type type, out string error, Type requiredBaseType = null)
        {
            error = string.Empty;
            type = null;

            if (string.IsNullOrWhiteSpace(typeName))
            {
                error = "Type name cannot be null or empty";
                return false;
            }

            // Check caches
            if (CacheByFqn.TryGetValue(typeName, out type) && PassesConstraint(type, requiredBaseType))
                return true;
            if (!typeName.Contains(".") && CacheByName.TryGetValue(typeName, out type) && PassesConstraint(type, requiredBaseType))
                return true;

            // Try direct Type.GetType
            type = Type.GetType(typeName, throwOnError: false);
            if (type != null && PassesConstraint(type, requiredBaseType))
            {
                Cache(type);
                return true;
            }

            // Search loaded assemblies (prefer Player assemblies)
            var candidates = FindCandidates(typeName, requiredBaseType);
            if (candidates.Count == 1)
            {
                type = candidates[0];
                Cache(type);
                return true;
            }
            if (candidates.Count > 1)
            {
                error = FormatAmbiguityError(typeName, candidates);
                type = null;
                return false;
            }

#if UNITY_EDITOR
            // Last resort: TypeCache (fast index)
            if (requiredBaseType != null)
            {
                var tc = TypeCache.GetTypesDerivedFrom(requiredBaseType)
                                  .Where(t => NamesMatch(t, typeName));
                candidates = PreferPlayer(tc).ToList();
                if (candidates.Count == 1)
                {
                    type = candidates[0];
                    Cache(type);
                    return true;
                }
                if (candidates.Count > 1)
                {
                    error = FormatAmbiguityError(typeName, candidates);
                    type = null;
                    return false;
                }
            }
#endif

            error = $"Type '{typeName}' not found in loaded runtime assemblies. " +
                    "Use a fully-qualified name (Namespace.TypeName) and ensure the script compiled.";
            type = null;
            return false;
        }

        /// <summary>
        /// Convenience method to resolve a Component type.
        /// </summary>
        public static Type ResolveComponent(string typeName)
        {
            if (TryResolve(typeName, out Type type, out _, typeof(Component)))
                return type;
            return null;
        }

        /// <summary>
        /// Convenience method to resolve a ScriptableObject type.
        /// </summary>
        public static Type ResolveScriptableObject(string typeName)
        {
            if (TryResolve(typeName, out Type type, out _, typeof(ScriptableObject)))
                return type;
            return null;
        }

        /// <summary>
        /// Convenience method to resolve any type without constraints.
        /// </summary>
        public static Type ResolveAny(string typeName)
        {
            if (TryResolve(typeName, out Type type, out _, null))
                return type;
            return null;
        }

        // --- Private Helpers ---

        private static bool PassesConstraint(Type type, Type requiredBaseType)
        {
            if (type == null) return false;
            if (requiredBaseType == null) return true;
            return requiredBaseType.IsAssignableFrom(type);
        }

        private static bool NamesMatch(Type t, string query) =>
            t.Name.Equals(query, StringComparison.Ordinal) ||
            (t.FullName?.Equals(query, StringComparison.Ordinal) ?? false);

        private static void Cache(Type t)
        {
            if (t == null) return;
            if (t.FullName != null) CacheByFqn[t.FullName] = t;
            CacheByName[t.Name] = t;
        }

        private static List<Type> FindCandidates(string query, Type requiredBaseType)
        {
            bool isShort = !query.Contains('.');
            var loaded = AppDomain.CurrentDomain.GetAssemblies();

#if UNITY_EDITOR
            // Names of Player (runtime) script assemblies
            var playerAsmNames = new HashSet<string>(
                CompilationPipeline.GetAssemblies(AssembliesType.Player).Select(a => a.name),
                StringComparer.Ordinal);

            var playerAsms = loaded.Where(a => playerAsmNames.Contains(a.GetName().Name));
            var editorAsms = loaded.Except(playerAsms);
#else
            var playerAsms = loaded;
            var editorAsms = Array.Empty<System.Reflection.Assembly>();
#endif

            Func<Type, bool> match = isShort
                ? (t => t.Name.Equals(query, StringComparison.Ordinal))
                : (t => t.FullName?.Equals(query, StringComparison.Ordinal) ?? false);

            var fromPlayer = playerAsms.SelectMany(SafeGetTypes)
                                       .Where(t => PassesConstraint(t, requiredBaseType))
                                       .Where(match);
            var fromEditor = editorAsms.SelectMany(SafeGetTypes)
                                       .Where(t => PassesConstraint(t, requiredBaseType))
                                       .Where(match);

            // Prefer Player over Editor
            var candidates = fromPlayer.ToList();
            if (candidates.Count == 0)
                candidates = fromEditor.ToList();

            return candidates;
        }

        private static IEnumerable<Type> SafeGetTypes(System.Reflection.Assembly assembly)
        {
            try { return assembly.GetTypes(); }
            catch (ReflectionTypeLoadException rtle) { return rtle.Types.Where(t => t != null); }
            catch { return Enumerable.Empty<Type>(); }
        }

        private static IEnumerable<Type> PreferPlayer(IEnumerable<Type> types)
        {
#if UNITY_EDITOR
            var playerAsmNames = new HashSet<string>(
                CompilationPipeline.GetAssemblies(AssembliesType.Player).Select(a => a.name),
                StringComparer.Ordinal);

            var list = types.ToList();
            var fromPlayer = list.Where(t => playerAsmNames.Contains(t.Assembly.GetName().Name)).ToList();
            return fromPlayer.Count > 0 ? fromPlayer : list;
#else
            return types;
#endif
        }

        private static string FormatAmbiguityError(string query, List<Type> candidates)
        {
            var names = string.Join(", ", candidates.Take(5).Select(t => t.FullName));
            if (candidates.Count > 5) names += $" ... ({candidates.Count - 5} more)";
            return $"Ambiguous type reference '{query}'. Found {candidates.Count} matches: [{names}]. Use a fully-qualified name.";
        }
    }
}

