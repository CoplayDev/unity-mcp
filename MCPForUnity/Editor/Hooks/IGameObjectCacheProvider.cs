namespace MCPForUnity.Editor.Hooks
{
    /// <summary>
    /// Interface for providing cached GameObject data.
    /// Used by UnityEventHooks to decouple from ActionTrace-specific implementations.
    /// Implementations can provide cached names and GlobalIDs for destroyed GameObjects.
    /// </summary>
    public interface IGameObjectCacheProvider
    {
        /// <summary>
        /// Get the cached name for a GameObject by its InstanceID.
        /// Returns null if the GameObject is not in the cache.
        /// </summary>
        /// <param name="instanceId">The InstanceID of the GameObject</param>
        /// <returns>The cached name, or null if not found</returns>
        string GetCachedName(int instanceId);

        /// <summary>
        /// Get the cached GlobalID for a GameObject by its InstanceID.
        /// Returns null if the GameObject is not in the cache.
        /// </summary>
        /// <param name="instanceId">The InstanceID of the GameObject</param>
        /// <returns>The cached GlobalID, or null if not found</returns>
        string GetCachedGlobalId(int instanceId);

        /// <summary>
        /// Register a GameObject for tracking.
        /// Called when a GameObject is selected or has a component added.
        /// </summary>
        /// <param name="gameObject">The GameObject to register</param>
        void RegisterGameObject(UnityEngine.GameObject gameObject);

        /// <summary>
        /// Detect and report GameObject changes (created and destroyed objects).
        /// </summary>
        /// <param name="onCreated">Callback for newly created GameObjects</param>
        /// <param name="onDestroyed">Callback for destroyed GameObjects with InstanceID</param>
        void DetectChanges(
            System.Action<UnityEngine.GameObject> onCreated,
            System.Action<int> onDestroyed);

        /// <summary>
        /// Initialize the tracking system.
        /// Should be called once when the editor loads.
        /// </summary>
        void InitializeTracking();

        /// <summary>
        /// Reset all tracking state.
        /// Called when scenes change or on domain reload.
        /// </summary>
        void Reset();
    }
}
