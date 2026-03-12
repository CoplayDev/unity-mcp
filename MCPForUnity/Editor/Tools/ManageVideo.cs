using System;
using System.Collections.Generic;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Video;

namespace MCPForUnity.Editor.Tools
{
    [McpForUnityTool("manage_video", AutoRegister = true)]
    public static class ManageVideo
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
                    "list_players" => ListPlayers(p),
                    "get_player"   => GetPlayer(p),
                    "set_player"   => SetPlayer(p),
                    "play"         => PlaybackControl(p, "play"),
                    "pause"        => PlaybackControl(p, "pause"),
                    "stop"         => PlaybackControl(p, "stop"),
                    "set_time"     => SetTime(p),
                    _ => new ErrorResponse($"Unknown action: '{action}'.")
                };
            }
            catch (Exception e)
            {
                McpLog.Error($"[ManageVideo] Action '{action}' failed: {e}");
                return new ErrorResponse($"Internal error: {e.Message}");
            }
        }

        private static object ListPlayers(ToolParams p)
        {
            var players = UnityEngine.Object.FindObjectsByType<VideoPlayer>(FindObjectsSortMode.InstanceID);
            var pagination = PaginationRequest.FromParams(
                new JObject { ["page_size"] = p.GetInt("page_size"), ["cursor"] = p.GetInt("cursor") });

            var allItems = new List<Dictionary<string, object>>();
            foreach (var vp in players)
            {
                allItems.Add(new Dictionary<string, object>
                {
                    ["name"] = vp.gameObject.name,
                    ["instance_id"] = vp.gameObject.GetInstanceID(),
                    ["source"] = vp.source.ToString(),
                    ["is_playing"] = vp.isPlaying,
                    ["url"] = vp.source == VideoSource.Url ? vp.url : null,
                    ["clip_name"] = vp.source == VideoSource.VideoClip && vp.clip != null ? vp.clip.name : null,
                });
            }

            var page = PaginationResponse<Dictionary<string, object>>.Create(allItems, pagination);
            return new SuccessResponse($"Found {players.Length} VideoPlayer(s).", page);
        }

        private static object GetPlayer(ToolParams p)
        {
            var vp = ResolveVideoPlayer(p);
            if (vp == null) return new ErrorResponse("VideoPlayer not found.");

            return new SuccessResponse($"VideoPlayer '{vp.gameObject.name}'.", new Dictionary<string, object>
            {
                ["name"] = vp.gameObject.name,
                ["source"] = vp.source.ToString(),
                ["url"] = vp.source == VideoSource.Url ? vp.url : null,
                ["clip_name"] = vp.source == VideoSource.VideoClip && vp.clip != null ? vp.clip.name : null,
                ["is_playing"] = vp.isPlaying,
                ["is_paused"] = vp.isPaused,
                ["is_prepared"] = vp.isPrepared,
                ["time"] = Math.Round(vp.time, 4),
                ["length"] = Math.Round(vp.length, 4),
                ["frame"] = vp.frame,
                ["frame_count"] = vp.frameCount,
                ["playback_speed"] = Math.Round(vp.playbackSpeed, 4),
                ["is_looping"] = vp.isLooping,
                ["render_mode"] = vp.renderMode.ToString(),
                ["audio_output_mode"] = vp.audioOutputMode.ToString(),
            });
        }

        private static object SetPlayer(ToolParams p)
        {
            var vp = ResolveVideoPlayer(p);
            if (vp == null) return new ErrorResponse("VideoPlayer not found.");

            var propsToken = p.GetRaw("properties");
            if (propsToken == null) return new ErrorResponse("'properties' parameter is required.");

            JObject props;
            if (propsToken.Type == JTokenType.String)
            {
                try { props = JObject.Parse(propsToken.ToString()); }
                catch { return new ErrorResponse("'properties' must be valid JSON."); }
            }
            else
            {
                props = propsToken as JObject;
                if (props == null) return new ErrorResponse("'properties' must be a JSON object.");
            }

            Undo.RecordObject(vp, "MCP SetPlayer");

            if (props["url"] != null) { vp.source = VideoSource.Url; vp.url = props["url"].ToString(); }
            if (props["playback_speed"] != null) vp.playbackSpeed = props["playback_speed"].ToObject<float>();
            if (props["loop"] != null) vp.isLooping = props["loop"].ToObject<bool>();
            if (props["skip_on_drop"] != null) vp.skipOnDrop = props["skip_on_drop"].ToObject<bool>();

            EditorUtility.SetDirty(vp);
            return new SuccessResponse($"VideoPlayer '{vp.gameObject.name}' updated.");
        }

        private static object PlaybackControl(ToolParams p, string command)
        {
            var vp = ResolveVideoPlayer(p);
            if (vp == null) return new ErrorResponse("VideoPlayer not found.");

            switch (command)
            {
                case "play":  vp.Play();  break;
                case "pause": vp.Pause(); break;
                case "stop":  vp.Stop();  break;
            }

            return new SuccessResponse($"VideoPlayer '{vp.gameObject.name}': {command} executed.");
        }

        private static object SetTime(ToolParams p)
        {
            var vp = ResolveVideoPlayer(p);
            if (vp == null) return new ErrorResponse("VideoPlayer not found.");

            float? time = p.GetFloat("time");
            if (time == null) return new ErrorResponse("'time' parameter is required.");

            vp.time = time.Value;
            return new SuccessResponse($"Time set to {time.Value:F4}s on '{vp.gameObject.name}'.");
        }

        private static VideoPlayer ResolveVideoPlayer(ToolParams p)
        {
            string target = p.Get("target");
            if (string.IsNullOrEmpty(target)) return null;
            var go = ObjectResolver.ResolveGameObject(new JValue(target));
            return go != null ? go.GetComponent<VideoPlayer>() : null;
        }
    }
}
