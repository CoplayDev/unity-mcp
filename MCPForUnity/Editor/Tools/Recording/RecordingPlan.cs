using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace MCPForUnity.Editor.Tools.Recording
{
    /// <summary>A validated, side-effect-free recording specification. All frame indices are zero based.</summary>
    public sealed class RecordingPlan
    {
        public string CaptureSource;
        public string Camera;
        public string Mode;
        public string Format;
        public int Width;
        public int Height;
        public int Fps;
        public double SourceFps;
        public double StartTime;
        public double Duration;
        public int FrameCount;
        public string OutputPath;
        public JObject Playback;
        public bool RestoreState;

        /// <summary>Resolve source-time ranges and output limits without rounding frame counts or creating files.</summary>
        public static RecordingPlan Parse(JObject input, double discoveredFps = 0)
        {
            Require(input["playback"] == null || input["playback"] is JObject, "playback must be an object.");
            var p = new RecordingPlan
            {
                CaptureSource = (string)input["capture_source"] ?? "game_view",
                Camera = (string)input["camera"],
                Format = (string)input["format"] ?? "mp4",
                Width = ReadInt(input, "width", 1280),
                Height = ReadInt(input, "height", 720),
                Fps = ReadInt(input, "fps", 30),
                Playback = (input["playback"] as JObject)?.DeepClone() as JObject ?? new JObject { ["type"] = "none" },
                RestoreState = input.Value<bool?>("restore_state") ?? true,
            };
            string type = (string)p.Playback["type"] ?? "none";
            p.Mode = (string)input["mode"] ?? (type == "none" ? "realtime" : "fixed_step");
            Require(new[] { "game_view", "camera", "scene_view" }.Contains(p.CaptureSource), "capture_source must be game_view, camera or scene_view.");
            Require(new[] { "realtime", "fixed_step" }.Contains(p.Mode), "mode must be realtime or fixed_step.");
            Require(new[] { "mp4", "webm", "png_sequence" }.Contains(p.Format), "format must be mp4, webm or png_sequence.");
            Require(p.Width >= 16 && p.Width <= 3840 && p.Height >= 16 && p.Height <= 3840 && (long)p.Width * p.Height <= 3840L * 2160,
                "Resolution must be between 16 and 3840 pixels per edge and at most 3840x2160 pixels.");
            Require(p.Width % 2 == 0 && p.Height % 2 == 0, "width and height must be even.");
            Require(p.Fps >= 1 && p.Fps <= 120, "fps must be an integer from 1 to 120.");
            p.SourceFps = input.Value<double?>("source_fps") ?? discoveredFps;
            if (input["source_fps"] != null) Require(Finite(p.SourceFps) && p.SourceFps > 0 && p.SourceFps <= 1000, "source_fps must be positive, finite and at most 1000.");
            bool hasStartFrame = input["start_frame"] != null;
            bool hasStartTime = input["start_time"] != null;
            bool hasEnd = input["end_frame"] != null;
            Require(!(hasStartFrame && hasStartTime), "Use either start_frame or start_time, not both.");
            Require(!(hasEnd && input["duration_seconds"] != null), "Use either end_frame (exclusive) or duration_seconds, not both.");
            if (hasStartFrame || hasEnd)
                Require(Finite(p.SourceFps) && p.SourceFps > 0 && p.SourceFps <= 1000,
                    "Frame addressing requires a known source_fps. Discover the source or provide source_fps explicitly; output fps is not source fps.");
            p.StartTime = hasStartFrame ? ReadInt(input, "start_frame", 0) / p.SourceFps : input.Value<double?>("start_time") ?? 0;
            Require(Finite(p.StartTime) && p.StartTime >= 0, "start_time/start_frame must be non-negative and finite.");
            Require(type != "none" || p.StartTime == 0, "A source start offset requires an explicit playback target.");
            p.Duration = hasEnd ? ReadInt(input, "end_frame", 0) / p.SourceFps - p.StartTime : input.Value<double?>("duration_seconds") ?? 5;
            Require(Finite(p.Duration) && p.Duration > 0 && p.Duration <= 300, "Recording duration must be greater than 0 and at most 300 seconds.");
            double frames = p.Duration * p.Fps;
            Require(Math.Abs(frames - Math.Round(frames)) < 0.00001,
                "The duration must contain a whole number of output frames. Adjust duration_seconds, fps or the source frame range.");
            p.FrameCount = checked((int)Math.Round(frames));
            Require(p.FrameCount > 0 && p.FrameCount <= 18000, "Recording must contain 1 to 18000 frames.");
            p.OutputPath = ResolveOutputPath(input, p.Format);
            return p;
        }

        /// <summary>Expose the resolved specification so clients can inspect timing and bound targets before starting.</summary>
        public object Describe() => new
        {
            capture_source = CaptureSource, camera = Camera, mode = Mode, format = Format,
            width = Width, height = Height, fps = Fps, source_fps = SourceFps > 0 ? (double?)SourceFps : null,
            start_time = StartTime, duration_seconds = Duration, end_time_exclusive = StartTime + Duration,
            expected_frames = FrameCount, frame_index_base = 0, output_path = OutputPath,
            resize = "fit_with_letterbox", audio = false, playback = Playback, restore_state = RestoreState,
        };

        /// <summary>Map a zero-based output frame to source seconds; output FPS is independent of source FPS.</summary>
        public double TimeForFrame(int index) => StartTime + (double)index / Fps;

        /// <summary>Reject fractional, non-finite and out-of-range values instead of coercing frame indices.</summary>
        internal static int ReadInt(JObject input, string key, int fallback)
        {
            JToken value = input[key];
            if (value == null) return fallback;
            double number = value.Value<double>();
            Require(Finite(number) && number == Math.Truncate(number) && number >= int.MinValue && number <= int.MaxValue,
                key + " must be an integer.");
            return (int)number;
        }

        /// <summary>Reserve a unique project-relative path outside import roots, traversal and linked ancestors.</summary>
        private static string ResolveOutputPath(JObject input, string format)
        {
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string folder = (string)input["output_folder"] ?? "Library/MCPForUnity/Recordings";
            Require(!Path.IsPathRooted(folder), "output_folder must be relative to the Unity project.");
            string absolute = Path.GetFullPath(Path.Combine(root, folder));
            var comparison = Application.platform == RuntimePlatform.WindowsEditor ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            Require(absolute.StartsWith(root + Path.DirectorySeparatorChar, comparison), "output_folder must stay inside the project.");
            string relative = absolute.Substring(root.Length + 1).Replace('\\', '/');
            // Package folder names are reserved even on case-insensitive macOS volumes.
            Require(!new[] { "Assets", "Packages", "ProjectSettings" }.Any(x =>
                relative.Equals(x, StringComparison.OrdinalIgnoreCase) ||
                relative.StartsWith(x + "/", StringComparison.OrdinalIgnoreCase)),
                "Record outside Assets, Packages and ProjectSettings to avoid imports during capture.");
            // Reject links/junctions in existing ancestors before creating any output files.
            for (var dir = new DirectoryInfo(absolute); dir != null && dir.FullName.Length >= root.Length; dir = dir.Parent)
                if (dir.Exists) Require((dir.Attributes & FileAttributes.ReparsePoint) == 0, "output_folder cannot pass through symbolic links or junctions.");
            string name = (string)input["file_name"] ?? "recording";
            Require(!string.IsNullOrWhiteSpace(name) && name.Length <= 100 && name.All(c => char.IsLetterOrDigit(c) || c == '-' || c == '_'),
                "file_name must be a base name of at most 100 letters, digits, hyphens or underscores, without an extension.");
            return Path.Combine(absolute, name + "-" + Guid.NewGuid().ToString("N") + (format == "png_sequence" ? "" : "." + format));
        }

        internal static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        /// <summary>Turn a violated recording contract into a caller-visible validation error.</summary>
        internal static void Require(bool condition, string message)
        {
            if (!condition) throw new ArgumentException(message);
        }
    }
}
