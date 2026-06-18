using System;
using System.Reflection;
using UnityEngine;

namespace MCPForUnity.Runtime.Helpers
{
    // Part of MCP for Unity's compat-shim family. See UnityCompatShims.cs in this
    // folder for the full list of shims, the audit policy, and the reflection pattern.
    /// <summary>
    /// Version-compatible wrappers for Physics / Physics2D properties whose surface
    /// changes across Unity versions.
    ///
    /// Currently covered:
    ///   - Physics.autoSyncTransforms     (deprecated in Unity 6.x; replacement is Physics.SyncTransforms())
    ///   - Physics2D.autoSyncTransforms   (deprecated in Unity 6.x; replacement is Physics2D.SyncTransforms())
    ///   - Physics.autoSimulation         (deprecated in 2022.2; replacement is Physics.simulationMode)
    ///
    /// We use reflection rather than direct property access so calls stay clean of
    /// CS0618 warnings AND survive eventual removal of the obsolete property without
    /// a recompile of this package.
    /// </summary>
    public static class UnityPhysicsCompat
    {
        /// <summary>
        /// Cross-version description of the 3D physics simulation mode.
        /// On 2022.2+ this maps onto <c>UnityEngine.SimulationMode</c>; on older
        /// versions it represents the on/off semantics of <c>autoSimulation</c>.
        /// </summary>
        public enum SimulationMode
        {
            FixedUpdate,
            Update,
            Script,
            Unknown,
        }

        // ---------- Physics2D ----------

        private static PropertyInfo _physics2DAutoSync;
        private static bool _physics2DProbed;

        private static PropertyInfo Physics2DAutoSyncProp
        {
            get
            {
                if (!_physics2DProbed)
                {
                    _physics2DProbed = true;
                    _physics2DAutoSync = typeof(Physics2D).GetProperty(
                        "autoSyncTransforms",
                        BindingFlags.Public | BindingFlags.Static);
                }
                return _physics2DAutoSync;
            }
        }

        /// <summary>
        /// Reads <c>Physics2D.autoSyncTransforms</c> if the property exists in this
        /// Unity version. Returns <c>null</c> if the property has been removed.
        /// </summary>
        public static bool? GetPhysics2DAutoSyncTransforms()
        {
            var prop = Physics2DAutoSyncProp;
            if (prop == null || !prop.CanRead) return null;
            try { return (bool)prop.GetValue(null); }
            catch { return null; }
        }

        /// <summary>
        /// Writes <c>Physics2D.autoSyncTransforms</c> if the property exists and is
        /// writable. Returns <c>true</c> if the write happened, <c>false</c> if the
        /// property is unavailable in this Unity version.
        /// </summary>
        public static bool TrySetPhysics2DAutoSyncTransforms(bool value)
        {
            var prop = Physics2DAutoSyncProp;
            if (prop == null || !prop.CanWrite) return false;
            try
            {
                prop.SetValue(null, value);
                return true;
            }
            catch
            {
                return false;
            }
        }

        // ---------- Physics (3D) ----------

        private static PropertyInfo _physicsAutoSync;
        private static bool _physicsProbed;

        private static PropertyInfo PhysicsAutoSyncProp
        {
            get
            {
                if (!_physicsProbed)
                {
                    _physicsProbed = true;
                    _physicsAutoSync = typeof(Physics).GetProperty(
                        "autoSyncTransforms",
                        BindingFlags.Public | BindingFlags.Static);
                }
                return _physicsAutoSync;
            }
        }

        /// <summary>
        /// Reads <c>Physics.autoSyncTransforms</c> if the property exists in this
        /// Unity version. Returns <c>null</c> if the property has been removed.
        /// </summary>
        public static bool? GetPhysicsAutoSyncTransforms()
        {
            var prop = PhysicsAutoSyncProp;
            if (prop == null || !prop.CanRead) return null;
            try { return (bool)prop.GetValue(null); }
            catch { return null; }
        }

        /// <summary>
        /// Writes <c>Physics.autoSyncTransforms</c> if the property exists and is
        /// writable. Returns <c>true</c> if the write happened, <c>false</c> if the
        /// property is unavailable in this Unity version.
        /// </summary>
        public static bool TrySetPhysicsAutoSyncTransforms(bool value)
        {
            var prop = PhysicsAutoSyncProp;
            if (prop == null || !prop.CanWrite) return false;
            try
            {
                prop.SetValue(null, value);
                return true;
            }
            catch
            {
                return false;
            }
        }

        // ---------- Physics simulation mode (3D) ----------
        // 2022.2+ : UnityEngine.Physics.simulationMode (UnityEngine.SimulationMode enum)
        // <2022.2 : UnityEngine.Physics.autoSimulation (bool — true ⇔ FixedUpdate, false ⇔ Script)

        private static PropertyInfo _physicsSimulationMode;
        private static bool _physicsSimulationModeProbed;
        private static PropertyInfo _physicsAutoSimulation;
        private static bool _physicsAutoSimulationProbed;

        private static PropertyInfo PhysicsSimulationModeProp
        {
            get
            {
                if (!_physicsSimulationModeProbed)
                {
                    _physicsSimulationModeProbed = true;
                    _physicsSimulationMode = typeof(Physics).GetProperty(
                        "simulationMode",
                        BindingFlags.Public | BindingFlags.Static);
                }
                return _physicsSimulationMode;
            }
        }

        private static PropertyInfo PhysicsAutoSimulationProp
        {
            get
            {
                if (!_physicsAutoSimulationProbed)
                {
                    _physicsAutoSimulationProbed = true;
                    _physicsAutoSimulation = typeof(Physics).GetProperty(
                        "autoSimulation",
                        BindingFlags.Public | BindingFlags.Static);
                }
                return _physicsAutoSimulation;
            }
        }

        /// <summary>
        /// Reads the current 3D physics simulation mode in a Unity-version-agnostic way.
        /// Returns <see cref="SimulationMode.Unknown"/> if neither API is available.
        /// </summary>
        public static SimulationMode GetPhysicsSimulationMode()
        {
            var modeProp = PhysicsSimulationModeProp;
            if (modeProp != null && modeProp.CanRead)
            {
                try { return ParseSimulationMode(modeProp.GetValue(null)?.ToString()); }
                catch { /* fall through */ }
            }

            var autoProp = PhysicsAutoSimulationProp;
            if (autoProp != null && autoProp.CanRead)
            {
                try
                {
                    var auto = (bool)autoProp.GetValue(null);
                    return auto ? SimulationMode.FixedUpdate : SimulationMode.Script;
                }
                catch { /* fall through */ }
            }

            return SimulationMode.Unknown;
        }

        /// <summary>
        /// Sets the 3D physics simulation mode in a Unity-version-agnostic way.
        /// Returns false if the requested mode isn't expressible on this Unity version
        /// (e.g. Update mode on pre-2022.2).
        /// </summary>
        public static bool TrySetPhysicsSimulationMode(SimulationMode mode)
        {
            var modeProp = PhysicsSimulationModeProp;
            if (modeProp != null && modeProp.CanWrite)
            {
                try
                {
                    var enumType = modeProp.PropertyType;
                    var enumValue = Enum.Parse(enumType, mode.ToString(), ignoreCase: true);
                    modeProp.SetValue(null, enumValue);
                    return true;
                }
                catch { /* fall through to legacy bool path */ }
            }

            var autoProp = PhysicsAutoSimulationProp;
            if (autoProp != null && autoProp.CanWrite)
            {
                try
                {
                    switch (mode)
                    {
                        case SimulationMode.FixedUpdate:
                            autoProp.SetValue(null, true);
                            return true;
                        case SimulationMode.Script:
                            autoProp.SetValue(null, false);
                            return true;
                        // Update mode does not exist pre-2022.2 — caller must handle false.
                    }
                }
                catch { /* fall through */ }
            }

            return false;
        }

        private static SimulationMode ParseSimulationMode(string s)
        {
            if (string.IsNullOrEmpty(s)) return SimulationMode.Unknown;
            switch (s.ToLowerInvariant())
            {
                case "fixedupdate": return SimulationMode.FixedUpdate;
                case "update": return SimulationMode.Update;
                case "script": return SimulationMode.Script;
                default: return SimulationMode.Unknown;
            }
        }
    }
}
