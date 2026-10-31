using System;
using System.Diagnostics;
using System.IO;
using MCPForUnity.Runtime.Helpers;
using MCPForUnity.Runtime.Recording;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Media;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UObject = UnityEngine.Object;

namespace MCPForUnity.Editor.Tools.Recording
{
    internal sealed class RecordingSession
    {
        public readonly string Id = Guid.NewGuid().ToString("N");
        public readonly RecordingPlan Plan;
        public string State = "prepared";
        public string Error;
        public string ArtifactPath;
        public int Frames;
        public int CapturedFrames;
        public int SourceWidth;
        public int SourceHeight;
        public readonly DateTime Created = DateTime.UtcNow;
        private readonly RecordingPlayback playback;
        private MediaEncoder encoder;
        private RecordingFramePump pump;
        private readonly Camera camera;
        private Texture2D outputTexture;
        private RenderTexture outputTarget;
        private readonly Stopwatch clock = new Stopwatch();
        private double lastCapture;
        private double lastSampleTime;
        private double nextEditorCapture;
        private double warmupTime;
        private bool playbackBegun;
        private bool settingsChanged;
        private bool oldRunInBackground;
        private float oldCaptureDelta;
        private string temporaryPath;
        private string restoreWarning;
        public Action Changed;
        public bool Active => State == "recording" || State == "warming_up" || State == "finalizing";

        public RecordingSession(RecordingPlan plan, RecordingPlayback source, Camera captureCamera)
        {
            Plan = plan;
            playback = source;
            camera = captureCamera;
        }

        /// <summary>Bind the requested camera once, including the currently active Scene view camera.</summary>
        public static Camera ResolveCamera(RecordingPlan plan)
        {
            if (plan.CaptureSource == "camera") return RecordingPlayback.ResolveComponent<Camera>(plan.Camera);
            if (plan.CaptureSource != "scene_view") return null;
            RecordingPlan.Require(SceneView.lastActiveSceneView != null, "Open a Scene view before recording it.");
            Camera camera = SceneView.lastActiveSceneView.camera;
            RecordingPlan.Require(camera != null, "The Scene view has no capture camera.");
            return camera;
        }

        /// <summary>Reject unstable Editor states, unsupported capture modes and platform codecs before playback changes.</summary>
        public static void ValidateEnvironment(RecordingPlan plan)
        {
            RecordingPlan.Require(!EditorApplication.isCompiling && !EditorApplication.isUpdating && EditorApplication.isPlayingOrWillChangePlaymode == Application.isPlaying,
                "Wait until the Editor finishes compiling, importing or changing Play Mode.");
            if (plan.CaptureSource == "game_view")
                RecordingPlan.Require(Application.isPlaying && !EditorApplication.isPaused && !Application.isBatchMode,
                    "Composited game_view recording requires unpaused Play Mode and a graphical Editor. Use camera or scene_view for Edit Mode.");
            if (Application.platform == RuntimePlatform.LinuxEditor)
                RecordingPlan.Require(plan.Format != "mp4", "H.264/MP4 is unavailable in the Linux Editor. Choose webm or png_sequence.");
            if (plan.Mode == "fixed_step" && Application.isPlaying)
                RecordingPlan.Require(!EditorApplication.isPaused, "Unpause Play Mode before recording.");
        }

        /// <summary>Revalidate bound resources, acquire playback and capture resources, and report failures through cleanup.</summary>
        public void Start()
        {
            try
            {
                ValidateEnvironment(Plan);
                RecordingPlan.Require(Plan.CaptureSource == "game_view" || camera != null,
                    "The prepared capture camera no longer exists. Prepare a new recording.");
                playback.Validate(Plan);
                Directory.CreateDirectory(Path.GetDirectoryName(Plan.OutputPath));
                temporaryPath = Plan.Format == "png_sequence" ? Plan.OutputPath + ".partial" : Path.Combine(Path.GetDirectoryName(Plan.OutputPath), Path.GetFileNameWithoutExtension(Plan.OutputPath) + ".partial." + Plan.Format);
                if (Plan.Format == "png_sequence") Directory.CreateDirectory(temporaryPath);
                else
                    encoder = new MediaEncoder(temporaryPath, new VideoTrackAttributes
                    {
                        frameRate = new MediaRational(Plan.Fps), width = (uint)Plan.Width, height = (uint)Plan.Height, includeAlpha = false,
                    });
                oldRunInBackground = Application.runInBackground; oldCaptureDelta = Time.captureDeltaTime; settingsChanged = true;
                Application.runInBackground = true;
                if (Plan.Mode == "fixed_step" && Application.isPlaying) Time.captureDeltaTime = 1f / Plan.Fps;
                playbackBegun = true;
                playback.Begin(Plan);
                lastSampleTime = Plan.StartTime;
                State = !playback.CanSeek && Plan.StartTime > 0 ? "warming_up" : "recording";
                if (Plan.CaptureSource == "game_view")
                {
                    Type gameView = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.GameView");
                    if (gameView != null) { var view = EditorWindow.GetWindow(gameView); view.Show(); view.Focus(); view.Repaint(); }
                    var go = new GameObject("MCP recording frame pump") { hideFlags = HideFlags.HideAndDontSave };
                    UObject.DontDestroyOnLoad(go);
                    pump = go.AddComponent<RecordingFramePump>();
                    pump.BeforeFrame = BeforeFrame; pump.OnFrame = AcceptFrame; pump.OnError = Fail; pump.Running = true;
                }
                clock.Start();
                WriteManifest();
                Changed?.Invoke();
            }
            catch (Exception ex) { Fail(ex); }
        }

        /// <summary>Drive bounded pre-roll or camera capture, enforcing no-frame and total wall-clock timeouts.</summary>
        public void Tick()
        {
            if (State == "warming_up")
            {
                try
                {
                    if (clock.Elapsed.TotalSeconds > Math.Max(60, Plan.StartTime * 30)) throw new TimeoutException("Source pre-roll exceeded its wall-clock budget.");
                    for (int i = 0; i < 16 && warmupTime < Plan.StartTime; i++)
                    {
                        double delta = Math.Min(1.0 / (Plan.SourceFps > 0 ? Plan.SourceFps : Plan.Fps), Plan.StartTime - warmupTime);
                        warmupTime += delta; playback.Sample(warmupTime, delta);
                    }
                    if (warmupTime >= Plan.StartTime) { State = "recording"; clock.Restart(); Changed?.Invoke(); }
                }
                catch (Exception ex) { Fail(ex); }
                return;
            }
            if (State != "recording") return;
            try
            {
                if (clock.Elapsed.TotalSeconds - lastCapture > 15)
                    throw new TimeoutException("No frame rendered for 15 seconds. Keep the Game view rendering; unpause the Editor, or use camera capture.");
                if (clock.Elapsed.TotalSeconds > Math.Max(60, Plan.Duration * 30))
                    throw new TimeoutException("Recording exceeded its wall-clock budget.");
                EditorApplication.QueuePlayerLoopUpdate();
                if (Plan.CaptureSource == "game_view") return;
                if (Plan.Mode == "realtime" && clock.Elapsed.TotalSeconds < nextEditorCapture) return;
                if (!BeforeFrame()) return;
                if (camera == null) throw new InvalidOperationException("The capture camera was destroyed.");
                Texture2D frame = null;
                try
                {
                    frame = ScreenshotUtility.RenderCameraToTexture(camera, Math.Max(Plan.Width, Plan.Height));
                    AcceptFrame(frame);
                }
                finally { if (frame != null) UObject.DestroyImmediate(frame); }
                nextEditorCapture = clock.Elapsed.TotalSeconds + 1.0 / Plan.Fps;
            }
            catch (Exception ex) { Fail(ex); }
        }

        /// <summary>Sample the selected source using the fixed output clock or the elapsed realtime clock.</summary>
        private bool BeforeFrame()
        {
            if (State != "recording") return false;
            double elapsed = clock.Elapsed.TotalSeconds;
            if (Plan.Mode == "realtime" && Frames > Math.Floor(elapsed * Plan.Fps)) return false;
            double sample = Plan.Mode == "fixed_step" ? Plan.TimeForFrame(Frames) : Plan.StartTime + Math.Min(elapsed, Plan.Duration - 1.0 / Plan.Fps);
            playback.Sample(sample, Math.Max(0, sample - lastSampleTime));
            lastSampleTime = sample;
            return true;
        }

        /// <summary>Encode one sampled image, duplicating realtime frames when needed, then finalize at the frame limit.</summary>
        private void AcceptFrame(Texture2D frame)
        {
            if (State != "recording") return;
            SourceWidth = frame.width; SourceHeight = frame.height;
            Resize(frame);
            CapturedFrames++;
            int count = Plan.Mode == "fixed_step" ? Frames + 1 : Math.Min(Plan.FrameCount, Math.Max(Frames + 1, (int)Math.Floor(clock.Elapsed.TotalSeconds * Plan.Fps) + 1));
            while (Frames < count)
            {
                if (encoder != null)
                {
                    if (!encoder.AddFrame(outputTexture)) throw new IOException("Unity MediaEncoder rejected a video frame.");
                }
                else File.WriteAllBytes(Path.Combine(temporaryPath, Frames.ToString("D06") + ".png"), outputTexture.EncodeToPNG());
                Frames++;
            }
            lastCapture = clock.Elapsed.TotalSeconds;
            if (Frames >= Plan.FrameCount) Finish("completed");
        }

        /// <summary>Fit pixels into the requested dimensions while preserving the source color-space interpretation.</summary>
        private void Resize(Texture2D frame)
        {
            bool sourceSrgb = GraphicsFormatUtility.IsSRGBFormat(frame.graphicsFormat);
            if (outputTarget == null)
            {
                outputTarget = new RenderTexture(Plan.Width, Plan.Height, 0, RenderTextureFormat.ARGB32,
                    sourceSrgb ? RenderTextureReadWrite.sRGB : RenderTextureReadWrite.Linear) { hideFlags = HideFlags.HideAndDontSave };
                outputTarget.Create();
                outputTexture = new Texture2D(Plan.Width, Plan.Height, TextureFormat.RGBA32, false, linear: !sourceSrgb) { hideFlags = HideFlags.HideAndDontSave };
            }
            RenderTexture previous = RenderTexture.active;
            bool oldSrgb = GL.sRGBWrite;
            try
            {
                RenderTexture.active = outputTarget;
                GL.sRGBWrite = sourceSrgb;
                GL.Clear(true, true, Color.black);
                float scale = Math.Min((float)Plan.Width / frame.width, (float)Plan.Height / frame.height);
                float w = frame.width * scale, h = frame.height * scale;
                GL.PushMatrix();
                try
                {
                    GL.LoadPixelMatrix(0, Plan.Width, Plan.Height, 0);
                    UnityEngine.Graphics.DrawTexture(new Rect((Plan.Width - w) / 2, (Plan.Height - h) / 2, w, h), frame);
                }
                finally { GL.PopMatrix(); }
                outputTexture.ReadPixels(new Rect(0, 0, Plan.Width, Plan.Height), 0, 0);
                outputTexture.Apply(false);
            }
            finally { RenderTexture.active = previous; GL.sRGBWrite = oldSrgb; }
        }

        /// <summary>Record the originating error and release all resources through the terminal failure path.</summary>
        public void Fail(Exception exception)
        {
            Error = exception.GetBaseException().Message;
            Finish("failed");
        }

        /// <summary>Release playback and Editor settings, close the encoder, and publish only successfully finalized output.</summary>
        public void Finish(string finalState, string reason = null)
        {
            if (!Active && State != "prepared") return;
            State = "finalizing";
            if (reason != null) Error = reason;
            if (pump != null)
            {
                pump.Running = false;
                // A completed capture is still executing the pump coroutine. Destroying that
                // MonoBehaviour synchronously can leave native Editor/Inspector references dangling.
                if (Application.isPlaying && finalState != "interrupted") UObject.Destroy(pump.gameObject);
                else UObject.DestroyImmediate(pump.gameObject);
                pump = null;
            }
            try { encoder?.Dispose(); }
            catch (Exception ex) { Error = "Video finalization failed: " + ex.Message; finalState = "failed"; }
            finally
            {
                encoder = null;
                if (playbackBegun)
                {
                    try { playback.Dispose(); }
                    catch (Exception ex) { restoreWarning = "Playback restoration failed: " + ex.GetBaseException().Message; }
                    playbackBegun = false;
                }
                if (settingsChanged) { Application.runInBackground = oldRunInBackground; Time.captureDeltaTime = oldCaptureDelta; settingsChanged = false; }
                if (outputTexture != null) UObject.DestroyImmediate(outputTexture);
                if (outputTarget != null) { outputTarget.Release(); UObject.DestroyImmediate(outputTarget); }
                clock.Stop();
            }
            try
            {
                if (finalState == "completed" && Frames == 0) { finalState = "failed"; Error = "No frames were recorded."; }
                if (temporaryPath != null)
                {
                    if (finalState == "completed")
                    {
                        if (Plan.Format == "png_sequence") Directory.Move(temporaryPath, Plan.OutputPath);
                        else File.Move(temporaryPath, Plan.OutputPath);
                        ArtifactPath = Plan.OutputPath;
                    }
                    else if (File.Exists(temporaryPath) || Directory.Exists(temporaryPath)) ArtifactPath = temporaryPath;
                }
            }
            catch (Exception ex) { finalState = "failed"; Error = "Could not publish recording: " + ex.Message; ArtifactPath = temporaryPath; }
            State = finalState;
            WriteManifest();
            Changed?.Invoke();
        }

        /// <summary>Persist artifact metadata only after capture initialization has reserved an output path.</summary>
        private void WriteManifest()
        {
            if (temporaryPath == null) return;
            try { File.WriteAllText(Plan.OutputPath + ".json", JObject.FromObject(Describe()).ToString(Formatting.Indented)); }
            catch (Exception ex) { restoreWarning = "Could not write recording manifest: " + ex.Message; }
        }

        /// <summary>Return progress and terminal readiness; a partial or failed artifact is never marked usable.</summary>
        public object Describe() => new
        {
            recording_id = Id, state = State, ready = State == "completed", created_at = Created.ToString("O"),
            plan = Plan.Describe(), captured_frames = CapturedFrames, encoded_frames = Frames,
            duplicated_frames = Math.Max(0, Frames - CapturedFrames), stopped_early = State == "completed" && Frames < Plan.FrameCount, duration_seconds = (double)Frames / Plan.Fps,
            elapsed_seconds = clock.Elapsed.TotalSeconds, progress = (double)Frames / Plan.FrameCount,
            source_width = SourceWidth, source_height = SourceHeight, pre_roll_seconds = warmupTime, path = ArtifactPath,
            manifest_path = temporaryPath == null ? null : Plan.OutputPath + ".json", error = Error, warning = restoreWarning,
        };
    }
}
