namespace MCPForUnity.Runtime.Helpers
{
    /// <summary>
    /// Index of the version-compatibility shims MCP for Unity ships. These wrap Unity APIs
    /// that have been renamed, deprecated, or scheduled for removal across the Unity versions
    /// the package targets (2021 LTS → 6.x → CoreCLR 6.8). Routing through a shim keeps
    /// CS0618 warnings out of the build and survives the eventual property/method removal
    /// without recompiling call sites.
    ///
    /// Active shims (deprecated_since → removed_in):
    ///   • <see cref="UnityFindObjectsCompat"/> — Object.FindObjectsOfType → FindObjectsByType (2023.1)
    ///   • <see cref="UnityObjectIdCompat"/>    — InstanceID ↔ EntityId (6000.3 → 6000.6 CS0619)
    ///   • <see cref="UnityPhysicsCompat"/>     — Physics{,2D}.autoSyncTransforms (6000.0),
    ///                                            Physics{,2D}.autoSimulation → simulationMode (2022.2)
    ///   • <see cref="UnityAssembliesCompat"/>  — AppDomain.GetAssemblies →
    ///                                            UnityEngine.Assemblies.CurrentAssemblies (Unity 6.8 CoreCLR)
    ///
    /// When to add a new shim:
    ///   1. The API is marked [Obsolete] AND the call site can't simply be deleted, OR
    ///   2. Three or more call sites need version gating for the same API, OR
    ///   3. A future Unity version has announced rename or removal.
    ///
    /// What does NOT belong in a shim: hot-path engine APIs (Transform.position, Vector3.*,
    /// GetComponent&lt;T&gt;), APIs Unity has not threatened to break (Mathf, Quaternion,
    /// most of AssetDatabase), and editor-internal undocumented APIs (those should break
    /// loudly so the package maintainers notice).
    ///
    /// Pattern: prefer #if UNITY_*_OR_NEWER for static dispatch when the new API exists in
    /// the SDK we compile against; use runtime reflection with a cached MethodInfo /
    /// PropertyInfo when the new API is in a version we don't yet target, or when the old
    /// API may eventually be removed (CS0619). Fail-soft: callers should treat missing APIs
    /// as no-ops, not throw.
    /// </summary>
    /// <remarks>
    /// This class is intentionally empty — its purpose is to anchor the catalog so any
    /// reader can <c>F12</c> from a shim file and land on the full list and policy.
    /// </remarks>
    public static class UnityCompatShims
    {
    }
}
