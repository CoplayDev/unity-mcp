using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using MCPForUnity.Editor.Helpers;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace MCPForUnityTests.Editor.Windows
{
    public class EditorAsyncRefreshTests
    {
        private FocusRefreshTestWindow window;
        private ManualResetEventSlim release;

        [SetUp]
        public void Setup()
        {
            window = ScriptableObject.CreateInstance<FocusRefreshTestWindow>();
            window.Show();
            release = new ManualResetEventSlim(false);
        }

        [TearDown]
        public void Cleanup()
        {
            release.Set();
            window.Close();
        }

        [UnityTest]
        public IEnumerator FocusRequests_JoinOneWorker_KeepEditorUpdating_AndUseCache()
        {
            var refresh = new EditorAsyncRefresh<int>(window.rootVisualElement, 30);
            int started = 0, applied = 0, applyThread = 0;
            int mainThread = Thread.CurrentThread.ManagedThreadId;
            Func<int> work = () => { Interlocked.Increment(ref started); release.Wait(5000); return 7; };
            Action<int, bool> apply = (value, fresh) => { applied = value; applyThread = Thread.CurrentThread.ManagedThreadId; };
            for (int i = 0; i < 20; i++) refresh.Request("same", work, apply, () => true);
            yield return WaitFor(() => Volatile.Read(ref started) == 1);
            int updates = 0;
            for (int i = 0; i < 5; i++) { yield return null; updates++; }
            Assert.AreEqual(5, updates);
            Assert.AreEqual(0, applied, "A result was applied before work finished.");
            Assert.AreEqual(1, started);
            release.Set();
            yield return WaitFor(() => applied == 7);
            Assert.AreEqual(mainThread, applyThread);
            for (int i = 0; i < 20; i++) refresh.Request("same", work, apply, () => true);
            Assert.AreEqual(1, started, "Focus bypassed the successful-result TTL.");
        }

        [UnityTest]
        public IEnumerator InputChanges_DiscardOldResult_AndRunOnlyLatestQueuedRequest()
        {
            var refresh = new EditorAsyncRefresh<int>(window.rootVisualElement, 30);
            var applied = new List<int>();
            int started = 0;
            refresh.Request("old", () => { Interlocked.Increment(ref started); release.Wait(5000); return 1; },
                (value, _) => applied.Add(value), () => true);
            yield return WaitFor(() => Volatile.Read(ref started) == 1);
            refresh.Request("middle", () => { Interlocked.Increment(ref started); return 2; },
                (value, _) => applied.Add(value), () => true);
            refresh.Request("latest", () => { Interlocked.Increment(ref started); return 3; },
                (value, _) => applied.Add(value), () => true);
            release.Set();
            yield return WaitFor(() => applied.Count > 0);
            CollectionAssert.AreEqual(new[] { 3 }, applied);
            Assert.AreEqual(2, started, "Superseded requests were not coalesced.");
        }

        [UnityTest]
        public IEnumerator Detach_DiscardsCompletion_AndReattachCanRefresh()
        {
            var root = new VisualElement();
            window.rootVisualElement.Add(root);
            var refresh = new EditorAsyncRefresh<int>(root, 30);
            int started = 0, completed = 0, applied = 0;
            refresh.Request("old", () =>
            {
                Interlocked.Increment(ref started);
                release.Wait(5000);
                Interlocked.Increment(ref completed);
                return 1;
            }, (value, _) => applied = value, () => true);
            yield return WaitFor(() => Volatile.Read(ref started) == 1);
            root.RemoveFromHierarchy();
            release.Set();
            yield return WaitFor(() => Volatile.Read(ref completed) == 1);
            for (int i = 0; i < 3; i++) yield return null;
            Assert.AreEqual(0, applied);
            window.rootVisualElement.Add(root);
            refresh.Request("new", () => 2, (value, _) => applied = value, () => true);
            yield return WaitFor(() => applied == 2);
        }

        [UnityTest]
        public IEnumerator ExpiredCache_Refreshes_AndMarksOnlyNewCompletionsFresh()
        {
            var refresh = new EditorAsyncRefresh<bool>(window.rootVisualElement, 0.05);
            int runs = 0, failures = 0;
            Func<bool> work = () => { Interlocked.Increment(ref runs); return false; };
            Action<bool, bool> apply = (value, fresh) => { if (fresh && !value) failures++; };
            refresh.Request("endpoint", work, apply, () => true);
            yield return WaitFor(() => failures == 1);
            for (int i = 0; i < 20; i++) refresh.Request("endpoint", work, apply, () => true);
            Assert.AreEqual(1, failures, "Cached failures counted as new probes.");
            double expires = EditorApplication.timeSinceStartup + 0.1;
            while (EditorApplication.timeSinceStartup < expires) yield return null;
            refresh.Request("endpoint", work, apply, () => true);
            yield return WaitFor(() => failures == 2);
            Assert.AreEqual(2, runs);
        }

        [UnityTest]
        public IEnumerator SettingsChangedWithoutAnotherRequest_DoNotApplyStaleResult()
        {
            var refresh = new EditorAsyncRefresh<int>(window.rootVisualElement, 30);
            int started = 0, completed = 0, applied = 0;
            bool sameSettings = true;
            refresh.Request("old", () =>
            {
                Interlocked.Increment(ref started);
                release.Wait(5000);
                Interlocked.Increment(ref completed);
                return 1;
            }, (value, _) => applied = value, () => sameSettings);
            yield return WaitFor(() => Volatile.Read(ref started) == 1);
            sameSettings = false;
            release.Set();
            yield return WaitFor(() => Volatile.Read(ref completed) == 1);
            for (int i = 0; i < 3; i++) yield return null;
            Assert.AreEqual(0, applied);
        }

        internal static IEnumerator WaitFor(Func<bool> condition)
        {
            double deadline = EditorApplication.timeSinceStartup + 10;
            while (!condition() && EditorApplication.timeSinceStartup < deadline) yield return null;
            Assert.IsTrue(condition(), "Asynchronous refresh did not reach the expected state.");
        }
    }
}
