using System;
using UnityEngine;
using MCPForUnity.Editor.Hooks;

namespace MCPForUnity.Editor.ActionTrace.Sources.Helpers
{
    /// <summary>
    /// ActionTrace's implementation of IGameObjectCacheProvider.
    /// Adapter that wraps GameObjectTrackingHelper to provide the interface.
    /// </summary>
    internal sealed class GameObjectTrackingCacheProvider : IGameObjectCacheProvider
    {
        private readonly GameObjectTrackingHelper _helper;

        public GameObjectTrackingCacheProvider(GameObjectTrackingHelper helper)
        {
            _helper = helper ?? throw new ArgumentNullException(nameof(helper));
        }

        public string GetCachedName(int instanceId)
        {
            return _helper.GetCachedName(instanceId);
        }

        public string GetCachedGlobalId(int instanceId)
        {
            return _helper.GetCachedGlobalId(instanceId);
        }

        public void RegisterGameObject(GameObject gameObject)
        {
            // Cache the GameObject for later component removal tracking
            // (This is handled by UnityEventHooks.Advanced.TrackComponentRemoval)
        }

        public void DetectChanges(Action<GameObject> onCreated, Action<int> onDestroyed)
        {
            var result = _helper.DetectChanges();

            foreach (var change in result.Changes)
            {
                if (change.isNew) onCreated?.Invoke(change.obj);
            }

            foreach (int id in result.DestroyedIds)
            {
                onDestroyed?.Invoke(id);
            }
        }

        public void InitializeTracking()
        {
            _helper.InitializeTracking();
        }

        public void Reset()
        {
            _helper.Reset();
        }
    }
}
