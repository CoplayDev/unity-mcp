using MCPForUnity.Runtime.Recording;
using UnityEngine;

namespace MCPForUnity.Samples.Recording
{
    /// <summary>Example opt-in state/tween adapter. Recorder ownership prevents double updates.</summary>
    public sealed class RecordingDemoSource : MonoBehaviour, IRecordingPlaybackSource
    {
        public Transform Card;
        public string State = "Open";
        public float PlaybackTime;
        private bool recorderOwnsClock;
        public string RecordingId => "demo-card";
        public string[] States => new[] { "Open", "Close", "Pulse" };
        public double FrameRate => 30;
        public double DurationSeconds => 4;
        public bool CanSeek => true;
        private sealed class Snapshot { public string State; public float Time; public bool Owned; }
        /// <summary>Snapshot the selected state, time and prior clock owner for reversible recording.</summary>
        public object CaptureState() => new Snapshot { State = State, Time = PlaybackTime, Owned = recorderOwnsClock };
        /// <summary>Select the requested demo state and suspend automatic ticking while the recorder owns the clock.</summary>
        public void Prepare(string state) { recorderOwnsClock = true; State = string.IsNullOrEmpty(state) ? State : state; Seek(0); }
        /// <summary>Evaluate the demo card directly at source time without relying on prior rendered frames.</summary>
        public void Seek(double seconds)
        {
            PlaybackTime = (float)seconds;
            if (Card == null) return;
            float t = Mathf.Clamp01(PlaybackTime / 2);
            float ease = 1 - Mathf.Pow(1 - t, 3);
            float scale = State == "Close" ? 1 - ease : State == "Pulse" ? 0.8f + 0.2f * Mathf.Sin(PlaybackTime * Mathf.PI * 2) : ease;
            Card.localScale = Vector3.one * Mathf.Max(0.01f, scale);
            Card.localRotation = Quaternion.Euler(0, 0, State == "Pulse" ? 12 * Mathf.Sin(PlaybackTime * 3) : 8 * (1 - ease));
        }
        public void Advance(double deltaSeconds) => Seek(PlaybackTime + deltaSeconds);
        /// <summary>Release ownership and restore the snapshot; null retains the final visual state.</summary>
        public void RestoreState(object snapshot)
        {
            if (snapshot is Snapshot saved) { State = saved.State; Seek(saved.Time); recorderOwnsClock = saved.Owned; }
            else recorderOwnsClock = false;
        }
        /// <summary>Advance normal demo playback only when no recording job owns its clock.</summary>
        private void Update() { if (Application.isPlaying && !recorderOwnsClock) Advance(UnityEngine.Time.deltaTime); }
    }
}
