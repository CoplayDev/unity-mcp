using System;
using System.Diagnostics;
using System.Threading;
using UnityEditor;

namespace MCPForUnity.Editor.Services
{
    /// <summary>
    /// Records that Unity's main thread is alive by stamping a timestamp from
    /// <see cref="EditorApplication.update"/>. Read from background threads (websocket receive loop,
    /// stdio socket thread) to tell "main thread is stalled" apart from "the process is gone".
    ///
    /// The stamp MUST be written by the main thread. A heartbeat written by whichever thread happens
    /// to answer the liveness request only proves that thread ran, and reports a healthy Editor while
    /// the main thread is frozen behind a modal dialog.
    /// </summary>
    [InitializeOnLoad]
    internal static class MainThreadHeartbeat
    {
        private static readonly Stopwatch Clock = Stopwatch.StartNew();
        private static long _lastTickMs;
        private static long _tickCount;

        static MainThreadHeartbeat()
        {
            Interlocked.Exchange(ref _lastTickMs, Clock.ElapsedMilliseconds);
            EditorApplication.update += Beat;
        }

        private static void Beat()
        {
            Interlocked.Exchange(ref _lastTickMs, Clock.ElapsedMilliseconds);
            Interlocked.Increment(ref _tickCount);
        }

        /// <summary>Milliseconds since the main thread last ran an editor update tick.</summary>
        internal static long StallMs
        {
            get
            {
                long last = Interlocked.Read(ref _lastTickMs);
                return Math.Max(0, Clock.ElapsedMilliseconds - last);
            }
        }

        /// <summary>Total editor update ticks observed since this domain loaded.</summary>
        internal static long TickCount => Interlocked.Read(ref _tickCount);
    }
}
