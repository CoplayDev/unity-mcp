using System;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;

namespace MCPForUnity.Editor.Helpers
{
    /// <summary>
    /// Executes coroutines in the Unity Editor using EditorApplication.update.
    /// This allows async-style operations that span multiple frames.
    /// Shared utility used by both Tools and Resources for async command execution.
    /// </summary>
    public static class EditorCoroutineExecutor
    {
        private class CoroutineHandle
        {
            public IEnumerator Enumerator;
            public Action<object> OnComplete;
            public Action<Exception> OnError;
            public bool IsComplete;
            public object Result;
        }

        private static List<CoroutineHandle> activeCoroutines = new List<CoroutineHandle>();
        private static bool isInitialized = false;

        /// <summary>
        /// Start a coroutine that runs in the editor.
        /// </summary>
        /// <param name="routine">The coroutine to execute</param>
        /// <param name="onComplete">Called when coroutine completes with the final yielded value</param>
        /// <param name="onError">Called if coroutine throws an exception</param>
        public static void StartCoroutine(
            IEnumerator routine,
            Action<object> onComplete = null,
            Action<Exception> onError = null)
        {
            if (routine == null)
            {
                onError?.Invoke(new ArgumentNullException(nameof(routine)));
                return;
            }

            EnsureInitialized();

            var handle = new CoroutineHandle
            {
                Enumerator = routine,
                OnComplete = onComplete,
                OnError = onError,
                IsComplete = false
            };

            lock (activeCoroutines)
            {
                activeCoroutines.Add(handle);
            }
        }

        private static void EnsureInitialized()
        {
            if (isInitialized) return;

            EditorApplication.update += Update;
            isInitialized = true;
        }

        private static void Update()
        {
            if (activeCoroutines.Count == 0) return;

            // Snapshot to avoid modification during iteration
            List<CoroutineHandle> toProcess;
            lock (activeCoroutines)
            {
                toProcess = new List<CoroutineHandle>(activeCoroutines);
            }

            var completedCoroutines = new List<CoroutineHandle>();

            foreach (var handle in toProcess)
            {
                if (handle.IsComplete) continue;

                try
                {
                    bool hasMore = handle.Enumerator.MoveNext();

                    if (hasMore)
                    {
                        // Store the current value as potential result
                        handle.Result = handle.Enumerator.Current;
                    }
                    else
                    {
                        // Coroutine completed
                        handle.IsComplete = true;
                        completedCoroutines.Add(handle);
                        handle.OnComplete?.Invoke(handle.Result);
                    }
                }
                catch (Exception ex)
                {
                    handle.IsComplete = true;
                    completedCoroutines.Add(handle);

                    // Wrap exception with more context
                    var wrappedException = new Exception(
                        $"Exception in coroutine MoveNext(): {ex.Message}",
                        ex
                    );
                    handle.OnError?.Invoke(wrappedException);
                }
            }

            // Remove completed coroutines
            if (completedCoroutines.Count > 0)
            {
                lock (activeCoroutines)
                {
                    foreach (var completed in completedCoroutines)
                    {
                        activeCoroutines.Remove(completed);
                    }
                }
            }
        }

        /// <summary>
        /// Stop all running coroutines. Called during cleanup.
        /// </summary>
        public static void StopAllCoroutines()
        {
            lock (activeCoroutines)
            {
                activeCoroutines.Clear();
            }
        }
    }
}
