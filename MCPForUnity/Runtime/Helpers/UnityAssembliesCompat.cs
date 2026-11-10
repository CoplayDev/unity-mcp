using System;
using System.Linq;
using System.Reflection;

namespace MCPForUnity.Runtime.Helpers
{
    // Part of MCP for Unity's compat-shim family. See UnityCompatShims.cs in this
    // folder for the full list of shims, the audit policy, and the reflection pattern.
    /// <summary>
    /// Version-compatible wrappers for enumerating and loading managed assemblies.
    /// Unity 7 uses its assembly load context and tracks paths for stream-loaded assemblies.
    /// Earlier versions retain the reflection-based enumeration shim and legacy loading APIs.
    /// </summary>
    public static class UnityAssembliesCompat
    {
        // Candidate assembly-qualified names to try BEFORE falling back to a full
        // assembly scan. Probing by name avoids calling AppDomain.GetAssemblies on
        // CoreCLR (where it emits warnings) in the common case.
#if !UNITY_7000_0_OR_NEWER
        private static readonly string[] CurrentAssembliesAqns =
        {
            "UnityEngine.Assemblies.CurrentAssemblies, UnityEngine.CoreModule",
            "UnityEngine.Assemblies.CurrentAssemblies, UnityEngine",
            "UnityEngine.Assemblies.CurrentAssemblies, UnityEditor.CoreModule",
            "UnityEngine.Assemblies.CurrentAssemblies, UnityEditor",
        };

        private static Func<Assembly[]> _getLoadedAssemblies;
        private static bool _probed;
#endif

        /// <summary>
        /// Returns all currently loaded managed assemblies in this Unity process.
        /// Unity 7 calls CurrentAssemblies directly and converts its read-only list to an array.
        /// Earlier versions retain the reflection shim and AppDomain fallback.
        /// </summary>
        public static Assembly[] GetLoadedAssemblies()
        {
#if UNITY_7000_0_OR_NEWER
            return UnityEngine.Assemblies.CurrentAssemblies.GetLoadedAssemblies().ToArray();
#else
            if (!_probed)
            {
                _probed = true;
                _getLoadedAssemblies = ResolveCurrentAssembliesDelegate();
            }

            if (_getLoadedAssemblies != null)
            {
                try
                {
                    return _getLoadedAssemblies();
                }
                catch
                {
                    // If the new API throws for any reason, fall through to the legacy path.
                }
            }

            return AppDomain.CurrentDomain.GetAssemblies();
#endif
        }

        public static Assembly LoadFromBytes(byte[] bytes)
        {
#if UNITY_7000_0_OR_NEWER
            return UnityEngine.Assemblies.CurrentAssemblies.LoadFromBytes(bytes);
#else
            return Assembly.Load(bytes);
#endif
        }

        public static string GetAssemblyPath(Assembly assembly)
        {
#if UNITY_7000_0_OR_NEWER
            return UnityEngine.AssemblyExtension.GetLoadedAssemblyPath(assembly);
#else
            return assembly.Location;
#endif
        }

#if !UNITY_7000_0_OR_NEWER
        private static Func<Assembly[]> ResolveCurrentAssembliesDelegate()
        {
            // 1. Try direct AQN lookups first — cheap, side-effect-free, and
            //    avoids touching AppDomain.GetAssemblies on CoreCLR.
            foreach (var aqn in CurrentAssembliesAqns)
            {
                Type type;
                try { type = Type.GetType(aqn, throwOnError: false); }
                catch { type = null; }

                var del = TryBindGetLoadedAssemblies(type);
                if (del != null) return del;
            }

            // 2. Fallback: scan every loaded assembly. This still uses the legacy
            //    enumeration API but only runs once during the bootstrap probe,
            //    and only when none of the AQNs above resolved.
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type;
                try { type = asm.GetType("UnityEngine.Assemblies.CurrentAssemblies", throwOnError: false); }
                catch { continue; }

                var del = TryBindGetLoadedAssemblies(type);
                if (del != null) return del;
            }

            return null;
        }

        private static Func<Assembly[]> TryBindGetLoadedAssemblies(Type type)
        {
            if (type == null) return null;

            var method = type.GetMethod(
                "GetLoadedAssemblies",
                BindingFlags.Public | BindingFlags.Static,
                null,
                Type.EmptyTypes,
                null);

            if (method == null || !typeof(Assembly[]).IsAssignableFrom(method.ReturnType))
                return null;

            try
            {
                return (Func<Assembly[]>)Delegate.CreateDelegate(typeof(Func<Assembly[]>), method);
            }
            catch
            {
                return () => (Assembly[])method.Invoke(null, null);
            }
        }
#endif
    }
}
