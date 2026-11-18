using System;
using System.Diagnostics;
using System.Threading;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Services.Transport;
using Newtonsoft.Json.Linq;
using UnityEditor;

namespace MCPForUnity.Editor.Services
{
    /// <summary>
    /// Answers "is Unity's main thread alive, and if not, why" without touching the main thread.
    ///
    /// The modal scan runs on a dedicated sampler thread, never on the request path: reading window
    /// text can block when the main thread is not pumping, which would stall the very answer that
    /// reports the stall. Requests read the last snapshot plus its age, and a snapshot that has
    /// stopped advancing is itself evidence of a non-pumping block.
    /// </summary>
    [InitializeOnLoad]
    internal static class EditorLivenessProbe
    {
        private const int SampleIntervalMs = 500;

        /// <summary>
        /// Only scan windows once the main thread has actually missed ticks. In the healthy case
        /// this keeps the sampler down to one interlocked read per interval.
        /// </summary>
        private const int ScanThresholdMs = 1000;

        private static readonly Stopwatch Clock = Stopwatch.StartNew();
        private static readonly object SnapshotLock = new object();

        private static ModalDialogInfo _snapshot = new ModalDialogInfo { Supported = ModalDialogProbe.IsSupported };
        private static long _snapshotAtMs;
        private static Thread _sampler;
        private static volatile bool _stopRequested;

        static EditorLivenessProbe()
        {
            _snapshotAtMs = Clock.ElapsedMilliseconds;

            AssemblyReloadEvents.beforeAssemblyReload += StopSampler;
            EditorApplication.quitting += StopSampler;

            if (!ModalDialogProbe.IsSupported)
            {
                return;
            }

            _sampler = new Thread(SampleLoop)
            {
                IsBackground = true,
                Name = "McpForUnity.LivenessSampler"
            };
            _sampler.Start();
        }

        private static void StopSampler()
        {
            _stopRequested = true;
        }

        private static void SampleLoop()
        {
            while (!_stopRequested)
            {
                try
                {
                    ModalDialogInfo sample = MainThreadHeartbeat.StallMs >= ScanThresholdMs
                        ? ModalDialogProbe.Capture()
                        : new ModalDialogInfo { Supported = true };

                    lock (SnapshotLock)
                    {
                        _snapshot = sample;
                        _snapshotAtMs = Clock.ElapsedMilliseconds;
                    }
                }
                catch (Exception)
                {
                    // A sampler failure must never take the transport down; the growing snapshot
                    // age tells the server the probe is not reporting.
                }

                Thread.Sleep(SampleIntervalMs);
            }
        }

        /// <summary>
        /// Build the liveness payload. Never blocks: reads interlocked counters and the last
        /// sampler snapshot only.
        /// </summary>
        internal static JObject Capture()
        {
            ModalDialogInfo snapshot;
            long ageMs;
            lock (SnapshotLock)
            {
                snapshot = _snapshot;
                ageMs = Math.Max(0, Clock.ElapsedMilliseconds - _snapshotAtMs);
            }

            var modal = new JObject
            {
                ["supported"] = snapshot.Supported,
                ["blocked"] = snapshot.Blocked
            };

            if (snapshot.Blocked)
            {
                modal["kind"] = snapshot.Kind;
                modal["answerable"] = snapshot.Answerable;
                modal["title"] = snapshot.Title;
                modal["body"] = snapshot.Body;
                modal["handle"] = snapshot.Handle;
                modal["buttons"] = new JArray(snapshot.Buttons.ToArray());
            }

            return new JObject
            {
                ["main_thread_stall_ms"] = MainThreadHeartbeat.StallMs,
                ["main_thread_ticks"] = MainThreadHeartbeat.TickCount,
                ["pending_commands"] = TransportCommandDispatcher.PendingCount,
                ["sample_age_ms"] = ageMs,
                ["modal"] = modal
            };
        }
    }
}
