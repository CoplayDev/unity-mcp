#if UNITY_TIMELINE
using System;
using System.Collections.Generic;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace MCPForUnity.Editor.Tools
{
    [McpForUnityTool("manage_timeline", AutoRegister = true)]
    public static class ManageTimeline
    {
        public static object HandleCommand(JObject @params)
        {
            if (@params == null) return new ErrorResponse("Parameters cannot be null.");
            var p = new ToolParams(@params);
            var actionResult = p.GetRequired("action");
            if (!actionResult.IsSuccess) return new ErrorResponse(actionResult.ErrorMessage);
            string action = actionResult.Value.ToLowerInvariant();

            try
            {
                return action switch
                {
                    "list_directors" => ListDirectors(p),
                    "get_director"   => GetDirector(p),
                    "play"           => PlaybackControl(p, "play"),
                    "pause"          => PlaybackControl(p, "pause"),
                    "stop"           => PlaybackControl(p, "stop"),
                    "set_time"       => SetTime(p),
                    "list_tracks"    => ListTracks(p),
                    "get_bindings"   => GetBindings(p),
                    _ => new ErrorResponse($"Unknown action: '{action}'.")
                };
            }
            catch (Exception e)
            {
                McpLog.Error($"[ManageTimeline] Action '{action}' failed: {e}");
                return new ErrorResponse($"Internal error: {e.Message}");
            }
        }

        private static object ListDirectors(ToolParams p)
        {
            var directors = UnityEngine.Object.FindObjectsByType<PlayableDirector>(FindObjectsSortMode.InstanceID);
            var pagination = PaginationRequest.FromParams(
                new JObject { ["page_size"] = p.GetInt("page_size"), ["cursor"] = p.GetInt("cursor") });

            var allItems = new List<Dictionary<string, object>>();
            foreach (var dir in directors)
            {
                allItems.Add(new Dictionary<string, object>
                {
                    ["name"] = dir.gameObject.name,
                    ["instance_id"] = dir.gameObject.GetInstanceID(),
                    ["state"] = dir.state.ToString(),
                    ["time"] = Math.Round(dir.time, 4),
                    ["duration"] = Math.Round(dir.duration, 4),
                    ["asset_name"] = dir.playableAsset != null ? dir.playableAsset.name : null,
                });
            }

            var page = PaginationResponse<Dictionary<string, object>>.Create(allItems, pagination);
            return new SuccessResponse($"Found {directors.Length} PlayableDirector(s).", page);
        }

        private static object GetDirector(ToolParams p)
        {
            var dir = ResolveDirector(p);
            if (dir == null) return new ErrorResponse("PlayableDirector not found.");

            return new SuccessResponse($"PlayableDirector '{dir.gameObject.name}'.", new Dictionary<string, object>
            {
                ["name"] = dir.gameObject.name,
                ["state"] = dir.state.ToString(),
                ["time"] = Math.Round(dir.time, 4),
                ["duration"] = Math.Round(dir.duration, 4),
                ["wrap_mode"] = dir.extrapolationMode.ToString(),
                ["initial_time"] = Math.Round(dir.initialTime, 4),
                ["asset_name"] = dir.playableAsset != null ? dir.playableAsset.name : null,
                ["time_update_mode"] = dir.timeUpdateMode.ToString(),
            });
        }

        private static object PlaybackControl(ToolParams p, string command)
        {
            var dir = ResolveDirector(p);
            if (dir == null) return new ErrorResponse("PlayableDirector not found.");

            switch (command)
            {
                case "play":  dir.Play();  break;
                case "pause": dir.Pause(); break;
                case "stop":  dir.Stop();  break;
            }

            return new SuccessResponse($"PlayableDirector '{dir.gameObject.name}': {command} executed.");
        }

        private static object SetTime(ToolParams p)
        {
            var dir = ResolveDirector(p);
            if (dir == null) return new ErrorResponse("PlayableDirector not found.");

            float? time = p.GetFloat("time");
            if (time == null) return new ErrorResponse("'time' parameter is required.");

            dir.time = time.Value;
            dir.Evaluate();
            return new SuccessResponse($"Time set to {time.Value:F4}s on '{dir.gameObject.name}'.");
        }

        private static object ListTracks(ToolParams p)
        {
            var dir = ResolveDirector(p);
            if (dir == null) return new ErrorResponse("PlayableDirector not found.");

            var asset = dir.playableAsset as TimelineAsset;
            if (asset == null) return new ErrorResponse("No TimelineAsset assigned.");

            var tracks = new List<Dictionary<string, object>>();
            foreach (var track in asset.GetOutputTracks())
            {
                tracks.Add(new Dictionary<string, object>
                {
                    ["name"] = track.name,
                    ["type"] = track.GetType().Name,
                    ["muted"] = track.muted,
                    ["clip_count"] = track.GetClips() != null ? System.Linq.Enumerable.Count(track.GetClips()) : 0,
                });
            }

            return new SuccessResponse($"Found {tracks.Count} track(s).", new Dictionary<string, object>
            {
                ["tracks"] = tracks,
            });
        }

        private static object GetBindings(ToolParams p)
        {
            var dir = ResolveDirector(p);
            if (dir == null) return new ErrorResponse("PlayableDirector not found.");

            var asset = dir.playableAsset as TimelineAsset;
            if (asset == null) return new ErrorResponse("No TimelineAsset assigned.");

            var bindings = new List<Dictionary<string, object>>();
            foreach (var output in asset.outputs)
            {
                var boundObj = dir.GetGenericBinding(output.sourceObject);
                bindings.Add(new Dictionary<string, object>
                {
                    ["track_name"] = output.sourceObject != null ? output.sourceObject.name : null,
                    ["stream_name"] = output.streamName,
                    ["output_type"] = output.outputTargetType?.Name,
                    ["bound_object"] = boundObj != null ? boundObj.name : null,
                });
            }

            return new SuccessResponse($"Found {bindings.Count} binding(s).", new Dictionary<string, object>
            {
                ["bindings"] = bindings,
            });
        }

        private static PlayableDirector ResolveDirector(ToolParams p)
        {
            string target = p.Get("target");
            if (string.IsNullOrEmpty(target)) return null;
            var go = ObjectResolver.ResolveGameObject(new JValue(target));
            return go != null ? go.GetComponent<PlayableDirector>() : null;
        }
    }
}
#endif
