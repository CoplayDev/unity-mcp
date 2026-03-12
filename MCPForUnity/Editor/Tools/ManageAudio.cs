using System;
using System.Collections.Generic;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;

namespace MCPForUnity.Editor.Tools
{
    /// <summary>
    /// MCP tool for Unity audio management.
    /// Actions: list_sources, get_source, set_source, play, stop, pause,
    ///          list_clips, get_clip_info, list_mixers, get_mixer, set_mixer_param.
    /// </summary>
    [McpForUnityTool("manage_audio", AutoRegister = true)]
    public static class ManageAudio
    {
        public static object HandleCommand(JObject @params)
        {
            if (@params == null)
                return new ErrorResponse("Parameters cannot be null.");

            var p = new ToolParams(@params);
            var actionResult = p.GetRequired("action");
            if (!actionResult.IsSuccess)
                return new ErrorResponse(actionResult.ErrorMessage);

            string action = actionResult.Value.ToLowerInvariant();

            try
            {
                return action switch
                {
                    "list_sources"    => ListSources(p),
                    "get_source"      => GetSource(p),
                    "set_source"      => SetSource(p),
                    "play"            => PlaybackControl(p, "play"),
                    "stop"            => PlaybackControl(p, "stop"),
                    "pause"           => PlaybackControl(p, "pause"),
                    "list_clips"      => ListClips(p),
                    "get_clip_info"   => GetClipInfo(p),
                    "list_mixers"     => ListMixers(p),
                    "get_mixer"       => GetMixer(p),
                    "set_mixer_param" => SetMixerParam(p),
                    _ => new ErrorResponse(
                        $"Unknown action: '{action}'. Supported: list_sources, get_source, set_source, " +
                        "play, stop, pause, list_clips, get_clip_info, list_mixers, get_mixer, set_mixer_param")
                };
            }
            catch (Exception e)
            {
                McpLog.Error($"[ManageAudio] Action '{action}' failed: {e}");
                return new ErrorResponse($"Internal error: {e.Message}");
            }
        }

        #region AudioSource Operations

        private static object ListSources(ToolParams p)
        {
            var sources = UnityEngine.Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.InstanceID);
            var pagination = PaginationRequest.FromParams(
                new JObject { ["page_size"] = p.GetInt("page_size"), ["cursor"] = p.GetInt("cursor") });

            var allItems = new List<Dictionary<string, object>>();
            foreach (var src in sources)
            {
                allItems.Add(new Dictionary<string, object>
                {
                    ["gameobject_name"] = src.gameObject.name,
                    ["instance_id"] = src.gameObject.GetInstanceID(),
                    ["clip_name"] = src.clip != null ? src.clip.name : null,
                    ["volume"] = Math.Round(src.volume, 4),
                    ["is_playing"] = src.isPlaying,
                    ["loop"] = src.loop,
                    ["spatial_blend"] = Math.Round(src.spatialBlend, 4),
                    ["mute"] = src.mute,
                });
            }

            var page = PaginationResponse<Dictionary<string, object>>.Create(allItems, pagination);
            return new SuccessResponse($"Found {sources.Length} AudioSource component(s).", page);
        }

        private static object GetSource(ToolParams p)
        {
            var targetResult = p.GetRequired("target");
            if (!targetResult.IsSuccess)
                return new ErrorResponse(targetResult.ErrorMessage);

            var go = ObjectResolver.ResolveGameObject(new JValue(targetResult.Value));
            if (go == null)
                return new ErrorResponse($"GameObject '{targetResult.Value}' not found.");

            var src = go.GetComponent<AudioSource>();
            if (src == null)
                return new ErrorResponse($"No AudioSource on '{go.name}'.");

            return new SuccessResponse($"AudioSource on '{go.name}'.", new Dictionary<string, object>
            {
                ["gameobject_name"] = go.name,
                ["instance_id"] = go.GetInstanceID(),
                ["clip_name"] = src.clip != null ? src.clip.name : null,
                ["volume"] = Math.Round(src.volume, 4),
                ["pitch"] = Math.Round(src.pitch, 4),
                ["spatial_blend"] = Math.Round(src.spatialBlend, 4),
                ["stereo_pan"] = Math.Round(src.panStereo, 4),
                ["doppler_level"] = Math.Round(src.dopplerLevel, 4),
                ["min_distance"] = Math.Round(src.minDistance, 4),
                ["max_distance"] = Math.Round(src.maxDistance, 4),
                ["is_playing"] = src.isPlaying,
                ["loop"] = src.loop,
                ["mute"] = src.mute,
                ["play_on_awake"] = src.playOnAwake,
                ["priority"] = src.priority,
                ["output_mixer_group"] = src.outputAudioMixerGroup != null ? src.outputAudioMixerGroup.name : null,
            });
        }

        private static object SetSource(ToolParams p)
        {
            var targetResult = p.GetRequired("target");
            if (!targetResult.IsSuccess)
                return new ErrorResponse(targetResult.ErrorMessage);

            var go = ObjectResolver.ResolveGameObject(new JValue(targetResult.Value));
            if (go == null)
                return new ErrorResponse($"GameObject '{targetResult.Value}' not found.");

            var src = go.GetComponent<AudioSource>();
            if (src == null)
                return new ErrorResponse($"No AudioSource on '{go.name}'.");

            var props = ParseProps(p);
            if (props == null) return new ErrorResponse("'properties' parameter is required (valid JSON).");

            Undo.RecordObject(src, "MCP SetAudioSource");

            if (props["volume"] != null) src.volume = props["volume"].ToObject<float>();
            if (props["pitch"] != null) src.pitch = props["pitch"].ToObject<float>();
            if (props["spatial_blend"] != null) src.spatialBlend = props["spatial_blend"].ToObject<float>();
            if (props["loop"] != null) src.loop = props["loop"].ToObject<bool>();
            if (props["mute"] != null) src.mute = props["mute"].ToObject<bool>();
            if (props["play_on_awake"] != null) src.playOnAwake = props["play_on_awake"].ToObject<bool>();
            if (props["priority"] != null) src.priority = props["priority"].ToObject<int>();
            if (props["min_distance"] != null) src.minDistance = props["min_distance"].ToObject<float>();
            if (props["max_distance"] != null) src.maxDistance = props["max_distance"].ToObject<float>();

            EditorUtility.SetDirty(src);
            return new SuccessResponse($"AudioSource on '{go.name}' updated.");
        }

        private static object PlaybackControl(ToolParams p, string command)
        {
            if (!Application.isPlaying)
                return new ErrorResponse($"'{command}' requires Play mode.");

            var targetResult = p.GetRequired("target");
            if (!targetResult.IsSuccess)
                return new ErrorResponse(targetResult.ErrorMessage);

            var go = ObjectResolver.ResolveGameObject(new JValue(targetResult.Value));
            if (go == null)
                return new ErrorResponse($"GameObject '{targetResult.Value}' not found.");

            var src = go.GetComponent<AudioSource>();
            if (src == null)
                return new ErrorResponse($"No AudioSource on '{go.name}'.");

            switch (command)
            {
                case "play":  src.Play();  break;
                case "stop":  src.Stop();  break;
                case "pause": src.Pause(); break;
            }

            return new SuccessResponse($"AudioSource '{go.name}': {command} executed.");
        }

        #endregion

        #region AudioClip Operations

        private static object ListClips(ToolParams p)
        {
            string filter = p.Get("filter", "");
            string searchFilter = string.IsNullOrEmpty(filter) ? "t:AudioClip" : $"t:AudioClip {filter}";
            var guids = AssetDatabase.FindAssets(searchFilter);

            var pagination = PaginationRequest.FromParams(
                new JObject { ["page_size"] = p.GetInt("page_size"), ["cursor"] = p.GetInt("cursor") });

            var allItems = new List<Dictionary<string, object>>();
            foreach (var guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                if (clip == null) continue;

                allItems.Add(new Dictionary<string, object>
                {
                    ["name"] = clip.name,
                    ["path"] = path,
                    ["length"] = Math.Round(clip.length, 3),
                    ["frequency"] = clip.frequency,
                    ["channels"] = clip.channels,
                });
            }

            var page = PaginationResponse<Dictionary<string, object>>.Create(allItems, pagination);
            return new SuccessResponse($"Found {allItems.Count} AudioClip asset(s).", page);
        }

        private static object GetClipInfo(ToolParams p)
        {
            var targetResult = p.GetRequired("target");
            if (!targetResult.IsSuccess)
                return new ErrorResponse(targetResult.ErrorMessage);

            string target = targetResult.Value;
            AudioClip clip = null;

            // Try as asset path first
            clip = AssetDatabase.LoadAssetAtPath<AudioClip>(target);
            if (clip == null)
            {
                // Try searching by name
                var guids = AssetDatabase.FindAssets($"t:AudioClip {target}");
                if (guids.Length > 0)
                    clip = AssetDatabase.LoadAssetAtPath<AudioClip>(AssetDatabase.GUIDToAssetPath(guids[0]));
            }

            if (clip == null)
                return new ErrorResponse($"AudioClip '{target}' not found.");

            return new SuccessResponse($"AudioClip '{clip.name}'.", new Dictionary<string, object>
            {
                ["name"] = clip.name,
                ["length"] = Math.Round(clip.length, 3),
                ["frequency"] = clip.frequency,
                ["channels"] = clip.channels,
                ["samples"] = clip.samples,
                ["load_type"] = clip.loadType.ToString(),
                ["preload_audio_data"] = clip.preloadAudioData,
                ["load_in_background"] = clip.loadInBackground,
            });
        }

        #endregion

        #region AudioMixer Operations

        private static object ListMixers(ToolParams p)
        {
            var guids = AssetDatabase.FindAssets("t:AudioMixer");
            var pagination = PaginationRequest.FromParams(
                new JObject { ["page_size"] = p.GetInt("page_size"), ["cursor"] = p.GetInt("cursor") });

            var allItems = new List<Dictionary<string, object>>();
            foreach (var guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>(path);
                if (mixer == null) continue;

                allItems.Add(new Dictionary<string, object>
                {
                    ["name"] = mixer.name,
                    ["path"] = path,
                });
            }

            var page = PaginationResponse<Dictionary<string, object>>.Create(allItems, pagination);
            return new SuccessResponse($"Found {allItems.Count} AudioMixer asset(s).", page);
        }

        private static object GetMixer(ToolParams p)
        {
            var targetResult = p.GetRequired("target");
            if (!targetResult.IsSuccess)
                return new ErrorResponse(targetResult.ErrorMessage);

            string target = targetResult.Value;
            AudioMixer mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>(target);
            if (mixer == null)
            {
                var guids = AssetDatabase.FindAssets($"t:AudioMixer {target}");
                if (guids.Length > 0)
                    mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>(AssetDatabase.GUIDToAssetPath(guids[0]));
            }

            if (mixer == null)
                return new ErrorResponse($"AudioMixer '{target}' not found.");

            // Enumerate exposed parameters via reflection (no public API)
            var exposedParams = new List<Dictionary<string, object>>();
            var exposed = mixer.FindMatchingGroups(string.Empty);
            // Try to read exposed parameters from the mixer
            var serialized = new UnityEditor.SerializedObject(mixer);
            var exposedProp = serialized.FindProperty("m_ExposedParameters");
            if (exposedProp != null && exposedProp.isArray)
            {
                for (int i = 0; i < exposedProp.arraySize; i++)
                {
                    var elem = exposedProp.GetArrayElementAtIndex(i);
                    string paramName = elem.FindPropertyRelative("name")?.stringValue;
                    if (!string.IsNullOrEmpty(paramName))
                    {
                        mixer.GetFloat(paramName, out float val);
                        exposedParams.Add(new Dictionary<string, object>
                        {
                            ["name"] = paramName,
                            ["value"] = Math.Round(val, 4),
                        });
                    }
                }
            }

            var groups = new List<string>();
            foreach (var g in mixer.FindMatchingGroups(string.Empty))
                groups.Add(g.name);

            return new SuccessResponse($"AudioMixer '{mixer.name}'.", new Dictionary<string, object>
            {
                ["name"] = mixer.name,
                ["groups"] = groups,
                ["exposed_parameters"] = exposedParams,
            });
        }

        private static object SetMixerParam(ToolParams p)
        {
            var targetResult = p.GetRequired("target");
            if (!targetResult.IsSuccess)
                return new ErrorResponse(targetResult.ErrorMessage);

            var paramResult = p.GetRequired("param_name");
            if (!paramResult.IsSuccess)
                return new ErrorResponse(paramResult.ErrorMessage);

            float? value = p.GetFloat("value");
            if (value == null)
                return new ErrorResponse("'value' parameter is required.");

            string target = targetResult.Value;
            AudioMixer mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>(target);
            if (mixer == null)
            {
                var guids = AssetDatabase.FindAssets($"t:AudioMixer {target}");
                if (guids.Length > 0)
                    mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>(AssetDatabase.GUIDToAssetPath(guids[0]));
            }

            if (mixer == null)
                return new ErrorResponse($"AudioMixer '{target}' not found.");

            bool ok = mixer.SetFloat(paramResult.Value, value.Value);
            if (!ok)
                return new ErrorResponse($"Could not set '{paramResult.Value}' — is it exposed?");

            return new SuccessResponse($"Mixer param '{paramResult.Value}' set to {value.Value}.");
        }

        #endregion

        #region Helpers

        private static JObject ParseProps(ToolParams p)
        {
            var propsToken = p.GetRaw("properties");
            if (propsToken == null) return null;

            if (propsToken.Type == JTokenType.String)
            {
                try { return JObject.Parse(propsToken.ToString()); }
                catch { return null; }
            }
            return propsToken as JObject;
        }

        #endregion
    }
}
