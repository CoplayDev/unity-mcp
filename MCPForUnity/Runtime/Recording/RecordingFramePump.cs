#if UNITY_EDITOR
using System;
using System.Collections;
using UnityEngine;

namespace MCPForUnity.Runtime.Recording
{
    /// <summary>Captures the composited Game view after UI has rendered.</summary>
    public sealed class RecordingFramePump : MonoBehaviour
    {
        public Func<bool> BeforeFrame;
        public Action<Texture2D> OnFrame;
        public Action<Exception> OnError;
        public bool Running;

        /// <summary>Sample before rendering and capture composited pixels after UI rendering, forwarding failures to the session.</summary>
        private IEnumerator Start()
        {
            var endOfFrame = new WaitForEndOfFrame();
            while (Running)
            {
                bool capture = false;
                try { capture = BeforeFrame != null && BeforeFrame(); }
                catch (Exception ex) { OnError?.Invoke(ex); }
                if (capture && Running)
                {
                    yield return endOfFrame;
                    if (!Running) yield break;
                    Texture2D frame = null;
                    try
                    {
                        frame = ScreenCapture.CaptureScreenshotAsTexture();
                        if (frame == null) throw new InvalidOperationException("Game view returned no pixels.");
                        OnFrame?.Invoke(frame);
                    }
                    catch (Exception ex) { OnError?.Invoke(ex); }
                    finally
                    {
                        if (frame != null) Destroy(frame);
                    }
                }
                yield return null;
            }
        }
    }
}
#endif
