using System;
using System.Collections.Generic;
using System.Reflection;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;

namespace MCPForUnity.Editor.Tools.Profiler
{
    internal static class FrameDebuggerOps
    {
        private static readonly Type UtilType;
        private static readonly PropertyInfo EventCountProp;
        private static readonly MethodInfo EnableMethod;
        private static readonly MethodInfo DisableMethod;
        private static readonly MethodInfo GetEventDataMethod;
        private static readonly bool ReflectionAvailable;

        static FrameDebuggerOps()
        {
            try
            {
                UtilType = Type.GetType("UnityEditorInternal.FrameDebuggerUtility, UnityEditor");
                if (UtilType != null)
                {
                    EventCountProp = UtilType.GetProperty("eventsCount", BindingFlags.Public | BindingFlags.Static)
                                  ?? UtilType.GetProperty("count", BindingFlags.Public | BindingFlags.Static);
                    EnableMethod = UtilType.GetMethod("SetEnabled", BindingFlags.Public | BindingFlags.Static);
                    DisableMethod = EnableMethod; // Same method, different arg
                    GetEventDataMethod = UtilType.GetMethod("GetFrameEventData", BindingFlags.Public | BindingFlags.Static);
                }
                ReflectionAvailable = UtilType != null && EventCountProp != null;
            }
            catch
            {
                ReflectionAvailable = false;
            }
        }

        internal static object Enable(JObject @params)
        {
            if (!ReflectionAvailable)
            {
                return new ErrorResponse(
                    "FrameDebuggerUtility not available via reflection in this Unity version.");
            }

            try
            {
                if (EnableMethod != null)
                {
                    EnableMethod.Invoke(null, new object[] { true, 0 });
                }
            }
            catch (Exception ex)
            {
                return new ErrorResponse($"Failed to enable Frame Debugger: {ex.Message}");
            }

            int eventCount = 0;
            string warning = null;
            try
            {
                eventCount = (int)EventCountProp.GetValue(null);
            }
            catch (Exception ex)
            {
                warning = $"Could not read event count: {ex.Message}";
            }

            var data = new Dictionary<string, object>
            {
                ["enabled"] = true,
                ["event_count"] = eventCount,
            };
            if (warning != null)
                data["warning"] = warning;

            return new SuccessResponse("Frame Debugger enabled.", data);
        }

        internal static object Disable(JObject @params)
        {
            if (!ReflectionAvailable)
            {
                return new ErrorResponse(
                    "FrameDebuggerUtility not available via reflection in this Unity version.");
            }

            try
            {
                if (EnableMethod != null)
                {
                    EnableMethod.Invoke(null, new object[] { false, 0 });
                }
            }
            catch (Exception ex)
            {
                return new ErrorResponse($"Failed to disable Frame Debugger: {ex.Message}");
            }

            return new SuccessResponse("Frame Debugger disabled.", new { enabled = false });
        }

        internal static object GetEvents(JObject @params)
        {
            if (!ReflectionAvailable || GetEventDataMethod == null)
            {
                return new SuccessResponse("Frame Debugger events (reflection unavailable).", new
                {
                    events = new List<object>(),
                    total_events = 0,
                    warning = "FrameDebuggerUtility API not available in this Unity version. "
                              + "Event data cannot be extracted programmatically.",
                });
            }

            var p = new ToolParams(@params);
            int pageSize = p.GetInt("page_size") ?? 50;
            int cursor = p.GetInt("cursor") ?? 0;

            int totalEvents = 0;
            try
            {
                totalEvents = (int)EventCountProp.GetValue(null);
            }
            catch
            {
                return new SuccessResponse("Could not read event count.", new
                {
                    events = new List<object>(),
                    total_events = 0,
                    warning = "Failed to read event count via reflection.",
                });
            }

            if (totalEvents == 0)
            {
                return new SuccessResponse("Frame Debugger has no events. Is it enabled?", new
                {
                    events = new List<object>(),
                    total_events = 0,
                });
            }

            var events = new List<object>();
            int end = Math.Min(cursor + pageSize, totalEvents);

            for (int i = cursor; i < end; i++)
            {
                try
                {
                    var eventData = GetEventDataMethod.Invoke(null, new object[] { i });
                    if (eventData != null)
                    {
                        var eventType = eventData.GetType();
                        var entry = new Dictionary<string, object> { ["index"] = i };

                        TryAddField(eventType, eventData, "shaderName", entry);
                        TryAddField(eventType, eventData, "passName", entry);
                        TryAddField(eventType, eventData, "rtName", entry);
                        TryAddField(eventType, eventData, "rtWidth", entry);
                        TryAddField(eventType, eventData, "rtHeight", entry);
                        TryAddField(eventType, eventData, "vertexCount", entry);
                        TryAddField(eventType, eventData, "indexCount", entry);
                        TryAddField(eventType, eventData, "instanceCount", entry);
                        TryAddField(eventType, eventData, "meshName", entry);

                        events.Add(entry);
                    }
                }
                catch
                {
                    events.Add(new Dictionary<string, object>
                    {
                        ["index"] = i,
                        ["error"] = "Failed to read event data",
                    });
                }
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

        private static void TryAddField(Type type, object obj, string fieldName, Dictionary<string, object> dict)
        {
            try
            {
                var field = type.GetField(fieldName, BindingFlags.Public | BindingFlags.Instance)
                         ?? type.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
                var prop = type.GetProperty(fieldName, BindingFlags.Public | BindingFlags.Instance)
                        ?? type.GetProperty(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
                if (field != null)
                    dict[fieldName] = field.GetValue(obj);
                else if (prop != null)
                    dict[fieldName] = prop.GetValue(obj);
            }
            catch { /* skip unavailable fields */ }
        }
    }
}
