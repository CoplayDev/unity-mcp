using System;

namespace MCPForUnity.Editor.ActionTrace.Capture
{
    /// <summary>
    /// Statistics for a capture point.
    /// </summary>
    [Serializable]
    public sealed class CapturePointStats
    {
        public int TotalEventsCaptured;
        public int EventsFiltered;
        public int EventsSampled;
        public long TotalCaptureTimeMs;
        public double AverageCaptureTimeMs;
        public int ErrorCount;

        private long _startTimeTicks;

        public void StartCapture()
        {
            _startTimeTicks = DateTimeOffset.UtcNow.Ticks;
        }

        public void EndCapture()
        {
            long elapsedTicks = DateTimeOffset.UtcNow.Ticks - _startTimeTicks;
            TotalCaptureTimeMs += elapsedTicks / 10000;
            TotalEventsCaptured++;
            UpdateAverage();
        }

        public void RecordFiltered()
        {
            EventsFiltered++;
        }

        public void RecordSampled()
        {
            EventsSampled++;
        }

        public void RecordError()
        {
            ErrorCount++;
        }

        public void UpdateAverage()
        {
            AverageCaptureTimeMs = TotalEventsCaptured > 0
                ? (double)TotalCaptureTimeMs / TotalEventsCaptured
                : 0;
        }

        public void Reset()
        {
            TotalEventsCaptured = 0;
            EventsFiltered = 0;
            EventsSampled = 0;
            TotalCaptureTimeMs = 0;
            AverageCaptureTimeMs = 0;
            ErrorCount = 0;
        }
    }
}
