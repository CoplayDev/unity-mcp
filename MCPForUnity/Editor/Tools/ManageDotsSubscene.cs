#if UNITY_ENTITIES
using System;
using System.Collections.Generic;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using Unity.Entities;
using Unity.Scenes;
using UnityEngine;

namespace MCPForUnity.Editor.Tools
{
    /// <summary>
    /// MCP tool for Unity DOTS SubScene management.
    /// Actions: list_subscenes, load_subscene, unload_subscene, get_subscene_status, list_sections
    /// Requires com.unity.entities package (SubScene is part of Unity.Scenes).
    /// </summary>
    [McpForUnityTool("manage_dots_subscene", AutoRegister = false)]
    public static class ManageDotsSubscene
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
                    "list_subscenes"     => ListSubscenes(p),
                    "load_subscene"      => LoadSubscene(p),
                    "unload_subscene"    => UnloadSubscene(p),
                    "get_subscene_status" => GetSubsceneStatus(p),
                    "list_sections"      => ListSections(p),
                    _ => new ErrorResponse(
                        $"Unknown action: '{action}'. Supported: list_subscenes, " +
                        "load_subscene, unload_subscene, get_subscene_status, list_sections")
                };
            }
            catch (Exception e)
            {
                McpLog.Error($"[ManageDotsSubscene] Action '{action}' failed: {e}");
                return new ErrorResponse($"Internal error: {e.Message}");
            }
        }

        #region List SubScenes

        private static object ListSubscenes(ToolParams p)
        {
            // Find all SubScene MonoBehaviours in the loaded scenes
            var subscenes = UnityEngine.Object.FindObjectsByType<SubScene>(FindObjectsSortMode.None);

            var results = new List<object>();
            foreach (var sub in subscenes)
            {
                var data = new Dictionary<string, object>
                {
                    ["name"]       = sub.SceneName,
                    ["game_object"] = sub.gameObject.name,
                    ["auto_load"]  = sub.AutoLoadScene,
                    ["is_loaded"]  = sub.IsLoaded,
                };

                if (sub.SceneAsset != null)
                {
                    data["scene_asset"] = sub.SceneAsset.name;
                }

                // Try to get scene entity for streaming state
                var world = World.DefaultGameObjectInjectionWorld;
                if (world != null && world.IsCreated)
                {
                    try
                    {
                        var sceneEntity = SceneSystem.GetSceneEntity(world.Unmanaged, sub.SceneGUID);
                        if (sceneEntity != Entity.Null)
                        {
                            data["scene_entity_index"] = sceneEntity.Index;
                            var state = SceneSystem.GetSceneStreamingState(world.Unmanaged, sceneEntity);
                            data["streaming_status"] = state.ToString();
                        }
                    }
                    catch { /* SceneEntity not available */ }
                }

                results.Add(data);
            }

            return new SuccessResponse(
                $"Found {results.Count} SubScene(s) in hierarchy.",
                new Dictionary<string, object>
                {
                    ["count"]     = results.Count,
                    ["subscenes"] = results
                });
        }

        #endregion

        #region Load / Unload

        private static object LoadSubscene(ToolParams p)
        {
            string sceneName = p.Get("scene_name");
            if (string.IsNullOrEmpty(sceneName))
                return new ErrorResponse("'scene_name' parameter is required.");

            var subscene = FindSubsceneByName(sceneName);
            if (subscene == null)
                return new ErrorResponse($"SubScene '{sceneName}' not found in hierarchy.");

            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
                return new ErrorResponse("Default World not available.");

            try
            {
                // Enable the SubScene component to trigger loading
                if (!subscene.gameObject.activeSelf)
                    subscene.gameObject.SetActive(true);

                var sceneEntity = SceneSystem.GetSceneEntity(world.Unmanaged, subscene.SceneGUID);
                if (sceneEntity != Entity.Null)
                {
                    // Add RequestSceneLoaded if not present
                    var em = world.EntityManager;
                    if (!em.HasComponent<RequestSceneLoaded>(sceneEntity))
                    {
                        em.AddComponentData(sceneEntity, new RequestSceneLoaded());
                    }
                }

                return new SuccessResponse(
                    $"Requested load for SubScene '{sceneName}'.",
                    new Dictionary<string, object>
                    {
                        ["scene_name"] = sceneName,
                        ["action"]     = "load_requested"
                    });
            }
            catch (Exception e)
            {
                return new ErrorResponse($"Failed to load SubScene: {e.Message}");
            }
        }

        private static object UnloadSubscene(ToolParams p)
        {
            string sceneName = p.Get("scene_name");
            if (string.IsNullOrEmpty(sceneName))
                return new ErrorResponse("'scene_name' parameter is required.");

            var subscene = FindSubsceneByName(sceneName);
            if (subscene == null)
                return new ErrorResponse($"SubScene '{sceneName}' not found in hierarchy.");

            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
                return new ErrorResponse("Default World not available.");

            try
            {
                var sceneEntity = SceneSystem.GetSceneEntity(world.Unmanaged, subscene.SceneGUID);
                if (sceneEntity != Entity.Null)
                {
                    SceneSystem.UnloadScene(
                        world.Unmanaged,
                        sceneEntity,
                        SceneSystem.UnloadParameters.DestroyMetaEntities);
                }

                return new SuccessResponse(
                    $"Requested unload for SubScene '{sceneName}'.",
                    new Dictionary<string, object>
                    {
                        ["scene_name"] = sceneName,
                        ["action"]     = "unload_requested"
                    });
            }
            catch (Exception e)
            {
                return new ErrorResponse($"Failed to unload SubScene: {e.Message}");
            }
        }

        #endregion

        #region Status & Sections

        private static object GetSubsceneStatus(ToolParams p)
        {
            string sceneName = p.Get("scene_name");
            if (string.IsNullOrEmpty(sceneName))
                return new ErrorResponse("'scene_name' parameter is required.");

            var subscene = FindSubsceneByName(sceneName);
            if (subscene == null)
                return new ErrorResponse($"SubScene '{sceneName}' not found in hierarchy.");

            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
                return new ErrorResponse("Default World not available.");

            var result = new Dictionary<string, object>
            {
                ["scene_name"]  = subscene.SceneName,
                ["auto_load"]   = subscene.AutoLoadScene,
                ["is_loaded"]   = subscene.IsLoaded,
                ["game_object"] = subscene.gameObject.name,
                ["active"]      = subscene.gameObject.activeSelf,
            };

            if (subscene.SceneAsset != null)
            {
                result["scene_asset_path"] = UnityEditor.AssetDatabase.GetAssetPath(subscene.SceneAsset);
            }

            try
            {
                var sceneEntity = SceneSystem.GetSceneEntity(world.Unmanaged, subscene.SceneGUID);
                if (sceneEntity != Entity.Null)
                {
                    result["scene_entity_index"] = sceneEntity.Index;
                    var state = SceneSystem.GetSceneStreamingState(world.Unmanaged, sceneEntity);
                    result["streaming_status"] = state.ToString();

                    // Count sections
                    var em = world.EntityManager;
                    if (em.HasBuffer<ResolvedSectionEntity>(sceneEntity))
                    {
                        var sections = em.GetBuffer<ResolvedSectionEntity>(sceneEntity);
                        result["section_count"] = sections.Length;

                        int loadedSections = 0;
                        for (int i = 0; i < sections.Length; i++)
                        {
                            if (em.HasComponent<RequestSceneLoaded>(sections[i].SectionEntity))
                                loadedSections++;
                        }
                        result["loaded_sections"] = loadedSections;
                    }
                }
                else
                {
                    result["streaming_status"] = "no_scene_entity";
                }
            }
            catch (Exception e)
            {
                result["streaming_status"] = $"error: {e.Message}";
            }

            return new SuccessResponse($"Status for SubScene '{sceneName}'.", result);
        }

        private static object ListSections(ToolParams p)
        {
            string sceneName = p.Get("scene_name");
            if (string.IsNullOrEmpty(sceneName))
                return new ErrorResponse("'scene_name' parameter is required.");

            var subscene = FindSubsceneByName(sceneName);
            if (subscene == null)
                return new ErrorResponse($"SubScene '{sceneName}' not found in hierarchy.");

            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
                return new ErrorResponse("Default World not available.");

            try
            {
                var sceneEntity = SceneSystem.GetSceneEntity(world.Unmanaged, subscene.SceneGUID);
                if (sceneEntity == Entity.Null)
                    return new ErrorResponse("SubScene has no scene entity. Is it loaded?");

                var em = world.EntityManager;
                if (!em.HasBuffer<ResolvedSectionEntity>(sceneEntity))
                    return new ErrorResponse("No resolved sections found. Scene may still be loading.");

                var sections = em.GetBuffer<ResolvedSectionEntity>(sceneEntity);
                var results = new List<object>();

                for (int i = 0; i < sections.Length; i++)
                {
                    var sectionEntity = sections[i].SectionEntity;
                    var sectionData = new Dictionary<string, object>
                    {
                        ["section_index"]    = i,
                        ["entity_index"]     = sectionEntity.Index,
                        ["entity_version"]   = sectionEntity.Version,
                        ["is_load_requested"] = em.HasComponent<RequestSceneLoaded>(sectionEntity),
                    };

                    // Try to get section streaming state
                    try
                    {
                        var sectionState = SceneSystem.GetSectionStreamingState(
                            world.Unmanaged, sectionEntity);
                        sectionData["streaming_state"] = sectionState.ToString();
                    }
                    catch { sectionData["streaming_state"] = "<unknown>"; }

                    results.Add(sectionData);
                }

                return new SuccessResponse(
                    $"SubScene '{sceneName}' has {results.Count} section(s).",
                    new Dictionary<string, object>
                    {
                        ["scene_name"]    = sceneName,
                        ["section_count"] = results.Count,
                        ["sections"]      = results
                    });
            }
            catch (Exception e)
            {
                return new ErrorResponse($"Failed to list sections: {e.Message}");
            }
        }

        #endregion

        #region Helpers

        private static SubScene FindSubsceneByName(string name)
        {
            var subscenes = UnityEngine.Object.FindObjectsByType<SubScene>(FindObjectsSortMode.None);
            foreach (var sub in subscenes)
            {
                if (string.Equals(sub.SceneName, name, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(sub.gameObject.name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return sub;
                }
            }
            return null;
        }

        #endregion
    }
}
#endif
