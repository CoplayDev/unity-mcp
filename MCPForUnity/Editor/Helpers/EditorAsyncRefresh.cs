using System;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine.UIElements;

namespace MCPForUnity.Editor.Helpers
{
    /// <summary>Runs bounded read-only work off-thread and applies current results on editor update.</summary>
    internal sealed class EditorAsyncRefresh<T>
    {
        private readonly double ttlSeconds;
        private Task<T> task;
        private Func<T> pendingWork;
        private Action<T, bool> apply;
        private Action<Exception> onFailure;
        private Func<bool> isCurrent;
        private string requestedKey, runningKey, cachedKey;
        private int generation, runningGeneration;
        private double completedAt;
        private T cached;
        private bool attached = true;

        internal EditorAsyncRefresh(VisualElement root, double ttlSeconds)
        {
            this.ttlSeconds = ttlSeconds;
            root.RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                attached = false;
                Invalidate();
                pendingWork = null;
                EditorApplication.update -= Poll;
            });
            root.RegisterCallback<AttachToPanelEvent>(_ => attached = true);
        }

        internal void Invalidate()
        {
            generation++;
            cachedKey = null;
        }

        internal void Request(string key, Func<T> work, Action<T, bool> onResult,
            Func<bool> stillCurrent, bool force = false, Action<Exception> onFailure = null)
        {
            if (!attached) return;
            if (requestedKey != key || force) Invalidate();
            requestedKey = key;
            apply = onResult;
            isCurrent = stillCurrent;
            this.onFailure = onFailure;
            if (!force && cachedKey == key && EditorApplication.timeSinceStartup - completedAt < ttlSeconds)
            {
                if (stillCurrent()) onResult(cached, false);
                return;
            }
            pendingWork = work;
            if (task == null) Start();
            else
            {
                // A focus event with the same inputs joins the existing request.
                if (runningKey == key && runningGeneration == generation) pendingWork = null;
                EditorApplication.update -= Poll;
                EditorApplication.update += Poll;
            }
        }

        private void Start()
        {
            var work = pendingWork;
            pendingWork = null;
            runningKey = requestedKey;
            runningGeneration = generation;
            task = Task.Run(work);
            EditorApplication.update -= Poll;
            EditorApplication.update += Poll;
        }

        private void Poll()
        {
            if (task == null || !task.IsCompleted) return;
            EditorApplication.update -= Poll;
            var completed = task;
            task = null;
            bool current = attached && runningGeneration == generation && isCurrent();
            try
            {
                T result = completed.GetAwaiter().GetResult();
                if (current)
                {
                    cached = result;
                    cachedKey = runningKey;
                    completedAt = EditorApplication.timeSinceStartup;
                    apply(result, true);
                }
            }
            catch (Exception ex)
            {
                // Avoid including private file contents, paths or credentials in refresh logs.
                if (current) McpLog.Warn("Window refresh failed; retry the refresh after checking your settings.");
                if (current && completed.IsFaulted && onFailure != null)
                {
                    try { onFailure(ex); }
                    catch (Exception)
                    {
                        McpLog.Warn("Window refresh failure handler failed; retry the refresh after checking your settings.");
                    }
                }
            }
            if (attached && pendingWork != null) Start();
        }
    }
}
