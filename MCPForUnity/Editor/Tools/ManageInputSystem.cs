#if UNITY_INPUT_SYSTEM
using System;
using System.Collections.Generic;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MCPForUnity.Editor.Tools
{
    [McpForUnityTool("manage_input_system", AutoRegister = true)]
    public static class ManageInputSystem
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
                    "list_action_assets" => ListActionAssets(),
                    "get_action_map"     => GetActionMap(p),
                    "get_action"         => GetAction(p),
                    "list_devices"       => ListDevices(),
                    "get_device"         => GetDevice(p),
                    "list_player_inputs" => ListPlayerInputs(p),
                    _ => new ErrorResponse($"Unknown action: '{action}'.")
                };
            }
            catch (Exception e)
            {
                McpLog.Error($"[ManageInputSystem] Action '{action}' failed: {e}");
                return new ErrorResponse($"Internal error: {e.Message}");
            }
        }

        private static object ListActionAssets()
        {
            var guids = AssetDatabase.FindAssets("t:InputActionAsset");
            var results = new List<Dictionary<string, object>>();
            foreach (var guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<InputActionAsset>(path);
                if (asset == null) continue;
                results.Add(new Dictionary<string, object>
                {
                    ["name"] = asset.name,
                    ["path"] = path,
                    ["action_map_count"] = asset.actionMaps.Count,
                });
            }
            return new SuccessResponse($"Found {results.Count} InputActionAsset(s).", new Dictionary<string, object>
            {
                ["assets"] = results,
            });
        }

        private static object GetActionMap(ToolParams p)
        {
            var asset = ResolveAsset(p);
            if (asset == null) return new ErrorResponse("InputActionAsset not found. Provide 'asset' param.");

            var mapResult = p.GetRequired("map_name");
            if (!mapResult.IsSuccess) return new ErrorResponse(mapResult.ErrorMessage);

            var map = asset.FindActionMap(mapResult.Value);
            if (map == null) return new ErrorResponse($"Action map '{mapResult.Value}' not found.");

            var actions = new List<Dictionary<string, object>>();
            foreach (var act in map.actions)
            {
                actions.Add(new Dictionary<string, object>
                {
                    ["name"] = act.name,
                    ["type"] = act.type.ToString(),
                    ["binding_count"] = act.bindings.Count,
                    ["enabled"] = act.enabled,
                });
            }

            return new SuccessResponse($"Action map '{map.name}'.", new Dictionary<string, object>
            {
                ["name"] = map.name,
                ["actions"] = actions,
            });
        }

        private static object GetAction(ToolParams p)
        {
            var asset = ResolveAsset(p);
            if (asset == null) return new ErrorResponse("InputActionAsset not found.");

            var actionResult = p.GetRequired("action_name");
            if (!actionResult.IsSuccess) return new ErrorResponse(actionResult.ErrorMessage);

            InputAction found = asset.FindAction(actionResult.Value);
            if (found == null) return new ErrorResponse($"Action '{actionResult.Value}' not found.");

            var bindings = new List<Dictionary<string, object>>();
            foreach (var binding in found.bindings)
            {
                bindings.Add(new Dictionary<string, object>
                {
                    ["path"] = binding.path,
                    ["effective_path"] = binding.effectivePath,
                    ["groups"] = binding.groups,
                    ["interactions"] = binding.interactions,
                    ["processors"] = binding.processors,
                    ["is_composite"] = binding.isComposite,
                    ["is_part_of_composite"] = binding.isPartOfComposite,
                });
            }

            return new SuccessResponse($"Action '{found.name}'.", new Dictionary<string, object>
            {
                ["name"] = found.name,
                ["type"] = found.type.ToString(),
                ["enabled"] = found.enabled,
                ["bindings"] = bindings,
            });
        }

        private static object ListDevices()
        {
            var devices = InputSystem.devices;
            var results = new List<Dictionary<string, object>>();
            foreach (var dev in devices)
            {
                results.Add(new Dictionary<string, object>
                {
                    ["name"] = dev.name,
                    ["display_name"] = dev.displayName,
                    ["layout"] = dev.layout,
                    ["device_id"] = dev.deviceId,
                    ["enabled"] = dev.enabled,
                    ["added"] = dev.added,
                });
            }
            return new SuccessResponse($"Found {results.Count} input device(s).", new Dictionary<string, object>
            {
                ["devices"] = results,
            });
        }

        private static object GetDevice(ToolParams p)
        {
            var nameResult = p.GetRequired("device_name");
            if (!nameResult.IsSuccess) return new ErrorResponse(nameResult.ErrorMessage);

            InputDevice found = null;
            foreach (var dev in InputSystem.devices)
            {
                if (string.Equals(dev.name, nameResult.Value, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(dev.displayName, nameResult.Value, StringComparison.OrdinalIgnoreCase))
                {
                    found = dev;
                    break;
                }
            }

            if (found == null) return new ErrorResponse($"Device '{nameResult.Value}' not found.");

            var controls = new List<Dictionary<string, object>>();
            foreach (var ctrl in found.allControls)
            {
                if (ctrl.parent == found) // top-level controls only
                {
                    controls.Add(new Dictionary<string, object>
                    {
                        ["name"] = ctrl.name,
                        ["layout"] = ctrl.layout,
                        ["path"] = ctrl.path,
                    });
                }
            }

            return new SuccessResponse($"Device '{found.displayName}'.", new Dictionary<string, object>
            {
                ["name"] = found.name,
                ["display_name"] = found.displayName,
                ["layout"] = found.layout,
                ["device_id"] = found.deviceId,
                ["top_level_controls"] = controls,
            });
        }

        private static object ListPlayerInputs(ToolParams p)
        {
            var players = UnityEngine.Object.FindObjectsByType<PlayerInput>(FindObjectsSortMode.InstanceID);
            var pagination = PaginationRequest.FromParams(
                new JObject { ["page_size"] = p.GetInt("page_size"), ["cursor"] = p.GetInt("cursor") });

            var allItems = new List<Dictionary<string, object>>();
            foreach (var pi in players)
            {
                allItems.Add(new Dictionary<string, object>
                {
                    ["name"] = pi.gameObject.name,
                    ["instance_id"] = pi.gameObject.GetInstanceID(),
                    ["current_action_map"] = pi.currentActionMap?.name,
                    ["default_scheme"] = pi.defaultControlScheme,
                    ["player_index"] = pi.playerIndex,
                });
            }

            var page = PaginationResponse<Dictionary<string, object>>.Create(allItems, pagination);
            return new SuccessResponse($"Found {players.Length} PlayerInput(s).", page);
        }

        private static InputActionAsset ResolveAsset(ToolParams p)
        {
            string assetRef = p.Get("asset");
            if (string.IsNullOrEmpty(assetRef)) return null;

            var asset = AssetDatabase.LoadAssetAtPath<InputActionAsset>(assetRef);
            if (asset != null) return asset;

            var guids = AssetDatabase.FindAssets($"t:InputActionAsset {assetRef}");
            if (guids.Length > 0)
                return AssetDatabase.LoadAssetAtPath<InputActionAsset>(AssetDatabase.GUIDToAssetPath(guids[0]));
            return null;
        }
    }
}
#endif
