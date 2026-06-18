using System;
using System.Reflection;

namespace MCPForUnity.Runtime.Helpers
{
    // Part of MCP for Unity's compat-shim family. See UnityCompatShims.cs in this
    // folder for the full list of shims, the audit policy, and the reflection pattern.
    /// <summary>
    /// Version-compatible wrapper for enumerating loaded assemblies.
    ///
    /// API timeline:
    ///   Pre-6.8           : AppDomain.CurrentDomain.GetAssemblies()
    ///   6.8 (CoreCLR)     : UnityEngine.Assemblies.CurrentAssemblies.GetLoadedAssemblies()
    ///                       (Unity 6.8 replaces Mono with CoreCLR and warns on
    ///                        AppDomain.GetAssemblies — see the official Path to
    ///                        CoreCLR 2026 upgrade guide.)
    ///
    /// Uses runtime reflection to discover the new API once and cache a delegate,
    /// so calling code stays warning-free across versions and survives the
    /// eventual removal of AppDomain.GetAssemblies if Unity ever takes that step.
    /// </summary>
    public static class UnityAssembliesCompat
    {
        // Candidate assembly-qualified names to try BEFORE falling back to a full
        // assembly scan. Probing by name avoids calling AppDomain.GetAssemblies on
        // CoreCLR (where it emits warnings) in the common case.
        private static readonly string[] CurrentAssembliesAqns =
        {
            "UnityEngine.Assemblies.CurrentAssemblies, UnityEngine.CoreModule",
            "UnityEngine.Assemblies.CurrentAssemblies, UnityEngine",
            "UnityEngine.Assemblies.CurrentAssemblies, UnityEditor.CoreModule",
            "UnityEngine.Assemblies.CurrentAssemblies, UnityEditor",
        };

        private static Func<Assembly[]> _getLoadedAssemblies;
        private static bool _probed;

        /// <summary>
        /// Returns all currently loaded managed assemblies in this Unity process.
        /// On Unity 6.8+ (CoreCLR) this dispatches to
        /// <c>UnityEngine.Assemblies.CurrentAssemblies.GetLoadedAssemblies()</c>;
        /// otherwise falls back to <c>AppDomain.CurrentDomain.GetAssemblies()</c>.
        /// </summary>
        public static Assembly[] GetLoadedAssemblies()
        {
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
        }

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
    }
}
