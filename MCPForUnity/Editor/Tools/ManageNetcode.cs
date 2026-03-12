#if UNITY_NETCODE
using System;
using System.Collections.Generic;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Unity.Netcode;

namespace MCPForUnity.Editor.Tools
{
    [McpForUnityTool("manage_netcode", AutoRegister = true)]
    public static class ManageNetcode
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
                    "get_network_manager"  => GetNetworkManager(),
                    "list_network_objects" => ListNetworkObjects(p),
                    "get_network_object"   => GetNetworkObject(p),
                    "start_host"           => StartHost(),
                    "start_server"         => StartServer(),
                    "start_client"         => StartClient(),
                    "shutdown"             => Shutdown(),
                    _ => new ErrorResponse($"Unknown action: '{action}'.")
                };
            }
            catch (Exception e)
            {
                McpLog.Error($"[ManageNetcode] Action '{action}' failed: {e}");
                return new ErrorResponse($"Internal error: {e.Message}");
            }
        }

        private static object GetNetworkManager()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null) return new ErrorResponse("No NetworkManager found.");

            var data = new Dictionary<string, object>
            {
                ["is_server"] = nm.IsServer,
                ["is_client"] = nm.IsClient,
                ["is_host"] = nm.IsHost,
                ["is_listening"] = nm.IsListening,
                ["connected_clients_count"] = nm.IsServer ? nm.ConnectedClientsList.Count : 0,
                ["local_client_id"] = nm.IsClient ? nm.LocalClientId : (ulong)0,
                ["transport"] = nm.NetworkConfig?.NetworkTransport != null
                    ? nm.NetworkConfig.NetworkTransport.GetType().Name : null,
            };

            if (nm.IsServer && nm.ConnectedClientsList != null)
            {
                var clients = new List<Dictionary<string, object>>();
                foreach (var client in nm.ConnectedClientsList)
                {
                    clients.Add(new Dictionary<string, object>
                    {
                        ["client_id"] = client.ClientId,
                        ["player_object"] = client.PlayerObject != null ? client.PlayerObject.gameObject.name : null,
                    });
                }
                data["connected_clients"] = clients;
            }

            return new SuccessResponse("NetworkManager state.", data);
        }

        private static object ListNetworkObjects(ToolParams p)
        {
            var netObjs = UnityEngine.Object.FindObjectsByType<NetworkObject>(FindObjectsSortMode.InstanceID);
            var pagination = PaginationRequest.FromParams(
                new JObject { ["page_size"] = p.GetInt("page_size"), ["cursor"] = p.GetInt("cursor") });

            var allItems = new List<Dictionary<string, object>>();
            foreach (var no in netObjs)
            {
                allItems.Add(new Dictionary<string, object>
                {
                    ["name"] = no.gameObject.name,
                    ["instance_id"] = no.gameObject.GetInstanceID(),
                    ["network_object_id"] = no.NetworkObjectId,
                    ["is_spawned"] = no.IsSpawned,
                    ["is_owned_by_server"] = no.IsOwnedByServer,
                    ["owner_client_id"] = no.OwnerClientId,
                });
            }

            var page = PaginationResponse<Dictionary<string, object>>.Create(allItems, pagination);
            return new SuccessResponse($"Found {netObjs.Length} NetworkObject(s).", page);
        }

        private static object GetNetworkObject(ToolParams p)
        {
            var targetResult = p.GetRequired("target");
            if (!targetResult.IsSuccess) return new ErrorResponse(targetResult.ErrorMessage);

            var go = ObjectResolver.ResolveGameObject(new JValue(targetResult.Value));
            if (go == null) return new ErrorResponse($"GameObject '{targetResult.Value}' not found.");

            var no = go.GetComponent<NetworkObject>();
            if (no == null) return new ErrorResponse($"No NetworkObject on '{go.name}'.");

            var behaviours = new List<string>();
            foreach (var nb in go.GetComponents<NetworkBehaviour>())
            {
                behaviours.Add(nb.GetType().Name);
            }

            return new SuccessResponse($"NetworkObject '{go.name}'.", new Dictionary<string, object>
            {
                ["name"] = go.name,
                ["network_object_id"] = no.NetworkObjectId,
                ["is_spawned"] = no.IsSpawned,
                ["is_owned_by_server"] = no.IsOwnedByServer,
                ["owner_client_id"] = no.OwnerClientId,
                ["is_player_object"] = no.IsPlayerObject,
                ["network_behaviours"] = behaviours,
            });
        }

        private static object StartHost()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null) return new ErrorResponse("No NetworkManager found.");
            if (!Application.isPlaying) return new ErrorResponse("Must be in Play mode.");

            bool success = nm.StartHost();
            return success
                ? new SuccessResponse("Started as host.")
                : new ErrorResponse("Failed to start host.");
        }

        private static object StartServer()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null) return new ErrorResponse("No NetworkManager found.");
            if (!Application.isPlaying) return new ErrorResponse("Must be in Play mode.");

            bool success = nm.StartServer();
            return success
                ? new SuccessResponse("Started as server.")
                : new ErrorResponse("Failed to start server.");
        }

        private static object StartClient()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null) return new ErrorResponse("No NetworkManager found.");
            if (!Application.isPlaying) return new ErrorResponse("Must be in Play mode.");

            bool success = nm.StartClient();
            return success
                ? new SuccessResponse("Started as client.")
                : new ErrorResponse("Failed to start client.");
        }

        private static object Shutdown()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null) return new ErrorResponse("No NetworkManager found.");

            nm.Shutdown();
            return new SuccessResponse("NetworkManager shut down.");
        }
    }
}
#endif
