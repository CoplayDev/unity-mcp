using System;
using System.Collections.Generic;
using System.Reflection;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEditor;

namespace MCPForUnity.Editor.Tools.Profiler
{
    internal static class FrameDebuggerOps
    {
        private static readonly Type UtilType;
        private static readonly PropertyInfo EventCountProp;
        private static readonly MethodInfo EnableMethod;
        private static readonly MethodInfo GetFrameEventsMethod;
        private static readonly MethodInfo GetEventDataMethod;
        private static readonly MethodInfo GetEventInfoNameMethod;
        private static readonly Type EventDataType;
        private static readonly bool Available;

        static FrameDebuggerOps()
        {
            try
            {
                // Unity 6+: moved to FrameDebuggerInternal sub-namespace
                UtilType = Type.GetType("UnityEditorInternal.FrameDebuggerInternal.FrameDebuggerUtility, UnityEditor");
                // Unity 2021–2022: original location
                UtilType ??= Type.GetType("UnityEditorInternal.FrameDebuggerUtility, UnityEditor");

                if (UtilType == null) return;

                EventCountProp = UtilType.GetProperty("count", BindingFlags.Public | BindingFlags.Static)
                              ?? UtilType.GetProperty("eventsCount", BindingFlags.Public | BindingFlags.Static);

                EnableMethod = UtilType.GetMethod("SetEnabled", BindingFlags.Public | BindingFlags.Static,
                                   null, new[] { typeof(bool), typeof(int) }, null)
                            ?? UtilType.GetMethod("SetEnabled", BindingFlags.Public | BindingFlags.Static);

                GetFrameEventsMethod = UtilType.GetMethod("GetFrameEvents", BindingFlags.Public | BindingFlags.Static);
                GetEventInfoNameMethod = UtilType.GetMethod("GetFrameEventInfoName", BindingFlags.Public | BindingFlags.Static);

                // Unity 6: GetFrameEventData(int, FrameDebuggerEventData) — 2 params, returns bool
                // Older: GetFrameEventData(int) — 1 param, returns event data object
                EventDataType = Type.GetType("UnityEditorInternal.FrameDebuggerInternal.FrameDebuggerEventData, UnityEditor")
                             ?? Type.GetType("UnityEditorInternal.FrameDebuggerEventData, UnityEditor");

                if (EventDataType != null)
                {
                    GetEventDataMethod = UtilType.GetMethod("GetFrameEventData", BindingFlags.Public | BindingFlags.Static,
                                             null, new[] { typeof(int), EventDataType }, null);
                }
                GetEventDataMethod ??= UtilType.GetMethod("GetFrameEventData", BindingFlags.Public | BindingFlags.Static);

                Available = EventCountProp != null && EnableMethod != null;
            }
            catch
            {
                Available = false;
            }
        }

        internal static object Enable(JObject @params)
        {
            if (!Available)
                return new ErrorResponse("FrameDebuggerUtility not found via reflection.");

            // Open the Frame Debugger window (required for event capture)
            EditorApplication.ExecuteMenuItem("Window/Analysis/Frame Debugger");

            // Frame Debugger requires game to be paused before enabling to capture events.
            if (EditorApplication.isPlaying && !EditorApplication.isPaused)
            {
                return new ErrorResponse(
                    "Game must be paused before enabling Frame Debugger. "
                    + "Call manage_editor action=pause first, then retry frame_debugger_enable.");
            }

            try
            {
                InvokeSetEnabled(true);
            }
            catch (Exception ex)
            {
                return new ErrorResponse($"Failed to enable Frame Debugger: {ex.Message}");
            }

            int eventCount = GetEventCount();
            return new SuccessResponse("Frame Debugger enabled.", new
            {
                enabled = true,
                event_count = eventCount,
            });
        }

        internal static object Disable(JObject @params)
        {
            if (!Available)
                return new ErrorResponse("FrameDebuggerUtility not found via reflection.");

            try
            {
                InvokeSetEnabled(false);
            }
            catch (Exception ex)
            {
                return new ErrorResponse($"Failed to disable Frame Debugger: {ex.Message}");
            }

            return new SuccessResponse("Frame Debugger disabled.", new { enabled = false });
        }

        internal static object GetEvents(JObject @params)
        {
            if (!Available)
                return new ErrorResponse("FrameDebuggerUtility not found via reflection.");

            var p = new ToolParams(@params);
            int pageSize = p.GetInt("page_size") ?? 50;
            int cursor = p.GetInt("cursor") ?? 0;

            int totalEvents = GetEventCount();
            if (totalEvents == 0)
            {
                return new SuccessResponse("Frame Debugger has no events. Is it enabled?", new
                {
                    events = new List<object>(),
                    total_events = 0,
                });
            }

            // Try GetFrameEvents() for the event descriptor array (has type/name info)
            object[] frameEvents = null;
            if (GetFrameEventsMethod != null)
            {
                try
                {
                    var raw = GetFrameEventsMethod.Invoke(null, null);
                    if (raw is Array arr)
                    {
                        frameEvents = new object[arr.Length];
                        arr.CopyTo(frameEvents, 0);
                    }
                }
                catch { /* fall through */ }
            }

            var events = new List<object>();
            int end = Math.Min(cursor + pageSize, totalEvents);

            for (int i = cursor; i < end; i++)
            {
                var entry = new Dictionary<string, object> { ["index"] = i };

                // Get event name
                if (GetEventInfoNameMethod != null)
                {
                    try { entry["name"] = (string)GetEventInfoNameMethod.Invoke(null, new object[] { i }); }
                    catch { /* skip */ }
                }

                // Get fields from FrameDebuggerEvent descriptor
                if (frameEvents != null && i < frameEvents.Length)
                {
                    var desc = frameEvents[i];
                    var descType = desc.GetType();
                    TryAddField(descType, desc, "type", entry, "event_type");
                    TryAddField(descType, desc, "gameObjectInstanceID", entry);
                }

                // Get detailed event data
                if (GetEventDataMethod != null)
                {
                    try
                    {
                        var paramInfos = GetEventDataMethod.GetParameters();
                        object eventData;

                        if (paramInfos.Length == 2 && EventDataType != null)
                        {
                            // Unity 6: bool GetFrameEventData(int, FrameDebuggerEventData)
                            eventData = Activator.CreateInstance(EventDataType);
                            var args = new object[] { i, eventData };
                            var ok = GetEventDataMethod.Invoke(null, args);
                            eventData = (ok is true) ? args[1] : null;
                        }
                        else
                        {
                            // Older: FrameDebuggerEventData GetFrameEventData(int)
                            eventData = GetEventDataMethod.Invoke(null, new object[] { i });
                        }

                        if (eventData != null)
                        {
                            var edType = eventData.GetType();
                            TryAddField(edType, eventData, "shaderName", entry);
                            TryAddField(edType, eventData, "passName", entry);
                            TryAddField(edType, eventData, "rtName", entry);
                            TryAddField(edType, eventData, "rtWidth", entry);
                            TryAddField(edType, eventData, "rtHeight", entry);
                            TryAddField(edType, eventData, "vertexCount", entry);
                            TryAddField(edType, eventData, "indexCount", entry);
                            TryAddField(edType, eventData, "instanceCount", entry);
                            TryAddField(edType, eventData, "meshName", entry);
                        }
                    }
                    catch { /* skip event data for this index */ }
                }

                events.Add(entry);
            }

            var result = new Dictionary<string, object>
            {
                ["events"] = events,
                ["total_events"] = totalEvents,
                ["page_size"] = pageSize,
                ["cursor"] = cursor,
            };
            if (end < totalEvents)
                result["next_cursor"] = end;

            return new SuccessResponse($"Frame Debugger events {cursor}-{end - 1} of {totalEvents}.", result);
        }

        private static void InvokeSetEnabled(bool value)
        {
            int paramCount = EnableMethod.GetParameters().Length;
            if (paramCount == 2)
                EnableMethod.Invoke(null, new object[] { value, 0 });
            else if (paramCount == 1)
                EnableMethod.Invoke(null, new object[] { value });
            else
                throw new InvalidOperationException($"SetEnabled has unexpected {paramCount} parameters.");
        }

        private static int GetEventCount()
        {
            try { return (int)EventCountProp.GetValue(null); }
            catch { return 0; }
        }

        private static void TryAddField(Type type, object obj, string fieldName, Dictionary<string, object> dict, string outputKey = null)
        {
            try
            {
                var field = type.GetField(fieldName, BindingFlags.Public | BindingFlags.Instance)
                         ?? type.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
                var prop = type.GetProperty(fieldName, BindingFlags.Public | BindingFlags.Instance)
                        ?? type.GetProperty(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
                object val = field != null ? field.GetValue(obj)
                           : prop != null ? prop.GetValue(obj)
                           : null;
                if (val != null)
                    dict[outputKey ?? fieldName] = val.GetType().IsEnum ? val.ToString() : val;
            }
            catch { /* skip unavailable fields */ }
        }

    }
}
