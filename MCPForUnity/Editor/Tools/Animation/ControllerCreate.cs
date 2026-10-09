using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Runtime.Helpers;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace MCPForUnity.Editor.Tools.Animation
{
    internal static class ControllerCreate
    {
        public static object Create(JObject @params)
        {
            string controllerPath = @params["controllerPath"]?.ToString();
            if (string.IsNullOrEmpty(controllerPath))
                return new { success = false, message = "'controllerPath' is required (e.g. 'Assets/Animations/Player.controller')" };

            controllerPath = AssetPathUtility.SanitizeAssetPath(controllerPath);
            if (controllerPath == null)
                return new { success = false, message = "Invalid asset path" };

            if (!controllerPath.EndsWith(".controller", StringComparison.OrdinalIgnoreCase))
                controllerPath += ".controller";

            string dir = Path.GetDirectoryName(controllerPath)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(dir) && !AssetDatabase.IsValidFolder(dir))
                CreateFoldersRecursive(dir);

            var existing = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (existing != null)
                return new { success = false, message = $"AnimatorController already exists at '{controllerPath}'. Delete it first or use a different path." };

            var controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            AssetDatabase.SaveAssets();

            return new
            {
                success = true,
                message = $"Created AnimatorController at '{controllerPath}'",
                data = new
                {
                    path = controllerPath,
                    name = controller.name,
                    layerCount = controller.layers.Length,
                    parameterCount = controller.parameters.Length
                }
            };
        }

        public static object AddState(JObject @params)
        {
            var controller = LoadController(@params);
            if (controller == null)
                return ControllerNotFoundError(@params);

            string stateName = @params["stateName"]?.ToString();
            if (string.IsNullOrEmpty(stateName))
                return new { success = false, message = "'stateName' is required" };

            int layerIndex = @params["layerIndex"]?.ToObject<int>() ?? 0;
            if (layerIndex < 0 || layerIndex >= controller.layers.Length)
                return new { success = false, message = $"Layer index {layerIndex} out of range (controller has {controller.layers.Length} layers)" };

            var rootStateMachine = controller.layers[layerIndex].stateMachine;

            // Check for duplicate state name
            foreach (var existingState in rootStateMachine.states)
            {
                if (existingState.state.name == stateName)
                    return new { success = false, message = $"State '{stateName}' already exists in layer {layerIndex}" };
            }

            var state = rootStateMachine.AddState(stateName);

            // Optionally assign a clip
            string clipPath = @params["clipPath"]?.ToString();
            if (!string.IsNullOrEmpty(clipPath))
            {
                clipPath = AssetPathUtility.SanitizeAssetPath(clipPath);
                if (clipPath != null)
                {
                    var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
                    if (clip != null)
                        state.motion = clip;
                }
            }

            float speed = @params["speed"]?.ToObject<float>() ?? 1f;
            state.speed = speed;

            bool isDefault = @params["isDefault"]?.ToObject<bool>() ?? false;
            if (isDefault)
                rootStateMachine.defaultState = state;

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();

            return new
            {
                success = true,
                message = $"Added state '{stateName}' to layer {layerIndex}",
                data = new
                {
                    stateName,
                    layerIndex,
                    hasMotion = state.motion != null,
                    speed = state.speed,
                    isDefault
                }
            };
        }

        public static object AddTransition(JObject @params)
        {
            var controller = LoadController(@params);
            if (controller == null)
                return ControllerNotFoundError(@params);

            string fromStateName = @params["fromState"]?.ToString();
            string toStateName = @params["toState"]?.ToString();
            if (string.IsNullOrEmpty(fromStateName) || string.IsNullOrEmpty(toStateName))
                return new { success = false, message = "'fromState' and 'toState' are required" };

            int layerIndex = @params["layerIndex"]?.ToObject<int>() ?? 0;
            if (layerIndex < 0 || layerIndex >= controller.layers.Length)
                return new { success = false, message = $"Layer index {layerIndex} out of range" };

            var rootStateMachine = controller.layers[layerIndex].stateMachine;

            // Check for AnyState as source
            bool isAnyState = string.Equals(fromStateName, "AnyState", StringComparison.OrdinalIgnoreCase)
                           || string.Equals(fromStateName, "Any", StringComparison.OrdinalIgnoreCase)
                           || string.Equals(fromStateName, "Any State", StringComparison.OrdinalIgnoreCase);

            AnimatorState toState = null;
            foreach (var cs in rootStateMachine.states)
            {
                if (cs.state.name == toStateName) toState = cs.state;
            }

            if (toState == null)
                return new { success = false, message = $"State '{toStateName}' not found in layer {layerIndex}" };

            AnimatorStateTransition transition;
            if (isAnyState)
            {
                transition = rootStateMachine.AddAnyStateTransition(toState);
                fromStateName = "AnyState";
            }
            else
            {
                AnimatorState fromState = null;
                foreach (var cs in rootStateMachine.states)
                {
                    if (cs.state.name == fromStateName) fromState = cs.state;
                }

                if (fromState == null)
                    return new { success = false, message = $"State '{fromStateName}' not found in layer {layerIndex}" };

                transition = fromState.AddTransition(toState);
            }

            bool hasExitTime = @params["hasExitTime"]?.ToObject<bool>() ?? true;
            transition.hasExitTime = hasExitTime;

            float duration = @params["duration"]?.ToObject<float>() ?? 0.25f;
            transition.duration = duration;

            float exitTime = @params["exitTime"]?.ToObject<float>() ?? 0.75f;
            transition.exitTime = exitTime;

            // Optional fields: only override Unity's defaults when supplied, so a faithful
            // remove+re-add round-trip (paired with get_info) loses nothing. Names match the
            // keys emitted by GetInfo.
            if (@params["offset"] != null)
                transition.offset = @params["offset"].ToObject<float>();
            if (@params["hasFixedDuration"] != null)
                transition.hasFixedDuration = @params["hasFixedDuration"].ToObject<bool>();
            if (@params["canTransitionToSelf"] != null)
                transition.canTransitionToSelf = @params["canTransitionToSelf"].ToObject<bool>();
            if (@params["orderedInterruption"] != null)
                transition.orderedInterruption = @params["orderedInterruption"].ToObject<bool>();
            if (@params["interruptionSource"] != null)
            {
                if (Enum.TryParse<TransitionInterruptionSource>(@params["interruptionSource"].ToString(), true, out var src))
                    transition.interruptionSource = src;
            }
            if (@params["mute"] != null)
                transition.mute = @params["mute"].ToObject<bool>();
            if (@params["solo"] != null)
                transition.solo = @params["solo"].ToObject<bool>();
            string transitionName = @params["name"]?.ToString();
            if (!string.IsNullOrEmpty(transitionName))
                transition.name = transitionName;

            // Add conditions
            JToken conditionsToken = @params["conditions"];
            int conditionCount = 0;
            if (conditionsToken is JArray conditionsArray)
            {
                foreach (var condItem in conditionsArray)
                {
                    if (condItem is not JObject condObj) continue;

                    string paramName = condObj["parameter"]?.ToString();
                    if (string.IsNullOrEmpty(paramName)) continue;

                    string modeStr = condObj["mode"]?.ToString()?.ToLowerInvariant() ?? "greater";
                    float threshold = condObj["threshold"]?.ToObject<float>() ?? 0f;

                    AnimatorConditionMode mode;
                    switch (modeStr)
                    {
                        case "greater": mode = AnimatorConditionMode.Greater; break;
                        case "less": mode = AnimatorConditionMode.Less; break;
                        case "equals": mode = AnimatorConditionMode.Equals; break;
                        case "notequal":
                        case "not_equal": mode = AnimatorConditionMode.NotEqual; break;
                        case "if":
                        case "true": mode = AnimatorConditionMode.If; break;
                        case "ifnot":
                        case "if_not":
                        case "false": mode = AnimatorConditionMode.IfNot; break;
                        default: mode = AnimatorConditionMode.Greater; break;
                    }

                    transition.AddCondition(mode, threshold, paramName);
                    conditionCount++;
                }
            }

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();

            return new
            {
                success = true,
                message = $"Added transition from '{fromStateName}' to '{toStateName}' with {conditionCount} conditions",
                data = new
                {
                    fromState = fromStateName,
                    toState = toStateName,
                    hasExitTime,
                    duration,
                    conditionCount
                }
            };
        }

        // Removes transitions from 'fromState' (or AnyState) to 'toState' in a layer.
        // If 'toState' is omitted, removes ALL outgoing transitions from 'fromState'.
        // Use with add_transition to "edit" a transition: remove then re-add with new timing.
        public static object RemoveTransition(JObject @params)
        {
            var controller = LoadController(@params);
            if (controller == null)
                return ControllerNotFoundError(@params);

            string fromStateName = @params["fromState"]?.ToString();
            if (string.IsNullOrEmpty(fromStateName))
                return new { success = false, message = "'fromState' is required" };
            string toStateName = @params["toState"]?.ToString(); // optional

            int layerIndex = @params["layerIndex"]?.ToObject<int>() ?? 0;
            if (layerIndex < 0 || layerIndex >= controller.layers.Length)
                return new { success = false, message = $"Layer index {layerIndex} out of range" };

            var rootStateMachine = controller.layers[layerIndex].stateMachine;

            bool isAnyState = string.Equals(fromStateName, "AnyState", StringComparison.OrdinalIgnoreCase)
                           || string.Equals(fromStateName, "Any", StringComparison.OrdinalIgnoreCase)
                           || string.Equals(fromStateName, "Any State", StringComparison.OrdinalIgnoreCase);

            int removed = 0;

            if (isAnyState)
            {
                foreach (var t in rootStateMachine.anyStateTransitions.ToArray())
                {
                    if (string.IsNullOrEmpty(toStateName) || (t.destinationState != null && t.destinationState.name == toStateName))
                    {
                        rootStateMachine.RemoveAnyStateTransition(t);
                        removed++;
                    }
                }
                fromStateName = "AnyState";
            }
            else
            {
                AnimatorState fromState = null;
                foreach (var cs in rootStateMachine.states)
                    if (cs.state.name == fromStateName) fromState = cs.state;
                if (fromState == null)
                    return new { success = false, message = $"State '{fromStateName}' not found in layer {layerIndex}" };

                foreach (var t in fromState.transitions.ToArray())
                {
                    if (string.IsNullOrEmpty(toStateName) || (t.destinationState != null && t.destinationState.name == toStateName))
                    {
                        fromState.RemoveTransition(t);
                        removed++;
                    }
                }
            }

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();

            return new
            {
                success = true,
                message = $"Removed {removed} transition(s) from '{fromStateName}'" + (string.IsNullOrEmpty(toStateName) ? "" : $" to '{toStateName}'") + ".",
                data = new { fromState = fromStateName, toState = toStateName, removed }
            };
        }

        public static object AddParameter(JObject @params)
        {
            var controller = LoadController(@params);
            if (controller == null)
                return ControllerNotFoundError(@params);

            string paramName = @params["parameterName"]?.ToString();
            if (string.IsNullOrEmpty(paramName))
                return new { success = false, message = "'parameterName' is required" };

            string typeStr = @params["parameterType"]?.ToString()?.ToLowerInvariant() ?? "float";

            AnimatorControllerParameterType paramType;
            switch (typeStr)
            {
                case "float": paramType = AnimatorControllerParameterType.Float; break;
                case "int":
                case "integer": paramType = AnimatorControllerParameterType.Int; break;
                case "bool":
                case "boolean": paramType = AnimatorControllerParameterType.Bool; break;
                case "trigger": paramType = AnimatorControllerParameterType.Trigger; break;
                default:
                    return new { success = false, message = $"Unknown parameter type '{typeStr}'. Valid: float, int, bool, trigger" };
            }

            // Check for duplicate
            foreach (var existing in controller.parameters)
            {
                if (existing.name == paramName)
                    return new { success = false, message = $"Parameter '{paramName}' already exists" };
            }

            controller.AddParameter(paramName, paramType);

            // Set default value if provided
            JToken defaultValue = @params["defaultValue"];
            if (defaultValue != null)
            {
                var allParams = controller.parameters;
                var addedParam = allParams[allParams.Length - 1];

                switch (paramType)
                {
                    case AnimatorControllerParameterType.Float:
                        addedParam.defaultFloat = defaultValue.ToObject<float>();
                        break;
                    case AnimatorControllerParameterType.Int:
                        addedParam.defaultInt = defaultValue.ToObject<int>();
                        break;
                    case AnimatorControllerParameterType.Bool:
                        addedParam.defaultBool = defaultValue.ToObject<bool>();
                        break;
                }

                controller.parameters = allParams;
            }

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();

            return new
            {
                success = true,
                message = $"Added {typeStr} parameter '{paramName}'",
                data = new
                {
                    parameterName = paramName,
                    parameterType = typeStr,
                    totalParameters = controller.parameters.Length
                }
            };
        }

        public static object GetInfo(JObject @params)
        {
            var controller = LoadController(@params);
            if (controller == null)
                return ControllerNotFoundError(@params);

            var layers = new List<object>();
            for (int i = 0; i < controller.layers.Length; i++)
            {
                var layer = controller.layers[i];
                var states = new List<object>();
                foreach (var cs in layer.stateMachine.states)
                {
                    var transitions = new List<object>();
                    foreach (var t in cs.state.transitions)
                    {
                        var conditions = new List<object>();
                        foreach (var c in t.conditions)
                        {
                            conditions.Add(new
                            {
                                parameter = c.parameter,
                                mode = c.mode.ToString(),
                                threshold = c.threshold
                            });
                        }

                        transitions.Add(new
                        {
                            name = t.name,
                            destinationState = t.destinationState?.name,
                            hasExitTime = t.hasExitTime,
                            exitTime = t.exitTime,
                            duration = t.duration,
                            offset = t.offset,
                            hasFixedDuration = t.hasFixedDuration,
                            canTransitionToSelf = t.canTransitionToSelf,
                            orderedInterruption = t.orderedInterruption,
                            interruptionSource = t.interruptionSource.ToString(),
                            mute = t.mute,
                            solo = t.solo,
                            conditionCount = t.conditions.Length,
                            conditions
                        });
                    }

                    states.Add(new
                    {
                        name = cs.state.name,
                        speed = cs.state.speed,
                        hasMotion = cs.state.motion != null,
                        motionName = cs.state.motion?.name,
                        isDefault = layer.stateMachine.defaultState == cs.state,
                        transitionCount = cs.state.transitions.Length,
                        transitions
                    });
                }

                layers.Add(new
                {
                    index = i,
                    name = layer.name,
                    stateCount = layer.stateMachine.states.Length,
                    states
                });
            }

            var parameters = new List<object>();
            foreach (var p in controller.parameters)
            {
                parameters.Add(new
                {
                    name = p.name,
                    type = p.type.ToString(),
                    defaultFloat = p.defaultFloat,
                    defaultInt = p.defaultInt,
                    defaultBool = p.defaultBool
                });
            }

            return new
            {
                success = true,
                data = new
                {
                    path = AssetDatabase.GetAssetPath(controller),
                    name = controller.name,
                    layerCount = controller.layers.Length,
                    parameterCount = controller.parameters.Length,
                    layers,
                    parameters
                }
            };
        }

        public static object AssignToGameObject(JObject @params)
        {
            var controller = LoadController(@params);
            if (controller == null)
                return ControllerNotFoundError(@params);

            var go = ObjectResolver.ResolveGameObject(@params["target"], @params["searchMethod"]?.ToString());
            if (go == null)
                return new { success = false, message = "Target GameObject not found" };

            var animator = go.GetComponent<Animator>();
            if (animator == null)
            {
                Undo.RecordObject(go, "Add Animator Component");
                animator = Undo.AddComponent<Animator>(go);
            }

            Undo.RecordObject(animator, "Assign AnimatorController");
            animator.runtimeAnimatorController = controller;
            EditorUtility.SetDirty(go);
            AssetDatabase.SaveAssets();

            return new
            {
                success = true,
                message = $"Assigned controller '{controller.name}' to '{go.name}'",
                data = new
                {
                    gameObject = go.name,
                    controllerName = controller.name,
                    controllerPath = AssetDatabase.GetAssetPath(controller)
                }
            };
        }

        // Reads per-state properties for every state (recurses into sub-state-machines).
        // Returns [{ name, instanceId, layer, x, y, speed, motionInstanceId, motionName,
        // motionType }]. 'instanceId' round-trips into set_state_properties for an exact
        // match (duplicate names are fine); 'motionInstanceId' lets a caller transfer a
        // Motion (incl. FBX-embedded clips) to another state BY REFERENCE - no asset path.
        // Pass 'layerIndex' to scope to one layer; results are paged (page_size/cursor).
        public static object GetStateProperties(JObject @params)
        {
            var controller = LoadController(@params);
            if (controller == null)
                return ControllerNotFoundError(@params);

            int? layerFilter = @params["layerIndex"]?.ToObject<int>();
            if (layerFilter.HasValue && (layerFilter < 0 || layerFilter >= controller.layers.Length))
                return new { success = false, message = $"Layer index {layerFilter} out of range (controller has {controller.layers.Length} layers)" };

            var nodes = new List<object>();
            for (int li = 0; li < controller.layers.Length; li++)
            {
                if (layerFilter.HasValue && li != layerFilter.Value)
                    continue;
                CollectProperties(controller.layers[li].stateMachine, li, nodes);
            }

            var pagination = PaginationRequest.FromParams(@params, defaultPageSize: 50);
            var paged = PaginationResponse<object>.Create(nodes, pagination);

            return new
            {
                success = true,
                message = $"Read {paged.Items.Count} of {paged.TotalCount} state(s).",
                data = new
                {
                    count = paged.TotalCount,
                    nodes = paged.Items,
                    pageSize = paged.PageSize,
                    cursor = paged.Cursor,
                    nextCursor = paged.NextCursor,
                    hasMore = paged.HasMore
                }
            };
        }

        private static void CollectProperties(AnimatorStateMachine sm, int layer, List<object> outList)
        {
            var children = sm.states;
            for (int i = 0; i < children.Length; i++)
            {
                var st = children[i].state;
                var motion = st.motion;
                outList.Add(new
                {
                    name = st.name,
                    instanceId = st.GetInstanceIDString(),
                    layer,
                    x = children[i].position.x,
                    y = children[i].position.y,
                    speed = st.speed,
                    motionInstanceId = motion != null ? motion.GetInstanceIDString() : null,
                    motionName = motion != null ? motion.name : null,
                    motionType = motion != null ? motion.GetType().Name : null
                });
            }
            foreach (var sub in sm.stateMachines)
                CollectProperties(sub.stateMachine, layer, outList);
        }

        // Sets per-state properties from a 'states' array of { instanceId, [x], [y],
        // [speed], [motionInstanceId] }. instanceId and motionInstanceId are STRING handles
        // (from GetInstanceIDString) - opaque ids carried as strings so large values survive
        // JSON transport. States are matched by 'instanceId' for an exact, unambiguous hit.
        // Each field is OPTIONAL - only provided fields are written, so the same call can move
        // nodes, retime speed, and/or assign motion. 'motionInstanceId' is resolved to a Motion
        // via UnityObjectIdCompat.InstanceIDFromString and assigned BY REFERENCE (works for FBX
        // sub-asset clips - no asset-path lookup). Recurses into sub-state-machines and
        // reassigns stateMachine.states so edits persist.
        public static object SetStateProperties(JObject @params)
        {
            var controller = LoadController(@params);
            if (controller == null)
                return ControllerNotFoundError(@params);

            if (!(@params["states"] is JArray arr) || arr.Count == 0)
                return new { success = false, message = "'states' array is required: [{ instanceId, x?, y?, speed?, motionInstanceId? }, ...]" };

            var want = new Dictionary<string, JObject>();
            foreach (var token in arr)
            {
                if (!(token is JObject entry))
                    continue;
                string instanceId = entry["instanceId"]?.ToString();
                if (!string.IsNullOrEmpty(instanceId))
                    want[instanceId] = entry;
            }
            if (want.Count == 0)
                return new { success = false, message = "No valid entries (each needs an 'instanceId')." };

            var matched = new HashSet<string>();
            var motionFailures = new List<object>();
            Undo.RecordObject(controller, "Set State Properties");
            for (int li = 0; li < controller.layers.Length; li++)
                ApplyProperties(controller.layers[li].stateMachine, want, matched, motionFailures);

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();

            var unmatched = want.Keys.Where(k => !matched.Contains(k)).ToList();
            return new
            {
                success = true,
                message = $"Updated {matched.Count} state(s); {unmatched.Count} id(s) unmatched; {motionFailures.Count} motion ref(s) failed.",
                data = new
                {
                    matched = matched.Count,
                    requested = want.Count,
                    unmatched,
                    motionFailures
                }
            };
        }

        private static void ApplyProperties(AnimatorStateMachine sm, Dictionary<string, JObject> want, HashSet<string> matched, List<object> motionFailures)
        {
            var children = sm.states;
            for (int i = 0; i < children.Length; i++)
            {
                string id = children[i].state.GetInstanceIDString();
                if (string.IsNullOrEmpty(id) || !want.TryGetValue(id, out var entry))
                    continue;

                var st = children[i].state;

                // Position (x and/or y) - keep the unspecified axis unchanged.
                if (entry["x"] != null || entry["y"] != null)
                {
                    var pos = children[i].position;
                    float x = entry["x"]?.ToObject<float>() ?? pos.x;
                    float y = entry["y"]?.ToObject<float>() ?? pos.y;
                    children[i].position = new Vector3(x, y, 0f);
                }

                // Speed
                if (entry["speed"] != null)
                    st.speed = entry["speed"].ToObject<float>();

                // Motion by reference (resolve string handle -> Motion object). empty/null/"0" clears it.
                if (entry["motionInstanceId"] != null)
                {
                    var token = entry["motionInstanceId"];
                    string refId = token.Type == JTokenType.Null ? null : token.ToString();
                    if (string.IsNullOrEmpty(refId) || refId == "0")
                    {
                        st.motion = null;
                    }
                    else
                    {
                        var obj = UnityObjectIdCompat.InstanceIDFromString(refId) as Motion;
                        if (obj != null)
                            st.motion = obj;
                        else
                            motionFailures.Add(new { instanceId = id, motionInstanceId = refId });
                    }
                }

                matched.Add(id);
            }
            sm.states = children; // reassign so edits persist

            foreach (var sub in sm.stateMachines)
                ApplyProperties(sub.stateMachine, want, matched, motionFailures);
        }

        private static AnimatorController LoadController(JObject @params)
        {
            string controllerPath = @params["controllerPath"]?.ToString();
            if (string.IsNullOrEmpty(controllerPath))
                return null;

            controllerPath = AssetPathUtility.SanitizeAssetPath(controllerPath);
            if (controllerPath == null)
                return null;

            return AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
        }

        private static object ControllerNotFoundError(JObject @params)
        {
            string path = @params["controllerPath"]?.ToString() ?? "(not specified)";
            return new { success = false, message = $"AnimatorController not found at '{path}'. Provide a valid 'controllerPath'." };
        }

        private static void CreateFoldersRecursive(string folderPath)
        {
            if (AssetDatabase.IsValidFolder(folderPath))
                return;

            string parent = Path.GetDirectoryName(folderPath)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(parent) && parent != "Assets" && !AssetDatabase.IsValidFolder(parent))
                CreateFoldersRecursive(parent);

            string folderName = Path.GetFileName(folderPath);
            if (!string.IsNullOrEmpty(parent) && !string.IsNullOrEmpty(folderName))
                AssetDatabase.CreateFolder(parent, folderName);
        }
    }
}
