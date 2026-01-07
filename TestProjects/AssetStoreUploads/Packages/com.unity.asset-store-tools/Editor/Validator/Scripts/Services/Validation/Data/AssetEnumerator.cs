using System;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using Object = UnityEngine.Object;

namespace AssetStoreTools.Validator.Services.Validation
{
    internal class AssetEnumerator<T> : IEnumerator<T>, IEnumerable<T> where T : Object
    {
        public const int Capacity = 32;

        private Queue<string> _pathQueue;
        private Queue<T> _loadedAssetQueue;

        private T _currentElement;

        public AssetEnumerator(IEnumerable<string> paths)
        {
            _pathQueue = new Queue<string>(paths);
            _loadedAssetQueue = new Queue<T>();
        }

        public bool MoveNext()
        {
            bool hasPathsButHasNoAssets = _pathQueue.Count != 0 && _loadedAssetQueue.Count == 0;
            if (hasPathsButHasNoAssets)
            {
                LoadMore();
            }

            bool dequeued = false;
            if (_loadedAssetQueue.Count != 0)
            {
                _currentElement = _loadedAssetQueue.Dequeue();
                dequeued = true;
            }

            return dequeued;
        }

        private void LoadMore()
        {
            int limit = Capacity;
            while (limit > 0 && _pathQueue.Count != 0)
            {
                string path = _pathQueue.Dequeue();
                T asset = AssetDatabase.LoadAssetAtPath<T>(path);
                if (asset != null)
                {
                    _loadedAssetQueue.Enqueue(asset);
                    limit--;
                }
            }

            // Unload other loose asset references
            EditorUtility.UnloadUnusedAssetsImmediate();
        }

        public void Reset()
        {
            throw new NotSupportedException("Asset Enumerator cannot be reset.");
        }

        public T Current => _currentElement;

        object IEnumerator.Current => Current;

        public void Dispose()
        {
            // No need to dispose
        }

        IEnumerator<T> IEnumerable<T>.GetEnumerator()
        {
            return this;
        }

        public IEnumerator GetEnumerator()
        {
            return this;
        }
    }
}