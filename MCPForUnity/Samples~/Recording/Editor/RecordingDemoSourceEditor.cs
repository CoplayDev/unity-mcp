using UnityEditor;

namespace MCPForUnity.Samples.Recording
{
    [CustomEditor(typeof(RecordingDemoSource))]
    public sealed class RecordingDemoSourceEditor : UnityEditor.Editor
    {
        /// <summary>Show source timing through an IMGUI inspector that remains safe across demo Play Mode restoration.</summary>
        public override void OnInspectorGUI()
        {
            // Keep this sample on IMGUI: the generic UI Toolkit inspector in Unity 2023.2.3
            // can crash in AudioUtil.HasAudioCallback while restoring a selected MonoBehaviour.
            DrawDefaultInspector();
            var source = (RecordingDemoSource)target;
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Recording source", source.RecordingId);
            EditorGUILayout.LabelField("Source frame", (source.PlaybackTime * source.FrameRate).ToString("F1"));
            EditorGUILayout.HelpBox("Discover this source with manage_recording and choose Open, Close or Pulse. Its clock is owned by the recorder only while a job is active.", MessageType.Info);
        }
    }
}
